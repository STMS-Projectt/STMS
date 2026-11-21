using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using SourcingTenderSystem.Security;
using SourcingTenderSystem.Services;

namespace SourcingTenderSystem.Controllers;

/// <summary>
/// AI Intelligence page: an interactive dashboard + AI chatbot.
/// Open to every employee role, but NOT to suppliers.
/// Each user only sees the tenders they are allowed to see (see AiInsights.LoadAsync).
/// </summary>
[Authorize(Roles = StaffRoles)]
public class AiIntelligenceController(AiInsights insights, UiText text) : Controller
{
    public const string StaffRoles = Roles.Admin + ",RequestingDepartment," + Roles.SourcingOfficer + "," +
        Roles.EvaluationCommittee + "," + Roles.SourcingManager + "," + Roles.TechnicalCommittee + "," +
        Roles.FinancialCommittee + "," + Roles.TopCommittee;

    // GET /AiIntelligence?tenderId=5
    public async Task<IActionResult> Index(int? tenderId)
    {
        var data = await insights.LoadAsync(User);
        ViewBag.Selected = data.Tenders.FirstOrDefault(t => t.Id == tenderId);
        return View(data);
    }

    // POST /AiIntelligence/Ask  (used by the chatbot)
    [HttpPost]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> Ask(string? question, IFormFile? file)
    {
        if (string.IsNullOrWhiteSpace(question) && file is null)
            return BadRequest(new { answer = text.T("Please write a question or attach a file.") });
        if (file is not null && !new[] { ".csv", ".txt", ".xlsx", ".pdf" }.Contains(Path.GetExtension(file.FileName).ToLowerInvariant()))
            return BadRequest(new { answer = text.T("Only Excel (.xlsx, .csv) and PDF files are supported.") });

        var data = await insights.LoadAsync(User);
        var answer = await insights.AskAsync(data, question ?? "", file, text.Arabic);
        return Json(answer);
    }

    // GET /AiIntelligence/ExportExcel?tenderId=5  -> a CSV file that opens in Excel
    public async Task<IActionResult> ExportExcel(int? tenderId)
    {
        var data = await insights.LoadAsync(User);
        var table = tenderId is int id
            ? AiInsights.BidsTable(data.Bids.Where(b => b.TenderId == id))
            : AiInsights.TendersTable(data.Tenders);

        var csv = new StringBuilder();
        csv.AppendLine(string.Join(",", table.Columns.Select(Cell)));
        foreach (var row in table.Rows) csv.AppendLine(string.Join(",", row.Select(Cell)));
        // The UTF-8 BOM makes Excel show Arabic text correctly.
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        return File(bytes, "text/csv", tenderId is null ? "ai-tenders-report.csv" : $"ai-tender-{tenderId}-report.csv");

        static string Cell(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    // GET /AiIntelligence/ExportPdf?tenderId=5
    public async Task<IActionResult> ExportPdf(int? tenderId)
    {
        var data = await insights.LoadAsync(User);
        var tender = data.Tenders.FirstOrDefault(t => t.Id == tenderId);

        var document = new PdfDocument();
        var page = document.AddPage();
        var gfx = XGraphics.FromPdfPage(page);
        var title = new XFont("Arial", 18, XFontStyleEx.Bold);
        var bold = new XFont("Arial", 10, XFontStyleEx.Bold);
        var normal = new XFont("Arial", 9);
        double y = 50;

        void Line(string value, XFont font, double gap = 16)
        {
            if (y > page.Height.Point - 50)   // new page when this one is full
            {
                gfx.Dispose();
                page = document.AddPage();
                gfx = XGraphics.FromPdfPage(page);
                y = 50;
            }
            gfx.DrawString(value.Length > 110 ? value[..110] + "..." : value, font, XBrushes.Black, 40, y);
            y += gap;
        }

        Line(tender is null ? "AI Intelligence - Tenders report" : $"AI Intelligence - Tender #{tender.Id} {tender.Title}", title, 30);
        Line($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}", normal, 24);

        Line("Key figures", bold);
        var cards = tender is null ? AiInsights.Kpis(data) :
        [
            new("Status", tender.Status), new("Bids", tender.BidCount.ToString()),
            new("Lowest", AiInsights.Money(tender.Lowest, tender.Currency)), new("Highest", AiInsights.Money(tender.Highest, tender.Currency)),
            new("Winner", tender.Winner ?? "-")
        ];
        foreach (var card in cards) Line($"- {card.Label}: {card.Value}", normal);
        y += 10;

        var table = tender is null ? AiInsights.TendersTable(data.Tenders) : AiInsights.BidsTable(data.Bids.Where(b => b.TenderId == tender.Id));
        Line(tender is null ? "Tenders" : "Bids", bold);
        Line(string.Join("  |  ", table.Columns), bold);
        foreach (var row in table.Rows) Line(string.Join("  |  ", row), normal, 14);
        y += 10;

        Line("AI recommendations (advice only)", bold);
        foreach (var r in AiInsights.Recommendations(data)) Line("- " + r, normal);

        gfx.Dispose();
        using var output = new MemoryStream();
        document.Save(output);
        return File(output.ToArray(), "application/pdf", tenderId is null ? "ai-tenders-report.pdf" : $"ai-tender-{tenderId}-report.pdf");
    }
}
