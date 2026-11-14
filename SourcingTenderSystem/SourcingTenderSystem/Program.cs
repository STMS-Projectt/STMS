using System.Threading.RateLimiting;
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
    options.Cookie.Name = ".STMS.BiometricAuth";
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
});
// Recheck the security stamp before honoring a session after a role or account change.
builder.Services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero);
builder.Services.AddControllersWithViews(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddSingleton<SupplierFileStore>();
// AI Advisor - Ollama
builder.Services.AddHttpClient(BidAiAdvisor.HttpClientName, client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Ollama:BaseUrl"] is { Length: > 0 } url ? url.TrimEnd('/') + "/" : "http://localhost:11434/");
    client.Timeout = TimeSpan.FromSeconds(builder.Configuration.GetValue("Ollama:TimeoutSeconds", 300));
});

builder.Services.AddSingleton<BidAiAdvisor>();

// Face++ Biometrics
builder.Services.AddHttpClient("FacePlusPlus");

builder.Services.AddScoped<
    SourcingTenderSystem.Services.Biometrics.IBiometricService,
    SourcingTenderSystem.Services.Biometrics.BiometricService>();

builder.Services.AddAuthentication().AddCookie("BiometricPending", options =>
{
    options.Cookie.Name = ".STMS.BiometricPending";
    options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
    options.SlidingExpiration = false;
});
builder.Services.Configure<IdentityPasskeyOptions>(options =>
{
    options.UserVerificationRequirement = "required";
    options.ResidentKeyRequirement = "required";
    options.AuthenticatorAttachment = "platform";
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("biometric-login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 12, Window = TimeSpan.FromMinutes(2) }));
});
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
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<SourcingTenderSystem.Services.Biometrics.BiometricEnforcementMiddleware>();

app.MapStaticAssets();
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
