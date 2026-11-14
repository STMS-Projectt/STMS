namespace SourcingTenderSystem.ViewModels.Biometrics;

public sealed class BiometricVerifyViewModel
{
    public bool Manage { get; init; }
    public bool CanRenewFace { get; init; }
    public bool HasFace { get; init; }
    public bool HasFingerprint { get; init; }
    public string? ReturnUrl { get; init; }
}
