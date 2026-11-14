using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderSystem.Data;
using SourcingTenderSystem.Models;
using SourcingTenderSystem.ViewModels;

namespace SourcingTenderSystem.Controllers;

// "My profile" for every signed-in user (employees and suppliers).
// One page (Views/Profile/Index.cshtml) with two forms: change the phone number, and change the password.
[Authorize]
public class ProfileController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly SignInManager<IdentityUser> _signInManager;

    public ProfileController(ApplicationDbContext context, UserManager<IdentityUser> userManager,
        SignInManager<IdentityUser> signInManager)
    {
        _context = context;
        _userManager = userManager;
        _signInManager = signInManager;
    }

    // GET: /Profile
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound();
        }

        var supplier = await GetSupplierAsync(user.Id);

        var model = new ProfileViewModel();
        model.PhoneNumber = GetSavedPhone(user, supplier);

        return await ShowProfilePageAsync(user, supplier, model);
    }

    // POST: /Profile   (form 1: phone number)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(ProfileViewModel model)
    {
        // The user always comes from the sign-in cookie, never from the form
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound();
        }

        // This form posts only the phone number: the password fields belong to form 2
        ModelState.Remove(nameof(model.NewPassword));
        ModelState.Remove(nameof(model.ConfirmNewPassword));

        var supplier = await GetSupplierAsync(user.Id);
        string phone = (model.PhoneNumber ?? string.Empty).Trim();

        // A supplier must keep a contact phone (it was required at registration)
        if (supplier != null && phone == "")
        {
            ModelState.AddModelError(nameof(model.PhoneNumber), "Suppliers must keep a contact phone number.");
        }

        if (!ModelState.IsValid)
        {
            return await ShowProfilePageAsync(user, supplier, model);
        }

        // Save every number in the same format: +962791234567
        if (phone != "")
        {
            phone = ToInternationalFormat(phone);
        }

        if (supplier != null)
        {
            supplier.ContactPhone = phone; // the phone the admin sees for a supplier
            await _context.SaveChangesAsync();
        }

        if (phone == "")
        {
            user.PhoneNumber = null;
        }
        else
        {
            user.PhoneNumber = phone;
        }

        // UpdateAsync saves the user WITHOUT changing the security stamp, so the user stays signed in
        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(nameof(model.PhoneNumber), error.Description);
            }
            return await ShowProfilePageAsync(user, supplier, model);
        }

        TempData["Message"] = "Your phone number was saved.";
        return RedirectToAction(nameof(Index));
    }

    // POST: /Profile/ChangePassword   (form 2: password, on the same page)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ProfileViewModel model)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound();
        }

        // This form posts only the passwords. If we show the page again, show the saved phone number in form 1.
        var supplier = await GetSupplierAsync(user.Id);
        ModelState.Remove(nameof(model.PhoneNumber));
        model.PhoneNumber = GetSavedPhone(user, supplier);

        if (!ModelState.IsValid)
        {
            return await ShowProfilePageAsync(user, supplier, model);
        }

        // The new password must be different: CheckPasswordAsync is true if it is the same as the saved one
        if (await _userManager.CheckPasswordAsync(user, model.NewPassword))
        {
            ModelState.AddModelError(nameof(model.NewPassword), "The new password must be different from the current one.");
            return await ShowProfilePageAsync(user, supplier, model);
        }

        // Set the new password without the old one: make a reset token here on the server and use it at once
        // (the token is never sent anywhere). ResetPasswordAsync checks the password rules, then saves the new hash.
        string resetToken = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, resetToken, model.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(nameof(model.NewPassword), error.Description);
            }
            return await ShowProfilePageAsync(user, supplier, model);
        }

        // ResetPasswordAsync changes the security stamp. Refresh the sign-in cookie so THIS browser
        // stays signed in (other devices that are signed in with the old password are signed out).
        await _signInManager.RefreshSignInAsync(user);

        // TODO: audit log + notification (team mechanism) - password changed

        TempData["Message"] = "Your password was changed.";
        return RedirectToAction(nameof(Index));
    }

    // ----------------- helper methods -----------------

    // Shows the profile page (Views/Profile/Index.cshtml)
    private async Task<IActionResult> ShowProfilePageAsync(IdentityUser user, SupplierProfile? supplier, ProfileViewModel model)
    {
        ViewBag.Info = await BuildInfoAsync(user, supplier);
        return View("Index", model);
    }

    // The supplier profile of this user, or null if the user is an employee
    private async Task<SupplierProfile?> GetSupplierAsync(string userId)
    {
        return await _context.SupplierProfiles.FirstOrDefaultAsync(s => s.UserId == userId);
    }

    // The saved phone number: suppliers -> SupplierProfiles.ContactPhone, others -> AspNetUsers.PhoneNumber
    private string? GetSavedPhone(IdentityUser user, SupplierProfile? supplier)
    {
        if (supplier != null)
        {
            return supplier.ContactPhone;
        }

        return user.PhoneNumber;
    }

    // Everything the page shows as read only
    private async Task<ProfileInfo> BuildInfoAsync(IdentityUser user, SupplierProfile? supplier)
    {
        var info = new ProfileInfo();
        info.Email = user.Email ?? string.Empty;
        info.Supplier = supplier;
        info.Employee = await _context.EmployeeProfiles
            .Include(e => e.Department)
            .FirstOrDefaultAsync(e => e.UserId == user.Id);

        // The name at the top of the page
        if (supplier != null)
        {
            info.DisplayName = supplier.CompanyName;
        }
        else if (info.Employee != null)
        {
            info.DisplayName = info.Employee.FullName;
        }
        if (info.DisplayName == "")
        {
            info.DisplayName = info.Email;
        }
        if (info.DisplayName == "")
        {
            info.DisplayName = "User";
        }

        return info;
    }

    // 0791234567 / 00962791234567 / +962791234567  ->  +962791234567
    // (the number was already checked by the RegularExpression in ProfileViewModel)
    private string ToInternationalFormat(string phone)
    {
        if (phone.StartsWith("+962"))
        {
            return phone;
        }

        if (phone.StartsWith("00962"))
        {
            return "+962" + phone.Substring(5); // remove "00962"
        }

        return "+962" + phone.Substring(1); // remove the first "0"
    }
}
