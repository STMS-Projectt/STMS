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
        return View(new DashboardCounts(
            await users.Users.CountAsync(), await db.Departments.CountAsync(),
            await db.EmployeeProfiles.CountAsync(x => x.IsActive),
            statuses.GetValueOrDefault(SupplierStatus.Pending), statuses.GetValueOrDefault(SupplierStatus.Approved),
            statuses.GetValueOrDefault(SupplierStatus.Rejected), statuses.GetValueOrDefault(SupplierStatus.Suspended),
            await db.PurchaseRequisitions.CountAsync(), await db.Tenders.CountAsync()));
    }
}
