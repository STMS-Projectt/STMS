using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SourcingTenderSystem.Data;
using SourcingTenderSystem.Security;
using SourcingTenderSystem.Services.Biometrics;
using SourcingTenderSystem.ViewModels;
using SourcingTenderSystem.ViewModels.Biometrics;

namespace SourcingTenderSystem.Controllers;

[Authorize, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AccountController(SignInManager<IdentityUser> signIn, UserManager<IdentityUser> users,
    ApplicationDbContext db, IBiometricService biometrics, IDataProtectionProvider protection) : Controller
{
    private const string Pending = "BiometricPending";
    private const string Provider = "Biometrics";
    private const string FaceToken = "FaceToken";
    private const string FaceDeviceCookie = ".STMS.FaceAccount";
    private const string FaceReference = "EncryptedFaceReference";
    private IDataProtector FaceProtector => protection.CreateProtector("STMS.FaceReference.v1");

    [AllowAnonymous, HttpGet]
    public IActionResult Login(string? returnUrl)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction(nameof(Continue));
        ViewData["KnownFaceAccount"] = RememberedFaceAccount() is not null;
        return View(new LoginInput { ReturnUrl = Local(returnUrl) });
    }

    [HttpGet]
    public async Task<IActionResult> Continue()
    {
        var user = await CurrentUser();
        if (user is null) return Forbid();
        return await HasFace(user) || (await users.GetPasskeysAsync(user)).Count > 0
            ? LocalRedirect(await Destination(user, null)) : RedirectToAction(nameof(BiometricSetup));
    }

    [AllowAnonymous, HttpPost]
    public async Task<IActionResult> Login(LoginInput input)
    {
        ViewData["KnownFaceAccount"] = RememberedFaceAccount() is not null;
        if (!ModelState.IsValid) return View(input);
        var rememberedId = RememberedFaceAccount();
        var user = !string.IsNullOrWhiteSpace(input.Email)
            ? await users.FindByEmailAsync(input.Email.Trim())
            : rememberedId is not null ? await users.FindByIdAsync(rememberedId) : null;
        if (rememberedId is null && string.IsNullOrWhiteSpace(input.Email))
            return BadRequest(new { message = "Enter your account email once to use face sign-in on this browser." });
        if (user is not null && await Active(user) &&
            !await users.GetTwoFactorEnabledAsync(user) &&
            (await signIn.CheckPasswordSignInAsync(user, input.Password, lockoutOnFailure: true)).Succeeded)
        {
            if (await HasFace(user) || (await users.GetPasskeysAsync(user)).Count > 0)
            {
                await HttpContext.SignOutAsync(Pending);
                await signIn.SignInAsync(user, input.RememberMe);
                return LocalRedirect(await Destination(user, input.ReturnUrl));
            }

            await signIn.SignOutAsync();
            var identity = new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim("stamp", await users.GetSecurityStampAsync(user)),
                new Claim("remember", input.RememberMe ? "1" : "0"),
                new Claim("return", Local(input.ReturnUrl) ?? "")
            ], Pending);
            await HttpContext.SignInAsync(Pending, new ClaimsPrincipal(identity));
            return RedirectToAction(nameof(BiometricVerify));
        }
        ModelState.AddModelError("", "Unable to sign in with those credentials.");
        return View(input);
    }

    [AllowAnonymous, HttpPost, EnableRateLimiting("biometric-login"), RequestSizeLimit(1_500_000)]
    public async Task<IActionResult> DirectCamera([FromBody] DirectBiometricInput input)
    {
        if (input.Data is null ||
            !input.Data.StartsWith("data:image/jpeg;base64,", StringComparison.Ordinal))
            return BadRequest(new { message = "Capture an image from your camera." });
        var rememberedId = RememberedFaceAccount();
        var user = !string.IsNullOrWhiteSpace(input.Email)
            ? await users.FindByEmailAsync(input.Email.Trim())
            : rememberedId is not null ? await users.FindByIdAsync(rememberedId) : null;
        if (rememberedId is null && string.IsNullOrWhiteSpace(input.Email))
            return BadRequest(new { message = "Enter your account email once to use face sign-in on this browser." });
        if (!await CanUseDirectLogin(user))
            return Unauthorized(new { message = "Could not verify your face. Try again or use your password." });
        var stored = await users.GetAuthenticationTokenAsync(user!, Provider, FaceToken);
        if (!string.IsNullOrEmpty(stored))
        {
            var detected = await biometrics.DetectFaceAsync(input.Data);
            if (string.IsNullOrEmpty(detected))
                return BadRequest(new { message = "Face++ could not detect one face. Check the lighting and try again." });
            var score = await Compare(user!, stored, detected);
            if (score is >= 75 and <= 100) { RememberFace(user!); return await DirectComplete(user!, input.ReturnUrl); }
            if (score == -2) return Conflict(new { message = "The saved face registration is no longer valid. Use your password, then renew your face in Sign-in security." });
            if (score < 0) return StatusCode(503, new { message = "Face++ could not complete the comparison. Try again later or use your registered phone." });
        }
        return Unauthorized(new { message = "Could not verify your face. Try again or use your password." });
    }

    [AllowAnonymous, HttpPost, EnableRateLimiting("biometric-login")]
    public async Task<IActionResult> DirectPhoneOptions() =>
        Content(await signIn.MakePasskeyRequestOptionsAsync(null), "application/json");

    [AllowAnonymous, HttpPost, EnableRateLimiting("biometric-login"), RequestSizeLimit(65536)]
    public async Task<IActionResult> DirectPhone([FromBody] DirectBiometricInput input)
    {
        if (!string.IsNullOrWhiteSpace(input.Data))
        {
            try
            {
                var result = await signIn.PerformPasskeyAssertionAsync(input.Data);
                if (result.Succeeded && await CanUseDirectLogin(result.User) &&
                    (await users.AddOrUpdatePasskeyAsync(result.User!, result.Passkey!)).Succeeded)
                    return await DirectComplete(result.User!, input.ReturnUrl);
            }
            catch (InvalidOperationException) { }
        }
        return Unauthorized(new { message = "Could not verify your phone. Try again or use your password." });
    }

    private async Task<bool> CanUseDirectLogin(IdentityUser? user) =>
        user is not null && await Active(user) && await signIn.CanSignInAsync(user) &&
        !await users.IsLockedOutAsync(user) && !await users.GetTwoFactorEnabledAsync(user);

    private async Task<IActionResult> DirectComplete(IdentityUser user, string? returnUrl)
    {
        await HttpContext.SignOutAsync(Pending);
        await signIn.SignInAsync(user, isPersistent: false);
        return Ok(new { redirectUrl = await Destination(user, returnUrl) });
    }

    [AllowAnonymous, HttpGet]
    public async Task<IActionResult> BiometricVerify()
    {
        var (user, _) = await PendingUser();
        if (user is null) return RedirectToAction(nameof(Login));
        return View("BiometricSetup", await Settings(user, manage: false));
    }

    [HttpGet]
    public async Task<IActionResult> BiometricSetup(string? returnUrl)
    {
        var user = await CurrentUser();
        if (user is null) return Forbid();
        return View(await Settings(user, manage: true, returnUrl));
    }

    [AllowAnonymous, HttpPost, RequestSizeLimit(1_500_000)]
    public async Task<IActionResult> Camera([FromBody] BiometricInput input)
    {
        var (user, ticket) = await Resolve(input.Manage);
        if (user is null) return Unauthorized(new { message = "Your session expired. Sign in again." });
        var stored = await users.GetAuthenticationTokenAsync(user, Provider, FaceToken);
        var renew = input.Manage && !string.IsNullOrEmpty(stored) && string.IsNullOrEmpty(await users.GetAuthenticationTokenAsync(user, Provider, FaceReference));
        if (input.Manage && ((!renew && !string.IsNullOrEmpty(stored)) || !await ConfirmPassword(user, input.Password)))
            return BadRequest(new { message = "Enter your current password. An existing face cannot be replaced here." });
        if (!input.Manage && stored is null && (await users.GetPasskeysAsync(user)).Count > 0)
            return BadRequest(new { message = "Sign in with your registered method before adding a face." });
        if (input.Data is null || !input.Data.StartsWith("data:image/jpeg;base64,", StringComparison.Ordinal))
            return BadRequest(new { message = "Capture a JPEG from your camera and try again." });
        var detected = await biometrics.DetectFaceAsync(input.Data);
        if (string.IsNullOrEmpty(detected)) return BadRequest(new { message = "Face++ could not detect one face. Check the lighting and try again." });
        var register = string.IsNullOrEmpty(stored) || renew;
        if (!register)
        {
            var score = await Compare(user, stored!, detected);
            if (score == -2 && !input.Manage)
                register = true; // Password was verified in the pending session. Replace only a provider-rejected legacy token.
            else if (score < 0)
                return StatusCode(503, new { message = "Face++ could not complete the comparison. Try again later or use your registered phone." });
            else if (score is not (>= 75 and <= 100))
                return Unauthorized(new { message = "The camera image did not match the registered face." });
        }
        if (register)
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            var encrypted = FaceProtector.CreateProtector(user.Id).Protect(input.Data["data:image/jpeg;base64,".Length..]);
            if (!(await users.SetAuthenticationTokenAsync(user, Provider, FaceReference, encrypted)).Succeeded ||
                !(await users.SetAuthenticationTokenAsync(user, Provider, FaceToken, detected)).Succeeded)
                return Conflict(new { message = "Could not save face enrollment. Reload and try again." });
            await transaction.CommitAsync();
        }
        RememberFace(user);
        return await Complete(user, ticket, input.Manage || register);
    }

    private string? RememberedFaceAccount()
    {
        if (!Request.Cookies.TryGetValue(FaceDeviceCookie, out var value)) return null;
        try { return protection.CreateProtector("STMS.FaceAccount.v1").Unprotect(value); }
        catch (System.Security.Cryptography.CryptographicException) { return null; }
    }

    private void RememberFace(IdentityUser user) => Response.Cookies.Append(FaceDeviceCookie,
        protection.CreateProtector("STMS.FaceAccount.v1").Protect(user.Id), new CookieOptions
        {
            HttpOnly = true, Secure = Request.IsHttps, SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromDays(30), Path = Url.Content("~/")
        });
    private async Task<double> Compare(IdentityUser user, string stored, string detected)
    {
        var encrypted = await users.GetAuthenticationTokenAsync(user, Provider, FaceReference);
        if (string.IsNullOrEmpty(encrypted)) return await biometrics.CompareFaceTokensAsync(stored, detected);
        try
        {
            var image = FaceProtector.CreateProtector(user.Id).Unprotect(encrypted);
            return await biometrics.CompareFaceTokensAsync(stored, detected, image);
        }
        catch (System.Security.Cryptography.CryptographicException) { return -1; }
    }
    [AllowAnonymous, HttpPost]
    public async Task<IActionResult> PhoneOptions([FromBody] BiometricInput input)
    {
        var (user, _) = await Resolve(input.Manage);
        if (user is null) return Unauthorized(new { message = "Your session expired. Sign in again." });
        var hasPhone = (await users.GetPasskeysAsync(user)).Count > 0;
        if (input.Manage && (hasPhone || !await ConfirmPassword(user, input.Password)))
            return BadRequest(new { message = "Enter your current password before adding your phone." });
        if (!input.Manage && !hasPhone && await HasFace(user))
            return BadRequest(new { message = "Verify your registered face before adding your phone." });
        var options = hasPhone
            ? await signIn.MakePasskeyRequestOptionsAsync(user)
            : await signIn.MakePasskeyCreationOptionsAsync(new PasskeyUserEntity
            { Id = user.Id, Name = user.Email ?? user.Id, DisplayName = user.Email ?? "Sourcing Desk" });
        return Content(options, "application/json");
    }

    [AllowAnonymous, HttpPost, RequestSizeLimit(65536)]
    public async Task<IActionResult> Phone([FromBody] BiometricInput input)
    {
        var (user, ticket) = await Resolve(input.Manage);
        if (user is null || string.IsNullOrWhiteSpace(input.Data))
            return Unauthorized(new { message = "Start phone verification again." });
        var hasPhone = (await users.GetPasskeysAsync(user)).Count > 0;
        try
        {
            if (!hasPhone && (input.Manage || !await HasFace(user)))
            {
                if (input.Manage && !await ConfirmPassword(user, input.Password))
                    return BadRequest(new { message = "Enter your current password." });
                var result = await signIn.PerformPasskeyAttestationAsync(input.Data);
                if (result.Succeeded && result.UserEntity?.Id == user.Id && result.Passkey is { } key)
                {
                    key.Name = "Phone verification";
                    if ((await users.AddOrUpdatePasskeyAsync(user, key)).Succeeded)
                        return await Complete(user, ticket, setup: true);
                }
            }
            else if (hasPhone && !input.Manage)
            {
                var result = await signIn.PerformPasskeyAssertionAsync(input.Data);
                if (result.Succeeded && result.User?.Id == user.Id && (await users.AddOrUpdatePasskeyAsync(user, result.Passkey!)).Succeeded)
                    return await Complete(user, ticket, setup: false);
            }
        }
        catch (InvalidOperationException) { }
        return BadRequest(new { message = "Phone verification failed. Start again and approve the prompt on your device." });
    }

    [AllowAnonymous, HttpPost]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(Pending);
        await signIn.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous, HttpGet]
    public IActionResult AccessDenied()
    {
        Response.StatusCode = 403;
        return View();
    }

    private async Task<IActionResult> Complete(IdentityUser user, ClaimsPrincipal? ticket, bool setup)
    {
        if (ticket is not null)
        {
            await HttpContext.SignOutAsync(Pending);
            await signIn.SignInAsync(user, ticket.FindFirstValue("remember") == "1");
        }
        var destination = await Destination(user, ticket?.FindFirstValue("return"));
        return Ok(new { redirectUrl = setup ? Url.Action(nameof(BiometricSetup), new { returnUrl = destination }) : destination });
    }

    private async Task<BiometricVerifyViewModel> Settings(IdentityUser user, bool manage, string? returnUrl = null) => new()
    {
        CanRenewFace = manage && await HasFace(user) && string.IsNullOrEmpty(await users.GetAuthenticationTokenAsync(user, Provider, FaceReference)),
        HasFace = await HasFace(user), HasFingerprint = (await users.GetPasskeysAsync(user)).Count > 0,
        Manage = manage, ReturnUrl = await Destination(user, returnUrl)
    };

    private Task<bool> ConfirmPassword(IdentityUser user, string? password) => string.IsNullOrEmpty(password)
        ? Task.FromResult(false) : CheckPassword(user, password);
    private async Task<bool> CheckPassword(IdentityUser user, string password) =>
        (await signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true)).Succeeded;
    private async Task<bool> HasFace(IdentityUser user) =>
        !string.IsNullOrEmpty(await users.GetAuthenticationTokenAsync(user, Provider, FaceToken));
    private async Task<IdentityUser?> CurrentUser()
    {
        var user = await users.GetUserAsync(User);
        return user is not null && await Active(user) && !await users.IsLockedOutAsync(user) ? user : null;
    }
    private async Task<(IdentityUser? User, ClaimsPrincipal? Ticket)> Resolve(bool manage) =>
        manage ? (await CurrentUser(), null) : await PendingUser();
    private async Task<(IdentityUser?, ClaimsPrincipal?)> PendingUser()
    {
        var ticket = (await HttpContext.AuthenticateAsync(Pending)).Principal;
        var id = ticket?.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = id is null ? null : await users.FindByIdAsync(id);
        return user is not null && await users.GetSecurityStampAsync(user) == ticket?.FindFirstValue("stamp") &&
            await Active(user) && await signIn.CanSignInAsync(user) && !await users.IsLockedOutAsync(user)
            ? (user, ticket) : (null, null);
    }
    private async Task<bool> Active(IdentityUser user) =>
        !await db.EmployeeProfiles.AnyAsync(x => x.UserId == user.Id && !x.IsActive);
    private string? Local(string? url) => Url.IsLocalUrl(url) ? url : null;
    private async Task<string> Destination(IdentityUser user, string? url)
    {
        if (Local(url) is { } local) return local;
        if (await users.IsInRoleAsync(user, Roles.Admin)) return "/AdminDashboard";
        if (await users.IsInRoleAsync(user, Roles.SourcingOfficer)) return "/SourcingDashboard";
        if (await users.IsInRoleAsync(user, Roles.TechnicalCommittee) || await users.IsInRoleAsync(user, Roles.FinancialCommittee) ||
            await users.IsInRoleAsync(user, Roles.SourcingManager)) return "/CommitteeReviews";
        if (await users.IsInRoleAsync(user, Roles.TopCommittee)) return "/EvaluationWorkflow";
        if (await users.IsInRoleAsync(user, "RequestingDepartment")) return "/PurchaseRequisitions/Dashboard";
        return await db.SupplierProfiles.AnyAsync(x => x.UserId == user.Id) ? "/SupplierWorkspace" : "/";
    }
}

public sealed record BiometricInput(bool Manage, string? Data, string? Password);

public sealed record DirectBiometricInput(string? Email, string? Data, string? ReturnUrl);
