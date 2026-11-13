using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderSystem.Data;
using SourcingTenderSystem.Models;
using SourcingTenderSystem.ViewModels.TenderApprovals;

namespace SourcingTenderSystem.Controllers;

public class TenderApprovalsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<IdentityUser> _userManager;

    public TenderApprovalsController(ApplicationDbContext context, UserManager<IdentityUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    // GET: /TenderApprovals/Pending
    public async Task<IActionResult> Pending(string? searchQuery, string? statusFilter)
    {
        var query = _context.EvaluationReports
            .Include(r => r.Tender)
                .ThenInclude(t => t.PurchaseRequisition)
                    .ThenInclude(pr => pr.Department)
            .Include(r => r.Tender)
                .ThenInclude(t => t.CommitteeMembers)
            .Include(r => r.RecommendedBid)
                .ThenInclude(b => b.SupplierProfile)
            .Include(r => r.PreparedByEmployeeProfile)
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            var search = searchQuery.Trim().ToLower();
            query = query.Where(r =>
                r.Tender.Title.ToLower().Contains(search) ||
                (r.Tender.PurchaseRequisition.Department != null && r.Tender.PurchaseRequisition.Department.Name.ToLower().Contains(search)) ||
                r.RecommendedBid.SupplierProfile.CompanyName.ToLower().Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(statusFilter))
        {
            query = query.Where(r => r.Status == statusFilter || r.Tender.Status == statusFilter);
        }

        var reports = await query
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new PendingApprovalItemViewModel
            {
                EvaluationReportId = r.Id,
                TenderId = r.TenderId,
                TenderTitle = r.Tender.Title,
                DepartmentName = r.Tender.PurchaseRequisition.Department != null ? r.Tender.PurchaseRequisition.Department.Name : "N/A",
                PreparedByName = r.PreparedByEmployeeProfile.FullName,
                RecommendedSupplierName = r.RecommendedBid.SupplierProfile.CompanyName,
                RecommendedBidPrice = r.RecommendedBid.TotalPrice,
                Currency = r.RecommendedBid.Currency,
                CreatedAt = r.CreatedAt,
                TenderStatus = r.Tender.Status,
                ReportStatus = r.Status,
                HasTopCommittee = r.Tender.CommitteeMembers.Any(cm => cm.CommitteeType == "TopCommittee")
            })
            .ToListAsync();

        var viewModel = new PendingApprovalsViewModel
        {
            SearchQuery = searchQuery,
            StatusFilter = statusFilter,
            Reports = reports
        };

        return View(viewModel);
    }

    // GET: /TenderApprovals/Details/5
    public async Task<IActionResult> Details(int? id)
    {
        if (!id.HasValue || id.Value <= 0)
        {
            return RedirectToAction(nameof(Pending));
        }

        var report = await _context.EvaluationReports
            .Include(r => r.Tender)
                .ThenInclude(t => t.PurchaseRequisition)
                    .ThenInclude(pr => pr.Department)
            .Include(r => r.Tender)
                .ThenInclude(t => t.Bids)
                    .ThenInclude(b => b.SupplierProfile)
            .Include(r => r.RecommendedBid)
                .ThenInclude(b => b.SupplierProfile)
            .Include(r => r.PreparedByEmployeeProfile)
            .Include(r => r.TenderApprovals)
                .ThenInclude(ta => ta.ApproverEmployeeProfile)
            .Include(r => r.TenderApprovals)
                .ThenInclude(ta => ta.SelectedBid)
                    .ThenInclude(sb => sb != null ? sb.SupplierProfile : null)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id.Value);

        if (report == null)
        {
            TempData["ErrorMessage"] = $"Evaluation Report #{id} not found.";
            return RedirectToAction(nameof(Pending));
        }

        var currentApproverId = await GetCurrentApproverProfileIdAsync();

        var viewModel = new ApprovalDetailsViewModel
        {
            EvaluationReportId = report.Id,
            TenderId = report.TenderId,
            TenderTitle = report.Tender.Title,
            TenderDescription = report.Tender.Description,
            TenderTerms = report.Tender.Terms,
            TenderStatus = report.Tender.Status,
            OpenDate = report.Tender.OpenDate,
            CloseDate = report.Tender.CloseDate,
            RequisitionTitle = report.Tender.PurchaseRequisition.Title,
            DepartmentName = report.Tender.PurchaseRequisition.Department?.Name ?? "N/A",
            Summary = report.Summary,
            SupplierComparison = report.SupplierComparison,
            Recommendation = report.Recommendation,
            ReportStatus = report.Status,
            PreparedByName = report.PreparedByEmployeeProfile.FullName,
            PreparedAt = report.CreatedAt,
            RecommendedBidId = report.RecommendedBidId,
            RecommendedSupplierName = report.RecommendedBid.SupplierProfile.CompanyName,
            RecommendedPrice = report.RecommendedBid.TotalPrice,
            RecommendedCurrency = report.RecommendedBid.Currency,
            RecommendedDeliveryDays = report.RecommendedBid.DeliveryDays,
            SelectedBidId = report.RecommendedBidId,
            CurrentApproverProfileId = currentApproverId,
            Bids = report.Tender.Bids.Select(b => new BidComparisonItemViewModel
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
            ApprovalHistory = report.TenderApprovals.OrderByDescending(a => a.DecidedAt).Select(a => new ApprovalHistoryItemViewModel
            {
                ApproverName = a.ApproverEmployeeProfile.FullName,
                Decision = a.Decision,
                SelectedSupplierName = a.SelectedBid?.SupplierProfile.CompanyName,
                Comments = a.Comments,
                DecidedAt = a.DecidedAt
            }).ToList()
        };

        return View(viewModel);
    }

    // POST: /TenderApprovals/Approve
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(ApprovalDetailsViewModel model)
    {
        var report = await _context.EvaluationReports
            .Include(r => r.Tender)
            .FirstOrDefaultAsync(r => r.Id == model.EvaluationReportId);

        if (report == null)
        {
            return NotFound();
        }

        var approverId = model.CurrentApproverProfileId ?? await GetCurrentApproverProfileIdAsync();

        var approval = new TenderApproval
        {
            EvaluationReportId = report.Id,
            ApproverEmployeeProfileId = approverId,
            Decision = "Approved",
            SelectedBidId = model.SelectedBidId ?? report.RecommendedBidId,
            Comments = string.IsNullOrWhiteSpace(model.Comments) ? "Approved by Sourcing Manager" : model.Comments.Trim(),
            DecidedAt = DateTime.UtcNow
        };

        _context.TenderApprovals.Add(approval);

        report.Status = "Approved";

        if (model.ForwardToTopCommittee)
        {
            report.Tender.Status = "Awaiting Committee Decision";
            TempData["SuccessMessage"] = $"Report #{report.Id} approved and forwarded to Top Committee.";
        }
        else
        {
            report.Tender.Status = "Manager Approved";
            TempData["SuccessMessage"] = $"Report #{report.Id} approved successfully and ready for award.";
        }

        await _context.SaveChangesAsync();

        return RedirectToAction(nameof(Pending));
    }

    // POST: /TenderApprovals/Reject
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(ApprovalDetailsViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Comments))
        {
            TempData["ErrorMessage"] = "Comments are mandatory when rejecting a recommendation.";
            return RedirectToAction(nameof(Details), new { id = model.EvaluationReportId });
        }

        var report = await _context.EvaluationReports
            .Include(r => r.Tender)
            .FirstOrDefaultAsync(r => r.Id == model.EvaluationReportId);

        if (report == null)
        {
            return NotFound();
        }

        var approverId = model.CurrentApproverProfileId ?? await GetCurrentApproverProfileIdAsync();

        var approval = new TenderApproval
        {
            EvaluationReportId = report.Id,
            ApproverEmployeeProfileId = approverId,
            Decision = "Rejected",
            SelectedBidId = model.SelectedBidId,
            Comments = model.Comments.Trim(),
            DecidedAt = DateTime.UtcNow
        };

        _context.TenderApprovals.Add(approval);

        report.Status = "Rejected";
        report.Tender.Status = "Manager Rejected";

        await _context.SaveChangesAsync();

        TempData["WarningMessage"] = $"Evaluation Report #{report.Id} has been rejected.";
        return RedirectToAction(nameof(Pending));
    }

    // POST: /TenderApprovals/Return
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Return(ApprovalDetailsViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Comments))
        {
            TempData["ErrorMessage"] = "Please provide feedback or required modifications in the comments.";
            return RedirectToAction(nameof(Details), new { id = model.EvaluationReportId });
        }

        var report = await _context.EvaluationReports
            .Include(r => r.Tender)
            .FirstOrDefaultAsync(r => r.Id == model.EvaluationReportId);

        if (report == null)
        {
            return NotFound();
        }

        var approverId = model.CurrentApproverProfileId ?? await GetCurrentApproverProfileIdAsync();

        var approval = new TenderApproval
        {
            EvaluationReportId = report.Id,
            ApproverEmployeeProfileId = approverId,
            Decision = "Returned",
            SelectedBidId = model.SelectedBidId,
            Comments = model.Comments.Trim(),
            DecidedAt = DateTime.UtcNow
        };

        _context.TenderApprovals.Add(approval);

        report.Status = "ReturnedForReview";
        report.Tender.Status = "Returned for Review";

        await _context.SaveChangesAsync();

        TempData["InfoMessage"] = $"Report #{report.Id} returned to Evaluation Committee for revisions.";
        return RedirectToAction(nameof(Pending));
    }

    private async Task<int> GetCurrentApproverProfileIdAsync()
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

        var fallbackProfile = await _context.EmployeeProfiles.FirstOrDefaultAsync(p => p.IsActive)
                              ?? await _context.EmployeeProfiles.FirstOrDefaultAsync();

        return fallbackProfile?.Id ?? 1;
    }
}
