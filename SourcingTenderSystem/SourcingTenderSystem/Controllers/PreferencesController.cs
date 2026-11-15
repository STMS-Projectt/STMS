using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;

namespace SourcingTenderSystem.Controllers;

public sealed class PreferencesController : Controller
{
    [HttpPost]
    public IActionResult Language(string language, string? returnUrl)
    {
        if (language is not ("ar" or "en")) return BadRequest();
        Response.Cookies.Append(CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture("en-US", language)),
            new CookieOptions { IsEssential = true, HttpOnly = true, Secure = Request.IsHttps,
                SameSite = SameSiteMode.Lax, MaxAge = TimeSpan.FromDays(365) });
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/");
    }
}
