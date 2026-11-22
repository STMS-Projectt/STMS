using System.ComponentModel.DataAnnotations;

namespace SourcingTenderSystem.ViewModels;

// الحقول التي يدخلها الموظف فقط؛ الحالة والموظف والقسم يحددها الكنترولر.
public class RequisitionInput
{
    [Required(ErrorMessage = "Select a category.")]
    public int? CategoryId { get; set; }
    [Required, StringLength(200)]
    public string Title { get; set; } = "";

    [Required, StringLength(2000)]
    public string Reason { get; set; } = "";

    [Required, RegularExpression("^(Low|Normal|High|Urgent)$")]
    public string Priority { get; set; } = "Normal";

    [Required, DataType(DataType.Date), Display(Name = "Target date")]
    public DateTime? TargetDate { get; set; } = DateTime.Today.AddDays(7);

    // Item lines entered on the New request page (saved together with the request).
    public List<RequisitionItemInput> Items { get; set; } = new();
}

// بيانات عنصر واحد داخل الطلب.
public class RequisitionItemInput
{
    [Required, StringLength(200), Display(Name = "Item name")]
    public string ItemName { get; set; } = "";

    [Range(1, 1000000)]
    public int Quantity { get; set; } = 1;

    [Required, StringLength(50)]
    public string Unit { get; set; } = "Piece";

    [Required, StringLength(2000)]
    public string Specifications { get; set; } = "";
}

// ملاحظات المراجع مطلوبة لكل قرار.
public class RequisitionReviewInput
{
    [Required, StringLength(2000), Display(Name = "Review notes")]
    public string Notes { get; set; } = "";
}
