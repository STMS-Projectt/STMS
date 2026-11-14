using System.Threading.Tasks;

namespace SourcingTenderSystem.Services.Biometrics;

public interface IBiometricService
{
    /// <summary>
    /// Sends a base64 image to Face++ Detect API and returns the face_token.
    /// Returns null if no face is detected in the image.
    /// </summary>
    Task<string?> DetectFaceAsync(string imageBase64);

    /// <summary>
    /// Calls Face++ Compare API with two face tokens.
    /// Returns the confidence score (0–100). Returns -1 on error.
    /// </summary>
    Task<double> CompareFaceTokensAsync(string faceToken1, string faceToken2);

    /// <summary>
    /// Verifies fingerprint by comparing stored WebAuthn credential ID with submitted one.
    /// </summary>
    bool VerifyFingerprint(string registeredCredentialId, string submittedCredentialId);
}
