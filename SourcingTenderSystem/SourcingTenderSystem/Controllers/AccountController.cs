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
            if (await db.SupplierProfiles.AnyAsync(x => x.UserId == user.Id))
                return RedirectToAction("RegistrationStatus", "SupplierRegistration");
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
