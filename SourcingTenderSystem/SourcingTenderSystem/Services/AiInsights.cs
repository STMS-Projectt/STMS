using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SourcingTenderSystem.Data;
using SourcingTenderSystem.Security;

namespace SourcingTenderSystem.Services;

// ------------------------------------------------------------------
// Simple data shapes used by the AI Intelligence page and the chatbot
// ------------------------------------------------------------------

public record AiTenderRow(int Id, string Title, string Status, string Department, string Category,
    DateTime CreatedAt, DateTime CloseDate, int BidCount, decimal? Lowest, decimal? Highest, decimal? Average,
    string Currency, string? LowestSupplier, string? HighestSupplier, string? Winner, double? DaysToAward);

public record AiBidRow(int TenderId, string TenderTitle, string Supplier, decimal Price, string Currency,
    int DeliveryDays, int ValidityDays, string? Outcome);

/// <summary>All the data one user is allowed to see. Everything on the page is built from this.</summary>
public record AiData(List<AiTenderRow> Tenders, List<AiBidRow> Bids);

public record AiCard(string Label, string Value);
public record AiTable(List<string> Columns, List<List<string>> Rows);
// Kind: "bars" (compare items) or "columns" (values over time).
public record AiChart(string Title, List<string> Labels, List<decimal> Values, string Kind = "bars");

/// <summary>What the chatbot sends back to the browser: text + optional cards, table and chart.</summary>
public class AiAnswer
{
    public string Answer { get; set; } = "";
    public List<AiCard> Cards { get; set; } = [];
    public AiTable? Table { get; set; }
    public AiChart? Chart { get; set; }
    public List<string> Recommendations { get; set; } = [];
    public bool UsedAiModel { get; set; }
    // Id used to download this answer as Excel or PDF (the answer is kept on the server for a while).
    public string? Id { get; set; }
}

/// <summary>
/// AI Intelligence service.
/// 1) Loads the REAL tenders and bids from the database, but only the ones the current user is allowed to see.
/// 2) Calculates numbers (lowest / highest price, KPIs ...) in C# so they are always correct.
/// 3) The chatbot sends the question + those numbers to the Qwen model (see QwenChatbot.GenerateAsync).
///    If Qwen is not configured or not reachable, a simple rule based answer is used.
/// The AI only gives advice. It never changes data or takes decisions.
/// </summary>
public class AiInsights(ApplicationDbContext db, QwenChatbot chatbot)
{
    // While a tender is still open, bid prices are sealed and must not be shown to anyone.
    private static readonly string[] SealedStatuses = ["Draft", "Published", "Open"];

    // ============================================================ 1) Load data (with permissions)

