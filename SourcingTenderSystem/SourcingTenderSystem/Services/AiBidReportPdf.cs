using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace SourcingTenderSystem.Services;

/// <summary>Lays out the AI bid analysis (the evaluation committee's advisory report) as a styled A4 PDF.</summary>
public static class AiBidReportPdf
{
    private static readonly XColor Navy = XColor.FromArgb(17, 39, 58);
    private static readonly XColor Brand = XColor.FromArgb(0, 120, 88);
    private static readonly XColor BrandDark = XColor.FromArgb(0, 84, 62);
    private static readonly XColor Muted = XColor.FromArgb(90, 110, 130);
    private static readonly XColor Soft = XColor.FromArgb(236, 249, 243);
    private static readonly XColor SoftLine = XColor.FromArgb(200, 233, 219);
    private static readonly XColor Line = XColor.FromArgb(225, 232, 238);
    private static readonly XColor HeadFill = XColor.FromArgb(244, 248, 251);
    private static readonly XColor Good = XColor.FromArgb(10, 146, 112);
    private static readonly XColor Bad = XColor.FromArgb(200, 60, 70);
    private static readonly XColor Warn = XColor.FromArgb(122, 75, 0);
    private static readonly XColor WarnSoft = XColor.FromArgb(255, 246, 225);
    private static readonly XColor WarnLine = XColor.FromArgb(242, 212, 143);
    private const double Margin = 42;

    public static PdfDocument Build(AiBidAnalysis analysis)
    {
        var document = new PdfDocument();
        document.Info.Title = $"AI bid analysis - Tender #{analysis.Input.TenderId}";
        document.Info.Author = "Sourcing Desk";
        var writer = new Writer(document);
        try { Write(writer, analysis); }
        finally { writer.Dispose(); }
        Footers(document, analysis.Input.TenderId);
        return document;
    }

