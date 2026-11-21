using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderSystem.Data;
using SourcingTenderSystem.Models;
using SourcingTenderSystem.Security;
using SourcingTenderSystem.Services;
using SourcingTenderSystem.ViewModels;

namespace SourcingTenderSystem.Controllers;

[Authorize(Roles = Roles.Admin)]
public sealed class AdminDashboardController(ApplicationDbContext db, UserManager<IdentityUser> users, UiText ui) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(int months = 6)
    {
        months = months is 3 or 6 or 12 ? months : 6;
        ViewBag.TrendMonths = months;
        var statuses = await db.SupplierProfiles.GroupBy(x => x.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Status, x => x.Count);
        ViewBag.TenderCounts = await db.Tenders.AsNoTracking().GroupBy(t => t.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() }).OrderByDescending(g => g.Count)
            .ToDictionaryAsync(g => g.Status, g => g.Count);
        var now = DateTime.UtcNow;
        var month = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var firstMonth = month.AddMonths(1 - months);
        var trend = await db.Tenders.AsNoTracking().Where(t => t.CreatedAt >= firstMonth && t.CreatedAt < month.AddMonths(1))
            .GroupBy(t => new { t.CreatedAt.Year, t.CreatedAt.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() }).ToListAsync();
        var requests = await db.PurchaseRequisitions.AsNoTracking()
            .Where(r => r.CreatedAt >= firstMonth && r.CreatedAt < month.AddMonths(1))
            .GroupBy(r => new { r.CreatedAt.Year, r.CreatedAt.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() }).ToListAsync();
        ViewBag.TenderTrend = Enumerable.Range(0, months).Select(i => firstMonth.AddMonths(i))
            .Select(date => (date.ToString("MMM yyyy", ui.DisplayCulture),
                trend.FirstOrDefault(t => t.Year == date.Year && t.Month == date.Month)?.Count ?? 0,
                requests.FirstOrDefault(r => r.Year == date.Year && r.Month == date.Month)?.Count ?? 0)).ToArray();
        await LoadAnalyticsAsync(now);
        return View(new DashboardCounts(
            await users.Users.CountAsync(), await db.Departments.CountAsync(),
            await db.EmployeeProfiles.CountAsync(x => x.IsActive),
            statuses.GetValueOrDefault(SupplierStatus.Pending), statuses.GetValueOrDefault(SupplierStatus.Approved),
            statuses.GetValueOrDefault(SupplierStatus.Rejected), statuses.GetValueOrDefault(SupplierStatus.Suspended),
            await db.PurchaseRequisitions.CountAsync(), await db.Tenders.CountAsync()));
    }

    // Analytics dashboard: key indicators (KPIs) and breakdown charts.
    // Every card and bar links to the report or register that holds the details behind the number.
    private async Task LoadAnalyticsAsync(DateTime now)
    {
        var reports = Url.Action("Index", "Reports");

        // Supplier participation: suppliers who submitted a bid out of the suppliers invited (all tenders).
        var tenders = await db.Tenders.Include(t => t.Invitations).Include(t => t.Bids).ToListAsync();
        var invited = 0;
        var bidders = 0;
        foreach (var tender in tenders)
        {
            invited += ReportRules.InvitedSuppliers(tender);
            bidders += ReportRules.BiddingSuppliers(tender);
        }
        var delayed = await ReportRules.DelayedEvaluationsAsync(db, now);
        var durations = await ReportRules.ApprovalDurationsAsync(db);

        // Three key indicators. The other totals (tenders, awards...) are already on this dashboard.
        var kpis = new List<ReportKpi>();
        kpis.Add(new ReportKpi
        {
            Label = ui.T("Supplier participation rate"),
            Value = ReportRules.Percent(bidders, invited),
            Icon = "supplier",
            Tone = "blue",
            Link = reports + "?tab=tenders#participation",
            Hint = ui.T("Suppliers who submitted a bid out of suppliers invited")
        });
        kpis.Add(new ReportKpi
        {
            Label = ui.T("Delayed evaluations"),
            Value = delayed.Count.ToString("N0"),
            Icon = "clock",
            Tone = "amber",
            Link = reports + "?tab=tenders#delays",
            Hint = ui.F("Waiting more than {0} day at one stage", ReportRules.EvaluationDelayDays)
        });
        kpis.Add(new ReportKpi
        {
            Label = ui.T("Average approval time"),
            Value = ReportRules.Duration(ReportRules.Average(durations.Values.ToList()), ui),
            Icon = "calendar",
            Tone = "violet",
            Link = reports + "?tab=requests#departments",
            Hint = ui.T("From submission to approval")
        });
        ViewBag.Kpis = kpis;

        var requests = await db.PurchaseRequisitions.ToListAsync();

        // Bar chart: requests per department
        var departments = await db.Departments.ToListAsync();
        var byDepartment = new List<DashboardBar>();
        foreach (var department in departments)
        {
            var count = requests.Count(r => r.DepartmentId == department.Id);
            if (count > 0) byDepartment.Add(new DashboardBar { Id = department.Id, Name = department.Name, Count = count });
        }
        ViewBag.RequestsByDepartment = byDepartment.OrderByDescending(b => b.Count).ToList();

        // Bar chart: requests per status
        var statuses = requests.Select(r => r.Status).Distinct().ToList();
        var byStatus = new List<DashboardBar>();
        foreach (var status in statuses)
        {
            byStatus.Add(new DashboardBar { Name = status, Count = requests.Count(r => r.Status == status) });
        }
        ViewBag.RequestsByStatus = byStatus.OrderByDescending(b => b.Count).ToList();

        // Bar chart: top 5 suppliers by number of awards
        var awardedBids = await db.Bids.Include(b => b.SupplierProfile).Where(b => b.AwardOutcome == "Awarded").ToListAsync();
        var bySupplier = new List<DashboardBar>();
        foreach (var bid in awardedBids)
        {
            var bar = bySupplier.FirstOrDefault(b => b.Id == bid.SupplierProfileId);
            if (bar == null)
            {
                bar = new DashboardBar { Id = bid.SupplierProfileId, Name = bid.SupplierProfile.CompanyName };
                bySupplier.Add(bar);
            }
            bar.Count++;
        }
        ViewBag.TopSuppliers = bySupplier.OrderByDescending(b => b.Count).Take(5).ToList();
    }
}
