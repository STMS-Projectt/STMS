using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace SourcingTenderSystem.Models;

[Table("BidDocuments")]
public class BidDocument
{
    public static IReadOnlyList<string> DocumentTypes { get; } = Array.AsReadOnly(new[] { "Quotation", "Technical Offer", "Certificate", "Other" });
    public int Id { get; set; }
    public int BidId { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; }

    public Bid Bid { get; set; } = null!;
}
