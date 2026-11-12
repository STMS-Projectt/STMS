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
    [Required, StringLength(200), Display(Name = "Company name")] public string CompanyName { get; set; } = "";
    [Required, StringLength(100), Display(Name = "Tax number")] public string TaxNumber { get; set; } = "";
    [Required, StringLength(100), Display(Name = "Commercial registration number")] public string CommercialRegNo { get; set; } = "";
    [Required, Phone, StringLength(50), Display(Name = "Contact phone")] public string ContactPhone { get; set; } = "";
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
    [Required, Display(Name = "PDF file")] public IFormFile File { get; set; } = null!;
}

public sealed class DecisionInput
{
    [Required, StringLength(1000)] public string Reason { get; set; } = "";
}

public sealed record DashboardCounts(int Users, int Departments, int ActiveEmployees, int PendingSuppliers,
    int ApprovedSuppliers, int RejectedSuppliers, int SuspendedSuppliers, int Requests, int Tenders);
