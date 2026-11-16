using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace SourcingTenderSystem.Services.Biometrics;

public sealed class BiometricEnforcementMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, UserManager<IdentityUser> users)
    {
        var endpoint = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
        // Supplier onboarding must finish before requiring sign-in security setup.
        if (endpoint is not null && endpoint.ControllerName != "Account" &&
            endpoint.ControllerName != "SupplierRegistration" && context.User.Identity?.IsAuthenticated == true)
        {
            var user = await users.GetUserAsync(context.User);
            if (user is not null && (await users.GetPasskeysAsync(user)).Count == 0)
            {
                if (HttpMethods.IsGet(context.Request.Method))
                    context.Response.Redirect(context.Request.PathBase + "/Account/BiometricSetup");
                else context.Response.StatusCode = 403;
                return;
            }
        }
        await next(context);
    }
}
