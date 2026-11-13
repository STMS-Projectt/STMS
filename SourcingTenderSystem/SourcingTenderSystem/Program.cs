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
    options.Stores.MaxLengthForKeys = 128;
})
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
});
// Recheck the security stamp before honoring a session after a role or account change.
builder.Services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero);
builder.Services.AddControllersWithViews(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddSingleton<SupplierFileStore>();

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
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
