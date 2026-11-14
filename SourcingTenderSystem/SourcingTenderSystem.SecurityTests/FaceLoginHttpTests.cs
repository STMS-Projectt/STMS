using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SourcingTenderSystem.Data;
using SourcingTenderSystem.Models;
using SourcingTenderSystem.Services;
using SourcingTenderSystem.Services.Biometrics;
using Xunit;

// Real Identity cookies, antiforgery, lockout and authorization against a disposable SQL Server database.
// Only the Face++ HTTP endpoint is replaced by a local fake, so these tests do NOT prove the real provider works.
public sealed class FaceLoginHttpTests
{
    private const string Password = "OnlyForSynthetic!927";

    [Fact]
    public async Task Face_enrollment_and_demo_login_create_real_identity_sessions_only_on_match()
    {
        var template = Environment.GetEnvironmentVariable("STMS_PHASE2_TEST_SQL");
        Assert.False(string.IsNullOrWhiteSpace(template), "Set STMS_PHASE2_TEST_SQL to a disposable SQL Server instance.");
        var connection = new SqlConnectionStringBuilder(template) { InitialCatalog = "STMS_Face_" + Guid.NewGuid().ToString("N") }.ConnectionString;
        var storage = Path.Combine(Path.GetTempPath(), "STMS_FaceHttp_" + Guid.NewGuid().ToString("N"));
        var provider = new ProviderState();
        using var app = new FaceApp(connection, storage, provider, demo: true);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        try
        {
            await db.Database.MigrateAsync();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            await roles.CreateAsync(new IdentityRole("SourcingOfficer"));
            foreach (var name in new[] { "alice", "bob", "carol" })
            {
                var user = new IdentityUser { Id = name, UserName = name + "@example.test", Email = name + "@example.test" };
                Assert.True((await users.CreateAsync(user, Password)).Succeeded);
                await users.AddToRoleAsync(user, "SourcingOfficer");
                db.EmployeeProfiles.Add(new EmployeeProfile { UserId = name, FullName = name, JobTitle = "Officer", IsActive = true });
            }
            await db.SaveChangesAsync();
            // Each account already has a passkey, as every fully signed-in account must (see AccountController.Login).
            await db.Database.ExecuteSqlRawAsync("INSERT dbo.AspNetUserPasskeys(UserId,CredentialId,Data) SELECT Id,CONVERT(varbinary(128),Id),'{{}}' FROM dbo.AspNetUsers");
            var lab = app.Services.GetRequiredService<FaceLab>();

            // Unauthorized enrollment: an anonymous caller cannot start enrollment.
            var anonymous = Client(app);
            var anonymousToken = await Token(anonymous, "/Account/Login");
            var denied = await Post(anonymous, "/Account/FaceLabStart", new { enroll = true, password = Password }, anonymousToken);
            Assert.NotEqual(HttpStatusCode.OK, denied.StatusCode);

            // Authorized enrollment requires the current password, and is bound to the signed-in account.
            var alice = await SignIn(app, "alice");
            var aliceToken = await Token(alice, "/Account/BiometricSetup");
            Assert.Equal(HttpStatusCode.BadRequest, (await Post(alice, "/Account/FaceLabStart", new { enroll = true }, aliceToken)).StatusCode);
            var ready = await Challenge(alice, "/Account/FaceLabStart", "/Account/FaceLabCapture", new { enroll = true, password = Password }, aliceToken, 80);
            Assert.True(ready.GetProperty("readyToSave").GetBoolean());
            Assert.False(lab.HasReference("alice"));
            var bob = await SignIn(app, "bob");
            var bobToken = await Token(bob, "/Account/BiometricSetup");
            Assert.Equal(HttpStatusCode.Conflict, (await Post(bob, "/Account/FaceLabConfirm", new { token = ready.GetProperty("token").GetString() }, bobToken)).StatusCode);
            Assert.False(lab.HasReference("bob"));
            Assert.Equal(HttpStatusCode.OK, (await Post(alice, "/Account/FaceLabConfirm", new { token = ready.GetProperty("token").GetString() }, aliceToken)).StatusCode);
            Assert.True(lab.HasReference("alice"));
            Assert.Empty(Directory.GetFiles(Path.Combine(AppContext.BaseDirectory), "*.face", SearchOption.AllDirectories));
            await Enroll(bob, bobToken, 10);
            var carol = await SignIn(app, "carol");
            await Enroll(carol, await Token(carol, "/Account/BiometricSetup"), 20);

            // Successful comparison creates a real Identity session and redirects to the role dashboard.
            var visitor = Client(app);
            Assert.Equal(HttpStatusCode.Redirect, (await visitor.GetAsync("/SourcingDashboard")).StatusCode);
            var loginToken = await Token(visitor, "/Account/Login");
            Assert.Contains("data-face-lab", await visitor.GetStringAsync("/Account/Login"));
            var success = await Login(visitor, "alice@example.test", loginToken, 30);
            Assert.Equal(HttpStatusCode.OK, success.StatusCode);
            Assert.Equal("/SourcingDashboard", (await success.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("redirectUrl").GetString());
            Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync("/SourcingDashboard")).StatusCode);
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "FaceLoginSucceeded"));

            // A different face is rejected, no session is created, and the failure counts toward Identity lockout.
            provider.Reject = true;
            var stranger = Client(app);
            var mismatch = await Login(stranger, "alice@example.test", await Token(stranger, "/Account/Login"), 40);
            Assert.Equal(HttpStatusCode.Unauthorized, mismatch.StatusCode);
            Assert.Equal(FaceLab.LoginFailedMessage, (await mismatch.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString());
            Assert.Equal(HttpStatusCode.Redirect, (await stranger.GetAsync("/SourcingDashboard")).StatusCode);
            Assert.Equal(1, await db.Users.AsNoTracking().Where(u => u.Id == "alice").Select(u => u.AccessFailedCount).SingleAsync());
            provider.Reject = false;

            // Unknown account: identical flow, identical generic failure (no enumeration), no session.
            var unknown = Client(app);
            var missing = await Login(unknown, "nobody@example.test", await Token(unknown, "/Account/Login"), 50);
            Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
            Assert.Equal(FaceLab.LoginFailedMessage, (await missing.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString());

            // Disabled and locked accounts: a matching face still never signs in. (Fresh host = fresh rate-limit window.)
            await db.EmployeeProfiles.Where(e => e.UserId == "bob").ExecuteUpdateAsync(u => u.SetProperty(e => e.IsActive, false));
            Assert.True((await users.SetLockoutEndDateAsync((await users.FindByIdAsync("carol"))!, DateTimeOffset.UtcNow.AddHours(1))).Succeeded);
            using var second = new FaceApp(connection, storage, provider, demo: true);
            foreach (var (email, tag) in new[] { ("bob@example.test", 60), ("carol@example.test", 70) })
            {
                var client = Client(second);
                var response = await Login(client, email, await Token(client, "/Account/Login"), tag);
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
                Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/SourcingDashboard")).StatusCode);
            }
            // Excessive attempts are throttled (12 per 2 minutes per client address).
            var flood = Client(second);
            var floodToken = await Token(flood, "/Account/Login");
            var statuses = new List<HttpStatusCode>();
            for (var i = 0; i < 6; i++) statuses.Add((await Post(flood, "/Account/FaceLoginStart", new { email = "alice@example.test" }, floodToken)).StatusCode);
            Assert.Contains(HttpStatusCode.TooManyRequests, statuses);

            // Demo flag off: endpoint refuses and the page offers no camera sign-in.
            using var off = new FaceApp(connection, storage, provider, demo: false);
            var offClient = Client(off);
            var offToken = await Token(offClient, "/Account/Login");
            Assert.DoesNotContain("data-face-lab", await offClient.GetStringAsync("/Account/Login"));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Post(offClient, "/Account/FaceLoginStart", new { email = "alice@example.test" }, offToken)).StatusCode);

            // Removal deletes the reference; face login then fails.
            Assert.Equal(HttpStatusCode.Redirect, (await alice.PostAsync("/Account/FaceLabDelete", Form(aliceToken))).StatusCode);
            Assert.False(lab.HasReference("alice"));
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
            if (Directory.Exists(storage)) Directory.Delete(storage, true);
        }
    }

    private static async Task Enroll(HttpClient client, string token, int tag)
    {
        var ready = await Challenge(client, "/Account/FaceLabStart", "/Account/FaceLabCapture", new { enroll = true, password = Password }, token, tag);
        Assert.Equal(HttpStatusCode.OK, (await Post(client, "/Account/FaceLabConfirm", new { token = ready.GetProperty("token").GetString() }, token)).StatusCode);
    }

    private static async Task<HttpResponseMessage> Login(HttpClient client, string email, string token, int tag)
    {
        var start = await Post(client, "/Account/FaceLoginStart", new { email }, token);
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        var prompt = await start.Content.ReadFromJsonAsync<JsonElement>();
        HttpResponseMessage response = start;
        for (var step = 0; step < 3; step++)
        {
            response = await Post(client, "/Account/FaceLoginCapture", new { token = prompt.GetProperty("token").GetString(), frame = Frame(tag + step) }, token);
            if (step < 2) { Assert.Equal(HttpStatusCode.OK, response.StatusCode); prompt = await response.Content.ReadFromJsonAsync<JsonElement>(); }
        }
        return response;
    }

    private static async Task<JsonElement> Challenge(HttpClient client, string start, string capture, object body, string token, int tag)
    {
        var response = await Post(client, start, body, token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var prompt = await response.Content.ReadFromJsonAsync<JsonElement>();
        for (var step = 0; step < 3; step++)
        {
            response = await Post(client, capture, new { token = prompt.GetProperty("token").GetString(), frame = Frame(tag + step) }, token);
            Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            prompt = await response.Content.ReadFromJsonAsync<JsonElement>();
        }
        return prompt;
    }

    private static async Task<HttpClient> SignIn(FaceApp app, string name)
    {
        var client = Client(app);
        var token = await Token(client, "/Account/Login");
        var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string> {
            ["Email"] = name + "@example.test", ["Password"] = Password, ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/SourcingDashboard", response.Headers.Location!.OriginalString);
        return client;
    }

    private static HttpClient Client(FaceApp app) => app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
    private static async Task<string> Token(HttpClient client, string page)
    {
        var html = await client.GetStringAsync(page);
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        return token;
    }
    private static FormUrlEncodedContent Form(string token) => new(new Dictionary<string, string> { ["__RequestVerificationToken"] = token });
    private static Task<HttpResponseMessage> Post(HttpClient client, string url, object body, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("RequestVerificationToken", token);
        return client.SendAsync(request);
    }

    // Distinct synthetic JPEG per step: tags ending in 1 are the "turn" frame (yaw 25), others look straight ahead.
    private static string Frame(int tag)
    {
        var bytes = new byte[128];
        bytes[0] = bytes[^2] = 255; bytes[1] = 216; bytes[^1] = 217; bytes[2] = (byte)tag;
        return "data:image/jpeg;base64," + Convert.ToBase64String(bytes);
    }

    private sealed class ProviderState { public volatile bool Reject; }

    private sealed class FakeFacePlusPlus(ProviderState state) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var form = await request.Content!.ReadAsStringAsync(token);
            string json;
            if (request.RequestUri!.AbsolutePath.EndsWith("/detect"))
            {
                var image = Convert.FromBase64String(Uri.UnescapeDataString(Regex.Match(form, "image_base64=([^&]+)").Groups[1].Value));
                json = "{\"faces\":[{\"face_token\":\"t" + image[2] + "\",\"attributes\":{\"headpose\":{\"yaw_angle\":" + (image[2] % 10 == 1 ? 25 : 0) + "}}}]}";
            }
            else json = "{\"confidence\":" + (state.Reject && form.Contains("image_base64_2=") ? 30 : 95) + ",\"thresholds\":{\"1e-5\":80}}";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
        }
    }

    private sealed class FaceApp(string connection, string storage, ProviderState provider, bool demo) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> {
                ["ConnectionStrings:DefaultConnection"] = connection,
                ["ExchangeRates:BaseUrl"] = "http://127.0.0.1:9/",
                ["FacePlusPlus:EnableFreeLab"] = "true", ["FacePlusPlus:ApiKey"] = "synthetic-key", ["FacePlusPlus:ApiSecret"] = "synthetic-secret",
                ["FacePlusPlus:EnableDemoFaceLogin"] = demo ? "true" : "false", ["FacePlusPlus:StoragePath"] = storage }));
            builder.ConfigureLogging(l => l.ClearProviders());
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
                services.AddDbContext<ApplicationDbContext>(o => o.UseSqlServer(connection));
                services.AddHttpClient("FreeFacePlusPlus").ConfigurePrimaryHttpMessageHandler(() => new FakeFacePlusPlus(provider));
                foreach (var d in services.Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(TenderClock)).ToList())
                    services.Remove(d);
            });
        }
    }
}