    private static void Write(Writer w, AiBidAnalysis analysis)
    {
        var input = analysis.Input;
        var output = analysis.Output;
        var best = analysis.RecommendedBid;
        var bestFacts = analysis.Facts.Single(f => f.BidId == best.BidId);
        var inner = w.Width - 2 * Margin;

        // Header band
        w.Gfx.DrawRectangle(new XLinearGradientBrush(new XPoint(0, 0), new XPoint(w.Width, 0), BrandDark, Brand), 0, 0, w.Width, 128);
        w.Gfx.DrawEllipse(new XSolidBrush(XColor.FromArgb(28, 255, 255, 255)), w.Width - 170, -60, 240, 240);
        w.Gfx.DrawString("SOURCING DESK  |  AI BID ANALYSIS", new XFont("Arial", 9, XFontStyleEx.Bold), new XSolidBrush(XColor.FromArgb(210, 255, 255, 255)), Margin, 38);
        var titleFont = new XFont("Arial", 24, XFontStyleEx.Bold);
        w.Gfx.DrawString(Fit(w.Gfx, input.Title, titleFont, inner), titleFont, XBrushes.White, Margin, 72);
        var subFont = new XFont("Arial", 10);
        var sub = $"Tender #{input.TenderId}   |   Generated {analysis.GeneratedAtUtc:yyyy-MM-dd HH:mm} UTC   |   Model {analysis.Model}   |   {input.Bids.Count} compliant bids compared";
        w.Gfx.DrawString(Fit(w.Gfx, sub, subFont, inner), subFont, new XSolidBrush(XColor.FromArgb(225, 255, 255, 255)), Margin, 100);
        w.Y = 152;

        // Advisory notice
        var notice = "Advisory only. This report was produced by an AI assistant to support the evaluation committee. It does not score the bids " +
            "and does not replace the committee's judgement or decision. Check every point against the bid documents before relying on it.";
        w.Box(notice, new XFont("Arial", 9), Warn, WarnSoft, WarnLine, Warn);
        w.Y += 14;

        // Recommended offer card
        var nameFont = new XFont("Arial", 20, XFontStyleEx.Bold);
        var headFont = new XFont("Arial", 11, XFontStyleEx.Bold);
        var bodyFont = new XFont("Arial", 10);
        var textWidth = inner - 36;
        var headline = w.WrapLines(output.Headline, headFont, textWidth);
        var summary = string.IsNullOrWhiteSpace(output.Summary) ? new List<string>() : w.WrapLines(output.Summary, bodyFont, textWidth);
        var cardHeight = 92 + headline.Count * 15 + (summary.Count > 0 ? 6 + summary.Count * 14 : 0) + 14;
        w.Ensure(cardHeight);
        var top = w.Y;
        w.Gfx.DrawRoundedRectangle(new XPen(SoftLine, 1), new XSolidBrush(Soft), Margin, top, inner, cardHeight, 14, 14);
        w.Gfx.DrawRectangle(new XSolidBrush(Brand), Margin, top + 12, 4, cardHeight - 24);
        w.Gfx.DrawString("RECOMMENDED OFFER", new XFont("Arial", 8.5, XFontStyleEx.Bold), new XSolidBrush(Brand), Margin + 20, top + 24);
        w.Gfx.DrawString(Fit(w.Gfx, best.Supplier, nameFont, textWidth - 120), nameFont, new XSolidBrush(Navy), Margin + 20, top + 50);
        Pill(w.Gfx, "AI PICK", Margin + inner - 18, top + 18, Brand, XColors.White, alignRight: true);
        var facts = $"{best.TotalPrice:N2} {best.Currency}   |   Delivery {best.DeliveryDays} days   |   Valid {best.ValidityDays} days   |   {bestFacts.ItemsCovered} of {bestFacts.ItemsRequested} items covered";
        w.Gfx.DrawString(Fit(w.Gfx, facts, bodyFont, textWidth), bodyFont, new XSolidBrush(Muted), Margin + 20, top + 70);
        var y = top + 94;
        foreach (var line in headline) { w.Gfx.DrawString(line, headFont, new XSolidBrush(Navy), Margin + 20, y); y += 15; }
        if (summary.Count > 0) y += 6;
        foreach (var line in summary) { w.Gfx.DrawString(line, bodyFont, new XSolidBrush(Navy), Margin + 20, y); y += 14; }
        w.Y = top + cardHeight + 4;

        if (output.WhyChosen.Count > 0)
        {
            w.Heading("Why the AI chose this offer");
            w.Bullets(output.WhyChosen, Good);
        }

        // Side-by-side comparison (system figures)
        w.Heading("Side-by-side comparison");
        w.Note("Figures in this table are calculated by the system from the submitted bids, not by the AI.");
        var ranked = output.Bids.OrderBy(b => b.OverallRank).Select(b => b.BidId)
            .Concat(analysis.Facts.Select(f => f.BidId)).Distinct().ToList();
        w.Table(["Rank", "Supplier", "Total price", "vs lowest", "Delivery", "Validity", "Items", "Docs"],
            [0.08, 0.28, 0.17, 0.11, 0.1, 0.1, 0.09, 0.07],
            [false, false, true, true, true, true, true, true],
            ranked.Select(id =>
            {
                var f = analysis.Facts.Single(x => x.BidId == id);
                var rank = analysis.AssessmentFor(id)?.OverallRank;
                return new[] {
                    rank is int r ? $"#{r}" : "-",
                    f.Supplier,
                    $"{f.TotalPrice:N2} {f.Currency}",
                    !f.PriceComparable ? "n/a" : f.PriceGapPercent == 0 ? "Lowest" : $"+{f.PriceGapPercent:0.#}%",
                    $"{f.DeliveryDays} d",
                    $"{f.ValidityDays} d",
                    $"{f.ItemsCovered}/{f.ItemsRequested}",
                    f.DocumentCount.ToString() };
            }).ToList(),
            ranked.Select(id => id == best.BidId).ToList());

        // Unit prices by item
        if (input.RequestedItems.Count > 0 && input.Bids.Count is > 0 and <= 5)
        {
            w.Heading("Unit prices by item");
            var bidShare = 0.62 / input.Bids.Count;
            w.Table(["Item", "Qty", .. input.Bids.Select(b => b.Supplier)],
                [0.28, 0.1, .. input.Bids.Select(_ => bidShare)],
                [false, true, .. input.Bids.Select(_ => true)],
                input.RequestedItems.Select(item => new[] { item.Name, $"{item.Quantity} {item.Unit}" }
                    .Concat(input.Bids.Select(b => b.Lines.FirstOrDefault(l => l.RequestedItemId == item.Id) is { } l
                        ? $"{l.UnitPrice:N2} {b.Currency}" : "Not offered")).ToArray()).ToList(),
                input.RequestedItems.Select(_ => false).ToList());
        }

        // Per-bid assessments
        if (output.Bids.Count > 0)
        {
            w.Heading("Strengths and weaknesses of each offer");
            foreach (var a in output.Bids.OrderBy(b => b.OverallRank))
            {
                var bid = input.Bids.FirstOrDefault(b => b.BidId == a.BidId);
                if (bid is null) continue;
                w.BidHeader($"#{a.OverallRank}  {bid.Supplier}", $"{bid.TotalPrice:N2} {bid.Currency}  |  {bid.DeliveryDays} days", a.BidId == best.BidId);
                if (!string.IsNullOrWhiteSpace(a.PriceAssessment)) w.Labelled("Price", a.PriceAssessment);
                if (!string.IsNullOrWhiteSpace(a.SpecificationAssessment)) w.Labelled("Specifications", a.SpecificationAssessment);
                if (!string.IsNullOrWhiteSpace(a.DeliveryAssessment)) w.Labelled("Delivery", a.DeliveryAssessment);
                if (a.Strengths.Count > 0) { w.Label("Strengths", Good); w.Bullets(a.Strengths, Good, indent: 14); }
                if (a.Weaknesses.Count > 0) { w.Label("Weaknesses", Bad); w.Bullets(a.Weaknesses, Bad, indent: 14); }
                if (a.Risks.Count > 0) { w.Label("Risks", Warn); w.Bullets(a.Risks, XColor.FromArgb(214, 140, 20), indent: 14); }
                w.Y += 10;
            }
        }

        if (output.ComparisonNotes.Count > 0)
        {
            w.Heading("Comparison notes");
            w.Bullets(output.ComparisonNotes, Brand);
        }
        if (output.QuestionsForCommittee.Count > 0)
        {
            w.Heading("Points to check before scoring");
            w.Bullets(output.QuestionsForCommittee, XColor.FromArgb(214, 140, 20));
        }

        w.Heading("Evaluation criteria considered");
        if (input.Criteria.Count == 0) w.Note("No criteria had been set when this analysis was produced.");
        else w.Table(["Criterion", "Type", "Weight", "Max score"], [0.46, 0.22, 0.16, 0.16], [false, false, true, true],
            input.Criteria.Select(c => new[] { c.Name, c.Type, $"{c.WeightPercent:0.##}%", c.MaxScore.ToString() }).ToList(),
            input.Criteria.Select(_ => false).ToList());
    }

