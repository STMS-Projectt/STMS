using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SourcingTenderSystem.Data;
using SourcingTenderSystem.Models;
using SourcingTenderSystem.Security;
using SourcingTenderSystem.ViewModels;

namespace SourcingTenderSystem.Controllers;

public sealed class EmployeesController(ApplicationDbContext db, UserManager<IdentityUser> users) : Controller
{
    private IQueryable<EmployeeProfile> Employees => db.EmployeeProfiles.Include(x => x.User).Include(x => x.Department);

    [HttpGet]
    public async Task<IActionResult> Index() => View(await Employees.AsNoTracking().OrderBy(x => x.FullName).ToListAsync());

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var employee = await Employees.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (employee is null) return NotFound();
        ViewBag.Roles = string.Join(", ", await users.GetRolesAsync(employee.User));
        ViewBag.IsSelf = employee.UserId == users.GetUserId(User);
        return View(employee);
    }

    [HttpGet]
    public async Task<IActionResult> Create() => await FormAsync(new CreateEmployeeInput { IsActive = true });

    [HttpPost]
    public async Task<IActionResult> Create(CreateEmployeeInput input)
    {
        await ValidateAsync(input);
        if (!ModelState.IsValid) return await FormAsync(input);

        await using var transaction = await db.Database.BeginTransactionAsync();
        var email = input.Email.Trim();
        var user = new IdentityUser { UserName = email, Email = email };
        if (!Accept(await users.CreateAsync(user, input.Password)) ||
            !Accept(await users.AddToRoleAsync(user, input.Role)) ||
            !await SetActiveAsync(user, input.IsActive))
            return await FormAsync(input);

        var employee = new EmployeeProfile { UserId = user.Id };
        Apply(employee, input);
        db.EmployeeProfiles.Add(employee);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return RedirectToAction(nameof(Details), new { id = employee.Id });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var employee = await Employees.SingleOrDefaultAsync(x => x.Id == id);
        if (employee is null) return NotFound();
        var roles = await users.GetRolesAsync(employee.User);
        return await FormAsync(new EmployeeInput
        {
            FullName = employee.FullName, JobTitle = employee.JobTitle, DepartmentId = employee.DepartmentId,
            IsActive = employee.IsActive, Role = roles.FirstOrDefault(Roles.EmployeeRoles.Contains) ?? ""
        }, employee.DepartmentId);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, EmployeeInput input)
    {
        var employee = await Employees.SingleOrDefaultAsync(x => x.Id == id);
        if (employee is null) return NotFound();
        await ValidateAsync(input, employee.DepartmentId);
        if (employee.UserId == users.GetUserId(User) && (!input.IsActive || input.Role != Roles.Admin))
            ModelState.AddModelError("", "You cannot deactivate your own account or remove your own Admin role.");
        if (!ModelState.IsValid) return await FormAsync(input, employee.DepartmentId);

        await using var transaction = await db.Database.BeginTransactionAsync();
        var current = await users.GetRolesAsync(employee.User);
        var removed = current.Where(role => Roles.EmployeeRoles.Contains(role) && role != input.Role).ToArray();
        if ((removed.Length > 0 && !Accept(await users.RemoveFromRolesAsync(employee.User, removed))) ||
            (!current.Contains(input.Role) && !Accept(await users.AddToRoleAsync(employee.User, input.Role))) ||
            (employee.IsActive != input.IsActive && !await SetActiveAsync(employee.User, input.IsActive)) ||
            ((removed.Length > 0 || !current.Contains(input.Role) || employee.IsActive != input.IsActive) &&
                !Accept(await users.UpdateSecurityStampAsync(employee.User))))
            return await FormAsync(input, employee.DepartmentId);

        Apply(employee, input);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> Deactivate(int id)
    {
        var employee = await Employees.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (employee is null) return NotFound();
        if (employee.UserId == users.GetUserId(User)) return Forbid();
        return View(employee);
    }

    [HttpPost, ActionName(nameof(Deactivate))]
    public async Task<IActionResult> ConfirmDeactivate(int id)
    {
        var employee = await Employees.SingleOrDefaultAsync(x => x.Id == id);
        if (employee is null) return NotFound();
        if (employee.UserId == users.GetUserId(User)) return Forbid();
        if (!employee.IsActive) return RedirectToAction(nameof(Details), new { id });

        await using var transaction = await db.Database.BeginTransactionAsync();
        if (!await SetActiveAsync(employee.User, false) || !Accept(await users.UpdateSecurityStampAsync(employee.User)))
            return View("Deactivate", employee);
        employee.IsActive = false;
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task ValidateAsync(EmployeeInput input, int? currentDepartment = null)
    {
        if (!Roles.EmployeeRoles.Contains(input.Role))
            ModelState.AddModelError(nameof(input.Role), "Select a valid employee role.");
        if (input.Role == "RequestingDepartment" && input.DepartmentId is null)
            ModelState.AddModelError(nameof(input.DepartmentId), "This role requires a department.");
        if (input.DepartmentId is int id && !await db.Departments.AnyAsync(x => x.Id == id && (x.IsActive || x.Id == currentDepartment)))
            ModelState.AddModelError(nameof(input.DepartmentId), "Select an active department.");
    }

    private async Task<IActionResult> FormAsync(EmployeeInput input, int? currentDepartment = null)
    {
        ViewBag.Departments = new SelectList(await db.Departments.AsNoTracking()
            .Where(x => x.IsActive || x.Id == currentDepartment).OrderBy(x => x.Name).ToListAsync(), "Id", "Name");
        ViewBag.Roles = new SelectList(Roles.EmployeeRoles);
        return View(input);
    }

    private static void Apply(EmployeeProfile employee, EmployeeInput input)
    {
        employee.FullName = input.FullName.Trim();
        employee.JobTitle = input.JobTitle?.Trim() ?? "";
        employee.DepartmentId = input.DepartmentId;
        employee.IsActive = input.IsActive;
    }

    private async Task<bool> SetActiveAsync(IdentityUser user, bool active) =>
        Accept(await users.SetLockoutEnabledAsync(user, true)) &&
        Accept(await users.SetLockoutEndDateAsync(user, active ? null : DateTimeOffset.MaxValue));

    private bool Accept(IdentityResult result)
    {
        foreach (var error in result.Errors) ModelState.AddModelError("", error.Description);
        return result.Succeeded;
    }
}
