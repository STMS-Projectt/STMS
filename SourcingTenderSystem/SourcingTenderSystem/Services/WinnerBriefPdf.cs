using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace SourcingTenderSystem.Services;

/// <summary>Lays out the winner brief as a one or two page PDF summary for the Top Committee.</summary>
public static class WinnerBriefPdf
{
    private static readonly XColor Navy = XColor.FromArgb(19, 46, 67);
    private static readonly XColor Teal = XColor.FromArgb(8, 118, 108);
    private static readonly XColor Muted = XColor.FromArgb(86, 101, 122);
    private static readonly XColor Soft = XColor.FromArgb(238, 248, 246);
    private static readonly XColor Line = XColor.FromArgb(226, 232, 240);
    private const double Margin = 40;

    public static PdfDocument Build(WinnerBrief brief)
    {
        var document = new PdfDocument();
        document.Info.Title = $"Winner brief - Tender #{brief.Input.TenderId}";
        var writer = new Writer(document);
        try { Write(writer, brief); }
        finally { writer.Dispose(); }
        return document;
    }

    private static void Write(Writer w, WinnerBrief brief)
    {
        var input = brief.Input;
        var win = brief.Winner;

        // Header band
        w.Gfx.DrawRectangle(new XSolidBrush(Navy), 0, 0, w.Width, 118);
        w.Gfx.DrawString("SOURCING DESK  |  TOP COMMITTEE", new XFont("Arial", 10), XBrushes.White, Margin, 36);
        w.Gfx.DrawString("Winning supplier brief", new XFont("Arial", 24, XFontStyleEx.Bold), XBrushes.White, Margin, 70);
        w.Gfx.DrawString(Fit(w.Gfx, $"Tender #{input.TenderId}  |  {input.TenderTitle}", new XFont("Arial", 11), w.Width - 2 * Margin),
            new XFont("Arial", 11), XBrushes.White, Margin, 96);
        w.Y = 140;

        // Winner card
        var cardTop = w.Y;
        w.Gfx.DrawRectangle(new XSolidBrush(Soft), Margin, cardTop, w.Width - 2 * Margin, 92);
        w.Gfx.DrawRectangle(new XSolidBrush(Teal), Margin, cardTop, 4, 92);
        w.Gfx.DrawString("RECOMMENDED WINNER", new XFont("Arial", 9, XFontStyleEx.Bold), new XSolidBrush(Teal), Margin + 16, cardTop + 20);
        var nameFont = new XFont("Arial", 18, XFontStyleEx.Bold);
        w.Gfx.DrawString(Fit(w.Gfx, win.Supplier, nameFont, w.Width - 2 * Margin - 32), nameFont, new XSolidBrush(Navy), Margin + 16, cardTop + 44);
        var facts = $"{win.TotalPrice:N2} {win.Currency}" + (win.UsdEquivalent is decimal usd && win.Currency != "USD" ? $" (~{usd:N2} USD)" : "") +
            $"   |   Delivery {win.DeliveryDays} days   |   Valid {win.ValidityDays} days";
        w.Gfx.DrawString(Fit(w.Gfx, facts, new XFont("Arial", 10), w.Width - 2 * Margin - 32), new XFont("Arial", 10), new XSolidBrush(Muted), Margin + 16, cardTop + 64);
        var score = win.Total is decimal t ? $"Committee score {t:0.##} / 100  |  Rank {win.ScoreRank} of {input.Bids.Count}  |  {win.ItemsCovered} of {input.RequestedItems.Count} items covered"
            : $"{win.ItemsCovered} of {input.RequestedItems.Count} items covered";
        w.Gfx.DrawString(Fit(w.Gfx, score, new XFont("Arial", 10, XFontStyleEx.Bold), w.Width - 2 * Margin - 32),
            new XFont("Arial", 10, XFontStyleEx.Bold), new XSolidBrush(Navy), Margin + 16, cardTop + 81);
        w.Y = cardTop + 112;

        w.Heading("About the supplier");
        w.Paragraph(brief.Output.SupplierDescription);
        w.Heading("The winning offer");
        w.Paragraph(brief.Output.OfferSummary);
        w.Heading("Why this offer won");
        w.Bullets(brief.Output.WhyWon);
        w.Heading("What sets it apart");
        w.Bullets(brief.Output.Differentiators);

        if (input.Bids.Count > 1)
        {
            w.Heading("Compliant offers compared");
            w.Table(["Supplier", "Price", "Delivery", "Technical", "Financial", "Total"],
                [0.34, 0.2, 0.11, 0.11, 0.11, 0.13],
                input.Bids.OrderBy(b => b.ScoreRank).Select(b => new[] {
                    (b.BidId == win.BidId ? "★ " : "") + b.Supplier,
                    $"{b.TotalPrice:N2} {b.Currency}",
                    $"{b.DeliveryDays} d",
                    b.Technical?.ToString("0.##") ?? "-",
                    b.Financial?.ToString("0.##") ?? "-",
                    b.Total?.ToString("0.##") ?? "-" }).ToList(),
                win.BidId, input.Bids.OrderBy(b => b.ScoreRank).Select(b => b.BidId).ToList());
        }

        if (brief.Output.PointsToVerify.Count > 0)
        {
            w.Heading("Points to verify before voting");
            w.Bullets(brief.Output.PointsToVerify);
        }

        if (!string.IsNullOrWhiteSpace(input.CommitteeJustification))
        {
            w.Heading("Evaluation committee justification");
            w.Paragraph(input.CommitteeJustification);
        }

        w.Y += 10;
        w.Paragraph((brief.AiGenerated
            ? $"Narrative written by a local AI model ({brief.Model}) from the recorded evaluation data on {brief.GeneratedAtUtc:yyyy-MM-dd HH:mm} UTC. "
            : "Facts-only summary: the AI model was unavailable, so this brief lists recorded figures without AI narrative. ") +
            "Advisory only. The winner is the bid recommended by the evaluation committee; the Top Committee's vote decides the award.",
            new XFont("Arial", 8.5, XFontStyleEx.Italic), Muted);
    }

