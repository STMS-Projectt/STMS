using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderSystem.Data;
using SourcingTenderSystem.Security;
using SourcingTenderSystem.ViewModels;

namespace SourcingTenderSystem.Controllers;

[Authorize]
public sealed class AccountController(SignInManager<IdentityUser> signIn, UserManager<IdentityUser> users,
    ApplicationDbContext db) : Controller
{
    [AllowAnonymous, HttpGet]
    public IActionResult Login(string? returnUrl) => View(new LoginInput
    {
        ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null
    });

    [AllowAnonymous, HttpPost]
    public async Task<IActionResult> Login(LoginInput input)
    {
        if (!ModelState.IsValid) return View(input);
        var user = await users.FindByEmailAsync(input.Email.Trim());
        if (user is not null &&
            !await db.EmployeeProfiles.AnyAsync(x => x.UserId == user.Id && !x.IsActive) &&
            (await signIn.PasswordSignInAsync(user, input.Password, input.RememberMe, lockoutOnFailure: true)).Succeeded)
        {
            if (Url.IsLocalUrl(input.ReturnUrl)) return LocalRedirect(input.ReturnUrl);
            if (await users.IsInRoleAsync(user, Roles.Admin)) return RedirectToAction("Index", "AdminDashboard");
            if (await users.IsInRoleAsync(user, Roles.SourcingOfficer))
                return RedirectToAction("Index", "SourcingDashboard");
            if (await users.IsInRoleAsync(user, Roles.TechnicalCommittee) || await users.IsInRoleAsync(user, Roles.FinancialCommittee) || await users.IsInRoleAsync(user, Roles.SourcingManager))
                return RedirectToAction("Index", "CommitteeReviews");
            if (await users.IsInRoleAsync(user, Roles.TopCommittee))
                return RedirectToAction("Index", "EvaluationWorkflow");
            if (await users.IsInRoleAsync(user, "RequestingDepartment"))
                return RedirectToAction("Dashboard", "PurchaseRequisitions");
            if (await db.SupplierProfiles.AnyAsync(x => x.UserId == user.Id))
                return RedirectToAction("Index", "SupplierWorkspace");
            return RedirectToAction("Index", "Home");
        }

        ModelState.AddModelError("", "Unable to sign in with those credentials.");
        return View(input);
    }

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await signIn.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    [AllowAnonymous, HttpGet]
    public IActionResult AccessDenied()
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return View();
    }
}
