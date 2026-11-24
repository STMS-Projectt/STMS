using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Memory;
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
public class AiIntelligenceController(AiInsights insights, AdminAiAssistant adminAssistant, UiText text, IMemoryCache cache) : Controller
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

    // GET /AiIntelligence/Chat  -> the AI assistant (chatbot) page, opened from the sidebar.
    public IActionResult Chat([FromServices] QwenChatbot chatbot)
    {
        ViewBag.ModelName = chatbot.ModelName;
        ViewBag.Local = chatbot.Local;
        return View();
    }

    // POST /AiIntelligence/Ask  (used by the chatbot)
    [HttpPost]
    [RequestSizeLimit(5 * 1024 * 1024)]
    // history: the last chat messages as JSON [{ "role": "user"|"assistant", "content": "..." }] for follow-up questions.
    public async Task<IActionResult> Ask(string? question, IFormFile? file, string? history, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(question) && file is null)
            return BadRequest(new { answer = text.T("Please write a question or attach a file.") });
        if (question?.Length > 2000)
            return BadRequest(new { answer = text.T("The question is too long (max 2,000 characters).") });
        if (file is not null && !new[] { ".csv", ".txt", ".xlsx", ".pdf" }.Contains(Path.GetExtension(file.FileName).ToLowerInvariant()))
            return BadRequest(new { answer = text.T("Only Excel (.xlsx, .csv) and PDF files are supported.") });

        // Admin asking to create a department / category / employee: the AI suggests it, the admin confirms it.
        if (User.IsInRole(Roles.Admin) && file is null && AdminAiAssistant.DetectAction(question ?? "") is not null &&
            await adminAssistant.GenerateAsync(question!, cancellationToken) is var (action, message))
        {
            action.Url = action.Action switch
            {
                "create_department" => Url.Action("Create", "Departments")!,
                "create_category" => Url.Action("Create", "Categories")!,
                _ => Url.Action("Create", "Employees")!
            };
            return Json(new AiAnswer { Answer = message, Action = action, UsedAiModel = true });
        }

        var data = await insights.LoadAsync(User);
        var answer = await insights.AskAsync(data, question ?? "", file, text.Arabic, ReadHistory(history), cancellationToken);

        // Keep the answer for one hour so the user can download it as Excel or PDF. Only the same user can download it.
        answer.Id = Guid.NewGuid().ToString("N");
        cache.Set(AnswerKey(answer.Id), (Question: question ?? file?.FileName ?? "", Answer: answer), TimeSpan.FromHours(1));
        return Json(answer);
    }

    // GET /AiIntelligence/DownloadAnswer/{id}?format=excel|pdf  -> one chatbot answer as a report file.
    public async Task<IActionResult> DownloadAnswer(string id, string format, [FromServices] ProtectedPdf pdf)
    {
        if (!cache.TryGetValue(AnswerKey(id), out (string Question, AiAnswer Answer) saved)) return NotFound();
        var answer = saved.Answer;
        var section = new ReportSection
        {
            Title = "AI assistant answer",
            Description = answer.Answer,
            NotesTitle = "AI answer and recommendations (advice only)",
            Notes = [answer.Answer, .. answer.Recommendations]
        };
        section.Kpis.AddRange(answer.Cards.Select(c => new ReportKpi { Label = c.Label, Value = c.Value }));
        if (answer.Table is not null) section.Tables.Add(Table("Answer table", answer.Table, "No rows."));
        if (answer.Chart is not null)
            section.Tables.Add(Table(answer.Chart.Title is { Length: > 0 } t ? t : "Chart data",
                new AiTable(["Item", "Value"], answer.Chart.Labels.Select((l, i) => new List<string> { l, answer.Chart.Values[i].ToString("N2") }).ToList()), "No data."));

        // Excel has no notes area, so the answer text and recommendations get their own sheet.
        if (format == "excel")
            section.Tables.Insert(0, Table("AI answer", new AiTable(["Text"], section.Notes.Select(n => new List<string> { n }).ToList()), "-"));

        var scope = text.F("Question: {0}", saved.Question.Length > 200 ? saved.Question[..200] + "…" : saved.Question);
        return format == "excel"
            ? ReportExport.ExcelFile(section, scope, text, "ai-answer")
            : await ReportExport.PdfFileAsync(this, pdf, section, scope, "ai-answer", "AI INTELLIGENCE");
    }

    private string AnswerKey(string id) => $"ai-answer:{User.FindFirstValue(ClaimTypes.NameIdentifier)}:{id}";

    private static List<AiChatTurn> ReadHistory(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 20000) return [];
        try
        {
            return (JsonSerializer.Deserialize<List<AiChatTurn>>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [])
                .Where(t => t is { Role: "user" or "assistant", Content.Length: > 0 }).TakeLast(6).ToList();
        }
        catch (JsonException) { return []; }
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

    }

    private static ReportTable Table(string title, AiTable source, string empty)
    {
        var table = new ReportTable { Title = title, Headers = source.Columns.ToArray(), Empty = empty };
        foreach (var row in source.Rows) table.Add(null, row.Cast<object?>().ToArray());
        return table;
    }
}
