using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace SourcingTenderSystem.Models;

[Table("BidDocuments")]
public class BidDocument
{
    public const string SampleImage = "Sample Image";
    public const string LegalWarranty = "Legal Warranty";
    public const string BankGuarantee = "Bank Guarantee";
    public static IReadOnlyList<string> DocumentTypes { get; } = Array.AsReadOnly(new[]
        { "Quotation", "Technical Offer", "Certificate", SampleImage, LegalWarranty, BankGuarantee, "Other" });
    // The winning supplier may still upload these after the award.
    public static IReadOnlyList<string> AfterAwardTypes { get; } = Array.AsReadOnly(new[] { LegalWarranty, BankGuarantee });
    [NotMapped] public bool IsImage => Path.GetExtension(FilePath).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png";
    public int Id { get; set; }
    public int BidId { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; }

    public Bid Bid { get; set; } = null!;
}