    private static void Footers(PdfDocument document, int tenderId)
    {
        var font = new XFont("Arial", 8);
        for (var i = 0; i < document.PageCount; i++)
        {
            var page = document.Pages[i];
            using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            var y = page.Height.Point - 34;
            gfx.DrawLine(new XPen(Line, 0.8), Margin, y - 10, page.Width.Point - Margin, y - 10);
            gfx.DrawString($"Sourcing Desk  |  AI assistant report for Tender #{tenderId}. The committee's scores and decision are final.",
                font, new XSolidBrush(Muted), Margin, y);
            gfx.DrawString($"Page {i + 1} of {document.PageCount}", font, new XSolidBrush(Muted),
                new XRect(Margin, y - 7, page.Width.Point - 2 * Margin, 10), XStringFormats.TopRight);
        }
    }

    private static void Pill(XGraphics gfx, string text, double x, double y, XColor fill, XColor ink, bool alignRight = false)
    {
        var font = new XFont("Arial", 7.5, XFontStyleEx.Bold);
        var width = gfx.MeasureString(text, font).Width + 14;
        if (alignRight) x -= width;
        gfx.DrawRoundedRectangle(new XSolidBrush(fill), x, y, width, 15, 15, 15);
        gfx.DrawString(text, font, new XSolidBrush(ink), new XRect(x, y, width, 15), XStringFormats.Center);
    }

