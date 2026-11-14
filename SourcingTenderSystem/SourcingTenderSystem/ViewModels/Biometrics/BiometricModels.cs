using System.Collections.Generic;

namespace SourcingTenderSystem.ViewModels.Biometrics;

public class BiometricSetupViewModel
{
    public string UserEmail { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public bool HasFaceRegistered { get; set; }
    public bool HasFingerprintRegistered { get; set; }
    public string? ReturnUrl { get; set; }
}

/// <summary>
/// Main request object for ProcessBiometricVerify endpoint.
/// ImageBase64 carries the raw webcam capture (JPEG/PNG base64) for Face++ processing.
/// </summary>
public class ProcessBiometricRequest
{
    public string Email { get; set; } = string.Empty;
    public string BiometricType { get; set; } = string.Empty;  // "Face" | "Fingerprint"
    public string Mode { get; set; } = string.Empty;           // "Enroll" | "Verify"
    public string? ImageBase64 { get; set; }                   // webcam snapshot (Face)
    public string? CredentialId { get; set; }                  // WebAuthn (Fingerprint)
    public string? ReturnUrl { get; set; }
}

public class FaceLoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string? ImageBase64 { get; set; }
    public string? ReturnUrl { get; set; }
}

public class FingerprintLoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string CredentialId { get; set; } = string.Empty;
    public string? ReturnUrl { get; set; }
}

public class BiometricAuthResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? RedirectUrl { get; set; }
}
