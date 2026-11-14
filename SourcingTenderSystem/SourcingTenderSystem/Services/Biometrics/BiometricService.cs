using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace SourcingTenderSystem.Services.Biometrics;

public class BiometricService : IBiometricService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BiometricService> _logger;

    private string ApiKey => _configuration["FacePlusPlus:ApiKey"] ?? "";
    private string ApiSecret => _configuration["FacePlusPlus:ApiSecret"] ?? "";
    private string BaseUrl => _configuration["FacePlusPlus:BaseUrl"] ?? "https://api-us.faceplusplus.com/facepp/v3";

    public BiometricService(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<BiometricService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Sends a base64-encoded image to Face++ Detect API using FormUrlEncodedContent.
    /// Returns the face_token string if a face is detected; returns null if no face is found or camera is covered.
    /// </summary>
    public async Task<string?> DetectFaceAsync(string imageBase64)
    {
        if (string.IsNullOrWhiteSpace(imageBase64))
            return null;

        try
        {
            var client = _httpClientFactory.CreateClient("FacePlusPlus");

            // Strip data URI prefix if present (e.g. "data:image/jpeg;base64,...")
            var base64Data = imageBase64.Contains(',')
                ? imageBase64[(imageBase64.IndexOf(',') + 1)..]
                : imageBase64;

            var parameters = new Dictionary<string, string>
            {
                { "api_key", ApiKey },
                { "api_secret", ApiSecret },
                { "image_base64", base64Data }
            };

            var content = new FormUrlEncodedContent(parameters);
            var response = await client.PostAsync($"{BaseUrl}/detect", content);
            var body = await response.Content.ReadAsStringAsync();

            _logger.LogInformation("Face++ detect HTTP {StatusCode}: {Body}", response.StatusCode, body);

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            // Check for API errors
            if (root.TryGetProperty("error_message", out var errProp))
            {
                _logger.LogWarning("Face++ detect returned error: {Error}", errProp.GetString());
                return null;
            }

            // Check if any face was detected
            if (!root.TryGetProperty("faces", out var faces) || faces.GetArrayLength() == 0)
            {
                _logger.LogInformation("Face++ detect: 0 faces found (camera may be covered).");
                return null;
            }

            var faceToken = faces[0].GetProperty("face_token").GetString();
            _logger.LogInformation("Face++ detect: successfully acquired face_token={Token}", faceToken);
            return faceToken;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during Face++ detect API call.");
            return null;
        }
    }

    /// <summary>
    /// Calls Face++ Compare API using FormUrlEncodedContent with two face_tokens.
    /// Returns confidence score (0–100), or -1 on failure.
    /// </summary>
    public async Task<double> CompareFaceTokensAsync(string faceToken1, string faceToken2)
    {
        if (string.IsNullOrWhiteSpace(faceToken1) || string.IsNullOrWhiteSpace(faceToken2))
            return -1;

        try
        {
            var client = _httpClientFactory.CreateClient("FacePlusPlus");

            var parameters = new Dictionary<string, string>
            {
                { "api_key", ApiKey },
                { "api_secret", ApiSecret },
                { "face_token1", faceToken1 },
                { "face_token2", faceToken2 }
            };

            var content = new FormUrlEncodedContent(parameters);
            var response = await client.PostAsync($"{BaseUrl}/compare", content);
            var body = await response.Content.ReadAsStringAsync();

            _logger.LogInformation("Face++ compare HTTP {StatusCode}: {Body}", response.StatusCode, body);

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.TryGetProperty("error_message", out var errProp))
            {
                _logger.LogWarning("Face++ compare error: {Error}", errProp.GetString());
                return -1;
            }

            if (!root.TryGetProperty("confidence", out var confidenceProp))
                return -1;

            var confidence = confidenceProp.GetDouble();
            _logger.LogInformation("Face++ compare confidence score: {Confidence:F2}", confidence);
            return confidence;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during Face++ compare API call.");
            return -1;
        }
    }

    /// <summary>
    /// Verifies fingerprint by exact match of WebAuthn credential IDs.
    /// </summary>
    public bool VerifyFingerprint(string registeredCredentialId, string submittedCredentialId)
    {
        if (string.IsNullOrWhiteSpace(registeredCredentialId) || string.IsNullOrWhiteSpace(submittedCredentialId))
            return false;

        return string.Equals(registeredCredentialId.Trim(), submittedCredentialId.Trim(), StringComparison.Ordinal);
    }
}
