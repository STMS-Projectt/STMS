using System.ComponentModel.DataAnnotations;
using SourcingTenderSystem.Models;
using SourcingTenderSystem.ViewModels.Biometrics;

namespace SourcingTenderSystem.ViewModels;

public sealed class PersonalPreferences
{
    [Display(Name = "Preferred name"), StringLength(80)] public string? PreferredName { get; set; }
    [Display(Name = "Work phone"), Phone, StringLength(40)] public string? WorkPhone { get; set; }
    [StringLength(100)] public string? Location { get; set; }
    [Display(Name = "About me"), StringLength(400)] public string? Bio { get; set; }

    public void Trim()
    {
        PreferredName = PreferredName?.Trim();
        WorkPhone = WorkPhone?.Trim();
        Location = Location?.Trim();
        Bio = Bio?.Trim();
    }
}

public sealed record AccountSettings(string DisplayName, string Email, IReadOnlyList<string> Roles,
    EmployeeProfile? Employee, SupplierProfile? Supplier, bool HasPhoto, PersonalPreferences Personal,
    BiometricVerifyViewModel? Security);
