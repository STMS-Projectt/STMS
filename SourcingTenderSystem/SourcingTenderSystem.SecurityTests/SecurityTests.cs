using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using SourcingTenderSystem.Controllers;
using SourcingTenderSystem.Services;
using Xunit;

public sealed class SecurityTests
{
    [Theory]
    [InlineData("Supplier", "Open", "Draft", true, true, false, false, true)]
    [InlineData("Supplier", "Closed", "Compliant", false, true, false, false, false)]
    [InlineData("TechnicalCommittee", "Under Evaluation", "Compliant", false, true, true, false, true)]
    [InlineData("TechnicalCommittee", "Under Evaluation", "Compliant", false, true, false, false, false)]
    [InlineData("TopCommittee", "Awaiting Committee Decision", "Compliant", false, true, false, false, false)]
    [InlineData("TopCommittee", "Awaiting Committee Decision", "Compliant", false, true, false, true, true)]
    [InlineData("TopCommittee", "Open", "Compliant", false, true, false, true, false)]
    [InlineData("SourcingOfficer", "Open", "Submitted", false, true, false, false, false)]
    [InlineData("SourcingOfficer", "Closed", "Submitted", false, true, false, false, true)]
    [InlineData("SourcingManager", "Awaiting Manager Review", "Compliant", false, true, true, false, true)]
    public void Document_policy(string role, string stage, string status, bool ownSupplier, bool active,
        bool exactEvaluationSeat, bool exactTopSeat, bool expected)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "test"));
        Assert.Equal(expected, BidDocumentAccess.Allowed(user, stage, status, status != "Draft",
            ownSupplier, active, exactEvaluationSeat, exactTopSeat));
    }

    [Theory]
    [InlineData("Camera")]
    [InlineData("DirectCamera")]
    public async Task StaticJpegNeverAuthenticates(string endpoint)
    {
        using var factory = new AuditApp();
        using var client = Client(factory);
        var token = await Token(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/Account/" + endpoint);
        request.Headers.Add("RequestVerificationToken", token);
        request.Content = new StringContent("""
            {"data":"data:image/jpeg;base64,/9j/2Q==","manage":false}
            """, System.Text.Encoding.UTF8, "application/json");
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out var cookies) &&
            cookies.Any(c => c.StartsWith(".STMS.BiometricAuth", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("/Account/FaceLabStart")]
    [InlineData("/Account/FaceLabCapture")]
    public async Task AnonymousCannotUseFaceLab(string endpoint)
    {
        using var factory = new AuditApp();
        using var client = Client(factory);
        var token = await Token(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Add("RequestVerificationToken", token);
        request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    [Theory]
    [InlineData("/BidDocuments/Download/42")]
    [InlineData("/BidDocuments/Download/42?preview=true")]
    public async Task AnonymousDocumentIsChallenged(string path)
    {
        using var factory = new AuditApp();
        using var client = Client(factory);
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task PasskeyChallengeStillRequiresUserVerification()
    {
        using var factory = new AuditApp();
        using var client = Client(factory);
        var token = await Token(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/Account/DirectPhoneOptions");
        request.Headers.Add("RequestVerificationToken", token);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var options = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("required", options.RootElement.GetProperty("userVerification").GetString());
    }

    [Fact]
    public void ConfigurationContainsNoFaceCredentials()
    {
        using var factory = new AuditApp();
        var env = factory.Services.GetRequiredService<IWebHostEnvironment>();
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(env.ContentRootPath,"appsettings.json")));
        var config = json.RootElement.GetProperty("FacePlusPlus");
        Assert.True(string.IsNullOrEmpty(config.GetProperty("ApiKey").GetString()));
        Assert.True(string.IsNullOrEmpty(config.GetProperty("ApiSecret").GetString()));
        Assert.DoesNotContain(Directory.EnumerateFiles(Path.Combine(env.ContentRootPath,"wwwroot"), "*", SearchOption.AllDirectories)
            , path => path.Replace((char)92, (char)47).Contains("/uploads/biometrics/"));
    }

    private static HttpClient Client(AuditApp factory) => factory.CreateClient(new()
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

    private static async Task<string> Token(HttpClient client)
    {
        var html = await client.GetStringAsync("/__security-test-token");
        var token = System.Text.RegularExpressions.Regex.Match(html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        return token;
    }

    private sealed class AuditApp : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string,string?> { ["ConnectionStrings:DefaultConnection"] =
                    "Server=localhost,1;Database=STMS_Synthetic_Unused;Integrated Security=True;Connect Timeout=1" }));
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IDataProtectionProvider, EphemeralDataProtectionProvider>();
                services.AddSingleton<IStartupFilter, TokenEndpoint>();
                foreach (var item in services.Where(d => d.ServiceType == typeof(IHostedService) &&
                    d.ImplementationType == typeof(TenderClock)).ToList()) services.Remove(item);
            });
        }
    }
}

public sealed class TokenEndpoint : IStartupFilter
{
    public Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> Configure(Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> next) =>
        app =>
        {
            app.Use(nextRequest => async context =>
            {
                if (context.Request.Path != "/__security-test-token") { await nextRequest(context); return; }
                var tokens = context.RequestServices.GetRequiredService<IAntiforgery>().GetAndStoreTokens(context);
                await context.Response.WriteAsync($"<input name=\"__RequestVerificationToken\" value=\"{tokens.RequestToken}\" />");
            });
            next(app);
        };
}
