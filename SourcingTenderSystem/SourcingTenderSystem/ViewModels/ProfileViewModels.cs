using System.ComponentModel.DataAnnotations;
using SourcingTenderSystem.Models;

namespace SourcingTenderSystem.ViewModels;

// ===== ProfileController =====

// The "My profile" page has two forms, and both use this one model:
//   form 1 posts PhoneNumber to Profile/Index
//   form 2 posts NewPassword + ConfirmNewPassword to Profile/ChangePassword
// Each action removes the other form's fields from ModelState (they were not posted).
// Email, role, name, department and company details are NOT here (the admin manages them).
public class ProfileViewModel
{
    // A Jordanian number, digits only:
    //   mobile   = 07 + 7/8/9 + 7 digits   (e.g. 0791234567)
    //   landline = 0 + 2/3/5/6 + 7 digits  (e.g. 064612345)
    // It can start with 0, +962 or 00962. Optional for employees, required for suppliers (checked in the controller).
    [RegularExpression(@"^(\+962|00962|0)(7[789][0-9]{7}|[2356][0-9]{7})$",
        ErrorMessage = "Enter a valid Jordanian phone number, digits only (e.g. 0791234567 or +962791234567).")]
    [Display(Name = "Phone number")]
    public string? PhoneNumber { get; set; }

    // Identity also checks: one uppercase letter, one lowercase letter, one digit and one symbol
    [Required(ErrorMessage = "Please enter a new password.")]
    [StringLength(128, MinimumLength = 6, ErrorMessage = "The new password must be at least 6 characters.")]
    [DataType(DataType.Password)]
    [Display(Name = "New password")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please type the new password again.")]
    [Compare(nameof(NewPassword), ErrorMessage = "The two new passwords do not match.")]
    [DataType(DataType.Password)]
    [Display(Name = "Confirm new password")]
    public string ConfirmNewPassword { get; set; } = string.Empty;
}

// Read-only information shown on the page (filled in the controller, never from the form)
public class ProfileInfo
{
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public EmployeeProfile? Employee { get; set; }   // null for suppliers
    public SupplierProfile? Supplier { get; set; }   // null for employees
}
