using System;
using System.ComponentModel.DataAnnotations;

namespace SourcingTenderSystem.ViewModels.Awards;

public class AwardConfirmationViewModel
{
    public int TenderId { get; set; }
    public string TenderTitle { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public int WinningBidId { get; set; }
    public string WinningSupplierName { get; set; } = string.Empty;
    public decimal AwardAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public int DeliveryDays { get; set; }

    [Required(ErrorMessage = "Contract Reference / PO Number is required.")]
    [Display(Name = "Contract / Award Reference Number")]
    public string ContractReferenceNumber { get; set; } = string.Empty;

    [Display(Name = "Official Award Notes & Stipulations")]
    public string? AwardNotes { get; set; }

    [Display(Name = "Agreed Delivery Target Date")]
    public DateTime? AgreedDeliveryDate { get; set; }

    [Range(typeof(bool), "true", "true", ErrorMessage = "You must confirm and verify the award decision before finalizing.")]
    public bool AcknowledgeConfirmation { get; set; }
}
