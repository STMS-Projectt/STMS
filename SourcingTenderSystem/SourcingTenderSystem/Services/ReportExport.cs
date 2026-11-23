using System.Globalization;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using SourcingTenderSystem.ViewModels;

namespace SourcingTenderSystem.Services;

// Writes a report section (KPIs + tables) as an Excel workbook (ClosedXML) or a PDF document (PDFsharp).
// The page, the Excel file and the PDF all use the same ReportSection, so they always match.
public static class ReportExport
{
    public const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static FileContentResult ExcelFile(ReportSection section, string filters, UiText text, string fileName)
    {
        var bytes = Excel(section, filters, text);
        var file = new FileContentResult(bytes, ExcelContentType);
        file.FileDownloadName = fileName + ".xlsx";
        return file;
    }

    // PDFs get the same watermark (user + time) as every other downloaded document in the system.
    public static async Task<FileContentResult> PdfFileAsync(Controller controller, ProtectedPdf pdf, ReportSection section,
        string filters, string fileName, string kicker = "REPORTS")
    {
        byte[] bytes;
        using (var document = Pdf(section, filters, kicker))
        {
            pdf.AddWatermark(document, controller.User, DateTime.UtcNow);
            bytes = await pdf.Render(document, null);
        }
        controller.Response.Headers.CacheControl = "private, no-store";
        var file = new FileContentResult(bytes, "application/pdf");
        file.FileDownloadName = fileName + ".pdf";
        return file;
    }

    // ------------------------------------------------------------ Excel (ClosedXML)

