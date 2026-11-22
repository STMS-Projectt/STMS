using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SourcingTenderSystem.Services;

// Facts about the awarded tender (all numbers come from the database, never from the AI).
public sealed record AwardEmailData(int TenderId, string TenderTitle, string WinnerCompany,
    decimal PriceBeforeTax, decimal Tax, decimal FinalCost, string Currency, int DeliveryDays,
    string OfferNature, int? WarrantyMonths, string? GuaranteeType, string? GuaranteeNumber, string? BankName,
    decimal? GuaranteeAmount, DateTime? GuaranteeExpiry, string TenderLink, string? VerifyLink, string UploadLink,
    IReadOnlyList<string> AttachmentPaths);

public sealed record AwardRecipient(string Email, string Name, bool IsWinner);

/// <summary>
/// Sends the "tender awarded" e-mail to everyone involved as soon as the Top Committee approves.
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
            data.OfferNature, data.WarrantyMonths, data.GuaranteeType, data.BankName, data.GuaranteeAmount,
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
                    "Announce that the Top Committee approved the award, mention the winning supplier, the final cost including 16% tax, " +
                    "and explain that the legal warranty and bank guarantee durations fit the offer type (product or service). " +
                    "Use only the facts given. Never invent numbers." },
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
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            logger.LogWarning("Ollama unavailable for the award e-mail of tender {TenderId}; using the template.", data.TenderId);
            return null;
        }
    }

    public static string TemplateIntro(AwardEmailData d) =>
        $"We are pleased to inform you that the Top Committee has approved the award of tender \"{d.TenderTitle}\" " +
        $"to {d.WinnerCompany}, with a final cost of {d.FinalCost:N2} {d.Currency} (including {d.Tax:N2} {d.Currency} sales tax).\n\n" +
        $"As this is a {d.OfferNature.ToLowerInvariant()} offer, the legal warranty is " +
        (d.WarrantyMonths is int m ? $"{m} months" : "still to be provided") +
        (d.GuaranteeNumber != null ? $" and the bank guarantee is valid until {d.GuaranteeExpiry:yyyy-MM-dd}." : " and the bank guarantee is still to be provided.");

    public static string BuildHtml(AwardEmailData d, string intro, AwardRecipient to)
    {
        static string E(string? s) => WebUtility.HtmlEncode(s ?? "—");
        string Row(string label, string? value) => $"<tr><td style=\"padding:6px 12px;color:#555\">{E(label)}</td><td style=\"padding:6px 12px\"><b>{E(value)}</b></td></tr>";

        var html = new StringBuilder();
        html.Append("<div style=\"font-family:Segoe UI,Arial,sans-serif;font-size:14px;color:#222\">");
        html.Append($"<p>Dear {E(to.Name)},</p>");
        foreach (var paragraph in intro.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            html.Append($"<p>{E(paragraph.Trim())}</p>");
        html.Append("<table style=\"border-collapse:collapse;border:1px solid #ddd\">");
        html.Append(Row("Tender", $"#{d.TenderId} {d.TenderTitle}"));
        html.Append(Row("Winning supplier", d.WinnerCompany));
        html.Append(Row("Price before tax", $"{d.PriceBeforeTax:N2} {d.Currency}"));
        html.Append(Row("Sales tax (16%)", $"{d.Tax:N2} {d.Currency}"));
        html.Append(Row("Final cost", $"{d.FinalCost:N2} {d.Currency}"));
        html.Append(Row("Delivery", $"{d.DeliveryDays} days"));
        html.Append(Row("Offer type", d.OfferNature));
        html.Append(Row("Legal warranty", d.WarrantyMonths is int m ? $"{m} months" : "Not provided yet"));
        if (d.GuaranteeNumber != null)
        {
            html.Append(Row("Bank guarantee", $"{d.GuaranteeType} #{d.GuaranteeNumber} – {d.BankName}"));
            html.Append(Row("Guarantee amount", $"{d.GuaranteeAmount:N2} {d.Currency}"));
            html.Append(Row("Guarantee valid until", d.GuaranteeExpiry?.ToString("yyyy-MM-dd")));
        }
        else html.Append(Row("Bank guarantee", "Not provided yet"));
        html.Append("</table>");
        if (d.VerifyLink != null)
            html.Append($"<p><a href=\"{E(d.VerifyLink)}\">Verify the bank guarantee online</a></p>");
        html.Append(to.IsWinner
            ? $"<p>Please upload the legal warranty and/or bank guarantee documents here: <a href=\"{E(d.UploadLink)}\">Upload documents</a></p>"
            : $"<p><a href=\"{E(d.TenderLink)}\">Open the tender in the system</a></p>");
        html.Append("<p style=\"color:#888;font-size:12px\">Sourcing &amp; Tender Management System – automatic message.</p></div>");
        return html.ToString();
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
