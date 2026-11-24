using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderSystem.Data;
using SourcingTenderSystem.Services;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentity<IdentityUser, IdentityRole>(options =>
{
    options.User.RequireUniqueEmail = true;
    options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
    options.Stores.MaxLengthForKeys = 128;
})
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = ".STMS.BiometricAuth.v2";
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
});
// Recheck the security stamp before honoring a session after a role or account change.
builder.Services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero);
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    options.Filters.Add(new UiValidationFilter());
    options.ModelMetadataDetailsProviders.Add(new UiDisplayMetadata());
});
builder.Services.AddSingleton<UiText>();
builder.Services.AddSingleton<SupplierFileStore>();
// AI Advisor - Ollama
builder.Services.AddHttpClient(BidAiAdvisor.HttpClientName, client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Ollama:BaseUrl"] is { Length: > 0 } url ? url.TrimEnd('/') + "/" : "http://localhost:11434/");
    client.Timeout = TimeSpan.FromSeconds(builder.Configuration.GetValue("Ollama:TimeoutSeconds", 300));
});

builder.Services.AddSingleton<BidAiAdvisor>();
builder.Services.AddSingleton<WinnerAiBriefing>();
builder.Services.AddSingleton<EvaluationWeightAdvisor>();
// AI Intelligence page + chatbot. The chatbot uses Qwen3.5-397B-A17B through Hugging Face Inference Providers.
// Token: dotnet user-secrets set "Qwen:ApiKey" "hf_xxx"  (or the HF_TOKEN environment variable).
builder.Services.AddHttpClient(QwenChatbot.HttpClientName, client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Qwen:BaseUrl"] is { Length: > 0 } url ? url.TrimEnd('/') + "/" : "https://router.huggingface.co/v1/");
    client.Timeout = TimeSpan.FromSeconds(builder.Configuration.GetValue("Qwen:TimeoutSeconds", 90));
});
builder.Services.AddSingleton<QwenChatbot>();
builder.Services.AddScoped<AiInsights>();
// Free exchange-rate reference (Frankfurter v2) for USD-equivalent bid display only.
builder.Services.AddMemoryCache();

var authentication = builder.Services.AddAuthentication().AddCookie("BiometricPending", options =>
{
    options.Cookie.Name = ".STMS.BiometricPending.v2";
    options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
    options.SlidingExpiration = false;
});
// Google sign-in is only for accounts an administrator already created; it never creates users.
if (builder.Configuration["Authentication:Google:ClientId"] is { Length: > 0 } googleClientId &&
    builder.Configuration["Authentication:Google:ClientSecret"] is { Length: > 0 } googleClientSecret)
{
    authentication.AddGoogle(options =>
    {
        options.ClientId = googleClientId.Trim();
        options.ClientSecret = googleClientSecret.Trim();
        options.SignInScheme = IdentityConstants.ExternalScheme;
        Microsoft.AspNetCore.Authentication.ClaimActionCollectionMapExtensions.MapJsonKey(options.ClaimActions, "email_verified", "email_verified");
        // A cancelled consent or a misconfigured client returns to the sign-in page instead of a 500 error.
        options.Events.OnRemoteFailure = context =>
        {
            context.Response.Redirect(context.Request.PathBase + "/Account/ExternalLoginCallback?remoteError=1");
            context.HandleResponse();
            return Task.CompletedTask;
        };
    });
}
builder.Services.Configure<IdentityPasskeyOptions>(options =>
{
    options.UserVerificationRequirement = "required";
    options.ResidentKeyRequirement = "required";
    options.AuthenticatorAttachment = null;
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("public-contact", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 8, Window = TimeSpan.FromMinutes(10) }));
    options.AddPolicy("face-lab", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ??
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 16, Window = TimeSpan.FromMinutes(5) }));
    options.AddPolicy("biometric-login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 12, Window = TimeSpan.FromMinutes(2) }));
    options.AddPolicy("guarantee-verify", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1) }));
    options.AddPolicy("evaluation-weight-advice", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<SourcingTenderSystem.Services.Biometrics.FaceLab>();
builder.Services.AddHttpClient("FreeFacePlusPlus", client =>
{
    client.Timeout = TimeSpan.FromSeconds(8);
    client.MaxResponseContentBufferSize = 64 * 1024;
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddScoped<ClarificationRouting>();
builder.Services.AddScoped<ProtectedPdf>();
ProtectedPdf.ConfigureFonts(builder.Configuration);
builder.Services.AddHostedService<TenderClock>();
builder.Services.AddScoped<BidDocumentAccess>();
// Award e-mails (AI-written text, sent by SMTP or saved to App_Data/OutgoingEmails)
builder.Services.AddSingleton<AwardEmailService>();
var app = builder.Build();

if (args.Contains("--reset-local-test-data"))
{
    if (!app.Environment.IsDevelopment())
        throw new InvalidOperationException("Test data reset requires Development.");
    await using var scope = app.Services.CreateAsyncScope();
    await LocalTestReset.RunAsync(scope.ServiceProvider, app.Configuration);
    return;
}
if (args.Contains("--initialize-admin"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await AdminSetup.InitializeAsync(scope.ServiceProvider, app.Configuration);
    return;
}

if (args.Contains("--initialize-sourcing-demo"))
{
    if (!app.Environment.IsDevelopment())
        throw new InvalidOperationException("Sourcing demo accounts can only be created in Development.");
    await using var scope = app.Services.CreateAsyncScope();
    await SourcingDemoSetup.InitializeAsync(scope.ServiceProvider, app.Configuration);
    return;
}
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
var languages = new RequestLocalizationOptions().SetDefaultCulture("en-US")
    .AddSupportedCultures("en-US").AddSupportedUICultures("en-US", "en", "ar");
languages.RequestCultureProviders = [new CookieRequestCultureProvider()];
app.UseRequestLocalization(languages);
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/uploads/biometrics", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = 404;
        return;
    }
    await next(context);
});
app.UseRouting();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.UseMiddleware<SourcingTenderSystem.Services.Biometrics.BiometricEnforcementMiddleware>();

app.MapStaticAssets();
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();

public partial class Program { }
