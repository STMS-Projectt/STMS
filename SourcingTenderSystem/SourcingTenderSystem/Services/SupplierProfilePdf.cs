using PdfSharp.Drawing;
using PdfSharp.Pdf;
using SourcingTenderSystem.ViewModels;

namespace SourcingTenderSystem.Services;

/// <summary>Supplier profile for the evaluation committee: who they are, how often they win, how much they earned and their bid history.
/// Uses the page writer of <see cref="WinnerBriefPdf"/> so all committee PDFs look alike.</summary>
public static class SupplierProfilePdf
{
    private static readonly XColor Navy = XColor.FromArgb(19, 46, 67);
    private static readonly XColor Teal = XColor.FromArgb(8, 118, 108);
    private static readonly XColor Muted = XColor.FromArgb(86, 101, 122);
    private static readonly XColor Soft = XColor.FromArgb(238, 248, 246);
    private const double Margin = 40;

    public static PdfDocument Build(CommitteeSupplier model, DateTime nowUtc)
    {
        var document = new PdfDocument();
        document.Info.Title = $"Supplier profile - {model.Supplier.CompanyName}";
        var w = new WinnerBriefPdf.Writer(document);
        try { Write(w, model, nowUtc); }
        finally { w.Dispose(); }
        return document;
    }

    private static string Money(IReadOnlyList<(string Currency, decimal Amount)> values) =>
        values.Count == 0 ? "0.00" : string.Join("  +  ", values.Select(v => $"{v.Amount:N2} {v.Currency}"));

    private static string Date(DateTime? value) => value?.ToString("yyyy-MM-dd") ?? "-";

