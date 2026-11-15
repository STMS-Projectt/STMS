using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderSystem.Data;
using SourcingTenderSystem.Models;
using SourcingTenderSystem.Security;

namespace SourcingTenderSystem.Controllers;

[Authorize(Roles = Roles.Admin)]
public sealed class CategoriesController(ApplicationDbContext db) : Controller
{
    public async Task<IActionResult> Index() => View(await db.Categories.AsNoTracking().OrderBy(c => c.Name).ToListAsync());

    [HttpPost]
    public async Task<IActionResult> Create(string? name)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 120)
            TempData["Error"] = "Enter a category name of 1–120 characters.";
        else if (await db.Categories.AnyAsync(c => c.Name == name))
            TempData["Error"] = "That category already exists.";
        else
        {
            db.Categories.Add(new Category { Name = name });
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 })
            { TempData["Error"] = "That category already exists."; }
        }
        return RedirectToAction(nameof(Index));
    }
}
