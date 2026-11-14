using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using SourcingTenderSystem.Services.Biometrics;
using Xunit;

public sealed class FaceLabTests
{
    [Fact]
    public async Task Challenge_is_expiring_single_use_and_bound_to_account_and_stamp()
    {
        using var fixture = new Fixture();
        var start = fixture.Lab.Start("owner", "stamp", true);
        Assert.Equal(409, (await Assert.ThrowsAsync<FaceLabError>(() =>
            fixture.Lab.Capture("other", "stamp", start.Token, Frame(1), default))).Status);
        Assert.Equal(409, (await Assert.ThrowsAsync<FaceLabError>(() =>
            fixture.Lab.Capture("owner", "different", start.Token, Frame(1), default))).Status);
        var next = await fixture.Lab.Capture("owner", "stamp", start.Token, Frame(1), default);
        Assert.Equal(2, next!.Step);
        Assert.Equal(409, (await Assert.ThrowsAsync<FaceLabError>(() =>
            fixture.Lab.Capture("owner", "stamp", start.Token, Frame(2), default))).Status);
        Assert.Equal(422, (await Assert.ThrowsAsync<FaceLabError>(() =>
            fixture.Lab.Capture("owner", "stamp", next.Token, Frame(1), default))).Status);
        Assert.Equal(1, fixture.Provider.Calls);
        Assert.False(fixture.Lab.HasReference("owner"));
        var expired = fixture.Lab.Start("owner", "stamp", true);
        fixture.Clock.Now = fixture.Clock.Now.AddMinutes(3);
        Assert.Equal(409, (await Assert.ThrowsAsync<FaceLabError>(() =>
            fixture.Lab.Capture("owner", "stamp", expired.Token, Frame(2), default))).Status);
        Assert.Equal(1, fixture.Provider.Calls);
    }

    [Fact]
    public async Task Mocked_provider_enrollment_then_match_and_mismatch()
    {
        using var fixture = new Fixture();
        await Complete(fixture, true);
        Assert.True(fixture.Lab.HasReference("owner"));
        Assert.NotEqual(Convert.FromBase64String(Frame(1).Split(',')[1]),
            File.ReadAllBytes(Directory.GetFiles(fixture.Root, "*.face", SearchOption.AllDirectories).Single()));
        await Complete(fixture, false);
        fixture.Provider.RejectReference = true;
        Assert.Equal(422, (await Assert.ThrowsAsync<FaceLabError>(() => Complete(fixture, false))).Status);
        fixture.Lab.Remove("owner");
        Assert.False(fixture.Lab.HasReference("owner"));
    }

    [Theory]
    [InlineData(429, 429)]
    [InlineData(503, 503)]
    public async Task Mocked_provider_errors_have_safe_fallback(int providerStatus, int expected)
    {
        using var fixture = new Fixture();
        fixture.Provider.Status = providerStatus;
        var start = fixture.Lab.Start("owner", "stamp", true);
        var error = await Assert.ThrowsAsync<FaceLabError>(() =>
            fixture.Lab.Capture("owner", "stamp", start.Token, Frame(1), default));
        Assert.Equal(expected, error.Status);
        Assert.Contains("password or passkey", error.Message);
    }

    [Fact]
    public async Task Persisted_budget_stops_provider_requests()
    {
        using var fixture = new Fixture(1);
        var start = fixture.Lab.Start("owner", "stamp", true);
        await fixture.Lab.Capture("owner", "stamp", start.Token, Frame(1), default);
        var otherProcess = new FaceLab(fixture.Provider, fixture.Config, new Host { ContentRootPath = fixture.Root },
            new EphemeralDataProtectionProvider(), fixture.Clock);
        var restarted = otherProcess.Start("owner", "stamp", true);
        Assert.Equal(429, (await Assert.ThrowsAsync<FaceLabError>(() =>
            otherProcess.Capture("owner", "stamp", restarted.Token, Frame(2), default))).Status);
        Assert.Equal(1, fixture.Provider.Calls);
    }

    private static async Task Complete(Fixture fixture, bool enroll)
    {
        var start = fixture.Lab.Start("owner", "stamp", enroll);
        fixture.Provider.Yaw = 0;
        var turn = (await fixture.Lab.Capture("owner", "stamp", start.Token, Frame(1), default))!;
        fixture.Provider.Yaw = turn.Instruction.EndsWith("left.") ? -25 : 25;
        var center = (await fixture.Lab.Capture("owner", "stamp", turn.Token, Frame(2), default))!;
        fixture.Provider.Yaw = 0;
        Assert.Null(await fixture.Lab.Capture("owner", "stamp", center.Token, Frame(3), default));
    }

    private static string Frame(byte b)
    {
        var bytes = new byte[128];
        bytes[0] = bytes[^2] = 255;
        bytes[1] = 216;
        bytes[^1] = 217;
        bytes[2] = b;
        return "data:image/jpeg;base64," + Convert.ToBase64String(bytes);
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "STMS_FaceLab_" + Guid.NewGuid().ToString("N"));
        public FakeProvider Provider { get; } = new();
        public FakeClock Clock { get; } = new();
        public FaceLab Lab { get; }
        public IConfiguration Config { get; }
        public Fixture(int budget = 450)
        {
            Config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
                ["FacePlusPlus:EnableFreeLab"] = "true", ["FacePlusPlus:ApiKey"] = "synthetic", ["FacePlusPlus:ApiSecret"] = "synthetic",
                ["FacePlusPlus:MonthlyCallsPerEndpoint"] = budget.ToString()
            }).Build();
            Lab = new(Provider, Config, new Host { ContentRootPath = Root }, new EphemeralDataProtectionProvider(), Clock);
        }
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }

    private sealed class FakeClock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeProvider : HttpMessageHandler, IHttpClientFactory
    {
        public int Status = 200, Calls, Yaw;
        public bool RejectReference;
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            var content = await request.Content!.ReadAsStringAsync(token);
            var json = request.RequestUri!.AbsolutePath.EndsWith("detect")
                ? "{\"faces\":[{\"face_token\":\"synthetic\",\"attributes\":{\"headpose\":{\"yaw_angle\":" + Yaw + "}}}]}"
                : "{\"confidence\":" + (RejectReference && content.Contains("image_base64_2=") ? 30 : 95) + ",\"thresholds\":{\"1e-5\":80}}";
            return new((HttpStatusCode)Status) { Content = new StringContent(json) };
        }
    }

    private sealed class Host : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Synthetic";
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = "";
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
