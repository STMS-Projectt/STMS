using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SourcingTenderSystem.Services;

// Facts about the awarded tender (all numbers come from the database, never from the AI).
public sealed record AwardEmailData(int TenderId, string TenderTitle, string WinnerCompany,
    decimal PriceBeforeTax, decimal Tax, decimal FinalCost, string Currency, int DeliveryDays,
    int? WarrantyMonths, string? BankName,
    decimal? GuaranteeAmount, DateTime? GuaranteeExpiry, string TenderLink, string? VerifyLink, string UploadLink,
    IReadOnlyList<string> AttachmentPaths);

// Unsuccessful = a supplier whose bid was not selected: gets a short "result of your bid" e-mail instead.
public sealed record AwardRecipient(string Email, string Name, bool IsWinner, bool Unsuccessful = false);

/// <summary>
/// Sends the "tender awarded" e-mail to everyone involved as soon as the Top Committee approves,
/// and a short "your bid was not selected" e-mail to the suppliers who did not win.
/// 1) The local Ollama model writes the e-mail text from the tender data (fallback: a fixed template).
/// 2) A facts table (final cost, warranty, bank guarantee) is always added by the system.
/// 3) Sent by SMTP when "Smtp:Host" is configured, otherwise saved as .eml files in App_Data/OutgoingEmails.
/// </summary>
public sealed class AwardEmailService(IHttpClientFactory httpClients, IConfiguration configuration,
    IWebHostEnvironment environment, ILogger<AwardEmailService> logger)
{
    private string ModelName => configuration["Ollama:Model"] is { Length: > 0 } m ? m : "llama3.1";

    public async Task SendAsync(AwardEmailData data, IReadOnlyList<AwardRecipient> recipients, CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Sending award e-mails for tender {TenderId} to: {Emails}", data.TenderId,
                string.Join(", ", recipients.Select(r => r.Email)));
            if (!recipients.Any(r => r.IsWinner))
                logger.LogWarning("The winning supplier of tender {TenderId} has no e-mail address.", data.TenderId);
            var intro = await ComposeWithAiAsync(data, cancellationToken) ?? TemplateIntro(data);
            foreach (var recipient in recipients.DistinctBy(r => r.Email.ToLowerInvariant()))
            {
                try
                {
                    if (recipient.Unsuccessful)
                        await SendOneAsync(recipient, $"Tender #{data.TenderId}: result of your bid - {data.TenderTitle}",
                            BuildUnsuccessfulHtml(data, recipient), [], cancellationToken);
                    else
                        await SendOneAsync(recipient, $"Tender #{data.TenderId} awarded: {data.TenderTitle}",
                            BuildHtml(data, intro, recipient), recipient.IsWinner ? data.AttachmentPaths : [], cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Award e-mail to {Email} failed for tender {TenderId}", recipient.Email, data.TenderId);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Award e-mails failed for tender {TenderId}", data.TenderId);
        }
    }

    // ------------------------------------------------------------ text

    // Ask Ollama for a short professional e-mail body. Returns null if Ollama is not available.
    private async Task<string?> ComposeWithAiAsync(AwardEmailData data, CancellationToken cancellationToken)
    {
        var facts = JsonSerializer.Serialize(new
        {
            data.TenderTitle, data.WinnerCompany, data.FinalCost, data.Tax, data.Currency, data.DeliveryDays,
            data.WarrantyMonths, data.BankName, data.GuaranteeAmount,
            GuaranteeExpiry = data.GuaranteeExpiry?.ToString("yyyy-MM-dd")
        });
        var payload = new JsonObject
        {
            ["model"] = ModelName,
            ["stream"] = false,
            ["options"] = new JsonObject { ["temperature"] = 0.3 },
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] =
                    "You write short, formal procurement e-mails. Write 2 short paragraphs of plain text (no markdown, no greeting line, no signature). " +
                    "Announce that the Top Committee approved the award and name the winning supplier and the tender. " +
                    "Do NOT write any numbers, amounts, dates or durations: the system adds an exact summary table below your text. " +
                    "Use only the facts given." },
                new JsonObject { ["role"] = "user", ["content"] = "Tender data: " + facts }
            }
        };
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(90));
            var client = httpClients.CreateClient(BidAiAdvisor.HttpClientName);
            using var response = await client.PostAsync("api/chat",
                new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"), timeout.Token);
            if (!response.IsSuccessStatusCode) return null;
            var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            var text = body?["message"]?["content"]?.GetValue<string>()?.Trim();
            // Numbers are shown only in the facts card; text with digits may contain invented figures, so use the template instead.
            return string.IsNullOrWhiteSpace(text) || text.Any(char.IsDigit) ? null : text;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            logger.LogWarning("Ollama unavailable for the award e-mail of tender {TenderId}; using the template.", data.TenderId);
            return null;
        }
    }

    public static string TemplateIntro(AwardEmailData d) =>
        $"We are pleased to inform you that the Top Committee has approved the award of tender \"{d.TenderTitle}\" to {d.WinnerCompany}.\n\n" +
        "The final cost, the legal warranty and the bank guarantee details are summarised below.";

    public static string BuildHtml(AwardEmailData d, string intro, AwardRecipient to)
    {
        static string E(string? s) => WebUtility.HtmlEncode(s ?? "—");
        const string Green = "#08766c", Ink = "#17243a", Muted = "#64748b", Line = "#e2e8f0";
        string Row(string label, string value, bool last = false) =>
            $"<tr><td style=\"padding:12px 0;color:{Muted};font-size:14px;{(last ? "" : $"border-bottom:1px solid {Line};")}\">{E(label)}</td>" +
            $"<td align=\"right\" style=\"padding:12px 0;color:{Ink};font-size:14px;font-weight:600;{(last ? "" : $"border-bottom:1px solid {Line};")}\">{value}</td></tr>";

        var h = new StringBuilder();
        h.Append($"<div style=\"margin:0;padding:24px 12px;background:#eef3f6;font-family:'Segoe UI',Tahoma,Arial,sans-serif;\">");
        h.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"max-width:620px;margin:0 auto;\">");

        // Header band
        h.Append($"<tr><td style=\"background:{Green};background-image:linear-gradient(135deg,#065b53,#0b8a7c 60%,#19a891);border-radius:18px 18px 0 0;padding:28px 32px;color:#ffffff;\">");
        h.Append("<div style=\"font-size:12px;letter-spacing:2px;font-weight:700;color:#b9f3e8;\">SOURCING &amp; TENDER MANAGEMENT</div>");
        h.Append("<div style=\"font-size:26px;font-weight:800;margin-top:8px;\">&#127942; Tender awarded</div>");
        h.Append($"<div style=\"font-size:15px;margin-top:6px;color:#e3fbf6;\">Tender #{d.TenderId} &middot; {E(d.TenderTitle)}</div></td></tr>");

        // Body
        h.Append("<tr><td style=\"background:#ffffff;padding:28px 32px;\">");
        foreach (var paragraph in intro.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            h.Append($"<p style=\"margin:0 0 12px;font-size:15px;line-height:1.6;color:#334155;\">{E(paragraph.Trim())}</p>");

        // Winner card
        h.Append($"<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"margin:20px 0;background:#e9f8f5;border:1px solid #bfe9e0;border-radius:14px;\"><tr><td style=\"padding:18px 20px;\">");
        h.Append($"<div style=\"font-size:12px;letter-spacing:1px;font-weight:700;color:{Green};\">WINNING SUPPLIER</div>");
        h.Append($"<div style=\"font-size:20px;font-weight:800;color:{Ink};margin-top:4px;\">{E(d.WinnerCompany)}</div></td>");
        h.Append($"<td align=\"right\" style=\"padding:18px 20px;\"><div style=\"font-size:12px;color:{Muted};font-weight:600;\">FINAL COST</div>");
        h.Append($"<div style=\"font-size:24px;font-weight:800;color:{Green};white-space:nowrap;\">{d.FinalCost:N2} <span style=\"font-size:13px;\">{E(d.Currency)}</span></div></td></tr></table>");

        // Cost breakdown
        h.Append($"<div style=\"font-size:13px;font-weight:700;color:{Ink};margin:0 0 4px;\">Cost breakdown</div>");
        h.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\">");
        h.Append(Row("Price before tax", $"{d.PriceBeforeTax:N2} {E(d.Currency)}"));
        h.Append(Row("Sales tax (16%)", $"{d.Tax:N2} {E(d.Currency)}"));
        h.Append(Row("Final cost", $"<span style=\"color:{Green};font-size:16px;\">{d.FinalCost:N2} {E(d.Currency)}</span>"));
        h.Append(Row("Delivery", $"{d.DeliveryDays} days", last: true));
        h.Append("</table>");

        // Warranty + guarantee
        h.Append($"<div style=\"font-size:13px;font-weight:700;color:{Ink};margin:22px 0 4px;\">Warranty and bank guarantee</div>");
        h.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\">");
        h.Append(Row("Legal warranty", d.WarrantyMonths is int m ? $"{m} months" : "<span style=\"color:#b45309;\">Not provided yet</span>"));
        if (d.BankName != null)
        {
            h.Append(Row("Issuing bank", E(d.BankName)));
            h.Append(Row("Guarantee amount", $"{d.GuaranteeAmount:N2} {E(d.Currency)}"));
            h.Append(Row("Valid until", E(d.GuaranteeExpiry?.ToString("dd MMM yyyy")), last: true));
        }
        else h.Append(Row("Bank guarantee", "<span style=\"color:#b45309;\">Not provided yet</span>", last: true));
        h.Append("</table>");

        if (to.IsWinner)
            h.Append($"<p style=\"margin:22px 0 0;font-size:13px;color:{Muted};\">Please upload the signed legal warranty and bank guarantee documents if you have not done so yet.</p>");
        h.Append("</td></tr>");

        // Footer
        h.Append($"<tr><td style=\"background:#f8fafc;border-top:1px solid {Line};border-radius:0 0 18px 18px;padding:16px 32px;text-align:center;font-size:12px;color:#94a3b8;\">");
        h.Append("This is an automatic message from the Sourcing &amp; Tender Management System. Please do not reply.</td></tr>");
        h.Append("</table></div>");
        return h.ToString();
    }

    // E-mail for a supplier whose bid was not selected. It does not show the winner's price.
    public static string BuildUnsuccessfulHtml(AwardEmailData d, AwardRecipient to)
    {
        static string E(string? s) => WebUtility.HtmlEncode(s ?? "—");
        const string Green = "#08766c", Line = "#e2e8f0";
        var h = new StringBuilder();
        h.Append("<div style=\"margin:0;padding:24px 12px;background:#eef3f6;font-family:'Segoe UI',Tahoma,Arial,sans-serif;\">");
        h.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"max-width:620px;margin:0 auto;\">");

        // Header band
        h.Append($"<tr><td style=\"background:{Green};background-image:linear-gradient(135deg,#065b53,#0b8a7c 60%,#19a891);border-radius:18px 18px 0 0;padding:28px 32px;color:#ffffff;\">");
        h.Append("<div style=\"font-size:12px;letter-spacing:2px;font-weight:700;color:#b9f3e8;\">SOURCING &amp; TENDER MANAGEMENT</div>");
        h.Append("<div style=\"font-size:26px;font-weight:800;margin-top:8px;\">Result of your bid</div>");
        h.Append($"<div style=\"font-size:15px;margin-top:6px;color:#e3fbf6;\">Tender #{d.TenderId} &middot; {E(d.TenderTitle)}</div></td></tr>");

        // Body
        h.Append("<tr><td style=\"background:#ffffff;padding:28px 32px;\">");
        h.Append($"<p style=\"margin:0 0 12px;font-size:15px;line-height:1.6;color:#334155;\">Dear {E(to.Name)},</p>");
        h.Append($"<p style=\"margin:0 0 12px;font-size:15px;line-height:1.6;color:#334155;\">Thank you for taking part in tender \"{E(d.TenderTitle)}\". " +
                 "After the evaluation, the Top Committee awarded the tender to another supplier, so your bid was not selected this time.</p>");
        h.Append("<p style=\"margin:0 0 12px;font-size:15px;line-height:1.6;color:#334155;\">We appreciate your offer and look forward to your participation in our future tenders.</p>");
        h.Append("</td></tr>");

        // Footer
        h.Append($"<tr><td style=\"background:#f8fafc;border-top:1px solid {Line};border-radius:0 0 18px 18px;padding:16px 32px;text-align:center;font-size:12px;color:#94a3b8;\">");
        h.Append("This is an automatic message from the Sourcing &amp; Tender Management System. Please do not reply.</td></tr>");
        h.Append("</table></div>");
        return h.ToString();
    }

    // ------------------------------------------------------------ sending

    private async Task SendOneAsync(AwardRecipient to, string subject, string html, IReadOnlyList<string> attachments, CancellationToken cancellationToken)
    {
        var from = configuration["Smtp:From"] is { Length: > 0 } f ? f : "no-reply@stms.local";
        using var message = new MailMessage(from, to.Email, subject, html) { IsBodyHtml = true };
        foreach (var path in attachments.Where(File.Exists))
            message.Attachments.Add(new Attachment(path));

        using var smtp = new SmtpClient();
        if (configuration["Smtp:Host"] is { Length: > 0 } host)
        {
            smtp.Host = host;
            smtp.Port = configuration.GetValue("Smtp:Port", 587);
            smtp.EnableSsl = configuration.GetValue("Smtp:EnableSsl", true);
            if (configuration["Smtp:User"] is { Length: > 0 } user)
                smtp.Credentials = new NetworkCredential(user, configuration["Smtp:Password"]);
        }
        else
        {
            // No SMTP server configured: keep the e-mail as an .eml file (opens in Outlook / any mail app).
            var folder = Path.Combine(environment.ContentRootPath, "App_Data", "OutgoingEmails");
            Directory.CreateDirectory(folder);
            smtp.DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory;
            smtp.PickupDirectoryLocation = folder;
        }
        await smtp.SendMailAsync(message, cancellationToken);
        if (smtp.DeliveryMethod == SmtpDeliveryMethod.SpecifiedPickupDirectory)
            logger.LogWarning("Smtp:Host is empty, so the award e-mail to {Email} was NOT sent. It was saved in {Folder}.",
                to.Email, smtp.PickupDirectoryLocation);
        else
            logger.LogInformation("Award e-mail sent to {Email}.", to.Email);
    }
}
