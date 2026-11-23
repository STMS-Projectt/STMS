using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SourcingTenderSystem.ViewModels;
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
    // Same template and watermark as the Reports PDFs (navy header band, KPI tiles, tables).
    public async Task<IActionResult> ExportPdf(int? tenderId, [FromServices] ProtectedPdf pdf)
    {
        var data = await insights.LoadAsync(User);
        var tender = data.Tenders.FirstOrDefault(t => t.Id == tenderId);

        var section = new ReportSection
        {
            Title = tender is null ? "AI Intelligence - Tenders report" : $"AI Intelligence - Tender #{tender.Id} {tender.Title}",
            NotesTitle = "AI recommendations (advice only)",
            Notes = tender is null ? AiInsights.Recommendations(data) : AiInsights.TenderRecommendations(tender, data)
        };
        var cards = tender is null ? AiInsights.Kpis(data) :
        [
            new("Status", tender.Status), new("Bids", tender.BidCount.ToString()),
            new("Lowest", AiInsights.Money(tender.Lowest, tender.Currency)), new("Highest", AiInsights.Money(tender.Highest, tender.Currency)),
            new("Winner", tender.Winner ?? "-")
        ];
        section.Kpis.AddRange(cards.Select(c => new ReportKpi { Label = c.Label, Value = c.Value }));

        if (tender is null)
        {
            section.Tables.Add(Table("Tenders", AiInsights.TendersTable(data.Tenders), "No tenders to show."));
            section.Tables.Add(Table("Suppliers", AiInsights.SuppliersTable(data), "No supplier bids to show."));
        }
        else
            section.Tables.Add(Table("Bids", AiInsights.BidsTable(data.Bids.Where(b => b.TenderId == tender.Id)), "No visible bids for this tender."));

        var scope = tender is null ? $"All tenders you can see ({data.Tenders.Count})" : $"Tender #{tender.Id}";
        return await ReportExport.PdfFileAsync(this, pdf, section, scope,
            tender is null ? "ai-tenders-report" : $"ai-tender-{tender.Id}-report", "AI INTELLIGENCE");

        static ReportTable Table(string title, AiTable source, string empty)
        {
            var table = new ReportTable { Title = title, Headers = source.Columns.ToArray(), Empty = empty };
            foreach (var row in source.Rows) table.Add(null, row.Cast<object?>().ToArray());
            return table;
        }
    }
}
