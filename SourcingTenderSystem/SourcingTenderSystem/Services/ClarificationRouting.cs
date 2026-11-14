using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SourcingTenderSystem.Data;
using SourcingTenderSystem.Models;
using SourcingTenderSystem.Security;

namespace SourcingTenderSystem.Services;

public sealed record ClarificationPerson(int? Employee, int? Supplier, string Name)
{
    public string Key => Employee is int id ? $"e{id}" : $"s{Supplier}";
}
public sealed record ClarificationTarget(int Id, string Title, string Status, bool CanClaim);
public sealed record ClarificationRoute(string Title, IReadOnlyList<ClarificationPerson> Recipients);

public sealed class ClarificationRouting(ApplicationDbContext db)
{
    public async Task<ClarificationPerson?> Current(ClaimsPrincipal principal)
    {
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (id is null) return null;
        var employee = await db.EmployeeProfiles.AsNoTracking().FirstOrDefaultAsync(e => e.UserId == id && e.IsActive);
        if (employee != null) return new(employee.Id, null, employee.FullName);
        if (!principal.IsInRole(Roles.Supplier)) return null;
        var supplier = await db.SupplierProfiles.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == id && s.Status == SupplierStatus.Approved);
        return supplier is null ? null : new(null, supplier.Id, supplier.CompanyName);
    }

    private IQueryable<EmployeeProfile> WithRole(string role) => db.EmployeeProfiles.AsNoTracking()
        .Where(e => e.IsActive && db.UserRoles.Any(ur => ur.UserId == e.UserId &&
            db.Roles.Any(r => r.Id == ur.RoleId && r.Name == role)));

    public async Task<IReadOnlyList<ClarificationTarget>> RequestTargets(ClarificationPerson actor)
    {
        if (actor.Employee is not int id) return [];
        var officer = await WithRole(Roles.SourcingOfficer).AnyAsync(e => e.Id == id);
        var requester = await WithRole("RequestingDepartment").AnyAsync(e => e.Id == id);
        var requests = db.PurchaseRequisitions.AsNoTracking();
        if (officer)
            requests = requests.Where(r => r.ReviewedByEmployeeProfileId == id ||
                (r.ReviewedByEmployeeProfileId == null && (r.Status == "Submitted" || r.Status == "Under Review")));
        else if (requester)
            requests = requests.Where(r => r.CreatedByEmployeeProfileId == id &&
                r.Department.IsActive && r.CreatedByEmployeeProfile.DepartmentId == r.DepartmentId);
        else return [];
        return await requests.Where(r => r.Status == "Submitted" || r.Status == "Under Review" || r.Status == "Returned")
            .OrderByDescending(r => r.Id).Take(20)
            .Select(r => new ClarificationTarget(r.Id, r.Title, r.Status,
                officer && r.ReviewedByEmployeeProfileId == null)).ToListAsync();
    }
    public async Task<ClarificationRoute?> Get(string kind, int id, ClarificationPerson actor)
    {
        var recipients = new List<ClarificationPerson>();
        var officers = WithRole(Roles.SourcingOfficer);
        if (kind == "request")
        {
            var request = await db.PurchaseRequisitions.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id);
            if (request is null || request.Status is not ("Submitted" or "Under Review" or "Returned")) return null;
            var officerId = request.ReviewedByEmployeeProfileId;
            if (officerId is null && request.Status != "Returned")
            {
                var available = await officers.Select(e => e.Id).Take(2).ToListAsync();
                if (available.Count == 1) officerId = available[0];
            }
            var requesters = WithRole("RequestingDepartment").Where(e => e.Id == request.CreatedByEmployeeProfileId &&
                e.DepartmentId == request.DepartmentId && e.Department!.IsActive);
            var assignedOfficer = officers.Where(e => e.Id == officerId);
            if (actor.Employee == request.CreatedByEmployeeProfileId && await requesters.AnyAsync())
                await Add(assignedOfficer, recipients);
            else if (actor.Employee.HasValue && await assignedOfficer.AnyAsync(e => e.Id == actor.Employee))
                await Add(requesters, recipients);
            return Route(request.Title, recipients);
        }

        if (kind == "bid")
        {
            var bid = await db.Bids.AsNoTracking().Include(b => b.Tender).Include(b => b.SupplierProfile)
                .SingleOrDefaultAsync(b => b.Id == id);
            if (bid is null || bid.SubmittedAt is null || bid.Status is not ("Submitted" or "Pending Clarification" or "Compliant" or "Non-Compliant") ||
                bid.Tender.Status is not ("Open" or "Closed" or "Under Evaluation") ||
                bid.SupplierProfile.Status != SupplierStatus.Approved) return null;
            var supplier = bid.SupplierProfile;
            if (!await db.UserRoles.AnyAsync(ur => ur.UserId == supplier.UserId &&
                db.Roles.Any(r => r.Id == ur.RoleId && r.Name == Roles.Supplier))) return null;
            var assignedOfficer = officers.Where(e => e.Id == bid.Tender.CreatedByEmployeeProfileId);
            if (actor.Supplier == supplier.Id) await Add(assignedOfficer, recipients);
            else if (actor.Employee.HasValue && await assignedOfficer.AnyAsync(e => e.Id == actor.Employee))
                recipients.Add(new(null, supplier.Id, supplier.CompanyName));
            return Route($"{bid.Tender.Title} · Bid #{bid.Id}", recipients);
        }

        if (kind != "tender" || actor.Employee is not int employeeId) return null;
        var tender = await db.Tenders.AsNoTracking().Include(t => t.EvaluationReport).SingleOrDefaultAsync(t => t.Id == id);
        if (tender is null) return null;
        var seats = db.TenderCommitteeMembers.Where(m => m.TenderId == id && m.CommitteeType == "Evaluation");
        var technical = WithRole(Roles.TechnicalCommittee).Where(e => seats.Any(m => m.EmployeeProfileId == e.Id && m.MemberRole == "Technical Representative"));
        var financial = WithRole(Roles.FinancialCommittee).Where(e => seats.Any(m => m.EmployeeProfileId == e.Id && m.MemberRole == "Financial Representative"));
        var reviewers = technical.Union(financial);
        var managers = WithRole(Roles.SourcingManager).Where(e => seats.Any(m => m.EmployeeProfileId == e.Id && m.MemberRole == "Sourcing Manager"));
        var owner = officers.Where(e => e.Id == tender.CreatedByEmployeeProfileId);
        var stage = tender.EvaluationReport?.Status;
        var isReviewer = await reviewers.AnyAsync(e => e.Id == employeeId);
        var isManager = await managers.AnyAsync(e => e.Id == employeeId);
        if (tender.Status == "Under Evaluation" || stage == "Pending Officer Approval")
        {
            if (await owner.AnyAsync(e => e.Id == employeeId)) await Add(reviewers, recipients);
            if (isReviewer) await Add(owner, recipients);
        }
        if (tender.Status == "Under Evaluation" || stage is "Pending Officer Approval" or "Pending Manager Approval")
        {
            if (isManager) await Add(reviewers, recipients);
            if (isReviewer) await Add(managers, recipients);
        }
        if (stage == "Pending Top Committee")
        {
            // Only the four members assigned at forwarding can participate.
            var top = WithRole(Roles.TopCommittee).Where(e => db.TenderCommitteeMembers.Any(m => m.TenderId == id && m.CommitteeType == "Top" && m.EmployeeProfileId == e.Id));
            if (isManager) await Add(top, recipients);
            if (await top.AnyAsync(e => e.Id == employeeId)) await Add(managers, recipients);
        }
        return Route(tender.Title, recipients);
    }

    private static ClarificationRoute? Route(string title, List<ClarificationPerson> recipients) =>
        recipients.Count == 0 ? null : new(title, recipients.DistinctBy(p => p.Key).OrderBy(p => p.Name).ToList());

    private static async Task Add(IQueryable<EmployeeProfile> query, List<ClarificationPerson> recipients) =>
        recipients.AddRange(await query.Select(e => new ClarificationPerson(e.Id, null, e.FullName)).ToListAsync());

}
