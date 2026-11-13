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
        var tenderCount = await Db.Tenders.CountAsync(cancellationToken);
        var invitationCount = await Db.Invitations.CountAsync(cancellationToken);
        var complianceCount = await Db.Tenders.CountAsync(t => t.Status == "Closed" &&
            t.Bids.Any(b => b.Status == "Submitted" || b.Status == "Compliant" || b.Status == "Non-Compliant"),
            cancellationToken);

        return View(new SourcingDashboard(employee.FullName, pendingCount, tenderCount,
            invitationCount, complianceCount, recent));
    }
}
