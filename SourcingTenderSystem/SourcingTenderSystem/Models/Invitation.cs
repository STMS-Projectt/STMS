using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace SourcingTenderSystem.Models;

[Table("Invitations")]
public class Invitation
{
    public int Id { get; set; }
    public int TenderId { get; set; }
    public int SupplierProfileId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime SentAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RespondedAt { get; set; }
    public string? DeclineReason { get; set; }

    public Tender Tender { get; set; } = null!;
    public SupplierProfile SupplierProfile { get; set; } = null!;
}
