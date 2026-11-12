using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace SourcingTenderSystem.Models;

[Table("ClarificationRequests")]
public class ClarificationRequest
{
    public int Id { get; set; }
    public int? SenderEmployeeProfileId { get; set; }
    public int? SenderSupplierProfileId { get; set; }
    public int? ReceiverEmployeeProfileId { get; set; }
    public int? ReceiverSupplierProfileId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Response { get; set; }
    public string Status { get; set; } = string.Empty;
    public int? PurchaseRequisitionId { get; set; }
    public int? TenderId { get; set; }
    public int? BidId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? AnsweredAt { get; set; }

    public EmployeeProfile? SenderEmployeeProfile { get; set; }
    public SupplierProfile? SenderSupplierProfile { get; set; }
    public EmployeeProfile? ReceiverEmployeeProfile { get; set; }
    public SupplierProfile? ReceiverSupplierProfile { get; set; }
    public PurchaseRequisition? PurchaseRequisition { get; set; }
    public Tender? Tender { get; set; }
    public Bid? Bid { get; set; }
}
