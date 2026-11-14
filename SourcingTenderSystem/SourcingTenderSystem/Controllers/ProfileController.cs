using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace SourcingTenderSystem.Controllers;

[Authorize, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ProfileController(UserManager<IdentityUser> users) : Controller
{
    private const string Provider = "Profile";
    private const string PhotoKey = "Photo";
    private const int MaxBytes = 1024 * 1024;

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Forbid();
        ViewData["HasPhoto"] = await users.GetAuthenticationTokenAsync(user, Provider, PhotoKey) is not null;
        return View(model: user.Email ?? user.UserName ?? "My account");
    }

    [HttpPost, RequestSizeLimit(MaxBytes + 65536)]
    public async Task<IActionResult> Upload(IFormFile? photo)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Forbid();
        if (photo is null || photo.Length < 12 || photo.Length > MaxBytes)
            return Fail("Choose a JPEG or PNG image up to 1 MB.");
        using var buffer = new MemoryStream((int)photo.Length);
        await photo.CopyToAsync(buffer, HttpContext.RequestAborted);
        var bytes = buffer.ToArray();
        var type = ImageType(bytes);
        if (type is null) return Fail("Choose a valid JPEG or PNG image. SVG and other file types are not supported.");
        var result = await users.SetAuthenticationTokenAsync(user, Provider, PhotoKey, Convert.ToBase64String(bytes));
        if (!result.Succeeded) return Fail("Could not save your photo. Try again.");
        TempData["Message"] = "Profile photo updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Photo()
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Forbid();
        var saved = await users.GetAuthenticationTokenAsync(user, Provider, PhotoKey);
        if (string.IsNullOrEmpty(saved)) return NotFound();
        byte[] bytes;
        try { bytes = Convert.FromBase64String(saved); }
        catch (FormatException) { return NotFound(); }
        var mime = ImageType(bytes);
        if (mime is null) return NotFound();
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(bytes, mime);
    }

    [HttpPost]
    public async Task<IActionResult> Remove()
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Forbid();
        if (!(await users.RemoveAuthenticationTokenAsync(user, Provider, PhotoKey)).Succeeded)
            return Fail("Could not remove your photo. Try again.");
        TempData["Message"] = "Profile photo removed.";
        return RedirectToAction(nameof(Index));
    }

    private IActionResult Fail(string message)
    {
        TempData["ProfileError"] = message;
        return RedirectToAction(nameof(Index));
    }

    private static string? ImageType(byte[] bytes)
    {
        if (bytes.Length < 12) return null;
        if (bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) &&
            bytes.AsSpan(12).StartsWith("IHDR"u8) && bytes.AsSpan().EndsWith(new byte[] { 73, 69, 78, 68, 174, 66, 96, 130 }))
            return "image/png";
        if (bytes.AsSpan().StartsWith(new byte[] { 255, 216, 255 }) && bytes.AsSpan().EndsWith(new byte[] { 255, 217 }))
            return "image/jpeg";
        return null;
    }
}
