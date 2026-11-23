using System.ComponentModel.DataAnnotations;
using SourcingTenderSystem.Models;

namespace SourcingTenderSystem.ViewModels;

// ===== BidsController =====

// Form for Bids/Create and Bids/Edit.
// Only the fields the supplier is allowed to type are here.
// TenderId, SupplierProfileId, Status, TotalPrice and dates are set in the controller, never from the form.
public class BidFormViewModel
{
    [Required(ErrorMessage = "Delivery days are required.")]
    [Range(1, 3650, ErrorMessage = "Delivery days must be between 1 and 3650.")]
    [Display(Name = "Delivery (days)")]
    public int DeliveryDays { get; set; }

    [Required(ErrorMessage = "Validity days are required.")]
    [Range(1, 3650, ErrorMessage = "Validity days must be between 1 and 3650.")]
    [Display(Name = "Offer validity (days)")]
    public int ValidityDays { get; set; }

    // 16% sales tax added on top of the item prices
    [Display(Name = "Add 16% sales tax")]
    public bool IncludesSalesTax { get; set; } = true;

    [Range(1, 120, ErrorMessage = "Warranty must be between 1 and 120 months.")]
    [Display(Name = "Legal warranty (months)")]
    public int? LegalWarrantyMonths { get; set; }

    [Display(Name = "Legal warranty file (PDF)")]
    public IFormFile? LegalWarrantyFile { get; set; }

    [StringLength(120)]
    [Display(Name = "Issuing bank")]
    public string? BankName { get; set; }

    [Range(0.01, 999999999.99, ErrorMessage = "Guarantee amount must be greater than 0.")]
    [Display(Name = "Guarantee amount (JOD)")]
    public decimal? BankGuaranteeAmount { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Guarantee expiry date")]
    public DateTime? BankGuaranteeExpiry { get; set; }

    [Display(Name = "Bank guarantee file (PDF)")]
    public IFormFile? BankGuaranteeFile { get; set; }

    // Server-side checks for the bank guarantee fields. Returns (field, error) pairs.
    public IEnumerable<(string Field, string Error)> GuaranteeErrors()
    {
        // The bank guarantee is optional, but if one field is filled the others are needed too.
        bool any = !string.IsNullOrWhiteSpace(BankName) || BankGuaranteeAmount != null || BankGuaranteeExpiry != null || BankGuaranteeFile != null;
        if (!any) yield break;
        if (string.IsNullOrWhiteSpace(BankName))
            yield return (nameof(BankName), "Enter the issuing bank.");
        if (BankGuaranteeAmount is null)
            yield return (nameof(BankGuaranteeAmount), "Enter the guarantee amount.");
        if (BankGuaranteeExpiry is not DateTime expiry || expiry.Date <= DateTime.UtcNow.Date)
            yield return (nameof(BankGuaranteeExpiry), "The guarantee expiry must be a future date.");
    }

    public static BidFormViewModel From(Bid bid) => new()
    {
        DeliveryDays = bid.DeliveryDays, ValidityDays = bid.ValidityDays, IncludesSalesTax = bid.IncludesSalesTax,
        LegalWarrantyMonths = bid.LegalWarrantyMonths, BankName = bid.BankName,
        BankGuaranteeAmount = bid.BankGuaranteeAmount, BankGuaranteeExpiry = bid.BankGuaranteeExpiry
    };

    // Copies the typed values (not the files) into the bid.
    public void CopyTo(Bid bid)
    {
        bid.DeliveryDays = DeliveryDays;
        bid.ValidityDays = ValidityDays;
        bid.IncludesSalesTax = IncludesSalesTax;
        bid.LegalWarrantyMonths = LegalWarrantyMonths;
        var hasGuarantee = !string.IsNullOrWhiteSpace(BankName);
        bid.BankName = hasGuarantee ? BankName!.Trim() : null;
        bid.BankGuaranteeAmount = hasGuarantee ? BankGuaranteeAmount : null;
        bid.BankGuaranteeExpiry = hasGuarantee ? BankGuaranteeExpiry?.Date : null;
    }
}

// Data for the Bids/MyBids page (two sections).
public class MyBidsViewModel
{
    // (a) Accepted invitations for open tenders where the supplier has no bid yet
    public List<Invitation> WaitingInvitations { get; set; } = new List<Invitation>();

