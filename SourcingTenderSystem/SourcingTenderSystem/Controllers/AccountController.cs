using System.Security.Claims;
using SourcingTenderSystem.Services.Biometrics;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SourcingTenderSystem.Data;
using SourcingTenderSystem.Security;

using SourcingTenderSystem.ViewModels;
using SourcingTenderSystem.ViewModels.Biometrics;

namespace SourcingTenderSystem.Controllers;

[Authorize, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AccountController(SignInManager<IdentityUser> signIn, UserManager<IdentityUser> users,
    ApplicationDbContext db) : Controller
{
    private const string Pending = "BiometricPending";
    [AllowAnonymous, HttpGet]
    public IActionResult Login(string? returnUrl)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction(nameof(Continue));
        return View(new LoginInput { ReturnUrl = Local(returnUrl) });
    }

    [HttpGet]
    public async Task<IActionResult> Continue()
    {
        var user = await CurrentUser();
        if (user is null) return Forbid();
        return (await users.GetPasskeysAsync(user)).Count > 0
            ? LocalRedirect(await Destination(user, null)) : RedirectToAction(nameof(BiometricSetup));
    }

    [AllowAnonymous, HttpPost]
    public async Task<IActionResult> Login(LoginInput input)
    {
        if (!ModelState.IsValid) return View(input);
        var user = await users.FindByEmailAsync(input.Email.Trim());
        if (user is not null && await Active(user) &&
            !await users.GetTwoFactorEnabledAsync(user) &&
            (await signIn.CheckPasswordSignInAsync(user, input.Password, lockoutOnFailure: true)).Succeeded)
        {
            if ((await users.GetPasskeysAsync(user)).Count > 0)
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
    public IActionResult DirectCamera() => FaceUnavailable();

    private IActionResult FaceUnavailable() => StatusCode(StatusCodes.Status503ServiceUnavailable,
        new { message = "Face verification requires a live challenge and is unavailable. Use your password or phone passkey." });

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

    [HttpPost, EnableRateLimiting("face-lab"), RequestSizeLimit(4096)]
    public async Task<IActionResult> FaceLabStart([FromBody] FaceLabInput input, [FromServices] FaceLab lab)
    {
        var user = await CurrentUser();
        if (user is null) return Forbid();
        if (!lab.Enabled) return FaceUnavailable();
        if (input.Enroll && !await ConfirmPassword(user, input.Password))
            return BadRequest(new { message = "Enter your current password to enroll a reference." });
        try { return Ok(lab.Start(user.Id, await users.GetSecurityStampAsync(user), input.Enroll)); }
        catch (FaceLabError error) { return StatusCode(error.Status, new { message = error.Message }); }
    }

    [HttpPost, EnableRateLimiting("face-lab"), RequestSizeLimit(410_000)]
    public async Task<IActionResult> FaceLabCapture([FromBody] FaceLabInput input, [FromServices] FaceLab lab, CancellationToken ct)
    {
        var user = await CurrentUser();
        if (user is null) return Forbid();
        try
        {
            var next = await lab.Capture(user.Id, await users.GetSecurityStampAsync(user), input.Token, input.Frame, ct);
            return next is null ? Ok(new { complete = true }) : Ok(next);
        }
        catch (FaceLabError error) { return StatusCode(error.Status, new { message = error.Message }); }
    }

    [HttpPost, EnableRateLimiting("face-lab"), RequestSizeLimit(4096)]
    public async Task<IActionResult> FaceLabConfirm([FromBody] FaceLabInput input, [FromServices] FaceLab lab, CancellationToken ct)
    {
        var user = await CurrentUser();
        if (user is null) return Forbid();
        try { await lab.ConfirmEnrollment(user.Id, await users.GetSecurityStampAsync(user), input.Token, ct); }
        catch (FaceLabError error) { return StatusCode(error.Status, new { message = error.Message }); }
        await FaceAudit(user, "FaceEnrolled", "Face reference saved (encrypted).");
        const string saved = "Face recognition is set up. You can now use it on the sign-in page.";
        TempData["Message"] = saved;
        return Ok(new { complete = true, message = saved });
    }

    // ---------------- Academic demo: webcam face sign-in (Face++ Detect/Compare, no certified liveness) ----------------

    [AllowAnonymous, HttpPost, EnableRateLimiting("biometric-login"), RequestSizeLimit(4096)]
    public async Task<IActionResult> FaceLoginStart([FromBody] FaceLoginInput input, [FromServices] FaceLab lab)
    {
        if (!lab.DemoLoginEnabled) return FaceUnavailable();
        var email = input.Email?.Trim();
        var user = string.IsNullOrEmpty(email) || email.Length > 256 ? null : await users.FindByEmailAsync(email);
        // Ineligible or unknown accounts receive an indistinguishable decoy challenge that always fails.
        var eligible = user is not null && await CanUseDirectLogin(user);
        try { return Ok(lab.StartLogin(eligible ? user!.Id : null, eligible ? await users.GetSecurityStampAsync(user!) : null)); }
        catch (FaceLabError error) { return StatusCode(error.Status, new { message = error.Message }); }
    }

    [AllowAnonymous, HttpPost, EnableRateLimiting("biometric-login"), RequestSizeLimit(410_000)]
    public async Task<IActionResult> FaceLoginCapture([FromBody] FaceLoginInput input, [FromServices] FaceLab lab, CancellationToken ct)
    {
        FaceLab.LoginStep step;
        try { step = await lab.CaptureLogin(input.Token, input.Frame, ct); }
        catch (FaceLabError error) { return StatusCode(error.Status, new { message = error.Message }); }
        if (step.Next is not null) return Ok(step.Next);
        var user = step.User is null ? null : await users.FindByIdAsync(step.User);
        // Re-check everything server side: the comparison result, the unchanged security stamp, and Identity eligibility.
        if (!step.Rejected && user is not null && step.Stamp == await users.GetSecurityStampAsync(user) && await CanUseDirectLogin(user))
        {
            await users.ResetAccessFailedCountAsync(user);
            await FaceAudit(user, "FaceLoginSucceeded", "Signed in with Face++ demo comparison.");
            return await DirectComplete(user, input.ReturnUrl);
        }
        if (user is not null)
        {
            await users.AccessFailedAsync(user); // counts toward the normal Identity lockout
            await FaceAudit(user, "FaceLoginFailed", "Face++ demo comparison rejected.");
        }
        return Unauthorized(new { message = FaceLab.LoginFailedMessage });
    }

    private async Task FaceAudit(IdentityUser user, string action, string details)
    {
        var employee = await db.EmployeeProfiles.Where(e => e.UserId == user.Id).Select(e => (int?)e.Id).FirstOrDefaultAsync();
        var supplier = employee is null ? await db.SupplierProfiles.Where(x => x.UserId == user.Id).Select(x => (int?)x.Id).FirstOrDefaultAsync() : null;
        db.AuditLogs.Add(new Models.AuditLog { EmployeeProfileId = employee, SupplierProfileId = supplier, Action = action,
            EntityName = "FaceRecognition", EntityId = employee ?? supplier ?? 0, Details = details, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    [HttpPost, EnableRateLimiting("face-lab")]
    public async Task<IActionResult> FaceLabDelete([FromServices] FaceLab lab)
    {
        var user = await CurrentUser();
        if (user is null) return Forbid();
        try { lab.Remove(user.Id); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return StatusCode(503, "Could not remove the face reference. Try again."); }
        await FaceAudit(user, "FaceRemoved", "Face reference removed.");
        TempData["Message"] = "Your face reference was removed.";
        return RedirectToAction(nameof(BiometricSetup));
    }

    [AllowAnonymous, HttpPost, RequestSizeLimit(1_500_000)]
    public IActionResult Camera() => FaceUnavailable();

    [AllowAnonymous, HttpPost]
    public async Task<IActionResult> PhoneOptions([FromBody] BiometricInput input)
    {
        var (user, _) = await Resolve(input.Manage);
        if (user is null) return Unauthorized(new { message = "Your session expired. Sign in again." });
        var hasPhone = (await users.GetPasskeysAsync(user)).Count > 0;
        if (input.Manage && (hasPhone || !await ConfirmPassword(user, input.Password)))
            return BadRequest(new { message = "Enter your current password before adding your phone." });
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
            if (!hasPhone)
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
        HasFingerprint = (await users.GetPasskeysAsync(user)).Count > 0,
        Manage = manage, ReturnUrl = await Destination(user, returnUrl)
    };

    private Task<bool> ConfirmPassword(IdentityUser user, string? password) => string.IsNullOrEmpty(password)
        ? Task.FromResult(false) : CheckPassword(user, password);
    private async Task<bool> CheckPassword(IdentityUser user, string password) =>
        (await signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true)).Succeeded;
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

public sealed record FaceLabInput(bool Enroll, string? Password, string? Token, string? Frame);

public sealed record DirectBiometricInput(string? Data, string? ReturnUrl);

public sealed record FaceLoginInput(string? Email, string? Token, string? Frame, string? ReturnUrl);