    private static void Write(WinnerBriefPdf.Writer w, CommitteeSupplier m, DateTime nowUtc)
    {
        var s = m.Supplier;
        var white = XBrushes.White;

        // Header band
        w.Gfx.DrawRectangle(new XSolidBrush(Navy), 0, 0, w.Width, 118);
        w.Gfx.DrawString("SOURCING DESK  |  EVALUATION COMMITTEE", new XFont("Arial", 10), white, Margin, 36);
        var title = new XFont("Arial", 24, XFontStyleEx.Bold);
        w.Gfx.DrawString(WinnerBriefPdf.Fit(w.Gfx, s.CompanyName, title, w.Width - 2 * Margin), title, white, Margin, 70);
        w.Gfx.DrawString(WinnerBriefPdf.Fit(w.Gfx, $"Supplier profile  |  Reviewed for tender #{m.TenderId} {m.TenderTitle}  |  Bid #{m.BidId}", new XFont("Arial", 11), w.Width - 2 * Margin),
            new XFont("Arial", 11), white, Margin, 96);
        w.Y = 140;

        // Key figures: four tiles
        var tiles = new (string Label, string Value)[]
        {
            ("TENDERS WON", m.Awards.Count.ToString()),
            ("TOTAL EARNED (AWARDS)", Money(m.AwardedValue)),
            ("WIN RATE", m.WinRate is decimal rate ? $"{rate:0.#}%" : "-"),
            ("BIDS SUBMITTED", m.BidsSubmitted.ToString())
        };
        var gap = 8d;
        var tileWidth = (w.Width - 2 * Margin - gap * 3) / 4;
        for (var i = 0; i < tiles.Length; i++)
        {
            var x = Margin + i * (tileWidth + gap);
            w.Gfx.DrawRectangle(new XSolidBrush(Soft), x, w.Y, tileWidth, 62);
            w.Gfx.DrawRectangle(new XSolidBrush(Teal), x, w.Y, 3, 62);
            w.Gfx.DrawString(tiles[i].Label, new XFont("Arial", 7.5, XFontStyleEx.Bold), new XSolidBrush(Teal), x + 12, w.Y + 18);
            var valueFont = new XFont("Arial", 15, XFontStyleEx.Bold);
            if (w.Gfx.MeasureString(tiles[i].Value, valueFont).Width > tileWidth - 20) valueFont = new XFont("Arial", 10, XFontStyleEx.Bold);
            w.Gfx.DrawString(WinnerBriefPdf.Fit(w.Gfx, tiles[i].Value, valueFont, tileWidth - 20), valueFont, new XSolidBrush(Navy), x + 12, w.Y + 44);
        }
        w.Y += 82;

        w.Heading("Company details");
        w.Table(["Field", "Value"], [0.32, 0.68],
        [
            ["Company", s.CompanyName],
            ["Status", s.Status],
            ["E-mail", m.Email ?? "-"],
            ["Phone", string.IsNullOrWhiteSpace(s.ContactPhone) ? "-" : s.ContactPhone],
            ["Address", string.IsNullOrWhiteSpace(s.Address) ? "-" : s.Address],
            ["Tax number", string.IsNullOrWhiteSpace(s.TaxNumber) ? "-" : s.TaxNumber],
            ["Commercial reg. no.", string.IsNullOrWhiteSpace(s.CommercialRegNo) ? "-" : s.CommercialRegNo],
            ["Registered on", Date(s.CreatedAt)],
            ["Categories", m.Categories.Count == 0 ? "-" : string.Join(", ", m.Categories)],
            ["Registration documents", m.DocumentCount.ToString()]
        ], -1, Enumerable.Repeat(0, 10).ToList());

        w.Heading("Track record");
        w.Table(["Measure", "Value"], [0.5, 0.5],
        [
            ["Invitations received", m.Invitations.ToString()],
            ["Invitations accepted / declined", $"{m.InvitationsAccepted} / {m.InvitationsDeclined}" + (m.InvitationAcceptRate is decimal r ? $"  ({r:0.#}% accepted)" : "")],
            ["Bids submitted", m.BidsSubmitted.ToString()],
            ["Compliant / non-compliant bids", $"{m.Compliant} / {m.NonCompliant}"],
            ["Tenders won / lost (decided)", $"{m.Awards.Count} / {m.Unsuccessful}"],
            ["Win rate (decided tenders)", m.WinRate is decimal winRate ? $"{winRate:0.#}%" : "No decided tenders yet"],
            ["Total awarded value (before tax)", Money(m.AwardedValue)],
            ["Average awarded contract", m.Awards.Count == 0 ? "-" : string.Join("  +  ", m.Awards.GroupBy(a => a.Currency).Select(g => $"{g.Average(a => a.TotalPrice):N2} {g.Key}"))],
            ["Average delivery promised", m.AverageDelivery is decimal d ? $"{d:0.#} days" : "-"],
            ["Last award", Date(m.LastAwardAt)]
        ], -1, Enumerable.Repeat(0, 10).ToList());

        w.Heading("Awarded tenders");
        if (m.Awards.Count == 0) w.Paragraph("This supplier has not won a tender yet.", null, Muted);
        else
            w.Table(["Tender", "Decision date", "Delivery", "Amount"], [0.46, 0.18, 0.12, 0.24],
                m.Awards.Select(a => new[] { $"#{a.TenderId} {a.TenderTitle}", Date(a.AwardedAt), $"{a.DeliveryDays} d", $"{a.TotalPrice:N2} {a.Currency}" }).ToList(),
                -1, m.Awards.Select(_ => 0).ToList());

        w.Heading("Bid history");
        if (m.Bids.Count == 0) w.Paragraph("No submitted bids.", null, Muted);
        else
            w.Table(["Tender", "Submitted", "Total", "Compliance", "Outcome"], [0.33, 0.14, 0.19, 0.18, 0.16],
                m.Bids.Select(b => new[] { (b.TenderId == m.TenderId ? "> " : "") + $"#{b.TenderId} {b.TenderTitle}", Date(b.SubmittedAt), $"{b.TotalPrice:N2} {b.Currency}", b.Status, b.AwardOutcome ?? (b.Status == "Non-Compliant" ? "Excluded" : "Open") }).ToList(),
                m.TenderId, m.Bids.Select(b => b.TenderId).ToList());

        w.Y += 10;
        w.Paragraph($"Generated {nowUtc:yyyy-MM-dd HH:mm} UTC from the records in the Sourcing Desk. Amounts are bid totals before sales tax. " +
            "The win rate counts only tenders that reached a decision. For the evaluation committee only; it does not change any score.",
            new XFont("Arial", 8.5, XFontStyleEx.Italic), Muted);
    }
}
