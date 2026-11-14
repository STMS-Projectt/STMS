using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace SourcingTenderSystem.Models;

[Table("PurchaseRequisitions")]
public class PurchaseRequisition
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public DateTime TargetDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public int DepartmentId { get; set; }
    public int CreatedByEmployeeProfileId { get; set; }
    public int? ReviewedByEmployeeProfileId { get; set; }
    public string? ReviewNotes { get; set; }
    public DateTime CreatedAt { get; set; }

    public Department Department { get; set; } = null!;
    public EmployeeProfile CreatedByEmployeeProfile { get; set; } = null!;
    public EmployeeProfile? ReviewedByEmployeeProfile { get; set; }
    public ICollection<PurchaseRequisitionItem> Items { get; set; } = new List<PurchaseRequisitionItem>();
    public ICollection<Tender> Tenders { get; set; } = new List<Tender>();
    public ICollection<ClarificationRequest> ClarificationRequests { get; set; } = new List<ClarificationRequest>();
}
