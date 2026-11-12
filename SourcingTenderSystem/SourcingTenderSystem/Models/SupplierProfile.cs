using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace SourcingTenderSystem.Models;

[Table("SupplierProfiles")]
public class SupplierProfile
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string TaxNumber { get; set; } = string.Empty;
    public string CommercialRegNo { get; set; } = string.Empty;
    public string ContactPhone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? StatusReason { get; set; }
    public DateTime CreatedAt { get; set; }

    public IdentityUser User { get; set; } = null!;
    public ICollection<SupplierDocument> SupplierDocuments { get; set; } = new List<SupplierDocument>();
    public ICollection<Invitation> Invitations { get; set; } = new List<Invitation>();
    public ICollection<Bid> Bids { get; set; } = new List<Bid>();
    public ICollection<ClarificationRequest> SentClarificationRequests { get; set; } = new List<ClarificationRequest>();
    public ICollection<ClarificationRequest> ReceivedClarificationRequests { get; set; } = new List<ClarificationRequest>();
    public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
    public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
}
