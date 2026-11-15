using System.ComponentModel.DataAnnotations;

namespace SourcingTenderSystem.ViewModels;

public sealed class LoginInput
{
    [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = "";
    [Required, DataType(DataType.Password)] public string Password { get; set; } = "";
    [Display(Name = "Remember me")] public bool RememberMe { get; set; }
    public string? ReturnUrl { get; set; }
}

public sealed class DepartmentInput
{
    [Required, StringLength(200)] public string Name { get; set; } = "";
    [StringLength(2000)] public string? Description { get; set; }
    [Display(Name = "Active")] public bool IsActive { get; set; } = true;
}

public class EmployeeInput
{
    [Required, StringLength(200), Display(Name = "Full name")] public string FullName { get; set; } = "";
    [StringLength(150), Display(Name = "Job title")] public string? JobTitle { get; set; }
    [Display(Name = "Department")] public int? DepartmentId { get; set; }
    [Required] public string Role { get; set; } = "";
    [Display(Name = "Active")] public bool IsActive { get; set; }
}

public sealed class CreateEmployeeInput : EmployeeInput
{
    [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = "";
    [Required, StringLength(128, MinimumLength = 6), DataType(DataType.Password)]
    public string Password { get; set; } = "";
}

public sealed class RegistrationInput
{
    public List<int> CategoryIds { get; set; } = [];
    [Required, StringLength(200), Display(Name = "Company name")] public string CompanyName { get; set; } = "";
    [Required, RegularExpression(SupplierRegistrationNumbers.NineDigits, ErrorMessage = "Enter exactly 9 digits for the tax number."), Display(Name = "Tax number")] public string TaxNumber { get; set; } = "";
    [Required, RegularExpression(SupplierRegistrationNumbers.NineDigits, ErrorMessage = "Enter exactly 9 digits for the commercial registration number."), Display(Name = "Commercial registration number")] public string CommercialRegNo { get; set; } = "";
    [Required, RegularExpression(SupplierRegistrationNumbers.JordanPhone, ErrorMessage = "Enter a valid Jordanian phone number, digits only (e.g. 0791234567 or +962791234567)."), Display(Name = "Contact phone")] public string ContactPhone { get; set; } = "";
    [Required, StringLength(500)] public string Address { get; set; } = "";
    [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = "";
    [Required, StringLength(128, MinimumLength = 6), DataType(DataType.Password)] public string Password { get; set; } = "";
    [Required, Compare(nameof(Password)), DataType(DataType.Password), Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = "";
}

public sealed class UploadInput
{
    [Required, Display(Name = "Document type")] public string DocumentType { get; set; } = "";
    [DataType(DataType.Date), Display(Name = "Expiry date")] public DateTime? ExpiryDate { get; set; }
    [Required, Display(Name = "PDF file")] public IFormFile? File { get; set; }
}

public sealed class DecisionInput
{
    [Required, StringLength(1000)] public string Reason { get; set; } = "";
}

public sealed record DashboardCounts(int Users, int Departments, int ActiveEmployees, int PendingSuppliers,
    int ApprovedSuppliers, int RejectedSuppliers, int SuspendedSuppliers, int Requests, int Tenders);



public static class SupplierRegistrationNumbers
{
    public const string NineDigits = "[0-9٠-٩۰-۹]{9}";
    public const string JordanPhone = @"(\+962|00962|0)(7[789][0-9]{7}|[2356][0-9]{7})";

    public static string NormalizeDigits(string value) => string.Concat(value.Select(c =>
        c is >= '٠' and <= '٩' ? (char)('0' + c - '٠') :
        c is >= '۰' and <= '۹' ? (char)('0' + c - '۰') : c));
}
