using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderSystem.Data;
using SourcingTenderSystem.Models;
using SourcingTenderSystem.Services;

namespace SourcingTenderSystem.Controllers;

[Authorize, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class NotificationsController(ApplicationDbContext db, ClarificationRouting routing) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var actor = await routing.Current(User);
        if (actor is null) return Forbid();
        return View(await For(db, actor).AsNoTracking().OrderByDescending(n => n.CreatedAt)
            .ThenByDescending(n => n.Id).ToListAsync());
    }

    [HttpGet]
    public async Task<IActionResult> Unread()
    {
        var actor = await routing.Current(User);
        if (actor is null) return Forbid();
        return Json(new { count = await For(db, actor).CountAsync(n => !n.IsRead) });
    }
    [HttpPost]
    public async Task<IActionResult> Read(int id)
    {
        var actor = await routing.Current(User);
        if (actor is null) return Forbid();
        var selected = For(db, actor).Where(n => n.Id == id);
        var item = await selected.AsNoTracking().SingleOrDefaultAsync();
        if (item is null) return NotFound();
        await selected.ExecuteUpdateAsync(update => update.SetProperty(n => n.IsRead, true));
        return Url.IsLocalUrl(item.Link) ? LocalRedirect(item.Link) : RedirectToAction(nameof(Index));
    }

    public static IQueryable<Notification> For(ApplicationDbContext db, ClarificationPerson actor) =>
        actor.Employee is int id ? db.Notifications.Where(n => n.EmployeeProfileId == id)
            : db.Notifications.Where(n => n.SupplierProfileId == actor.Supplier);
}
