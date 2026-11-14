using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderSystem.Data;
using SourcingTenderSystem.Security;
using SourcingTenderSystem.Services.Biometrics;
using SourcingTenderSystem.ViewModels;
using SourcingTenderSystem.ViewModels.Biometrics;

namespace SourcingTenderSystem.Controllers;

[Authorize]
public sealed class AccountController(
    SignInManager<IdentityUser> signIn,
    UserManager<IdentityUser> users,
    ApplicationDbContext db,
    IBiometricService biometrics) : Controller
{
    // Minimum Face++ confidence (0–100) required to pass face verification
    private const double FaceConfidenceThreshold = 75.0;

    // ─────────────────────────── LOGIN ───────────────────────────

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
            await users.CheckPasswordAsync(user, input.Password))
        {
            // Password verified → redirect to mandatory biometric gate
            TempData["BiometricEmail"] = user.Email;
            TempData["BiometricReturnUrl"] = input.ReturnUrl;
            return RedirectToAction(nameof(BiometricVerify), new { email = user.Email, returnUrl = input.ReturnUrl });
        }

        ModelState.AddModelError("", "Unable to sign in with those credentials.");
        return View(input);
    }

    // ─────────────────────────── BIOMETRIC VERIFY (2FA Gate) ───────────────────────────

    [AllowAnonymous, HttpGet]
    public async Task<IActionResult> BiometricVerify(string? email, string? returnUrl, bool? reset = false)
    {
        var targetEmail = email ?? TempData["BiometricEmail"]?.ToString();
        if (string.IsNullOrWhiteSpace(targetEmail))
            return RedirectToAction(nameof(Login));

        var user = await users.FindByEmailAsync(targetEmail.Trim());
        if (user == null)
            return RedirectToAction(nameof(Login));

        // If user requested a reset/re-enrollment of biometrics
        if (reset == true)
        {
            await users.RemoveAuthenticationTokenAsync(user, "Biometrics", "FaceToken");
            await users.RemoveAuthenticationTokenAsync(user, "Biometrics", "FingerprintCredentialId");
            await users.RemoveAuthenticationTokenAsync(user, "Biometrics", "IsEnrolled");
            await users.RemoveAuthenticationTokenAsync(user, "Biometrics", "FaceImageUrl");
        }

        var faceToken = await users.GetAuthenticationTokenAsync(user, "Biometrics", "FaceToken");
        var faceImageUrl = await users.GetAuthenticationTokenAsync(user, "Biometrics", "FaceImageUrl");
        var fp = await users.GetAuthenticationTokenAsync(user, "Biometrics", "FingerprintCredentialId");
        var isEnrolled = await users.GetAuthenticationTokenAsync(user, "Biometrics", "IsEnrolled");

        var employee = await db.EmployeeProfiles.FirstOrDefaultAsync(e => e.UserId == user.Id);
        var supplier = await db.SupplierProfiles.FirstOrDefaultAsync(s => s.UserId == user.Id);

        var hasFace = !string.IsNullOrWhiteSpace(faceToken);
        var hasFp = !string.IsNullOrWhiteSpace(fp);

        var model = new BiometricVerifyViewModel
        {
            UserEmail = user.Email ?? user.UserName ?? "User",
            FullName = employee?.FullName ?? supplier?.CompanyName ?? user.UserName ?? "User",
            IsEnrolled = isEnrolled == "true" && (hasFace || hasFp),
            HasFace = hasFace,
            HasFingerprint = hasFp,
            FaceImageUrl = faceImageUrl,
            ReturnUrl = returnUrl,
            Mode = (isEnrolled == "true" && (hasFace || hasFp)) ? "Verify" : "EnrollAndVerify"
        };

        return View(model);
    }

    // ─────────────────────────── PROCESS BIOMETRIC (Enroll & Verify) ───────────────────────────

    [AllowAnonymous, HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> ProcessBiometricVerify([FromBody] ProcessBiometricRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            return BadRequest(new BiometricAuthResponse { Success = false, Message = "Email is required." });

        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user == null)
            return Unauthorized(new BiometricAuthResponse { Success = false, Message = "User account not found." });

        if (await db.EmployeeProfiles.AnyAsync(x => x.UserId == user.Id && !x.IsActive))
            return Unauthorized(new BiometricAuthResponse { Success = false, Message = "This account is inactive." });

        // ── FACE ──
        if (request.BiometricType == "Face")
        {
            if (string.IsNullOrWhiteSpace(request.ImageBase64))
                return BadRequest(new BiometricAuthResponse { Success = false, Message = "No image received. Please open the camera and try again." });

            // Call Face++ to detect a real face in the submitted image
            var newFaceToken = await biometrics.DetectFaceAsync(request.ImageBase64);
            if (string.IsNullOrWhiteSpace(newFaceToken))
                return Unauthorized(new BiometricAuthResponse
                {
                    Success = false,
                    Message = "No face detected in the image. Please position your face clearly in front of the camera (covering the camera is blocked)."
                });

            // ── ENROLLMENT ──
            if (request.Mode == "Enroll")
            {
                // Store the face_token returned by Face++ as the registered biometric
                await users.SetAuthenticationTokenAsync(user, "Biometrics", "FaceToken", newFaceToken);
                await users.SetAuthenticationTokenAsync(user, "Biometrics", "IsEnrolled", "true");

                // Save captured face photo to disk & record URL in tokens
                try
                {
                    var webRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                    var uploadsDir = Path.Combine(webRoot, "uploads", "biometrics");
                    Directory.CreateDirectory(uploadsDir);

                    var base64Data = request.ImageBase64.Contains(',')
                        ? request.ImageBase64[(request.ImageBase64.IndexOf(',') + 1)..]
                        : request.ImageBase64;
                    var imageBytes = Convert.FromBase64String(base64Data);
                    var filePath = Path.Combine(uploadsDir, $"{user.Id}.jpg");
                    await System.IO.File.WriteAllBytesAsync(filePath, imageBytes);

                    await users.SetAuthenticationTokenAsync(user, "Biometrics", "FaceImageUrl", $"/uploads/biometrics/{user.Id}.jpg");
                }
                catch
                {
                    // Non-critical if file system write fails
                }

                await signIn.SignInAsync(user, isPersistent: false);
                var redirect = await GetRedirectPathAsync(user, request.ReturnUrl);
                return Ok(new BiometricAuthResponse
                {
                    Success = true,
                    Message = "Face registered successfully with Face++ AI! Welcome to Sourcing Desk.",
                    RedirectUrl = redirect
                });
            }

            // ── VERIFICATION ──
            var storedFaceToken = await users.GetAuthenticationTokenAsync(user, "Biometrics", "FaceToken");
            if (string.IsNullOrWhiteSpace(storedFaceToken))
                return Unauthorized(new BiometricAuthResponse
                {
                    Success = false,
                    Message = "No registered face found for this account. Please enroll first."
                });

            // Compare live face with stored face using Face++ Compare API
            var confidence = await biometrics.CompareFaceTokensAsync(storedFaceToken, newFaceToken);

            if (confidence < FaceConfidenceThreshold)
            {
                var msg = confidence < 0
                    ? "Face comparison failed. Please try again."
                    : $"Identity mismatch — confidence {confidence:F1}% (required: {FaceConfidenceThreshold}%). Access denied.";

                return Unauthorized(new BiometricAuthResponse { Success = false, Message = msg });
            }

            await signIn.SignInAsync(user, isPersistent: false);
            var redirectUrl = await GetRedirectPathAsync(user, request.ReturnUrl);
            return Ok(new BiometricAuthResponse
            {
                Success = true,
                Message = $"Identity confirmed with Face++ ({confidence:F1}% match). Signing you in...",
                RedirectUrl = redirectUrl
            });
        }

        // ── FINGERPRINT ──
        if (request.BiometricType == "Fingerprint")
        {
            if (request.Mode == "Enroll")
            {
                if (string.IsNullOrWhiteSpace(request.CredentialId))
                    return BadRequest(new BiometricAuthResponse { Success = false, Message = "Valid fingerprint credential is required." });

                await users.SetAuthenticationTokenAsync(user, "Biometrics", "FingerprintCredentialId", request.CredentialId.Trim());
                await users.SetAuthenticationTokenAsync(user, "Biometrics", "IsEnrolled", "true");

                await signIn.SignInAsync(user, isPersistent: false);
                var redirect = await GetRedirectPathAsync(user, request.ReturnUrl);
                return Ok(new BiometricAuthResponse
                {
                    Success = true,
                    Message = "Fingerprint registered successfully! Welcome to Sourcing Desk.",
                    RedirectUrl = redirect
                });
            }

            var storedCredential = await users.GetAuthenticationTokenAsync(user, "Biometrics", "FingerprintCredentialId");
            if (string.IsNullOrWhiteSpace(storedCredential))
                return Unauthorized(new BiometricAuthResponse
                {
                    Success = false,
                    Message = "Fingerprint is not enrolled for this account yet."
                });

            if (!biometrics.VerifyFingerprint(storedCredential, request.CredentialId ?? ""))
                return Unauthorized(new BiometricAuthResponse
                {
                    Success = false,
                    Message = "Fingerprint verification failed. Hardware credential mismatch."
                });

            await signIn.SignInAsync(user, isPersistent: false);
            var redirectUrl = await GetRedirectPathAsync(user, request.ReturnUrl);
            return Ok(new BiometricAuthResponse
            {
                Success = true,
                Message = "Fingerprint verified! Signing you in...",
                RedirectUrl = redirectUrl
            });
        }

        return BadRequest(new BiometricAuthResponse { Success = false, Message = "Invalid verification request." });
    }

    // ─────────────────────────── DIRECT BIOMETRIC LOGIN ───────────────────────────

    [AllowAnonymous, HttpPost]
    [IgnoreAntiforgeryToken]
    public Task<IActionResult> FaceLogin([FromBody] FaceLoginRequest request) =>
        ProcessBiometricVerify(new ProcessBiometricRequest
        {
            Email = request.Email,
            BiometricType = "Face",
            Mode = "Verify",
            ImageBase64 = request.ImageBase64,
            ReturnUrl = request.ReturnUrl
        });

    [AllowAnonymous, HttpPost]
    [IgnoreAntiforgeryToken]
    public Task<IActionResult> FingerprintLogin([FromBody] FingerprintLoginRequest request) =>
        ProcessBiometricVerify(new ProcessBiometricRequest
        {
            Email = request.Email,
            BiometricType = "Fingerprint",
            Mode = "Verify",
            CredentialId = request.CredentialId,
            ReturnUrl = request.ReturnUrl
        });

    // ─────────────────────────── LOGOUT / ACCESS DENIED ───────────────────────────

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

    // ─────────────────────────── HELPERS ───────────────────────────

    private async Task<string> GetRedirectPathAsync(IdentityUser user, string? returnUrl)
    {
        if (Url.IsLocalUrl(returnUrl)) return returnUrl;
        if (await users.IsInRoleAsync(user, Roles.Admin)) return Url.Action("Index", "AdminDashboard") ?? "/AdminDashboard";
        if (await users.IsInRoleAsync(user, Roles.SourcingOfficer)) return Url.Action("Index", "SourcingDashboard") ?? "/SourcingDashboard";
        if (await users.IsInRoleAsync(user, Roles.TechnicalCommittee) ||
            await users.IsInRoleAsync(user, Roles.FinancialCommittee) ||
            await users.IsInRoleAsync(user, Roles.SourcingManager))
            return Url.Action("Index", "CommitteeReviews") ?? "/CommitteeReviews";
        if (await users.IsInRoleAsync(user, Roles.TopCommittee))
            return Url.Action("Index", "EvaluationWorkflow") ?? "/EvaluationWorkflow";
        if (await users.IsInRoleAsync(user, "RequestingDepartment"))
            return Url.Action("Dashboard", "PurchaseRequisitions") ?? "/PurchaseRequisitions/Dashboard";
        if (await db.SupplierProfiles.AnyAsync(x => x.UserId == user.Id))
            return Url.Action("Index", "SupplierWorkspace") ?? "/SupplierWorkspace";
        return Url.Action("Index", "Home") ?? "/";
    }
}
