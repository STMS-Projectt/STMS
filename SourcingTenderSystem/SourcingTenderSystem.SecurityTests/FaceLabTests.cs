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
        var last = await fixture.Lab.Capture("owner", "stamp", center.Token, Frame(3), default);
        if (!enroll) { Assert.Null(last); return; }
        Assert.True(last!.ReadyToSave);
        await fixture.Lab.ConfirmEnrollment("owner", "stamp", last.Token, default);
    }

    // ---------------- Academic demo face sign-in ----------------

    private static async Task<FaceLab.LoginStep> Login(Fixture fixture, string? user, string? stamp = "stamp", int turnYaw = 25)
    {
        var start = fixture.Lab.StartLogin(user, stamp);
        fixture.Provider.Yaw = 0;
        var first = await fixture.Lab.CaptureLogin(start.Token, Frame(11), default);
        fixture.Provider.Yaw = turnYaw;
        var second = await fixture.Lab.CaptureLogin(first.Next!.Token, Frame(12), default);
        fixture.Provider.Yaw = 0;
        return await fixture.Lab.CaptureLogin(second.Next!.Token, Frame(13), default);
    }

    [Fact]
    public async Task Enrolled_face_login_succeeds_only_for_the_challenged_account()
    {
        using var fixture = new Fixture();
        await Complete(fixture, true);
        var result = await Login(fixture, "owner", turnYaw: -25); // either turn direction is accepted unless strict
        Assert.False(result.Rejected);
        Assert.Equal(("owner", "stamp"), (result.User, result.Stamp));
    }

    [Fact]
    public async Task Incorrect_face_or_malformed_compare_is_rejected()
    {
        using var fixture = new Fixture();
        await Complete(fixture, true);
        fixture.Provider.RejectReference = true;
        var mismatch = await Login(fixture, "owner");
        Assert.True(mismatch.Rejected);
        Assert.Null(mismatch.Stamp);
        fixture.Provider.RejectReference = false;
        fixture.Provider.MalformedReferenceCompare = true;
        var malformed = await Login(fixture, "owner");
        Assert.True(malformed.Rejected);
        Assert.Null(malformed.Stamp);
    }

    [Fact]
    public async Task Unknown_or_unenrolled_accounts_get_an_indistinguishable_failing_decoy()
    {
        using var fixture = new Fixture();
        var unknown = await Login(fixture, null, null);
        Assert.True(unknown.Rejected);
        Assert.Null(unknown.User);
        var unenrolled = await Login(fixture, "nobody");
        Assert.True(unenrolled.Rejected);
        Assert.Null(unenrolled.User);
        Assert.False(fixture.Lab.HasReference("nobody"));
    }

    [Fact]
    public async Task Login_challenge_tokens_and_frames_cannot_be_replayed()
    {
        using var fixture = new Fixture();
        await Complete(fixture, true);
        var start = fixture.Lab.StartLogin("owner", "stamp");
        fixture.Provider.Yaw = 0;
        var first = await fixture.Lab.CaptureLogin(start.Token, Frame(21), default);
        Assert.Equal(409, (await Assert.ThrowsAsync<FaceLabError>(() => fixture.Lab.CaptureLogin(start.Token, Frame(22), default))).Status);
        fixture.Provider.Yaw = 25;
        Assert.Equal(422, (await Assert.ThrowsAsync<FaceLabError>(() => fixture.Lab.CaptureLogin(first.Next!.Token, Frame(21), default))).Status);
        Assert.Equal(409, (await Assert.ThrowsAsync<FaceLabError>(() => fixture.Lab.CaptureLogin(first.Next!.Token, Frame(23), default))).Status);
        // Enrollment tokens are not valid for login and vice versa.
        var enroll = fixture.Lab.Start("owner", "stamp", true);
        Assert.Equal(409, (await Assert.ThrowsAsync<FaceLabError>(() => fixture.Lab.CaptureLogin(enroll.Token, Frame(24), default))).Status);
    }

    [Fact]
    public async Task Provider_failure_and_quota_never_produce_a_login()
    {
        using var fixture = new Fixture();
        await Complete(fixture, true);
        fixture.Provider.Status = 503;
        var start = fixture.Lab.StartLogin("owner", "stamp");
        Assert.Equal(503, (await Assert.ThrowsAsync<FaceLabError>(() => fixture.Lab.CaptureLogin(start.Token, Frame(31), default))).Status);
        fixture.Provider.Status = 429;
        start = fixture.Lab.StartLogin("owner", "stamp");
        Assert.Equal(429, (await Assert.ThrowsAsync<FaceLabError>(() => fixture.Lab.CaptureLogin(start.Token, Frame(32), default))).Status);
    }

    [Fact]
    public async Task Enrollment_requires_confirmation_and_can_be_replaced_and_removed()
    {
        using var fixture = new Fixture();
        var start = fixture.Lab.Start("owner", "stamp", true);
        fixture.Provider.Yaw = 0;
        var turn = (await fixture.Lab.Capture("owner", "stamp", start.Token, Frame(41), default))!;
        fixture.Provider.Yaw = 25;
        var center = (await fixture.Lab.Capture("owner", "stamp", turn.Token, Frame(42), default))!;
        fixture.Provider.Yaw = 0;
        var ready = (await fixture.Lab.Capture("owner", "stamp", center.Token, Frame(43), default))!;
        Assert.True(ready.ReadyToSave);
        Assert.False(fixture.Lab.HasReference("owner"));
        Assert.Equal(409, (await Assert.ThrowsAsync<FaceLabError>(() => fixture.Lab.ConfirmEnrollment("intruder", "stamp", ready.Token, default))).Status);
        await fixture.Lab.ConfirmEnrollment("owner", "stamp", ready.Token, default);
        var original = File.ReadAllBytes(Directory.GetFiles(fixture.Root, "*.face", SearchOption.AllDirectories).Single());
        await Complete(fixture, true); // replace
        Assert.NotEqual(original, File.ReadAllBytes(Directory.GetFiles(fixture.Root, "*.face", SearchOption.AllDirectories).Single()));
        fixture.Lab.Remove("owner");
        Assert.True((await Login(fixture, "owner")).Rejected);
    }

    [Theory]
    [InlineData("Development", "true", null, true)]
    [InlineData("Development", "false", null, false)]
    [InlineData("Development", null, null, false)]
    [InlineData("Production", "true", null, false)]
    [InlineData("Production", "true", "true", true)]
    public void Demo_face_login_needs_explicit_flag_and_is_off_in_production(string environment, string? flag, string? production, bool expected)
    {
        using var fixture = new Fixture(demo: flag, environment: environment, production: production);
        Assert.Equal(expected, fixture.Lab.DemoLoginEnabled);
        if (!expected) Assert.Equal(503, Assert.Throws<FaceLabError>(() => fixture.Lab.StartLogin("owner", "stamp")).Status);
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
        public Fixture(int budget = 450, string? demo = "true", string environment = "Testing", string? production = null)
        {
            Config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
                ["FacePlusPlus:EnableFreeLab"] = "true", ["FacePlusPlus:ApiKey"] = "synthetic", ["FacePlusPlus:ApiSecret"] = "synthetic",
                ["FacePlusPlus:MonthlyCallsPerEndpoint"] = budget.ToString(), ["FacePlusPlus:EnableDemoFaceLogin"] = demo,
                ["FacePlusPlus:AllowDemoFaceLoginInProduction"] = production
            }).Build();
            Lab = new(Provider, Config, new Host { ContentRootPath = Root, EnvironmentName = environment }, new EphemeralDataProtectionProvider(), Clock);
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
        public bool RejectReference, MalformedReferenceCompare;
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            var content = await request.Content!.ReadAsStringAsync(token);
            var reference = content.Contains("image_base64_2=");
            var json = request.RequestUri!.AbsolutePath.EndsWith("detect")
                ? "{\"faces\":[{\"face_token\":\"synthetic\",\"attributes\":{\"headpose\":{\"yaw_angle\":" + Yaw + "}}}]}"
                : MalformedReferenceCompare && reference ? "{\"confidence\":\"95\"}"
                : "{\"confidence\":" + (RejectReference && reference ? 30 : 95) + ",\"thresholds\":{\"1e-5\":80}}";
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