    public async Task<AiData> LoadAsync(ClaimsPrincipal user)
    {
        var tenders = db.Tenders.AsNoTracking();

        // Admin, sourcing officer, sourcing manager and top committee can see every tender.
        var seesAll = user.IsInRole(Roles.Admin) || user.IsInRole(Roles.SourcingOfficer) ||
                      user.IsInRole(Roles.SourcingManager) || user.IsInRole(Roles.TopCommittee);
        if (!seesAll)
        {
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            var employee = await db.EmployeeProfiles.AsNoTracking().Where(e => e.UserId == userId)
                .Select(e => new { e.Id, e.DepartmentId }).SingleOrDefaultAsync();
            if (employee is null) return new AiData([], []);

            var isDepartment = user.IsInRole("RequestingDepartment");
            // Department employees: tenders of their own department.
            // Committee members: tenders they are a member of.
            tenders = tenders.Where(t =>
                (isDepartment && t.PurchaseRequisition.DepartmentId == employee.DepartmentId) ||
                t.CommitteeMembers.Any(m => m.EmployeeProfileId == employee.Id));
        }

        var tenderList = await tenders.OrderByDescending(t => t.CreatedAt).Select(t => new
        {
            t.Id, t.Title, t.Status, t.CreatedAt, t.CloseDate,
            Department = t.PurchaseRequisition.Department.Name,
            Category = t.PurchaseRequisition.Category != null ? t.PurchaseRequisition.Category.Name : "-",
            AwardedAt = t.EvaluationReport != null ? t.EvaluationReport.TenderApprovals.Max(a => (DateTime?)a.DecidedAt) : null
        }).ToListAsync();

        var ids = tenderList.Select(t => t.Id).ToList();
        var bids = await db.Bids.AsNoTracking()
            .Where(b => ids.Contains(b.TenderId) && b.SubmittedAt != null &&
                        b.Status != "Withdrawn" && b.Status != "Draft" &&
                        !SealedStatuses.Contains(b.Tender.Status))
            .Select(b => new AiBidRow(b.TenderId, b.Tender.Title, b.SupplierProfile.CompanyName, b.TotalPrice,
                b.Currency, b.DeliveryDays, b.ValidityDays, b.AwardOutcome))
            .ToListAsync();

        var rows = new List<AiTenderRow>();
        foreach (var t in tenderList)
        {
            var tb = bids.Where(b => b.TenderId == t.Id).ToList();
            var low = tb.OrderBy(b => b.Price).FirstOrDefault();
            var high = tb.OrderByDescending(b => b.Price).FirstOrDefault();
            var winner = tb.FirstOrDefault(b => b.Outcome == "Awarded")?.Supplier;
            double? days = t.Status == "Awarded" && t.AwardedAt is DateTime at ? Math.Round((at - t.CreatedAt).TotalDays, 1) : null;
            rows.Add(new AiTenderRow(t.Id, t.Title, t.Status, t.Department, t.Category, t.CreatedAt, t.CloseDate,
                tb.Count, low?.Price, high?.Price, tb.Count > 0 ? Math.Round(tb.Average(b => b.Price), 2) : null,
                low?.Currency ?? "JOD", low?.Supplier, high?.Supplier, winner, days));
        }
        return new AiData(rows, bids);
    }

    // ============================================================ 2) Calculations used by the dashboard

    public static List<AiCard> Kpis(AiData data)
    {
        var awarded = data.Tenders.Where(t => t.DaysToAward != null).ToList();
        var withBids = data.Tenders.Where(t => t.BidCount > 0).ToList();
        return
        [
            new("Total tenders", data.Tenders.Count.ToString()),
            new("Open tenders", data.Tenders.Count(t => t.Status == "Open").ToString()),
            new("Awarded tenders", data.Tenders.Count(t => t.Status == "Awarded").ToString()),
            new("Total bids", data.Bids.Count.ToString()),
            new("Participating suppliers", data.Bids.Select(b => b.Supplier).Distinct().Count().ToString()),
            new("Average bids per tender", withBids.Count > 0 ? withBids.Average(t => t.BidCount).ToString("0.0") : "-"),
            new("Average days from tender creation to award", awarded.Count > 0 ? awarded.Average(t => t.DaysToAward!.Value).ToString("0.0") : "-")
        ];
    }

    public static AiChart StatusChart(AiData data) => Chart("Tenders by status",
        data.Tenders.GroupBy(t => t.Status).OrderByDescending(g => g.Count()).Select(g => (g.Key, (decimal)g.Count())));

    public static AiChart BidsPerTenderChart(AiData data) => Chart("Bids per tender",
        data.Tenders.Where(t => t.BidCount > 0).Take(8).Select(t => ($"#{t.Id} {t.Title}", (decimal)t.BidCount)));

    public static AiChart SupplierChart(AiData data) => Chart("Supplier participation (number of bids)",
        data.Bids.GroupBy(b => b.Supplier).OrderByDescending(g => g.Count()).Take(8).Select(g => (g.Key, (decimal)g.Count())));

