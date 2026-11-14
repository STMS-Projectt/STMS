using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace SourcingTenderSystem.Services.Biometrics;

public sealed class BiometricEnforcementMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, UserManager<IdentityUser> users)
    {
        var endpoint = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
        if (endpoint is not null && endpoint.ControllerName != "Account" && context.User.Identity?.IsAuthenticated == true)
        {
            var user = await users.GetUserAsync(context.User);
            if (user is not null && (await users.GetPasskeysAsync(user)).Count == 0 &&
                string.IsNullOrEmpty(await users.GetAuthenticationTokenAsync(user, "Biometrics", "FaceToken")))
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
