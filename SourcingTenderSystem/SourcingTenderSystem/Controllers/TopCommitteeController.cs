using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderSystem.Data;
using SourcingTenderSystem.Models;
using SourcingTenderSystem.ViewModels.TopCommittee;

namespace SourcingTenderSystem.Controllers;

public class TopCommitteeController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<IdentityUser> _userManager;

    public TopCommitteeController(ApplicationDbContext context, UserManager<IdentityUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    // GET: /TopCommittee/Pending
    public async Task<IActionResult> Pending(string? searchQuery)
    {
        var query = _context.Tenders
            .Include(t => t.PurchaseRequisition)
                .ThenInclude(pr => pr.Department)
            .Include(t => t.EvaluationReport!)
                .ThenInclude(er => er.RecommendedBid)
                    .ThenInclude(b => b.SupplierProfile)
            .Include(t => t.EvaluationReport!)
                .ThenInclude(er => er.TenderApprovals)
                    .ThenInclude(ta => ta.ApproverEmployeeProfile)
            .Include(t => t.CommitteeMembers)
                .ThenInclude(cm => cm.EmployeeProfile)
            .Where(t => t.Status == "Awaiting Committee Decision" || 
                        t.CommitteeMembers.Any(cm => cm.CommitteeType == "TopCommittee"))
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            var search = searchQuery.Trim().ToLower();
            query = query.Where(t =>
                t.Title.ToLower().Contains(search) ||
                (t.PurchaseRequisition.Department != null && t.PurchaseRequisition.Department.Name.ToLower().Contains(search)) ||
                (t.EvaluationReport != null && t.EvaluationReport.RecommendedBid.SupplierProfile.CompanyName.ToLower().Contains(search)));
        }

        var tenders = await query
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new TopCommitteePendingItemViewModel
            {
                TenderId = t.Id,
                EvaluationReportId = t.EvaluationReport != null ? t.EvaluationReport.Id : 0,
                TenderTitle = t.Title,
                DepartmentName = t.PurchaseRequisition.Department != null ? t.PurchaseRequisition.Department.Name : "N/A",
                RecommendedSupplierName = t.EvaluationReport != null ? t.EvaluationReport.RecommendedBid.SupplierProfile.CompanyName : "N/A",
                RecommendedPrice = t.EvaluationReport != null ? t.EvaluationReport.RecommendedBid.TotalPrice : 0,
                Currency = t.EvaluationReport != null ? t.EvaluationReport.RecommendedBid.Currency : "JOD",
                ManagerDecision = t.EvaluationReport != null && t.EvaluationReport.TenderApprovals.Any(a => a.Decision == "Approved") ? "Approved & Forwarded" : "Pending",
                ManagerComments = t.EvaluationReport != null ? t.EvaluationReport.TenderApprovals.OrderBy(a => a.DecidedAt).Select(a => a.Comments).FirstOrDefault() : null,
                SubmittedDate = t.CreatedAt,
                TotalMembers = t.CommitteeMembers.Count(cm => cm.CommitteeType == "TopCommittee"),
                VotesRecorded = t.EvaluationReport != null ? t.EvaluationReport.TenderApprovals.Count(a => t.CommitteeMembers.Any(cm => cm.CommitteeType == "TopCommittee" && cm.EmployeeProfileId == a.ApproverEmployeeProfileId)) : 0,
                TenderStatus = t.Status
            })
            .ToListAsync();

        var viewModel = new TopCommitteePendingViewModel
        {
            SearchQuery = searchQuery,
            Tenders = tenders
        };

        return View(viewModel);
    }

    // GET: /TopCommittee/Details/5
    public async Task<IActionResult> Details(int? id)
    {
        if (!id.HasValue || id.Value <= 0)
        {
            return RedirectToAction(nameof(Pending));
        }

        var tender = await _context.Tenders
            .Include(t => t.PurchaseRequisition)
                .ThenInclude(pr => pr.Department)
            .Include(t => t.Bids)
                .ThenInclude(b => b.SupplierProfile)
            .Include(t => t.EvaluationReport!)
                .ThenInclude(er => er.RecommendedBid)
                    .ThenInclude(b => b.SupplierProfile)
            .Include(t => t.EvaluationReport!)
                .ThenInclude(er => er.PreparedByEmployeeProfile)
            .Include(t => t.EvaluationReport!)
                .ThenInclude(er => er.TenderApprovals)
                    .ThenInclude(ta => ta.ApproverEmployeeProfile)
            .Include(t => t.EvaluationReport!)
                .ThenInclude(er => er.TenderApprovals)
                    .ThenInclude(ta => ta.SelectedBid!)
                        .ThenInclude(sb => sb != null ? sb.SupplierProfile : null)
            .Include(t => t.CommitteeMembers)
                .ThenInclude(cm => cm.EmployeeProfile)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id.Value);

        if (tender == null || tender.EvaluationReport == null)
        {
            TempData["ErrorMessage"] = $"Tender #{id} not found or has no evaluation report.";
            return RedirectToAction(nameof(Pending));
        }

        var report = tender.EvaluationReport;
        var currentMemberProfileId = await GetCurrentMemberProfileIdAsync();

        // Extract manager approval (first approval on report)
        var managerApproval = report.TenderApprovals
            .OrderBy(a => a.DecidedAt)
            .FirstOrDefault();

        // Top committee members
        var topCommitteeMembers = tender.CommitteeMembers
            .Where(cm => cm.CommitteeType == "TopCommittee")
            .ToList();

        var memberVoteRecords = new List<CommitteeMemberVoteRecord>();
        foreach (var member in topCommitteeMembers)
        {
            var vote = report.TenderApprovals
                .FirstOrDefault(a => a.ApproverEmployeeProfileId == member.EmployeeProfileId && a.Id != managerApproval?.Id);

            memberVoteRecords.Add(new CommitteeMemberVoteRecord
            {
                MemberName = member.EmployeeProfile.FullName,
                MemberRole = string.IsNullOrWhiteSpace(member.MemberRole) ? "Committee Member" : member.MemberRole,
                Decision = vote != null ? vote.Decision : "Pending Vote",
                SelectedSupplierName = vote?.SelectedBid?.SupplierProfile.CompanyName,
                SelectedBidPrice = vote?.SelectedBid?.TotalPrice,
                Currency = vote?.SelectedBid?.Currency,
                Justification = vote?.Comments,
                VotedAt = vote?.DecidedAt
            });
        }

        var currentUserVote = report.TenderApprovals
            .FirstOrDefault(a => a.ApproverEmployeeProfileId == currentMemberProfileId && a.Id != managerApproval?.Id);

        var viewModel = new TopCommitteeDetailsViewModel
        {
            TenderId = tender.Id,
            EvaluationReportId = report.Id,
            TenderTitle = tender.Title,
            TenderDescription = tender.Description,
            TenderTerms = tender.Terms,
            TenderStatus = tender.Status,
            OpenDate = tender.OpenDate,
            CloseDate = tender.CloseDate,
            RequisitionTitle = tender.PurchaseRequisition?.Title ?? "N/A",
            DepartmentName = tender.PurchaseRequisition?.Department?.Name ?? "N/A",
            SourcingManagerName = managerApproval?.ApproverEmployeeProfile.FullName ?? "Sourcing Manager",
            SourcingManagerDecision = managerApproval?.Decision ?? "Approved",
            SourcingManagerComments = managerApproval?.Comments,
            SourcingManagerDecidedAt = managerApproval?.DecidedAt,
            ReportSummary = report.Summary,
            ReportComparison = report.SupplierComparison,
            ReportRecommendation = report.Recommendation,
            PreparedByName = report.PreparedByEmployeeProfile.FullName,
            ReportCreatedAt = report.CreatedAt,
            RecommendedBidId = report.RecommendedBidId,
            RecommendedSupplierName = report.RecommendedBid.SupplierProfile.CompanyName,
            RecommendedPrice = report.RecommendedBid.TotalPrice,
            RecommendedCurrency = report.RecommendedBid.Currency,
            RecommendedDeliveryDays = report.RecommendedBid.DeliveryDays,
            Bids = tender.Bids.Select(b => new TopCommitteeBidOptionViewModel
            {
                BidId = b.Id,
                SupplierName = b.SupplierProfile.CompanyName,
                TotalPrice = b.TotalPrice,
                Currency = b.Currency,
                DeliveryDays = b.DeliveryDays,
                ValidityDays = b.ValidityDays,
                Status = b.Status,
                IsRecommended = b.Id == report.RecommendedBidId
            }).ToList(),
            CommitteeMembers = memberVoteRecords,
            CurrentUserHasVoted = currentUserVote != null,
            VoteForm = new TopCommitteeVoteViewModel
            {
                TenderId = tender.Id,
                EvaluationReportId = report.Id,
                SelectedBidId = report.RecommendedBidId,
                ApproverEmployeeProfileId = currentMemberProfileId
            }
        };

        return View(viewModel);
    }

    // POST: /TopCommittee/Vote
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Vote(TopCommitteeVoteViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = "Please fill all required vote fields and provide full justifications.";
            return RedirectToAction(nameof(Details), new { id = model.TenderId });
        }

        var tender = await _context.Tenders
            .Include(t => t.EvaluationReport!)
                .ThenInclude(er => er.TenderApprovals)
            .Include(t => t.CommitteeMembers)
            .FirstOrDefaultAsync(t => t.Id == model.TenderId);

        if (tender == null || tender.EvaluationReport == null)
        {
            return NotFound();
        }

        var memberId = model.ApproverEmployeeProfileId != 0 
            ? model.ApproverEmployeeProfileId 
            : await GetCurrentMemberProfileIdAsync();

        // Check if member already voted
        var existingVote = tender.EvaluationReport.TenderApprovals
            .FirstOrDefault(a => a.ApproverEmployeeProfileId == memberId);

        if (existingVote != null)
        {
            existingVote.Decision = model.Decision;
            existingVote.SelectedBidId = model.SelectedBidId;
            existingVote.Comments = model.Comments.Trim();
            existingVote.DecidedAt = DateTime.UtcNow;
        }
        else
        {
            var newVote = new TenderApproval
            {
                EvaluationReportId = tender.EvaluationReport.Id,
                ApproverEmployeeProfileId = memberId,
                Decision = model.Decision,
                SelectedBidId = model.SelectedBidId,
                Comments = model.Comments.Trim(),
                DecidedAt = DateTime.UtcNow
            };
            _context.TenderApprovals.Add(newVote);
        }

        // Tally votes
        var topCommitteeMembers = tender.CommitteeMembers
            .Where(cm => cm.CommitteeType == "TopCommittee")
            .Select(cm => cm.EmployeeProfileId)
            .ToList();

        int totalMembers = topCommitteeMembers.Count > 0 ? topCommitteeMembers.Count : 4;

        var allDecisions = tender.EvaluationReport.TenderApprovals
            .Where(a => topCommitteeMembers.Contains(a.ApproverEmployeeProfileId))
            .Select(a => a.Decision)
            .ToList();

        // Add the current decision if it was a new vote
        if (existingVote == null)
        {
            allDecisions.Add(model.Decision);
        }

        int approveCount = allDecisions.Count(d => d == "Approved");
        int rejectCount = allDecisions.Count(d => d == "Rejected");

        // Determine majority threshold (e.g. 3 out of 4, or > 50%)
        int majority = (totalMembers / 2) + 1;

        if (approveCount >= majority)
        {
            tender.Status = "Committee Approved";
            tender.EvaluationReport.Status = "Committee Approved";
        }
        else if (rejectCount >= majority)
        {
            tender.Status = "Committee Rejected";
            tender.EvaluationReport.Status = "Committee Rejected";
        }

        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = "Your vote and justification have been securely logged in the Top Committee record.";
        return RedirectToAction(nameof(Results), new { id = model.TenderId });
    }

    // GET: /TopCommittee/Results/5
    public async Task<IActionResult> Results(int id)
    {
        var tender = await _context.Tenders
            .Include(t => t.PurchaseRequisition)
                .ThenInclude(pr => pr.Department)
            .Include(t => t.EvaluationReport!)
                .ThenInclude(er => er.RecommendedBid)
                    .ThenInclude(b => b.SupplierProfile)
            .Include(t => t.EvaluationReport!)
                .ThenInclude(er => er.TenderApprovals)
                    .ThenInclude(ta => ta.ApproverEmployeeProfile)
            .Include(t => t.EvaluationReport!)
                .ThenInclude(er => er.TenderApprovals)
                    .ThenInclude(ta => ta.SelectedBid!)
                        .ThenInclude(sb => sb != null ? sb.SupplierProfile : null)
            .Include(t => t.CommitteeMembers)
                .ThenInclude(cm => cm.EmployeeProfile)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id);

        if (tender == null || tender.EvaluationReport == null)
        {
            return NotFound();
        }

        var report = tender.EvaluationReport;
        var topCommitteeMembers = tender.CommitteeMembers
            .Where(cm => cm.CommitteeType == "TopCommittee")
            .ToList();

        var memberVoteRecords = new List<CommitteeMemberVoteRecord>();
        foreach (var member in topCommitteeMembers)
        {
            var vote = report.TenderApprovals
                .FirstOrDefault(a => a.ApproverEmployeeProfileId == member.EmployeeProfileId);

            memberVoteRecords.Add(new CommitteeMemberVoteRecord
            {
                MemberName = member.EmployeeProfile.FullName,
                MemberRole = string.IsNullOrWhiteSpace(member.MemberRole) ? "Committee Member" : member.MemberRole,
                Decision = vote != null ? vote.Decision : "Pending Vote",
                SelectedSupplierName = vote?.SelectedBid?.SupplierProfile.CompanyName,
                SelectedBidPrice = vote?.SelectedBid?.TotalPrice,
                Currency = vote?.SelectedBid?.Currency,
                Justification = vote?.Comments,
                VotedAt = vote?.DecidedAt
            });
        }

        int approveCount = memberVoteRecords.Count(v => v.Decision == "Approved");
        int rejectCount = memberVoteRecords.Count(v => v.Decision == "Rejected");
        int needMoreDetailsCount = memberVoteRecords.Count(v => v.Decision == "NeedMoreDetails" || v.Decision == "Returned");

        string outcome = "In Progress";
        if (tender.Status == "Committee Approved")
        {
            outcome = "Approved by Top Committee";
        }
        else if (tender.Status == "Committee Rejected")
        {
            outcome = "Rejected by Top Committee";
        }

        var viewModel = new TopCommitteeResultsViewModel
        {
            TenderId = tender.Id,
            EvaluationReportId = report.Id,
            TenderTitle = tender.Title,
            TenderStatus = tender.Status,
            DepartmentName = tender.PurchaseRequisition?.Department?.Name ?? "N/A",
            RecommendedSupplierName = report.RecommendedBid.SupplierProfile.CompanyName,
            RecommendedPrice = report.RecommendedBid.TotalPrice,
            Currency = report.RecommendedBid.Currency,
            TotalMembersCount = topCommitteeMembers.Count > 0 ? topCommitteeMembers.Count : 4,
            ApproveCount = approveCount,
            RejectCount = rejectCount,
            NeedMoreDetailsCount = needMoreDetailsCount,
            FinalOutcome = outcome,
            MemberVotes = memberVoteRecords
        };

        return View(viewModel);
    }

    private async Task<int> GetCurrentMemberProfileIdAsync()
    {
        var userId = _userManager.GetUserId(User);
        if (!string.IsNullOrEmpty(userId))
        {
            var profile = await _context.EmployeeProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
            if (profile != null)
            {
                return profile.Id;
            }
        }

        var committeeMember = await _context.TenderCommitteeMembers
            .Where(m => m.CommitteeType == "TopCommittee")
            .Select(m => m.EmployeeProfileId)
            .FirstOrDefaultAsync();

        if (committeeMember != 0)
        {
            return committeeMember;
        }

        var fallbackProfile = await _context.EmployeeProfiles.FirstOrDefaultAsync(p => p.IsActive)
                              ?? await _context.EmployeeProfiles.FirstOrDefaultAsync();

        return fallbackProfile?.Id ?? 1;
    }
}