    public static AiChart MonthlyChart(AiData data)
    {
        var start = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1).AddMonths(-5);
        var months = Enumerable.Range(0, 6).Select(i => start.AddMonths(i));
        return Chart("New tenders per month", months.Select(m => (m.ToString("MMM yyyy"),
            (decimal)data.Tenders.Count(t => t.CreatedAt.Year == m.Year && t.CreatedAt.Month == m.Month))));
    }

    public static AiTable TendersTable(IEnumerable<AiTenderRow> tenders) => new(
        ["Tender", "Status", "Department", "Bids", "Lowest", "Highest", "Average", "Winner"],
        tenders.Select(t => new List<string> { $"#{t.Id} {t.Title}", t.Status, t.Department, t.BidCount.ToString(),
            Money(t.Lowest, t.Currency), Money(t.Highest, t.Currency), Money(t.Average, t.Currency), t.Winner ?? "-" }).ToList());

    public static AiTable BidsTable(IEnumerable<AiBidRow> bids) => new(
        ["Supplier", "Price", "Delivery (days)", "Validity (days)", "Result"],
        bids.OrderBy(b => b.Price).Select(b => new List<string> { b.Supplier, Money(b.Price, b.Currency),
            b.DeliveryDays.ToString(), b.ValidityDays.ToString(), b.Outcome ?? "-" }).ToList());

    public static AiTable SuppliersTable(AiData data) => new(
        ["Supplier", "Bids", "Wins", "Lowest offers", "Average delivery (days)"],
        data.Bids.GroupBy(b => b.Supplier).OrderByDescending(g => g.Count()).Select(g => new List<string>
        {
            g.Key, g.Count().ToString(), g.Count(b => b.Outcome == "Awarded").ToString(),
            data.Tenders.Count(t => t.LowestSupplier == g.Key).ToString(), g.Average(b => b.DeliveryDays).ToString("0")
        }).ToList());

    /// <summary>Simple rule based recommendations. They only advise the user.</summary>
    public static List<string> Recommendations(AiData data)
    {
        var list = new List<string>();
        foreach (var t in data.Tenders.Where(t => t.Status is "Closed" or "Evaluation" && t.BidCount == 1).Take(3))
            list.Add($"Tender #{t.Id} \"{t.Title}\" received only one bid. Consider inviting more suppliers next time to get better competition.");
        foreach (var t in data.Tenders.Where(t => t.Lowest > 0 && t.Highest > t.Lowest * 1.3m).Take(3))
            list.Add($"Tender #{t.Id}: the highest price is {(t.Highest / t.Lowest - 1) * 100:0}% above the lowest. Check that all suppliers understood the specifications.");
        var top = data.Bids.Where(b => b.Outcome == "Awarded").GroupBy(b => b.Supplier).OrderByDescending(g => g.Count()).FirstOrDefault();
        if (top is not null && top.Count() >= 2)
            list.Add($"\"{top.Key}\" won {top.Count()} tenders. Consider reviewing supplier diversity to reduce dependency on one supplier.");
        var slow = data.Tenders.Where(t => t.DaysToAward > 30).ToList();
        if (slow.Count > 0)
            list.Add($"{slow.Count} tender(s) took more than 30 days from creation to award. Review the evaluation steps to save time.");
        if (list.Count == 0) list.Add("No risks found in the current data. Keep monitoring the participation of suppliers.");
        return list;
    }

    /// <summary>Recommendations about the bids of ONE tender. Advice only.</summary>
    public static List<string> TenderRecommendations(AiTenderRow t, AiData data)
    {
        var bids = data.Bids.Where(b => b.TenderId == t.Id).OrderBy(b => b.Price).ToList();
        var list = new List<string>();
        if (bids.Count == 0)
        {
            list.Add($"Tender #{t.Id} \"{t.Title}\" has no visible bids yet. Bids stay sealed while the tender is open.");
            return list;
        }

        var cheapest = bids[0];
        var fastest = bids.OrderBy(b => b.DeliveryDays).First();
        var average = bids.Average(b => b.Price);

        if (bids.Count == 1)
            list.Add($"Only one bid was received ({cheapest.Supplier}). There is no competition, so compare the price with the market or previous purchases before deciding.");

        list.Add($"Lowest price: {cheapest.Supplier} with {Money(cheapest.Price, cheapest.Currency)} ({(average > 0 ? (1 - cheapest.Price / average) * 100 : 0):0}% below the average of the bids).");

        if (fastest.Supplier != cheapest.Supplier)
            list.Add($"Fastest delivery: {fastest.Supplier} ({fastest.DeliveryDays} days) vs {cheapest.DeliveryDays} days for the cheapest offer. Choose based on what matters more for this tender: price or time.");
        else
            list.Add($"{cheapest.Supplier} offers both the lowest price and the fastest delivery ({cheapest.DeliveryDays} days). It looks like the best value offer.");

        foreach (var b in bids.Where(b => bids.Count > 1 && b.Price < average * 0.7m))
            list.Add($"{b.Supplier}'s price is much lower than the average. Make sure the offer covers all items and specifications before accepting it.");

        if (t.Lowest > 0 && t.Highest > t.Lowest * 1.3m)
            list.Add($"The highest bid is {(t.Highest / t.Lowest - 1) * 100:0}% above the lowest. A big gap like this may mean the suppliers understood the specifications differently.");

        foreach (var b in bids.Where(b => b.ValidityDays > 0 && b.ValidityDays < 30))
            list.Add($"{b.Supplier}'s offer is valid for only {b.ValidityDays} days. Finish the evaluation before it expires.");

        if (t.Winner is not null)
            list.Add($"This tender was awarded to {t.Winner}.");
        return list;
    }

    // ============================================================ 3) Chatbot

    /// <summary>
    /// Answers one chat message. The real work is done by QwenChatbot.GenerateAsync (prompt + database data + file);
    /// when Qwen is not available, the simple rule based answer below is returned instead.
    /// </summary>
    public async Task<AiAnswer> AskAsync(AiData data, string question, IFormFile? file, bool arabic,
        IReadOnlyList<AiChatTurn>? history = null, CancellationToken cancellationToken = default)
    {
        question = (question ?? "").Trim();
        var fileTable = file is { Length: > 0 } ? ReadFile(file) : null;
        var focus = FindTender(data, question.ToLowerInvariant());

        var answer = await chatbot.GenerateAsync(question, Facts(data, focus), fileTable is null ? null : FileText(file!.FileName, fileTable),
            history ?? [], arabic, cancellationToken);
        if (answer is null) return RuleBasedAnswer(data, question, file, fileTable, focus);

        // Always show what was read from the file, even if the model did not return a table.
        if (fileTable is not null && answer.Table is null && fileTable.Rows.Count > 0) answer.Table = fileTable;
        return answer;
    }

    /// <summary>
    /// The database data sent to the model. Every number is calculated here in C# (lowest, highest, average, rank,
    /// % above the lowest, wins ...) so the model only has to read them, which keeps the answers accurate.
    /// When the question is about one tender, only that tender's details are sent.
    /// </summary>
    public static object Facts(AiData data, AiTenderRow? focus = null)
    {
        var tenders = focus is null ? data.Tenders.Take(150).ToList() : [focus];
        var ids = tenders.Select(t => t.Id).ToHashSet();
        var bids = data.Bids.Where(b => ids.Contains(b.TenderId)).Take(600).ToList();
        var byTender = data.Tenders.ToDictionary(t => t.Id);

        return new
        {
            today = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            note = "Bid prices of Draft/Published/Open tenders are sealed and not included. Prices are in the currency shown.",
            focusTenderId = focus?.Id,
            summary = new
            {
                totalTenders = data.Tenders.Count,
                tendersByStatus = data.Tenders.GroupBy(t => t.Status).ToDictionary(g => g.Key, g => g.Count()),
                totalBids = data.Bids.Count,
                participatingSuppliers = data.Bids.Select(b => b.Supplier).Distinct().Count(),
                averageBidsPerTender = data.Tenders.Any(t => t.BidCount > 0) ? Math.Round(data.Tenders.Where(t => t.BidCount > 0).Average(t => t.BidCount), 1) : (double?)null,
                averageDaysToAward = data.Tenders.Any(t => t.DaysToAward != null) ? Math.Round(data.Tenders.Where(t => t.DaysToAward != null).Average(t => t.DaysToAward!.Value), 1) : (double?)null,
                // Lowest and highest bid overall, per currency (never mix currencies).
                priceExtremesByCurrency = data.Bids.GroupBy(b => b.Currency).Select(g => new
                {
                    currency = g.Key,
                    lowest = g.MinBy(b => b.Price) is { } lo ? new { lo.Price, lo.Supplier, lo.TenderId, lo.TenderTitle } : null,
                    highest = g.MaxBy(b => b.Price) is { } hi ? new { hi.Price, hi.Supplier, hi.TenderId, hi.TenderTitle } : null
                })
            },
            tenders = tenders.Select(t => new
            {
                t.Id, t.Title, t.Status, t.Department, t.Category,
                created = t.CreatedAt.ToString("yyyy-MM-dd"), closes = t.CloseDate.ToString("yyyy-MM-dd"),
                t.BidCount, t.Currency, t.Lowest, t.LowestSupplier, t.Highest, t.HighestSupplier, t.Average,
                spreadPercent = t.Lowest > 0 && t.Highest is decimal h ? Math.Round((h / t.Lowest.Value - 1) * 100, 1) : (decimal?)null,
                t.Winner, t.DaysToAward
            }),
            bids = bids.GroupBy(b => b.TenderId).SelectMany(g =>
            {
                var lowest = g.Min(b => b.Price);
                return g.OrderBy(b => b.Price).Select((b, i) => new
                {
                    b.TenderId, b.TenderTitle, b.Supplier, b.Price, b.Currency, b.DeliveryDays, b.ValidityDays,
                    result = b.Outcome, rankByPrice = i + 1,
                    percentAboveLowest = lowest > 0 ? Math.Round((b.Price / lowest - 1) * 100, 1) : 0
                });
            }),
            suppliers = data.Bids.Where(b => focus is null || b.TenderId == focus.Id).GroupBy(b => b.Supplier)
                .OrderByDescending(g => g.Count()).Take(100).Select(g => new
                {
                    name = g.Key,
                    bids = g.Count(),
                    tendersParticipated = g.Select(b => b.TenderId).Distinct().Count(),
                    wins = g.Count(b => b.Outcome == "Awarded"),
                    lowestOfferIn = g.Count(b => byTender.TryGetValue(b.TenderId, out var t) && t.LowestSupplier == g.Key),
                    averageDeliveryDays = Math.Round(g.Average(b => b.DeliveryDays), 1),
                    participationPercent = data.Tenders.Count(t => t.BidCount > 0) is > 0 and var n
                        ? Math.Round(g.Select(b => b.TenderId).Distinct().Count() * 100m / n, 1) : 0
                })
        };
    }

    // Simple answer used when the Qwen model is not available. Numbers are calculated in C#.
    private static AiAnswer RuleBasedAnswer(AiData data, string question, IFormFile? file, AiTable? fileTable, AiTenderRow? focus)
    {
        var q = question.ToLowerInvariant();
        var answer = new AiAnswer();
        string text;

        if (file is not null && fileTable is not null)
        {
            answer.Table = fileTable;
            answer.Cards = [new("File", file.FileName), new("Rows", fileTable.Rows.Count.ToString()), new("Columns", fileTable.Columns.Count.ToString())];
            answer.Chart = NumberColumnChart(fileTable);
            text = $"I read the file \"{file.FileName}\": {fileTable.Rows.Count} rows and {fileTable.Columns.Count} columns." +
                   (answer.Chart is null ? "" : $" The chart shows the column \"{answer.Chart.Title}\".");
        }
        else if (focus is AiTenderRow t)
        {
            var bids = data.Bids.Where(b => b.TenderId == t.Id).ToList();
            answer.Cards = [new("Status", t.Status), new("Bids", t.BidCount.ToString()), new("Lowest", Money(t.Lowest, t.Currency)),
                new("Highest", Money(t.Highest, t.Currency)), new("Winner", t.Winner ?? "-")];
            answer.Table = BidsTable(bids);
            answer.Chart = Chart($"Bid prices - tender #{t.Id}", bids.OrderBy(b => b.Price).Select(b => (b.Supplier, b.Price)));
            text = bids.Count == 0
                ? $"Tender #{t.Id} \"{t.Title}\" is {t.Status} and has no visible bids yet (bids stay sealed while a tender is open)."
                : $"Tender #{t.Id} \"{t.Title}\" is {t.Status} with {t.BidCount} bids. The lowest offer is {Money(t.Lowest, t.Currency)} from {t.LowestSupplier}" +
                  $" and the highest is {Money(t.Highest, t.Currency)} from {t.HighestSupplier}.";
        }
        else if (Has(q, "lowest", "cheapest", "minimum", "أقل", "ارخص", "أرخص", "اقل"))
        {
            var list = data.Tenders.Where(t => t.Lowest != null).ToList();
            answer.Table = new(["Tender", "Lowest price", "Supplier"], list.Select(t => new List<string> { $"#{t.Id} {t.Title}", Money(t.Lowest, t.Currency), t.LowestSupplier ?? "-" }).ToList());
            answer.Chart = Chart("Lowest price per tender", list.Take(8).Select(t => ($"#{t.Id}", t.Lowest!.Value)));
            text = list.Count == 0 ? "There are no visible bids yet." : $"I listed the lowest offer for {list.Count} tenders.";
        }
        else if (Has(q, "highest", "maximum", "expensive", "أعلى", "اعلى", "أغلى", "اغلى"))
        {
            var list = data.Tenders.Where(t => t.Highest != null).ToList();
            answer.Table = new(["Tender", "Highest price", "Supplier"], list.Select(t => new List<string> { $"#{t.Id} {t.Title}", Money(t.Highest, t.Currency), t.HighestSupplier ?? "-" }).ToList());
            answer.Chart = Chart("Highest price per tender", list.Take(8).Select(t => ($"#{t.Id}", t.Highest!.Value)));
            text = list.Count == 0 ? "There are no visible bids yet." : $"I listed the highest offer for {list.Count} tenders.";
        }
        else if (Has(q, "supplier", "participation", "offer", "مورد", "موردين", "مشاركة", "عروض"))
        {
            answer.Table = SuppliersTable(data);
            answer.Chart = SupplierChart(data);
            text = $"{answer.Table.Rows.Count} suppliers submitted bids. The table shows how many bids and wins each supplier has.";
        }
        else if (Has(q, "status", "state", "حالة", "وضع"))
        {
            answer.Chart = StatusChart(data);
            answer.Table = new(["Status", "Tenders"], answer.Chart.Labels.Select((l, i) => new List<string> { l, answer.Chart.Values[i].ToString("0") }).ToList());
            text = "Here is the number of tenders in each status.";
        }
        else if (Has(q, "compare", "comparison", "vs", "قارن", "مقارنة", "قارنة"))
        {
            answer.Table = TendersTable(data.Tenders.Where(t => t.BidCount > 0).Take(10));
            answer.Chart = Chart("Average bid price per tender", data.Tenders.Where(t => t.Average != null).Take(8).Select(t => ($"#{t.Id}", t.Average!.Value)));
            text = "Here is a comparison of the tenders that have bids: number of bids, lowest, highest and average price.";
        }
        else if (Has(q, "recommend", "advice", "suggest", "توصية", "توصيات", "نصيحة", "اقتراح"))
        {
            text = "Here are my recommendations based on the current data.";
        }
        else
        {
            answer.Cards = Kpis(data).Take(4).ToList();
            answer.Table = TendersTable(data.Tenders.Take(10));
            text = "Here is a general overview of the tenders you can see.";
        }

        // Question about one tender -> recommendations for that tender only.
        answer.Recommendations = focus is null ? Recommendations(data) : TenderRecommendations(focus, data);
        answer.Answer = text;
        return answer;
    }

    // ============================================================ 4) Reading uploaded Excel / CSV / PDF files

    public static AiTable ReadFile(IFormFile file)
    {
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        using var stream = file.OpenReadStream();
        var lines = extension switch
        {
            ".xlsx" => ReadXlsx(stream),
            ".pdf" => ReadPdf(stream).Select(l => new List<string> { l }).ToList(),
            _ => ReadCsv(stream)
        };
        lines = lines.Where(l => l.Any(c => !string.IsNullOrWhiteSpace(c))).Take(extension == ".pdf" ? 600 : 301).ToList();
        if (lines.Count == 0) return new(["Content"], []);
        if (extension == ".pdf") return new(["Text"], lines);
        var columns = lines[0];
        var rows = lines.Skip(1).Select(r => Enumerable.Range(0, columns.Count).Select(i => i < r.Count ? r[i] : "").ToList()).ToList();
        return new(columns, rows);
    }

    /// <summary>The uploaded file as plain text for the model (CSV style for tables, lines for PDFs).</summary>
    public static string FileText(string fileName, AiTable table)
    {
        var text = new StringBuilder($"File name: {fileName}\n");
        if (table.Columns is ["Text"]) text.AppendJoin('\n', table.Rows.Select(r => r[0]));
        else
        {
            static string Cell(string v) => v.Contains(',') || v.Contains('"') ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
            text.AppendLine(string.Join(",", table.Columns.Select(Cell)));
            foreach (var row in table.Rows) text.AppendLine(string.Join(",", row.Select(Cell)));
        }
        return text.ToString();
    }

    private static List<List<string>> ReadCsv(Stream stream)
    {
        using var reader = new StreamReader(stream);
        var result = new List<List<string>>();
        while (reader.ReadLine() is { } line)
        {
            var separator = line.Contains(';') && !line.Contains(',') ? ';' : ',';
            result.Add(line.Split(separator).Select(c => c.Trim().Trim('"')).ToList());
        }
        return result;
    }

    // Excel: the first sheet that has data (ClosedXML reads formulas' results, dates and numbers correctly).
    private static List<List<string>> ReadXlsx(Stream stream)
    {
        try
        {
            using var book = new ClosedXML.Excel.XLWorkbook(stream);
            var sheet = book.Worksheets.FirstOrDefault(w => w.RangeUsed() != null);
            var range = sheet?.RangeUsed();
            if (range is null) return [];
            return range.Rows().Take(301).Select(r => r.Cells().Select(c => c.GetFormattedString().Trim()).ToList()).ToList();
        }
        catch (Exception)
        {
            return [["This Excel file could not be read."]];
        }
    }

    // PDF: the text of every page, line by line (PdfPig understands the fonts, so Arabic and tables read well).
    private static List<string> ReadPdf(Stream stream)
    {
        var lines = new List<string>();
        try
        {
            using var document = UglyToad.PdfPig.PdfDocument.Open(stream);
            foreach (var page in document.GetPages().Take(30))
            {
                var text = UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor.ContentOrderTextExtractor.GetText(page);
                lines.AddRange(text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }
            if (lines.Count == 0) lines.Add("This PDF has no readable text (it may be a scanned image).");
        }
        catch (Exception)
        {
            lines.Add("This PDF could not be read (it may be scanned or protected).");
        }
        return lines;
    }

    // Finds the first column that contains numbers and draws it as a chart.
    private static AiChart? NumberColumnChart(AiTable table)
    {
        for (var c = 1; c < table.Columns.Count; c++)
        {
            var values = table.Rows.Select(r => decimal.TryParse(r[c], out var v) ? v : (decimal?)null).ToList();
            if (values.Count > 0 && values.All(v => v != null))
                return Chart(table.Columns[c], table.Rows.Take(10).Select((r, i) => (r[0], values[i]!.Value)));
        }
        return null;
    }

    // ============================================================ small helpers

    private static AiChart Chart(string title, IEnumerable<(string Label, decimal Value)> points)
    {
        var list = points.ToList();
        return new AiChart(title, list.Select(p => p.Label).ToList(), list.Select(p => p.Value).ToList());
    }

    private static bool Has(string text, params string[] words) => words.Any(text.Contains);

    private static AiTenderRow? FindTender(AiData data, string q)
    {
        // By number: "#3", "tender 3", "مناقصة 3"
        var match = System.Text.RegularExpressions.Regex.Match(q, @"(?:#|tender\s*|مناقصة\s*|المناقصة\s*|رقم\s*)(\d+)");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var id) && data.Tenders.FirstOrDefault(t => t.Id == id) is { } byId)
            return byId;
        // By name: "recommendations for the ffg tender". Longest title first so "Laptops 2" wins over "Laptops".
        return data.Tenders.Where(t => t.Title.Trim().Length >= 3)
            .OrderByDescending(t => t.Title.Length)
            .FirstOrDefault(t => q.Contains(t.Title.Trim().ToLowerInvariant()));
    }

    public static string Money(decimal? value, string currency) => value is null ? "-" : $"{value:N2} {currency}";
}