    private static string Fit(XGraphics gfx, string text, XFont font, double width)
    {
        text = Clean(text).Replace('\n', ' ');
        if (gfx.MeasureString(text, font).Width <= width) return text;
        while (text.Length > 1 && gfx.MeasureString(text + "…", font).Width > width) text = text[..^1];
        return text + "…";
    }

    private static string Clean(string? text) => new((text ?? "").Replace("\r", "").Where(c => c == '\n' || !char.IsControl(c)).ToArray());

    private sealed class Writer : IDisposable
    {
        private readonly PdfDocument document;
        private PdfPage page;
        public XGraphics Gfx { get; private set; }
        public double Y { get; set; }
        public double Width => page.Width.Point;
        private double Bottom => page.Height.Point - 60;
        private double Inner => Width - 2 * Margin;

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
            Y = 54;
        }

        public void Heading(string text)
        {
            Ensure(56);
            Y += 18;
            Gfx.DrawRectangle(new XSolidBrush(Brand), Margin, Y - 11, 3, 14);
            Gfx.DrawString(text, new XFont("Arial", 13, XFontStyleEx.Bold), new XSolidBrush(Navy), Margin + 10, Y);
            Y += 8;
            Gfx.DrawLine(new XPen(Line, 1), Margin, Y, Width - Margin, Y);
            Y += 16;
        }

        public void Note(string text) => Paragraph(text, new XFont("Arial", 8.5, XFontStyleEx.Italic), Muted, 12);

        public void Paragraph(string text, XFont? font = null, XColor? color = null, double lineHeight = 14.5, double indent = 0)
        {
            font ??= new XFont("Arial", 10);
            var brush = new XSolidBrush(color ?? Navy);
            foreach (var line in Wrap(text, font, Inner - indent))
            {
                Ensure(lineHeight);
                Gfx.DrawString(line, font, brush, Margin + indent, Y);
                Y += lineHeight;
            }
        }

        public void Box(string text, XFont font, XColor ink, XColor fill, XColor border, XColor accent)
        {
            var lines = WrapLines(text, font, Inner - 30);
            var height = lines.Count * 13 + 20;
            Ensure(height);
            Gfx.DrawRoundedRectangle(new XPen(border, 0.8), new XSolidBrush(fill), Margin, Y, Inner, height, 10, 10);
            Gfx.DrawRectangle(new XSolidBrush(accent), Margin, Y + 8, 3, height - 16);
            var y = Y + 17;
            foreach (var line in lines) { Gfx.DrawString(line, font, new XSolidBrush(ink), Margin + 16, y); y += 13; }
            Y += height;
        }

        public void Label(string text, XColor color)
        {
            Ensure(30);
            Y += 4;
            Gfx.DrawString(text.ToUpperInvariant(), new XFont("Arial", 8, XFontStyleEx.Bold), new XSolidBrush(color), Margin + 14, Y);
            Y += 13;
        }

        public void Labelled(string label, string text)
        {
            Label(label, Muted);
            Paragraph(text, indent: 14);
            Y += 2;
        }