    // (b) All bids of the current supplier
    public List<Bid> MyBids { get; set; } = new List<Bid>();
}

// One row in the "requested items" table on Bids/Details:
// the item the department asked for + my price for it (null = no price yet).
public class BidItemRow
{
    public PurchaseRequisitionItem RequestedItem { get; set; } = new PurchaseRequisitionItem();
    public BidItem? BidItem { get; set; }
}

// Data for the Bids/Details page.
public class BidDetailsViewModel
{
    public Bid Bid { get; set; } = new Bid();
    public List<BidItemRow> Rows { get; set; } = new List<BidItemRow>();

    public bool TenderIsOpen { get; set; }   // Tender.Status == "Open" and now < CloseDate
    public int MissingPrices { get; set; }   // requested items that still have no price
    public bool CanChange { get; set; }      // Draft + tender open: edit info, prices and documents
    public bool CanSubmit { get; set; }      // all rules for Submit are OK
    public bool CanWithdraw { get; set; }    // Draft/Submitted + tender open
}

// ===== BidItemsController =====

// Form for BidItems/Create and BidItems/Edit.
// Only UnitPrice and Notes come from the form.
// BidId, PurchaseRequisitionItemId and Quantity are set in the controller, never from the form.
public class BidItemFormViewModel
{
    [Required(ErrorMessage = "Unit price is required.")]
    [Range(0.01, 999999999.99, ErrorMessage = "Unit price must be greater than 0.")]
    [Display(Name = "Unit price")]
    public decimal UnitPrice { get; set; }

    [StringLength(500, ErrorMessage = "Notes can be at most 500 characters.")]
    [Display(Name = "Notes (optional)")]
    public string? Notes { get; set; }

    // Optional photos of the sample (JPG/PNG), several at once. On Edit they are added to the current photos.
    [Display(Name = "Sample images (optional)")]
    public List<IFormFile> SampleImages { get; set; } = new();

    // Edit only: ids of current photos the supplier ticked for removal (checked against the item on the server)
    public List<int> RemoveImageIds { get; set; } = new();
}

// ===== BidDocumentsController =====

// Form for BidDocuments/Upload.
// BidId, FileName, FilePath and UploadedAt are set in the controller, never from the form.
public class BidDocumentUploadViewModel
{
    [Required(ErrorMessage = "Please choose a document type.")]
    [Display(Name = "Document type")]
    public string DocumentType { get; set; } = string.Empty;

    // IFormFile = the file the user picked in <input type="file">
    [Required(ErrorMessage = "Please choose a file.")]
    [Display(Name = "PDF file")]
    public IFormFile File { get; set; } = null!;
}

// ===== ComplianceController =====

// One row on Compliance/Pending: a closed tender + how many bids are in each status.
public class CompliancePendingRow
{
    public Tender Tender { get; set; } = new Tender();
    public int ReceivedCount { get; set; }        // bids that arrived while the tender is still open (content stays sealed)
    public int SubmittedCount { get; set; }       // still waiting for the compliance check
    public int CompliantCount { get; set; }
    public int NonCompliantCount { get; set; }
    public bool ReadyForEvaluation { get; set; }  // no Submitted left and at least one Compliant
}

// Data for Compliance/Review: side-by-side comparison of the bids of one tender.
public class ComplianceReviewViewModel
{
    public Tender Tender { get; set; } = new Tender();
    public List<PurchaseRequisitionItem> RequestedItems { get; set; } = new List<PurchaseRequisitionItem>();
    public List<Bid> Bids { get; set; } = new List<Bid>();  // sorted by TotalPrice, lowest first

    public bool TenderIsClosed { get; set; }      // the check buttons work only while the tender is "Closed"
    public bool MixedCurrencies { get; set; }     // the bids don't all use the same currency
    public int SubmittedCount { get; set; }
    public int CompliantCount { get; set; }
    public bool CanSendToEvaluation { get; set; }
}

// Form for Compliance/MarkNonCompliant.
// Only the reason comes from the form. The bid id is in the URL and the status is set in the controller.
public class NonCompliantViewModel
{
    [Required(ErrorMessage = "Please write the reason.")]
    [StringLength(1000, ErrorMessage = "The reason can be at most 1000 characters.")]
    [Display(Name = "Reason (the supplier will see it)")]
    public string Reason { get; set; } = string.Empty;
}
