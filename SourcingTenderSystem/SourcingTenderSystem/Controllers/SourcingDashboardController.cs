using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderSystem.Data;
using SourcingTenderSystem.Security;
using SourcingTenderSystem.ViewModels;

namespace SourcingTenderSystem.Controllers;

[Authorize(Roles = Roles.SourcingOfficer)]
public sealed class SourcingDashboardController(ApplicationDbContext db) : RequisitionBaseController(db)
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var employee = await CurrentEmployeeAsync();
        if (employee is null) return Forbid();

        var pending = Db.PurchaseRequisitions.AsNoTracking()
            .Where(r => r.Status == "Submitted" || r.Status == "Under Review");
        var recent = await pending.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id).Take(5)
            .Select(r => new SourcingRequestRow(r.Id, r.Title, r.Department.Name, r.Priority, r.Status))
            .ToListAsync(cancellationToken);
        var pendingCount = await pending.CountAsync(cancellationToken);
        var tenderStatuses = await Db.Tenders.GroupBy(t => t.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Status, g => g.Count, cancellationToken);
        var invitationCount = await Db.Invitations.CountAsync(cancellationToken);
        var complianceCount = await Db.Tenders.CountAsync(t => t.Status == "Closed" &&
            t.Bids.Any(b => b.Status == "Submitted" || b.Status == "Pending Clarification" || b.Status == "Compliant" || b.Status == "Non-Compliant"),
            cancellationToken);

        var now = DateTime.UtcNow;
        var first = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-5);
        var end = first.AddMonths(6);
        async Task<Dictionary<(int, int), int>> Monthly(IQueryable<DateTime> dates) =>
            await dates.Where(d => d >= first && d < end)
                .GroupBy(d => new { d.Year, d.Month })
                .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
                .ToDictionaryAsync(g => (g.Year, g.Month), g => g.Count, cancellationToken);
        var requestsByMonth = await Monthly(Db.PurchaseRequisitions.Select(r => r.CreatedAt));
        var tendersByMonth = await Monthly(Db.Tenders.Select(t => t.CreatedAt));
        var trend = Enumerable.Range(0, 6).Select(first.AddMonths)
            .Select(m => new SourcingMonth(m, requestsByMonth.GetValueOrDefault((m.Year, m.Month)),
                tendersByMonth.GetValueOrDefault((m.Year, m.Month)))).ToArray();
        return View(new SourcingDashboard(employee.FullName, pendingCount, tenderStatuses.Values.Sum(),
            invitationCount, complianceCount, recent, trend, tenderStatuses));
    }
}
