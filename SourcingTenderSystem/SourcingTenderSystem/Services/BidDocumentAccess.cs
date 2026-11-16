using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SourcingTenderSystem.Data;
using SourcingTenderSystem.Models;
using SourcingTenderSystem.Security;

namespace SourcingTenderSystem.Services;

public sealed class BidDocumentAccess(ApplicationDbContext db)
{
    public async Task<bool> CanRead(ClaimsPrincipal principal, Bid bid)
    {
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (principal.Identity?.IsAuthenticated != true || string.IsNullOrEmpty(id)) return false;
        if (principal.IsInRole(Roles.Supplier))
        {
            var ownSupplier = await db.SupplierProfiles.AnyAsync(s => s.Id == bid.SupplierProfileId &&
                s.UserId == id && s.Status == SupplierStatus.Approved);
            return Allowed(principal, bid.Tender.Status, bid.Status, bid.SubmittedAt != null, ownSupplier, false, false, false);
        }

        var active = await db.EmployeeProfiles.AnyAsync(e => e.UserId == id && e.IsActive);
        if (!active) return false;
        var seats = await db.TenderCommitteeMembers.Where(m => m.TenderId == bid.TenderId &&
            m.EmployeeProfile.UserId == id && m.EmployeeProfile.IsActive)
            .Select(m => new { m.CommitteeType, m.MemberRole }).ToListAsync();
        var evaluation = seats.Any(m => m.CommitteeType == "Evaluation" &&
            (m.MemberRole == "Technical Representative" && principal.IsInRole(Roles.TechnicalCommittee) ||
             m.MemberRole == "Financial Representative" && principal.IsInRole(Roles.FinancialCommittee) ||
             m.MemberRole == "Department Representative" && principal.IsInRole("RequestingDepartment") ||
             m.MemberRole == "Sourcing Manager" && principal.IsInRole(Roles.SourcingManager)));
        var top = seats.Any(m => m.CommitteeType == "Top" && m.MemberRole == "Top Committee Member");
        return Allowed(principal, bid.Tender.Status, bid.Status, bid.SubmittedAt != null, false, active, evaluation, top);
    }

    public static bool Allowed(ClaimsPrincipal principal, string stage, string status, bool submitted,
        bool ownSupplier, bool activeEmployee, bool evaluationSeat, bool topSeat)
    {
        if (principal.Identity?.IsAuthenticated != true) return false;
        if (principal.IsInRole(Roles.Supplier)) return ownSupplier;
        if (!activeEmployee || !submitted || status is not ("Submitted" or "Pending Clarification" or "Compliant" or "Non-Compliant")) return false;

        // Compliance is a shared sourcing queue; ownership is not a precondition.
        if (principal.IsInRole(Roles.SourcingOfficer) &&
            stage is ("Closed" or "Under Evaluation" or "Evaluation Closed" or "Awaiting Manager Review" or
                "Awaiting Committee Decision" or "Awarded" or "Cancelled"))
            return true;

        if (status != "Compliant") return false;
        if (principal.IsInRole(Roles.TopCommittee) && topSeat &&
            stage is ("Awaiting Committee Decision" or "Awarded" or "Cancelled")) return true;

        return evaluationSeat && (stage == "Under Evaluation" ||
            principal.IsInRole(Roles.SourcingManager) && stage is ("Evaluation Closed" or "Awaiting Manager Review" or
                "Awaiting Committee Decision" or "Awarded" or "Cancelled"));
    }
}
