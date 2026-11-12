using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace SourcingTenderSystem.Models;

[Table("EmployeeProfiles")]
public class EmployeeProfile
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public int? DepartmentId { get; set; }
    public string? JobTitle { get; set; }
    public bool IsActive { get; set; }

    public IdentityUser User { get; set; } = null!;
    public Department? Department { get; set; }
    public ICollection<PurchaseRequisition> CreatedPurchaseRequisitions { get; set; } = new List<PurchaseRequisition>();
    public ICollection<PurchaseRequisition> ReviewedPurchaseRequisitions { get; set; } = new List<PurchaseRequisition>();
    public ICollection<Tender> CreatedTenders { get; set; } = new List<Tender>();
    public ICollection<TenderCommitteeMember> TenderCommitteeMembers { get; set; } = new List<TenderCommitteeMember>();
    public ICollection<TechnicalEvaluation> TechnicalEvaluations { get; set; } = new List<TechnicalEvaluation>();
    public ICollection<FinancialEvaluation> FinancialEvaluations { get; set; } = new List<FinancialEvaluation>();
    public ICollection<EvaluationReport> PreparedEvaluationReports { get; set; } = new List<EvaluationReport>();
    public ICollection<TenderApproval> TenderApprovals { get; set; } = new List<TenderApproval>();
    public ICollection<ClarificationRequest> SentClarificationRequests { get; set; } = new List<ClarificationRequest>();
    public ICollection<ClarificationRequest> ReceivedClarificationRequests { get; set; } = new List<ClarificationRequest>();
    public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
    public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
}
