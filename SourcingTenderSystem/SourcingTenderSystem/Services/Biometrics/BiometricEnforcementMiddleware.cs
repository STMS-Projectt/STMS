using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

namespace SourcingTenderSystem.Services.Biometrics;

public class BiometricEnforcementMiddleware
{
    private readonly RequestDelegate _next;

    public BiometricEnforcementMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, UserManager<IdentityUser> userManager)
    {
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";

        // Paths allowed without prior biometric enrollment
        var isExempt = path.StartsWith("/account/login") ||
                       path.StartsWith("/account/biometricverify") ||
                       path.StartsWith("/account/processbiometricverify") ||
                       path.StartsWith("/account/facelogin") ||
                       path.StartsWith("/account/fingerprintlogin") ||
                       path.StartsWith("/account/biometricsetup") ||
                       path.StartsWith("/account/registerface") ||
                       path.StartsWith("/account/registerfingerprint") ||
                       path.StartsWith("/account/completesetup") ||
                       path.StartsWith("/account/logout") ||
                       path.StartsWith("/account/accessdenied") ||
                       path.StartsWith("/supplierregistration") ||
                       path.StartsWith("/css") ||
                       path.StartsWith("/js") ||
                       path.StartsWith("/lib") ||
                       path.StartsWith("/sourcing-desk.svg") ||
                       path.StartsWith("/favicon.ico");

        if (context.User.Identity?.IsAuthenticated == true && !isExempt)
        {
            var user = await userManager.GetUserAsync(context.User);
            if (user != null)
            {
                var isEnrolled = await userManager.GetAuthenticationTokenAsync(user, "Biometrics", "IsEnrolled");
                if (string.IsNullOrEmpty(isEnrolled))
                {
                    context.Response.Redirect($"/Account/BiometricVerify?email={user.Email}");
                    return;
                }
            }
        }

        await _next(context);
    }
}
