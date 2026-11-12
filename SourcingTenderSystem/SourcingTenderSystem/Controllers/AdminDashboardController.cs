using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderSystem.Data;
using SourcingTenderSystem.Models;
using SourcingTenderSystem.Security;
using SourcingTenderSystem.ViewModels;

namespace SourcingTenderSystem.Controllers;

[Authorize(Roles = Roles.Admin)]
public sealed class AdminDashboardController(ApplicationDbContext db, UserManager<IdentityUser> users) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var statuses = await db.SupplierProfiles.GroupBy(x => x.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Status, x => x.Count);
        return View(new DashboardCounts(
            await users.Users.CountAsync(), await db.Departments.CountAsync(),
            await db.EmployeeProfiles.CountAsync(x => x.IsActive),
            statuses.GetValueOrDefault(SupplierStatus.Pending), statuses.GetValueOrDefault(SupplierStatus.Approved),
            statuses.GetValueOrDefault(SupplierStatus.Rejected), statuses.GetValueOrDefault(SupplierStatus.Suspended),
            await db.PurchaseRequisitions.CountAsync(), await db.Tenders.CountAsync()));
    }
}