    public static byte[] Excel(ReportSection section, string filters, UiText text)
    {
        using (var book = new XLWorkbook())
        {
            var usedNames = new List<string>();

            // First sheet: title, filters and the KPIs.
            var summary = book.Worksheets.Add(SheetName(text.T("Summary"), usedNames));
            summary.Cell(1, 1).Value = section.Title;
            summary.Cell(1, 1).Style.Font.Bold = true;
            summary.Cell(1, 1).Style.Font.FontSize = 14;
            summary.Cell(2, 1).Value = filters;
            summary.Cell(3, 1).Value = text.F("Generated {0} UTC", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
            var row = 5;
            foreach (var kpi in section.Kpis)
            {
                summary.Cell(row, 1).Value = kpi.Label;
                summary.Cell(row, 1).Style.Font.Bold = true;
                summary.Cell(row, 2).Value = kpi.Value;
                row++;
            }
            summary.Column(1).Width = 42;
            summary.Column(2).Width = 24;
            if (text.Arabic) summary.RightToLeft = true;

            // One sheet for each table.
            foreach (var table in section.Tables)
            {
                AddTableSheet(book, table, text, usedNames);
            }

            using (var stream = new MemoryStream())
            {
                book.SaveAs(stream);
                return stream.ToArray();
            }
        }
    }

    private static void AddTableSheet(XLWorkbook book, ReportTable table, UiText text, List<string> usedNames)
    {
        var sheet = book.Worksheets.Add(SheetName(table.Title, usedNames));
        sheet.Cell(1, 1).Value = table.Title;
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 13;
        if (table.Note != null) sheet.Cell(2, 1).Value = table.Note;

        // Column headers on row 4, data from row 5.
        var headerRow = 4;
        for (var c = 0; c < table.Headers.Length; c++)
        {
            var cell = sheet.Cell(headerRow, c + 1);
            cell.Value = table.Headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#132E43");
        }
        for (var r = 0; r < table.Rows.Count; r++)
        {
            for (var c = 0; c < table.Rows[r].Length; c++)
            {
                WriteCell(sheet.Cell(headerRow + 1 + r, c + 1), table.Rows[r][c]);
            }
        }

        if (table.Rows.Count == 0)
            sheet.Cell(headerRow + 1, 1).Value = table.Empty;
        else
            sheet.Range(headerRow, 1, headerRow + table.Rows.Count, table.Headers.Length).SetAutoFilter();

        sheet.SheetView.FreezeRows(headerRow);

        // Column width = longest text in the column (between 10 and 60 characters).
        for (var c = 0; c < table.Headers.Length; c++)
        {
            var longest = table.Headers[c].Length;
            foreach (var cells in table.Rows)
            {
                var length = ReportRules.Cell(cells[c], CultureInfo.InvariantCulture).Length;
                if (length > longest) longest = length;
            }
            sheet.Column(c + 1).Width = Math.Clamp(longest + 3, 10, 60);
        }
        if (text.Arabic) sheet.RightToLeft = true;
    }

    // Numbers and dates are written as real Excel values, so they can be sorted and summed in Excel.
    private static void WriteCell(IXLCell cell, object? value)
    {
        if (value == null)
        {
            cell.Value = "—";
        }
        else if (value is int)
        {
            cell.Value = Convert.ToDouble(value);
            cell.Style.NumberFormat.Format = "#,##0";
        }
        else if (value is decimal)
        {
            cell.Value = Convert.ToDouble(value);
            cell.Style.NumberFormat.Format = "#,##0.00";
        }
        else if (value is double)
        {
            cell.Value = (double)value;
            cell.Style.NumberFormat.Format = "0.0";
        }
        else if (value is DateTime)
        {
            cell.Value = (DateTime)value;
            cell.Style.DateFormat.Format = "yyyy-mm-dd";
        }
        else
        {
            cell.Value = value.ToString() ?? "";
        }
    }

    // Excel sheet names: at most 31 characters, unique, and without the characters []:*?/\'
    private static string SheetName(string title, List<string> usedNames)
    {
        var clean = "";
        foreach (var ch in title)
        {
            if ("[]:*?/\\'".IndexOf(ch) < 0) clean += ch;
        }
        clean = clean.Trim();
        if (clean.Length == 0) clean = "Sheet";
        if (clean.Length > 27) clean = clean.Substring(0, 27).Trim();

        var name = clean;
        var number = 2;
        while (usedNames.Contains(name.ToLower()))
        {
            name = clean + " " + number;
            number++;
        }
        usedNames.Add(name.ToLower());
        return name;
    }

    // ------------------------------------------------------------ PDF (PDFsharp)

    private static readonly XColor Navy = XColor.FromArgb(19, 46, 67);
    private static readonly XColor Muted = XColor.FromArgb(86, 101, 122);
    private static readonly XColor Soft = XColor.FromArgb(238, 248, 246);
    private static readonly XColor Stripe = XColor.FromArgb(245, 248, 251);
    private static readonly XColor LineColor = XColor.FromArgb(226, 232, 240);
    private const double Margin = 36;

    public static PdfDocument Pdf(ReportSection section, string filters, string kicker = "REPORTS")
    {
        var document = new PdfDocument();
        document.Info.Title = section.Title;
        var w = new PdfWriter(document);
        var fullWidth = w.Width - 2 * Margin;

        // Header band
        w.Gfx.DrawRectangle(new XSolidBrush(Navy), 0, 0, w.Width, 92);
        w.Text("SOURCING DESK  |  " + kicker, new XFont("Arial", 9), XBrushes.White, Margin, 22, fullWidth);
        w.Text(section.Title, new XFont("Arial", 20, XFontStyleEx.Bold), XBrushes.White, Margin, 38, fullWidth);
        var generated = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        w.Text(filters + "  |  Generated " + generated + " UTC", new XFont("Arial", 9), XBrushes.White, Margin, 68, fullWidth);
        w.Y = 110;

        // KPI boxes, four on each row
        var perRow = 4;
        var gap = 10.0;
        var boxHeight = 46.0;
        var boxWidth = (fullWidth - gap * (perRow - 1)) / perRow;
        for (var i = 0; i < section.Kpis.Count; i++)
        {
            if (i % perRow == 0)
            {
                if (i > 0) w.Y += boxHeight + gap;
                w.Ensure(boxHeight + gap);
            }
            var x = Margin + (i % perRow) * (boxWidth + gap);
            var kpi = section.Kpis[i];
            w.Gfx.DrawRectangle(new XSolidBrush(Soft), x, w.Y, boxWidth, boxHeight);
            w.Text(kpi.Value, new XFont("Arial", 14, XFontStyleEx.Bold), new XSolidBrush(Navy), x + 8, w.Y + 7, boxWidth - 16);
            w.Text(kpi.Label, new XFont("Arial", 8.5), new XSolidBrush(Muted), x + 8, w.Y + 28, boxWidth - 16);
        }
        if (section.Kpis.Count > 0) w.Y += boxHeight + 14;

        foreach (var table in section.Tables)
        {
            DrawTable(w, table);
        }

        if (section.Notes.Count > 0) DrawNotes(w, section.NotesTitle ?? "Notes", section.Notes);

        w.Close();
        return document;
    }

    private static void DrawTable(PdfWriter w, ReportTable table)
    {
        var total = w.Width - 2 * Margin;
        var cellFont = new XFont("Arial", 8.5);
        var ink = new XSolidBrush(Navy);

        // Table title and note
        w.Ensure(70);
        w.Y += 8;
        w.Text(table.Title, new XFont("Arial", 12, XFontStyleEx.Bold), ink, Margin, w.Y, total);
        w.Y += 18;
        w.Gfx.DrawLine(new XPen(LineColor, 1), Margin, w.Y, Margin + total, w.Y);
        w.Y += 6;
        if (table.Note != null)
        {
            w.Text(table.Note, new XFont("Arial", 8.5, XFontStyleEx.Italic), new XSolidBrush(Muted), Margin, w.Y, total);
            w.Y += 14;
        }
        if (table.Rows.Count == 0)
        {
            w.Text(table.Empty, cellFont, new XSolidBrush(Muted), Margin, w.Y, total);
            w.Y += 20;
            return;
        }

        var widths = ColumnWidths(table, total);
        DrawHeaderRow(w, table, widths);
        for (var r = 0; r < table.Rows.Count; r++)
        {
            // When a new page starts, the column headers are drawn again.
            if (w.Ensure(16)) DrawHeaderRow(w, table, widths);
            if (r % 2 == 1) w.Gfx.DrawRectangle(new XSolidBrush(Stripe), Margin, w.Y, total, 16);

            var x = Margin;
            for (var c = 0; c < table.Headers.Length; c++)
            {
                object? value = null;
                if (c < table.Rows[r].Length) value = table.Rows[r][c];
                w.Text(ReportRules.Cell(value, CultureInfo.InvariantCulture), cellFont, ink, x + 4, w.Y + 3, widths[c] - 8);
                x += widths[c];
            }
            w.Y += 16;
        }
        w.Y += 10;
    }

    // Bulleted points that wrap over several lines (a table cell would cut them).
    private static void DrawNotes(PdfWriter w, string title, IReadOnlyList<string> notes)
    {
        var total = w.Width - 2 * Margin;
        var ink = new XSolidBrush(Navy);
        var font = new XFont("Arial", 9.5);
        w.Ensure(60);
        w.Y += 8;
        w.Text(title, new XFont("Arial", 12, XFontStyleEx.Bold), ink, Margin, w.Y, total);
        w.Y += 18;
        w.Gfx.DrawLine(new XPen(LineColor, 1), Margin, w.Y, Margin + total, w.Y);
        w.Y += 8;
        foreach (var note in notes)
        {
            var lines = w.Wrap(note, font, total - 30);
            var height = lines.Count * 14 + 12;
            w.Ensure(height + 6);
            w.Gfx.DrawRectangle(new XSolidBrush(Soft), Margin, w.Y, total, height);
            w.Gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(8, 118, 108)), Margin, w.Y, 3, height);
            var y = w.Y + 6;
            foreach (var line in lines)
            {
                w.Text(line, font, ink, Margin + 16, y, total - 24);
                y += 14;
            }
            w.Y += height + 6;
        }
    }

    private static void DrawHeaderRow(PdfWriter w, ReportTable table, double[] widths)
    {
        var total = w.Width - 2 * Margin;
        var headFont = new XFont("Arial", 8.5, XFontStyleEx.Bold);
        w.Gfx.DrawRectangle(new XSolidBrush(Navy), Margin, w.Y, total, 18);
        var x = Margin;
        for (var c = 0; c < table.Headers.Length; c++)
        {
            w.Text(table.Headers[c], headFont, XBrushes.White, x + 4, w.Y + 4, widths[c] - 8);
            x += widths[c];
        }
        w.Y += 18;
    }

    // Longer content gets a wider column (each column counts between 6 and 40 characters).
    private static double[] ColumnWidths(ReportTable table, double total)
    {
        var weights = new double[table.Headers.Length];
        double sum = 0;
        for (var c = 0; c < weights.Length; c++)
        {
            var longest = table.Headers[c].Length;
            foreach (var row in table.Rows.Take(200))
            {
                if (c < row.Length)
                {
                    var length = ReportRules.Cell(row[c], CultureInfo.InvariantCulture).Length;
                    if (length > longest) longest = length;
                }
            }
            weights[c] = Math.Clamp(longest, 6, 40);
            sum += weights[c];
        }

        var widths = new double[weights.Length];
        for (var c = 0; c < weights.Length; c++)
        {
            widths[c] = total * weights[c] / sum;
        }
        return widths;
    }

    // Keeps the current page, the drawing surface and the Y position. Adds a new landscape A4 page when needed.
    private class PdfWriter
    {
        private readonly PdfDocument _document;
        private PdfPage _page;

        public XGraphics Gfx { get; private set; }
        public double Y { get; set; }
        public double Width { get { return _page.Width.Point; } }

        public PdfWriter(PdfDocument document)
        {
            _document = document;
            _page = NewPage();
            Gfx = XGraphics.FromPdfPage(_page);
        }

        private PdfPage NewPage()
        {
            var page = _document.AddPage();
            page.Size = PdfSharp.PageSize.A4;
            page.Orientation = PdfSharp.PageOrientation.Landscape;
            return page;
        }

        // Returns true when there was no room left and a new page was started.
        public bool Ensure(double height)
        {
            var bottom = _page.Height.Point - 44;
            if (Y + height <= bottom) return false;
            Gfx.Dispose();
            _page = NewPage();
            Gfx = XGraphics.FromPdfPage(_page);
            Y = 40;
            return true;
        }

        // Draws one line of text inside a box, starting at the left like its column header. Long text is cut with "…".
        public void Text(string value, XFont font, XBrush brush, double x, double y, double width)
        {
            var text = "";
            foreach (var ch in value)
            {
                if (!char.IsControl(ch)) text += ch;
            }
            if (Gfx.MeasureString(text, font).Width > width)
            {
                while (text.Length > 1 && Gfx.MeasureString(text + "…", font).Width > width)
                {
                    text = text.Substring(0, text.Length - 1);
                }
                text += "…";
            }
            Gfx.DrawString(text, font, brush, new XRect(x, y, width, font.Size + 4), XStringFormats.TopLeft);
        }

        // Splits text into lines that fit the width (very long words are cut).
        public List<string> Wrap(string value, XFont font, double width)
        {
            var lines = new List<string>();
            var line = "";
            foreach (var word in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && Gfx.MeasureString(candidate, font).Width > width)
                {
                    lines.Add(line);
                    line = word;
                }
                else line = candidate;
            }
            if (line.Length > 0) lines.Add(line);
            return lines;
        }

        public void Close()
        {
            Gfx.Dispose();
        }
    }
}
