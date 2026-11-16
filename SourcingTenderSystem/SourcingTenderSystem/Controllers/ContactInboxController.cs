using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderSystem.Data;
using SourcingTenderSystem.Security;
using SourcingTenderSystem.ViewModels;

namespace SourcingTenderSystem.Controllers;

[Authorize(Roles = Roles.Admin)]
public sealed class ContactInboxController(ApplicationDbContext db) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(int page = 1, CancellationToken cancellationToken = default)
    {
        const int size = 25;
        page = Math.Clamp(page, 1, 100000);
        var messages = await db.ContactMessages.AsNoTracking().OrderBy(x => x.Reply != null)
            .ThenByDescending(x => x.Id).Skip((page - 1) * size).Take(size + 1).ToListAsync(cancellationToken);
        return View(new ContactInbox(messages.Take(size).ToList(), page, messages.Count > size));
    }

    [HttpPost]
    public async Task<IActionResult> Reply(int id, ContactReplyInput input, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Forbid();
        if (!ModelState.IsValid)
        {
            TempData["ContactError"] = "Enter a reply of 1 to 4000 characters.";
            return RedirectToAction(nameof(Index), null, $"contact-{id}");
        }

        var updated = await db.ContactMessages.Where(x => x.Id == id && x.Reply == null)
            .ExecuteUpdateAsync(changes => changes
                .SetProperty(x => x.Reply, input.Reply.Trim())
                .SetProperty(x => x.RepliedAtUtc, DateTime.UtcNow)
                .SetProperty(x => x.RepliedByUserId, userId), cancellationToken);
        if (updated == 0)
        {
            if (!await db.ContactMessages.AnyAsync(x => x.Id == id, cancellationToken)) return NotFound();
            TempData["ContactError"] = "This message already has a reply.";
        }
        else TempData["ContactSuccess"] = "Reply published on the sender's private tracking page.";
        return RedirectToAction(nameof(Index));
    }
}
