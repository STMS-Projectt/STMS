using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SourcingTenderSystem.Data;
using SourcingTenderSystem.Models;
using SourcingTenderSystem.Security;
using SourcingTenderSystem.ViewModels;

namespace SourcingTenderSystem.Controllers;

[Authorize(Roles = Roles.Admin)]
public sealed class DepartmentsController(ApplicationDbContext db) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index() => View(await db.Departments.AsNoTracking().OrderBy(x => x.Name)
        .Select(x => new DepartmentDirectoryRow(x.Id, x.Name, x.Description, x.IsActive, x.EmployeeProfiles.Count)).ToListAsync());

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var department = await db.Departments.AsNoTracking().Include(x => x.EmployeeProfiles)
            .SingleOrDefaultAsync(x => x.Id == id);
        return department is null ? NotFound() : View(department);
    }

    [HttpGet]
    public IActionResult Create() => View(new DepartmentInput());

    [HttpPost]
    public async Task<IActionResult> Create(DepartmentInput input)
    {
        var department = new Department();
        if (!ModelState.IsValid) return View(input);
        db.Departments.Add(department);
        return await SaveAsync(department, input) ? RedirectToAction(nameof(Details), new { id = department.Id }) : View(input);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var department = await db.Departments.FindAsync(id);
        return department is null ? NotFound() : View(new DepartmentInput
        {
            Name = department.Name, Description = department.Description, IsActive = department.IsActive
        });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, DepartmentInput input)
    {
        var department = await db.Departments.FindAsync(id);
        if (department is null) return NotFound();
        if (!ModelState.IsValid) return View(input);
        return await SaveAsync(department, input) ? RedirectToAction(nameof(Details), new { id }) : View(input);
    }

    private async Task<bool> SaveAsync(Department department, DepartmentInput input)
    {
        department.Name = input.Name.Trim();
        department.Description = input.Description?.Trim();
        department.IsActive = input.IsActive;
        try { await db.SaveChangesAsync(); return true; }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            ModelState.AddModelError(nameof(input.Name), "A department with this name already exists.");
            return false;
        }
    }
}