    private static string Fit(XGraphics gfx, string text, XFont font, double width)
    {
        text = Clean(text);
        if (gfx.MeasureString(text, font).Width <= width) return text;
        while (text.Length > 1 && gfx.MeasureString(text + "…", font).Width > width) text = text[..^1];
        return text + "…";
    }

    private static string Clean(string text) => new(text.Replace("\r", "").Where(c => c == '\n' || !char.IsControl(c)).ToArray());

    private sealed class Writer : IDisposable
    {
        private readonly PdfDocument document;
        private PdfPage page;
        public XGraphics Gfx { get; private set; }
        public double Y { get; set; }
        public double Width => page.Width.Point;
        private double Bottom => page.Height.Point - 50;

        public Writer(PdfDocument document)
        {
            this.document = document;
            page = NewPage(document);
            Gfx = XGraphics.FromPdfPage(page);
        }

        private static PdfPage NewPage(PdfDocument document)
        {
            var p = document.AddPage();
            p.Size = PdfSharp.PageSize.A4;
            return p;
        }

        public void Ensure(double height)
        {
            if (Y + height <= Bottom) return;
            Gfx.Dispose();
            page = NewPage(document);
            Gfx = XGraphics.FromPdfPage(page);
            Y = 50;
        }

        public void Heading(string text)
        {
            Ensure(48);
            Y += 14;
            Gfx.DrawString(text, new XFont("Arial", 13, XFontStyleEx.Bold), new XSolidBrush(Navy), Margin, Y);
            Y += 6;
            Gfx.DrawLine(new XPen(Line, 1), Margin, Y, Width - Margin, Y);
            Y += 16;
        }

        public void Paragraph(string text, XFont? font = null, XColor? color = null)
        {
            font ??= new XFont("Arial", 10.5);
            var brush = new XSolidBrush(color ?? Navy);
            foreach (var line in Wrap(text, font, Width - 2 * Margin))
            {
                Ensure(15);
                Gfx.DrawString(line, font, brush, Margin, Y);
                Y += 15;
            }
        }

        public void Bullets(IEnumerable<string> items)
        {
            var font = new XFont("Arial", 10.5);
            var brush = new XSolidBrush(Navy);
            var any = false;
            foreach (var item in items)
            {
                any = true;
                var first = true;
                foreach (var line in Wrap(item, font, Width - 2 * Margin - 16))
                {
                    Ensure(15);
                    if (first) Gfx.DrawEllipse(new XSolidBrush(Teal), Margin + 2, Y - 6.5, 5, 5);
                    Gfx.DrawString(line, font, brush, Margin + 16, Y);
                    Y += 15;
                    first = false;
                }
                Y += 3;
            }
            if (!any) Paragraph("-");
        }

        public void Table(string[] headers, double[] widths, IReadOnlyList<string[]> rows, int highlightId, IReadOnlyList<int> ids)
        {
            var total = Width - 2 * Margin;
            var head = new XFont("Arial", 9, XFontStyleEx.Bold);
            var cell = new XFont("Arial", 9.5);
            void Row(string[] values, XFont font, XBrush? fill)
            {
                Ensure(22);
                if (fill is not null) Gfx.DrawRectangle(fill, Margin, Y - 14, total, 21);
                var x = Margin;
                for (var i = 0; i < values.Length; i++)
                {
                    var width = total * widths[i];
                    Gfx.DrawString(Fit(Gfx, values[i], font, width - 8), font, new XSolidBrush(Navy), x + 4, Y);
                    x += width;
                }
                Gfx.DrawLine(new XPen(Line, 0.8), Margin, Y + 7, Margin + total, Y + 7);
                Y += 21;
            }
            Row(headers, head, new XSolidBrush(XColor.FromArgb(244, 247, 251)));
            for (var r = 0; r < rows.Count; r++)
                Row(rows[r], ids[r] == highlightId ? new XFont("Arial", 9.5, XFontStyleEx.Bold) : cell,
                    ids[r] == highlightId ? new XSolidBrush(Soft) : null);
        }

        private IEnumerable<string> Wrap(string value, XFont font, double width)
        {
            foreach (var paragraph in Clean(value).Split('\n'))
            {
                var line = "";
                foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    var candidate = line.Length == 0 ? word : line + " " + word;
                    if (line.Length > 0 && Gfx.MeasureString(candidate, font).Width > width)
                    {
                        yield return line;
                        line = word;
                    }
                    else line = candidate;
                    // A single word longer than the line is cut so it never runs off the page.
                    while (Gfx.MeasureString(line, font).Width > width && line.Length > 1)
                    {
                        var cut = line.Length - 1;
                        while (cut > 1 && Gfx.MeasureString(line[..cut], font).Width > width) cut--;
                        yield return line[..cut];
                        line = line[cut..];
                    }
                }
                yield return line;
            }
        }

        public void Dispose() => Gfx.Dispose();
    }
}
