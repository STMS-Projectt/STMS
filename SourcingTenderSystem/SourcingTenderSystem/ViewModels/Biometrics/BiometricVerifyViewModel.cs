namespace SourcingTenderSystem.ViewModels.Biometrics;

public class BiometricVerifyViewModel
{
    public string UserEmail { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public bool IsEnrolled { get; set; }
    public bool HasFace { get; set; }
    public bool HasFingerprint { get; set; }
    public string? FaceImageUrl { get; set; }
    public string? ReturnUrl { get; set; }
    public string Mode { get; set; } = "Verify"; // "Verify" | "EnrollAndVerify"
}
