using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace SourcingTenderSystem.Models;

[Table("Tenders")]
public class Tender
{
    public int Id { get; set; }
    public int PurchaseRequisitionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Terms { get; set; } = string.Empty;
    public DateTime OpenDate { get; set; }
    public DateTime CloseDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public int CreatedByEmployeeProfileId { get; set; }
    public DateTime CreatedAt { get; set; }
    [Column("counter")]
    public int Counter { get; set; } = 1;

    public PurchaseRequisition PurchaseRequisition { get; set; } = null!;
    public EmployeeProfile CreatedByEmployeeProfile { get; set; } = null!;
    public ICollection<Invitation> Invitations { get; set; } = new List<Invitation>();
    public ICollection<TenderCommitteeMember> CommitteeMembers { get; set; } = new List<TenderCommitteeMember>();
    public ICollection<Bid> Bids { get; set; } = new List<Bid>();
    public ICollection<EvaluationCriterion> EvaluationCriteria { get; set; } = new List<EvaluationCriterion>();
    public ICollection<ClarificationRequest> ClarificationRequests { get; set; } = new List<ClarificationRequest>();
    public EvaluationReport? EvaluationReport { get; set; }
}
