using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

namespace SourcingTenderSystem.Services.Biometrics;

public sealed class FaceLabError(string message, int status = 422) : Exception(message)
{
    public int Status { get; } = status;
}

public sealed class FaceLab(IHttpClientFactory http, IConfiguration config, IWebHostEnvironment host,
    IDataProtectionProvider protection, TimeProvider clock)
{
    // One generic message for every failed face sign-in, so responses never reveal whether an account exists,
    // is enrolled, is eligible, or simply did not match.
    public const string LoginFailedMessage = "Face verification failed. Try again or sign in with your password or passkey.";
    private DateTimeOffset nextCall;
    private readonly object gate = new();
    private readonly Dictionary<string, Attempt> attempts = new();
    private readonly IDataProtector protector = protection.CreateProtector("STMS.FaceLab.Reference.v1");
    // Outside wwwroot; never committed (App_Data is ignored). Overridable for isolated test runs.
    private string Folder => config["FacePlusPlus:StoragePath"] is { Length: > 0 } custom ? custom
        : Path.Combine(host.ContentRootPath, "App_Data", "Biometrics", "FaceLab");
    public bool Enabled => config.GetValue<bool>("FacePlusPlus:EnableFreeLab") &&
        !string.IsNullOrWhiteSpace(config["FacePlusPlus:ApiKey"]) && !string.IsNullOrWhiteSpace(config["FacePlusPlus:ApiSecret"]);
    // Academic demo only: webcam sign-in without certified liveness. Off unless explicitly enabled, and refused in
    // the Production environment unless that is separately and explicitly allowed.
    public bool DemoLoginEnabled => Enabled && config.GetValue<bool>("FacePlusPlus:EnableDemoFaceLogin") &&
        (!host.IsProduction() || config.GetValue<bool>("FacePlusPlus:AllowDemoFaceLoginInProduction"));
    private string Reference(string id) => Path.Combine(Folder, Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(id))) + ".face");
    public void Remove(string id)
    {
        lock (gate)
        {
            foreach (var key in attempts.Where(x => x.Value.User == id).Select(x => x.Key).ToArray())
                attempts.Remove(key);
            if (HasReference(id)) File.Delete(Reference(id));
        }
    }

    public bool HasReference(string id) => File.Exists(Reference(id));

    private enum Mode { Verify, Enroll, Login, Decoy }
    private sealed record Attempt(string User, string Stamp, Mode Mode, int Stage, int Direction,
        DateTimeOffset Expires, byte[]? Center, string? CenterToken, string? TurnToken, byte[]? TurnHash);
    public sealed record Prompt(string Token, string Instruction, int Step, bool ReadyToSave = false);
    public sealed record LoginStep(Prompt? Next, string? User, string? Stamp, bool Rejected);
    private sealed record Usage(string Month, int Detect, int Compare);

    public Prompt Start(string user, string stamp, bool enroll)
    {
        CheckEnabled();
        if (!enroll && !HasReference(user)) throw new FaceLabError("Enroll a face reference first.");
        return Issue(New(user, stamp, enroll ? Mode.Enroll : Mode.Verify));
    }

    // Unknown, unenrolled or ineligible accounts get a decoy challenge that behaves identically and always fails.
    public Prompt StartLogin(string? user, string? stamp)
    {
        if (!DemoLoginEnabled) throw new FaceLabError("Face sign-in is not enabled. Use your password or passkey.", 503);
        return user is not null && stamp is not null && HasReference(user)
            ? Issue(New(user, stamp, Mode.Login))
            : Issue(New("decoy:" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)), "", Mode.Decoy));
    }

    private Attempt New(string user, string stamp, Mode mode) => new(user, stamp, mode, 0,
        RandomNumberGenerator.GetInt32(2) == 0 ? -1 : 1, clock.GetUtcNow().AddMinutes(2), null, null, null, null);

    private Prompt Issue(Attempt attempt)
    {
        lock (gate)
        {
            var now = clock.GetUtcNow();
            foreach (var key in attempts.Where(x => x.Value.Expires <= now || x.Value.User == attempt.User).Select(x => x.Key).ToArray())
                attempts.Remove(key);
            if (attempts.Count >= 64) throw new FaceLabError("Camera verification is busy. Try again later.", 429);
            var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            attempts.Add(token, attempt);
            return new(token, attempt.Stage switch
            {
                0 => "Center your face and look straight ahead.",
                1 => attempt.Direction < 0 ? "Turn your head slightly toward your left." : "Turn your head slightly toward your right.",
                2 => "Return to the center and look straight ahead.",
                _ => "Face captured. Save it to finish enrollment."
            }, attempt.Stage + 1, attempt.Stage == 3);
        }
    }

    private Attempt Take(string? token, Func<Attempt, bool> owner)
    {
        lock (gate)
        {
            if (token is null || !attempts.TryGetValue(token, out var found) || !owner(found) ||
                !attempts.Remove(token) || found.Expires <= clock.GetUtcNow())
                throw new FaceLabError("This challenge expired or was already used. Start again.", 409);
            return found;
        }
    }

    public async Task<Prompt?> Capture(string user, string stamp, string? token, string? image, CancellationToken ct)
    {
        CheckEnabled();
        var attempt = Take(token, a => a.User == user && a.Stamp == stamp && a.Mode is Mode.Enroll or Mode.Verify && a.Stage < 3);
        var (next, face) = await Advance(attempt, image, ct);
        if (next is not null) return next;
        if (attempt.Mode == Mode.Enroll)
            return Issue(attempt with { Stage = 3, Expires = clock.GetUtcNow().AddMinutes(2) });
        if (!await Match(face, null, await ReadReference(user, ct), ct))
            throw new FaceLabError("The face did not match the enrolled reference.");
        if (attempt.Expires <= clock.GetUtcNow())
            throw new FaceLabError("Challenge expired. Start again.", 409);
        return null;
    }

    // The user explicitly confirms the captured reference; it replaces any earlier one.
    public async Task ConfirmEnrollment(string user, string stamp, string? token, CancellationToken ct)
    {
        CheckEnabled();
        var attempt = Take(token, a => a.User == user && a.Stamp == stamp && a.Mode == Mode.Enroll && a.Stage == 3);
        Directory.CreateDirectory(Folder);
        var temp = Reference(user) + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temp, protector.Protect(attempt.Center!), ct);
            File.Move(temp, Reference(user), overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { throw new FaceLabError("Could not save the face reference. Start again.", 409); }
        finally { File.Delete(temp); }
    }

    public async Task<LoginStep> CaptureLogin(string? token, string? image, CancellationToken ct)
    {
        if (!DemoLoginEnabled) throw new FaceLabError("Face sign-in is not enabled. Use your password or passkey.", 503);
        var attempt = Take(token, a => a.Mode is Mode.Login or Mode.Decoy);
        var (next, face) = await Advance(attempt, image, ct);
        if (next is not null) return new(next, null, null, false);
        if (attempt.Mode == Mode.Decoy) return new(null, null, null, true);
        byte[] reference;
        try { reference = await ReadReference(attempt.User, ct); }
        catch (FaceLabError) { return new(null, attempt.User, null, true); }
        var matched = await Match(face, null, reference, ct);
        return matched && attempt.Expires > clock.GetUtcNow()
            ? new(null, attempt.User, attempt.Stamp, false)
            : new(null, attempt.User, null, true);
    }

    private async Task<byte[]> ReadReference(string user, CancellationToken ct)
    {
        try { return protector.Unprotect(await File.ReadAllBytesAsync(Reference(user), ct)); }
        catch (Exception ex) when (ex is IOException or CryptographicException or UnauthorizedAccessException)
        { throw new FaceLabError("Face reference unavailable. Enroll again.", 409); }
    }

    // Runs one challenge frame. Returns the next prompt, or (null, face token) after the third frame.
    private async Task<(Prompt? Next, string Face)> Advance(Attempt attempt, string? image, CancellationToken ct)
    {
        var bytes = ReadJpeg(image);
        var hash = SHA256.HashData(bytes);
        if (attempt.Center is not null && (hash.SequenceEqual(SHA256.HashData(attempt.Center)) ||
            (attempt.TurnHash is not null && hash.SequenceEqual(attempt.TurnHash))))
            throw new FaceLabError("The same frame was reused. Start a new challenge.");
        var (face, yaw) = await Detect(bytes, ct);
        if (attempt.Stage == 1 ? !ValidTurn(yaw, attempt.Direction, StrictTurn) : Math.Abs(yaw) > 12)
            throw new FaceLabError(attempt.Stage == 1
                ? "Turn your head a little further (about 20 to 45 degrees), then capture again from the start."
                : "Look straight at the camera. Start a new challenge.");
        if (attempt.Stage == 0)
            return (Issue(attempt with { Stage = 1, Center = bytes, CenterToken = face }), face);
        if (attempt.Stage == 1)
            return (Issue(attempt with { Stage = 2, TurnToken = face, TurnHash = hash }), face);
        if (!await Match(face, attempt.CenterToken!, null, ct) || !await Match(face, attempt.TurnToken!, null, ct))
            throw new FaceLabError("The challenge frames did not show the same face. Start again.");
        if (attempt.Expires <= clock.GetUtcNow())
            throw new FaceLabError("Challenge expired. Start again.", 409);
        return (null, face);
    }

    private bool StrictTurn => config.GetValue<bool>("FacePlusPlus:StrictTurnDirection");

    // Face++ yaw sign for "left" depends on camera mirroring and is not verified on real devices, so by default either
    // direction of a 20-45 degree turn is accepted. Set FacePlusPlus:StrictTurnDirection=true once verified.
    public static bool ValidTurn(double yaw, int direction, bool strict = true) =>
        (strict ? yaw * direction : Math.Abs(yaw)) is >= 20 and <= 45;

    private void CheckEnabled()
    {
        if (!Enabled) throw new FaceLabError("The free Face++ demo is unavailable. Use a password or passkey.", 503);
    }

    private static byte[] ReadJpeg(string? image)
    {
        const string prefix = "data:image/jpeg;base64,";
        if (image is null || !image.StartsWith(prefix, StringComparison.Ordinal) || image.Length > 400_000)
            throw new FaceLabError("Capture a JPEG camera frame under 300 KB.");
        byte[] bytes;
        try { bytes = Convert.FromBase64String(image[prefix.Length..]); }
        catch (FormatException) { throw new FaceLabError("Invalid camera frame."); }
        if (bytes.Length < 128 || bytes[0] != 0xff || bytes[1] != 0xd8 || bytes[^2] != 0xff || bytes[^1] != 0xd9)
            throw new FaceLabError("Invalid JPEG camera frame.");
        return bytes;
    }

    private async Task<(string Token, double Yaw)> Detect(byte[] frame, CancellationToken ct)
    {
        using var result = await Call("detect", new() { ["image_base64"] = Convert.ToBase64String(frame),
            ["return_attributes"] = "headpose" }, ct);
        try
        {
            var faces = result.RootElement.GetProperty("faces");
            if (faces.GetArrayLength() != 1) throw new FaceLabError("Position exactly one face in the camera guide.");
            var face = faces[0];
            var token = face.GetProperty("face_token").GetString();
            var yaw = face.GetProperty("attributes").GetProperty("headpose").GetProperty("yaw_angle").GetDouble();
            if (string.IsNullOrWhiteSpace(token) || !double.IsFinite(yaw)) throw ProviderUnavailable();
            return (token, yaw);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or FormatException)
        { throw ProviderUnavailable(); }
    }

    private async Task<bool> Match(string probeToken, string? referenceToken, byte[]? reference, CancellationToken ct)
    {
        var fields = new Dictionary<string, string> { ["face_token1"] = probeToken };
        if (referenceToken is not null) fields["face_token2"] = referenceToken;
        else fields["image_base64_2"] = Convert.ToBase64String(reference!);
        using var result = await Call("compare", fields, ct);
        var data = result.RootElement;
        return data.TryGetProperty("confidence", out var confidence) && confidence.ValueKind == JsonValueKind.Number &&
            confidence.TryGetDouble(out var score) &&
            data.TryGetProperty("thresholds", out var thresholds) && thresholds.ValueKind == JsonValueKind.Object &&
            thresholds.TryGetProperty(ThresholdKey, out var threshold) && threshold.ValueKind == JsonValueKind.Number &&
            threshold.TryGetDouble(out var cutoff) && cutoff is > 0 and <= 100 && score >= cutoff && score <= 100;
    }

    // Face++ returns confidence thresholds at false-accept rates 1e-3, 1e-4 and 1e-5; 1e-5 (strictest) is the default.
    private string ThresholdKey => config["FacePlusPlus:CompareThreshold"] is "1e-3" or "1e-4" ? config["FacePlusPlus:CompareThreshold"]! : "1e-5";

    private readonly SemaphoreSlim providerQueue = new(1, 1);

    private async Task<JsonDocument> Call(string endpoint, Dictionary<string, string> fields, CancellationToken ct)
    {
        if (!await providerQueue.WaitAsync(TimeSpan.FromSeconds(2), ct))
            throw new FaceLabError("Camera service is busy. Try again later.", 429);
        try
        {
            var delay = nextCall - clock.GetUtcNow();
            if (delay > TimeSpan.Zero) await Task.Delay(delay, ct);
            return await Send(endpoint, fields, ct);
        }
        finally { providerQueue.Release(); }
    }

    private async Task<JsonDocument> Send(string endpoint, Dictionary<string, string> fields, CancellationToken ct)
    {
        Reserve(endpoint);
        fields["api_key"] = config["FacePlusPlus:ApiKey"]!;
        fields["api_secret"] = config["FacePlusPlus:ApiSecret"]!;
        using var body = new FormUrlEncodedContent(fields);
        try
        {
            using var client = http.CreateClient("FreeFacePlusPlus");
            using var response = await client.PostAsync(BaseUrl + endpoint, body, ct);
            if ((int)response.StatusCode == 429) throw Quota();
            using var stream = await response.Content.ReadAsStreamAsync(ct);
            var result = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (result.RootElement.ValueKind == JsonValueKind.Object && response.IsSuccessStatusCode &&
                !result.RootElement.TryGetProperty("error_message", out _)) return result;
            var message = result.RootElement.ValueKind == JsonValueKind.Object &&
                result.RootElement.TryGetProperty("error_message", out var error) && error.ValueKind == JsonValueKind.String
                ? error.GetString() ?? "" : "";
            result.Dispose();
            if (message.Contains("LIMIT", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("QUOTA", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("BALANCE", StringComparison.OrdinalIgnoreCase)) throw Quota();
            throw ProviderUnavailable();
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException || ex is OperationCanceledException && !ct.IsCancellationRequested)
        { throw ProviderUnavailable(); }
    }

    // HTTPS only (a loopback http URL is allowed for local provider mocks).
    private string BaseUrl => Uri.TryCreate(config["FacePlusPlus:BaseUrl"], UriKind.Absolute, out var url) &&
        (url.Scheme == Uri.UriSchemeHttps || url.IsLoopback) ? url.ToString().TrimEnd('/') + "/" : "https://api-us.faceplusplus.com/facepp/v3/";

    private void Reserve(string endpoint)
    {
        lock (gate)
        {
            try
            {
                var now = clock.GetUtcNow();
                nextCall = now.AddMilliseconds(350);
                Directory.CreateDirectory(Folder);
                var month = clock.GetUtcNow().ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
                using var lease = File.Open(Path.Combine(Folder, "quota.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                var path = Path.Combine(Folder, "quota.json");
                var usage = File.Exists(path) ? JsonSerializer.Deserialize<Usage>(File.ReadAllText(path)) : new Usage(month, 0, 0);
                if (usage is null || usage.Detect < 0 || usage.Compare < 0) throw ProviderUnavailable();
                if (usage.Month != month) usage = new(month, 0, 0);
                var limit = Math.Clamp(config.GetValue("FacePlusPlus:MonthlyCallsPerEndpoint", 450), 1, 450);
                if (endpoint == "detect" ? usage.Detect >= limit : usage.Compare >= limit) throw Quota();
                usage = endpoint == "detect" ? usage with { Detect = usage.Detect + 1 } : usage with { Compare = usage.Compare + 1 };
                File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(usage));
                File.Move(path + ".tmp", path, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            { throw ProviderUnavailable(); }
        }
    }

    private static FaceLabError Quota() => new("Free Face++ quota or request limit reached. Use your password or passkey.", 429);
    private static FaceLabError ProviderUnavailable() => new("Face++ is temporarily unavailable. Use your password or passkey.", 503);
}
