using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace SourcingTenderSystem.Models;

[Table("SupplierDocuments")]
public class SupplierDocument
{
    public int Id { get; set; }
    public int SupplierProfileId { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public DateTime? ExpiryDate { get; set; }
    public DateTime UploadedAt { get; set; }

    public SupplierProfile SupplierProfile { get; set; } = null!;
}