        public void BidHeader(string title, string meta, bool best)
        {
            Ensure(70);
            Y += 4;
            var top = Y;
            Gfx.DrawRoundedRectangle(new XPen(best ? SoftLine : Line, 0.8), new XSolidBrush(best ? Soft : HeadFill), Margin, top, Inner, 30, 9, 9);
            if (best) Gfx.DrawRectangle(new XSolidBrush(Brand), Margin, top + 6, 3, 18);
            var font = new XFont("Arial", 11, XFontStyleEx.Bold);
            var metaFont = new XFont("Arial", 9);
            var metaWidth = Gfx.MeasureString(meta, metaFont).Width;
            var titleWidth = Inner - metaWidth - (best ? 90 : 40);
            var shown = Fit(Gfx, title, font, titleWidth);
            Gfx.DrawString(shown, font, new XSolidBrush(Navy), Margin + 14, top + 19.5);
            if (best) Pill(Gfx, "AI PICK", Margin + 22 + Gfx.MeasureString(shown, font).Width, top + 7.5, Brand, XColors.White);
            Gfx.DrawString(meta, metaFont, new XSolidBrush(Muted), new XRect(Margin, top + 9, Inner - 14, 14), XStringFormats.TopRight);
            Y = top + 44;
        }

        public void Bullets(IEnumerable<string> items, XColor dot, double indent = 0)
        {
            var font = new XFont("Arial", 10);
            var brush = new XSolidBrush(Navy);
            foreach (var item in items)
            {
                var first = true;
                foreach (var line in Wrap(item, font, Inner - indent - 16))
                {
                    Ensure(14.5);
                    if (first) Gfx.DrawEllipse(new XSolidBrush(dot), Margin + indent + 2, Y - 6.5, 5, 5);
                    Gfx.DrawString(line, font, brush, Margin + indent + 16, Y);
                    Y += 14.5;
                    first = false;
                }
                Y += 3;
            }
        }

        public void Table(string[] headers, double[] widths, bool[] numeric, IReadOnlyList<string[]> rows, IReadOnlyList<bool> highlight)
        {
            var total = Inner;
            var head = new XFont("Arial", 7.5, XFontStyleEx.Bold);
            var cell = new XFont("Arial", 9);
            var strong = new XFont("Arial", 9, XFontStyleEx.Bold);
            const double rowHeight = 22;
            void Row(string[] values, XFont font, XBrush? fill, XColor ink, bool isHeader)
            {
                Ensure(rowHeight + 2);
                if (fill is not null) Gfx.DrawRectangle(fill, Margin, Y, total, rowHeight);
                var x = Margin;
                for (var i = 0; i < values.Length; i++)
                {
                    var width = total * widths[i];
                    var text = Fit(Gfx, isHeader ? values[i].ToUpperInvariant() : values[i], font, width - 12);
                    var rect = new XRect(x + 6, Y, width - 12, rowHeight);
                    Gfx.DrawString(text, font, new XSolidBrush(ink), rect, numeric[i] ? XStringFormats.CenterRight : XStringFormats.CenterLeft);
                    x += width;
                }
                Gfx.DrawLine(new XPen(Line, 0.7), Margin, Y + rowHeight, Margin + total, Y + rowHeight);
                Y += rowHeight;
            }
            Row(headers, head, new XSolidBrush(HeadFill), Muted, true);
            for (var r = 0; r < rows.Count; r++)
                Row(rows[r], highlight[r] ? strong : cell,
                    highlight[r] ? new XSolidBrush(Soft) : r % 2 == 1 ? new XSolidBrush(XColor.FromArgb(250, 252, 253)) : null, Navy, false);
            Y += 8;
        }

        public List<string> WrapLines(string value, XFont font, double width) => Wrap(value, font, width).ToList();

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
                if (line.Length > 0) yield return line;
            }
        }

        public void Dispose() => Gfx.Dispose();
    }
}
