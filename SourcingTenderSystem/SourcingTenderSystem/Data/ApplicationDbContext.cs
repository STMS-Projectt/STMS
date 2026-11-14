using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SourcingTenderSystem.Models;

namespace SourcingTenderSystem.Data;

public class ApplicationDbContext : IdentityDbContext<IdentityUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public DbSet<Department> Departments => Set<Department>();
    public DbSet<EmployeeProfile> EmployeeProfiles => Set<EmployeeProfile>();
    public DbSet<SupplierProfile> SupplierProfiles => Set<SupplierProfile>();
    public DbSet<SupplierDocument> SupplierDocuments => Set<SupplierDocument>();
    public DbSet<PurchaseRequisition> PurchaseRequisitions => Set<PurchaseRequisition>();
    public DbSet<PurchaseRequisitionItem> PurchaseRequisitionItems => Set<PurchaseRequisitionItem>();
    public DbSet<Tender> Tenders => Set<Tender>();
    public DbSet<TenderCommitteeMember> TenderCommitteeMembers => Set<TenderCommitteeMember>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<BidItem> BidItems => Set<BidItem>();
    public DbSet<Bid> Bids => Set<Bid>();
    public DbSet<BidDocument> BidDocuments => Set<BidDocument>();
    public DbSet<EvaluationCriterion> EvaluationCriteria => Set<EvaluationCriterion>();
    public DbSet<TechnicalEvaluation> TechnicalEvaluations => Set<TechnicalEvaluation>();
    public DbSet<FinancialEvaluation> FinancialEvaluations => Set<FinancialEvaluation>();
    public DbSet<EvaluationReport> EvaluationReports => Set<EvaluationReport>();
    public DbSet<TenderApproval> TenderApprovals => Set<TenderApproval>();
    public DbSet<ClarificationRequest> ClarificationRequests => Set<ClarificationRequest>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<EmployeeProfile>()
            .HasOne(x => x.User)
            .WithOne()
            .HasForeignKey<EmployeeProfile>(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SupplierProfile>()
            .HasOne(x => x.User)
            .WithOne()
            .HasForeignKey<SupplierProfile>(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<EmployeeProfile>()
            .HasOne(x => x.Department)
            .WithMany(x => x.EmployeeProfiles)
            .HasForeignKey(x => x.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<SupplierDocument>()
            .HasOne(x => x.SupplierProfile)
            .WithMany(x => x.SupplierDocuments)
            .HasForeignKey(x => x.SupplierProfileId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PurchaseRequisition>()
            .HasOne(x => x.Department)
            .WithMany(x => x.PurchaseRequisitions)
            .HasForeignKey(x => x.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<PurchaseRequisition>()
            .HasOne(x => x.CreatedByEmployeeProfile)
            .WithMany(x => x.CreatedPurchaseRequisitions)
            .HasForeignKey(x => x.CreatedByEmployeeProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<PurchaseRequisition>()
            .HasOne(x => x.ReviewedByEmployeeProfile)
            .WithMany(x => x.ReviewedPurchaseRequisitions)
            .HasForeignKey(x => x.ReviewedByEmployeeProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<PurchaseRequisitionItem>()
            .HasOne(x => x.PurchaseRequisition)
            .WithMany(x => x.Items)
            .HasForeignKey(x => x.PurchaseRequisitionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Tender>()
            .HasOne(x => x.PurchaseRequisition)
            .WithMany(x => x.Tenders)
            .HasForeignKey(x => x.PurchaseRequisitionId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Tender>()
            .HasOne(x => x.CreatedByEmployeeProfile)
            .WithMany(x => x.CreatedTenders)
            .HasForeignKey(x => x.CreatedByEmployeeProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<TenderCommitteeMember>()
            .HasOne(x => x.Tender)
            .WithMany(x => x.CommitteeMembers)
            .HasForeignKey(x => x.TenderId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TenderCommitteeMember>()
            .HasOne(x => x.EmployeeProfile)
            .WithMany(x => x.TenderCommitteeMembers)
            .HasForeignKey(x => x.EmployeeProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Invitation>()
            .HasOne(x => x.Tender)
            .WithMany(x => x.Invitations)
            .HasForeignKey(x => x.TenderId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Invitation>()
            .HasOne(x => x.SupplierProfile)
            .WithMany(x => x.Invitations)
            .HasForeignKey(x => x.SupplierProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Bid>()
            .HasOne(x => x.Tender)
            .WithMany(x => x.Bids)
            .HasForeignKey(x => x.TenderId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Bid>()
            .HasOne(x => x.SupplierProfile)
            .WithMany(x => x.Bids)
            .HasForeignKey(x => x.SupplierProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<BidItem>()
            .HasOne(x => x.Bid)
            .WithMany(x => x.BidItems)
            .HasForeignKey(x => x.BidId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<BidItem>()
            .HasOne(x => x.PurchaseRequisitionItem)
            .WithMany(x => x.BidItems)
            .HasForeignKey(x => x.PurchaseRequisitionItemId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<BidDocument>()
            .HasOne(x => x.Bid)
            .WithMany(x => x.BidDocuments)
            .HasForeignKey(x => x.BidId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<EvaluationCriterion>()
            .HasOne(x => x.Tender)
            .WithMany(x => x.EvaluationCriteria)
            .HasForeignKey(x => x.TenderId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TechnicalEvaluation>()
            .HasOne(x => x.Bid)
            .WithMany(x => x.TechnicalEvaluations)
            .HasForeignKey(x => x.BidId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<TechnicalEvaluation>()
            .HasOne(x => x.EvaluationCriterion)
            .WithMany(x => x.TechnicalEvaluations)
            .HasForeignKey(x => x.EvaluationCriteriaId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<TechnicalEvaluation>()
            .HasOne(x => x.EvaluatorEmployeeProfile)
            .WithMany(x => x.TechnicalEvaluations)
            .HasForeignKey(x => x.EvaluatorEmployeeProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<FinancialEvaluation>()
            .HasOne(x => x.Bid)
            .WithMany(x => x.FinancialEvaluations)
            .HasForeignKey(x => x.BidId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<FinancialEvaluation>()
            .HasOne(x => x.EvaluationCriterion)
            .WithMany(x => x.FinancialEvaluations)
            .HasForeignKey(x => x.EvaluationCriteriaId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<FinancialEvaluation>()
            .HasOne(x => x.EvaluatorEmployeeProfile)
            .WithMany(x => x.FinancialEvaluations)
            .HasForeignKey(x => x.EvaluatorEmployeeProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<EvaluationReport>()
            .HasOne(x => x.Tender)
            .WithOne(x => x.EvaluationReport)
            .HasForeignKey<EvaluationReport>(x => x.TenderId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<EvaluationReport>()
            .HasOne(x => x.RecommendedBid)
            .WithMany(x => x.RecommendedInEvaluationReports)
            .HasForeignKey(x => x.RecommendedBidId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<EvaluationReport>()
            .HasOne(x => x.PreparedByEmployeeProfile)
            .WithMany(x => x.PreparedEvaluationReports)
            .HasForeignKey(x => x.PreparedByEmployeeProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<TenderApproval>()
            .HasOne(x => x.EvaluationReport)
            .WithMany(x => x.TenderApprovals)
            .HasForeignKey(x => x.EvaluationReportId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<TenderApproval>()
            .HasOne(x => x.ApproverEmployeeProfile)
            .WithMany(x => x.TenderApprovals)
            .HasForeignKey(x => x.ApproverEmployeeProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<TenderApproval>()
            .HasOne(x => x.SelectedBid)
            .WithMany(x => x.SelectedInTenderApprovals)
            .HasForeignKey(x => x.SelectedBidId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ClarificationRequest>()
            .HasOne(x => x.SenderEmployeeProfile)
            .WithMany(x => x.SentClarificationRequests)
            .HasForeignKey(x => x.SenderEmployeeProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ClarificationRequest>()
            .HasOne(x => x.SenderSupplierProfile)
            .WithMany(x => x.SentClarificationRequests)
            .HasForeignKey(x => x.SenderSupplierProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ClarificationRequest>()
            .HasOne(x => x.ReceiverEmployeeProfile)
            .WithMany(x => x.ReceivedClarificationRequests)
            .HasForeignKey(x => x.ReceiverEmployeeProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ClarificationRequest>()
            .HasOne(x => x.ReceiverSupplierProfile)
            .WithMany(x => x.ReceivedClarificationRequests)
            .HasForeignKey(x => x.ReceiverSupplierProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ClarificationRequest>()
            .HasOne(x => x.PurchaseRequisition)
            .WithMany(x => x.ClarificationRequests)
            .HasForeignKey(x => x.PurchaseRequisitionId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ClarificationRequest>()
            .HasOne(x => x.Tender)
            .WithMany(x => x.ClarificationRequests)
            .HasForeignKey(x => x.TenderId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ClarificationRequest>()
            .HasOne(x => x.Bid)
            .WithMany(x => x.ClarificationRequests)
            .HasForeignKey(x => x.BidId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Notification>()
            .HasOne(x => x.EmployeeProfile)
            .WithMany(x => x.Notifications)
            .HasForeignKey(x => x.EmployeeProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Notification>()
            .HasOne(x => x.SupplierProfile)
            .WithMany(x => x.Notifications)
            .HasForeignKey(x => x.SupplierProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<AuditLog>()
            .HasOne(x => x.EmployeeProfile)
            .WithMany(x => x.AuditLogs)
            .HasForeignKey(x => x.EmployeeProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<AuditLog>()
            .HasOne(x => x.SupplierProfile)
            .WithMany(x => x.AuditLogs)
            .HasForeignKey(x => x.SupplierProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Department>()
            .HasIndex(x => x.Name)
            .IsUnique();

        modelBuilder.Entity<SupplierProfile>()
            .HasIndex(x => x.TaxNumber)
            .IsUnique();

        modelBuilder.Entity<Bid>()
            .HasIndex(x => new { x.TenderId, x.SupplierProfileId })
            .IsUnique();

        modelBuilder.Entity<Tender>()
            .Property(x => x.Counter)
            .HasColumnName("counter")
            .HasDefaultValue(1);
    }
}
