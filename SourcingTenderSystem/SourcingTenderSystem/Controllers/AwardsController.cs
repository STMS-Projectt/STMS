using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderSystem.Data;
using SourcingTenderSystem.Models;
using SourcingTenderSystem.ViewModels.Awards;

namespace SourcingTenderSystem.Controllers;

public class AwardsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<IdentityUser> _userManager;

    public AwardsController(ApplicationDbContext context, UserManager<IdentityUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    // GET: /Awards
    public async Task<IActionResult> Index(string? searchQuery, string? statusFilter)
    {
        var query = _context.Tenders
            .Include(t => t.PurchaseRequisition)
                .ThenInclude(pr => pr.Department)
            .Include(t => t.EvaluationReport!)
                .ThenInclude(er => er.RecommendedBid)
                    .ThenInclude(b => b.SupplierProfile)
            .Include(t => t.EvaluationReport!)
                .ThenInclude(er => er.TenderApprovals)
            .Where(t => t.Status == "Manager Approved" || 
                        t.Status == "Committee Approved" || 
                        t.Status == "Awarded")
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            var search = searchQuery.Trim().ToLower();
            query = query.Where(t =>
                t.Title.ToLower().Contains(search) ||
                (t.PurchaseRequisition.Department != null && t.PurchaseRequisition.Department.Name.ToLower().Contains(search)) ||
                (t.EvaluationReport != null && t.EvaluationReport.RecommendedBid.SupplierProfile.CompanyName.ToLower().Contains(search)));
        }

        if (statusFilter == "ReadyForAward")
        {
            query = query.Where(t => t.Status == "Manager Approved" || t.Status == "Committee Approved");
        }
        else if (statusFilter == "Awarded")
        {
            query = query.Where(t => t.Status == "Awarded");
        }

        var awards = await query
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new AwardListItemViewModel
            {
                TenderId = t.Id,
                TenderTitle = t.Title,
                DepartmentName = t.PurchaseRequisition.Department != null ? t.PurchaseRequisition.Department.Name : "N/A",
                WinningSupplierName = t.EvaluationReport != null ? t.EvaluationReport.RecommendedBid.SupplierProfile.CompanyName : "N/A",
                AwardAmount = t.EvaluationReport != null ? t.EvaluationReport.RecommendedBid.TotalPrice : 0,
                Currency = t.EvaluationReport != null ? t.EvaluationReport.RecommendedBid.Currency : "JOD",
                ApprovalLevel = t.Status == "Committee Approved" ? "Top Committee Approved" : "Manager Approved",
                TenderStatus = t.Status,
                FinalDecisionDate = t.EvaluationReport != null && t.EvaluationReport.TenderApprovals.Any()
                    ? t.EvaluationReport.TenderApprovals.OrderByDescending(a => a.DecidedAt).Select(a => (DateTime?)a.DecidedAt).FirstOrDefault()
                    : t.CreatedAt
            })
            .ToListAsync();

        var viewModel = new AwardListViewModel
        {
            SearchQuery = searchQuery,
            StatusFilter = statusFilter,
            Awards = awards
        };

        return View(viewModel);
    }

    // GET: /Awards/Details/5
    public async Task<IActionResult> Details(int? id)
    {
        if (!id.HasValue || id.Value <= 0)
        {
            return RedirectToAction(nameof(Index));
        }

        var tender = await _context.Tenders
            .Include(t => t.PurchaseRequisition)
                .ThenInclude(pr => pr.Department)
            .Include(t => t.EvaluationReport!)
                .ThenInclude(er => er.RecommendedBid)
                    .ThenInclude(b => b.SupplierProfile)
            .Include(t => t.EvaluationReport!)
                .ThenInclude(er => er.RecommendedBid)
                    .ThenInclude(b => b.BidItems)
                        .ThenInclude(bi => bi.PurchaseRequisitionItem)
            .Include(t => t.EvaluationReport!)
                .ThenInclude(er => er.TenderApprovals)
                    .ThenInclude(ta => ta.ApproverEmployeeProfile)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id.Value);

        if (tender == null || tender.EvaluationReport == null)
        {
            TempData["InfoMessage"] = $"No tender or evaluation report found with ID #{id}. Please select a tender from the list.";
            return RedirectToAction(nameof(Index));
        }

        var report = tender.EvaluationReport;
        var winningBid = report.RecommendedBid;
        var supplier = winningBid.SupplierProfile;

        var lineItems = winningBid.BidItems.Select(bi => new AwardItemDetailViewModel
        {
            ItemName = bi.PurchaseRequisitionItem?.ItemName ?? "Item",
            Quantity = bi.Quantity,
            Unit = bi.PurchaseRequisitionItem?.Unit ?? "Unit",
            UnitPrice = bi.UnitPrice,
            Specifications = bi.PurchaseRequisitionItem?.Specifications ?? string.Empty,
            Notes = bi.Notes
        }).ToList();

        var approvalsTrail = report.TenderApprovals
            .OrderBy(ta => ta.DecidedAt)
            .Select(ta => new AwardApprovalStepViewModel
            {
                ApproverName = ta.ApproverEmployeeProfile.FullName,
                Stage = ta.Decision == "Approved" ? "Approval Verified" : ta.Decision,
                Decision = ta.Decision,
                Comments = ta.Comments,
                DecidedAt = ta.DecidedAt
            }).ToList();

        var viewModel = new AwardDetailsViewModel
        {
            TenderId = tender.Id,
            TenderTitle = tender.Title,
            TenderDescription = tender.Description,
            TenderTerms = tender.Terms,
            TenderStatus = tender.Status,
            RequisitionTitle = tender.PurchaseRequisition?.Title ?? "N/A",
            DepartmentName = tender.PurchaseRequisition?.Department?.Name ?? "N/A",
            SupplierId = supplier.Id,
            CompanyName = supplier.CompanyName,
            TaxNumber = supplier.TaxNumber,
            CommercialRegNo = supplier.CommercialRegNo,
            ContactPhone = supplier.ContactPhone,
            Address = supplier.Address,
            BidId = winningBid.Id,
            TotalAwardAmount = winningBid.TotalPrice,
            Currency = winningBid.Currency,
            DeliveryDays = winningBid.DeliveryDays,
            ValidityDays = winningBid.ValidityDays,
            SubmittedAt = winningBid.SubmittedAt,
            LineItems = lineItems,
            ApprovalsTrail = approvalsTrail
        };

        return View(viewModel);
    }

    // GET: /Awards/Confirm/5
    public async Task<IActionResult> Confirm(int id)
    {
        var tender = await _context.Tenders
            .Include(t => t.PurchaseRequisition)
                .ThenInclude(pr => pr.Department)
            .Include(t => t.EvaluationReport!)
                .ThenInclude(er => er.RecommendedBid)
                    .ThenInclude(b => b.SupplierProfile)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id);

        if (tender == null || tender.EvaluationReport == null)
        {
            return NotFound();
        }

        if (tender.Status == "Awarded")
        {
            TempData["InfoMessage"] = $"Tender #{tender.Id} is already officially awarded.";
            return RedirectToAction(nameof(Details), new { id = tender.Id });
        }

        var winningBid = tender.EvaluationReport.RecommendedBid;

        var viewModel = new AwardConfirmationViewModel
        {
            TenderId = tender.Id,
            TenderTitle = tender.Title,
            DepartmentName = tender.PurchaseRequisition?.Department?.Name ?? "N/A",
            WinningBidId = winningBid.Id,
            WinningSupplierName = winningBid.SupplierProfile.CompanyName,
            AwardAmount = winningBid.TotalPrice,
            Currency = winningBid.Currency,
            DeliveryDays = winningBid.DeliveryDays,
            ContractReferenceNumber = $"PO-{DateTime.UtcNow:yyyyMM}-{tender.Id:D4}",
            AgreedDeliveryDate = DateTime.UtcNow.AddDays(winningBid.DeliveryDays)
        };

        return View(viewModel);
    }

    // POST: /Awards/Confirm
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(AwardConfirmationViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var tender = await _context.Tenders
            .Include(t => t.PurchaseRequisition)
            .Include(t => t.Bids)
            .Include(t => t.EvaluationReport!)
                .ThenInclude(er => er.RecommendedBid)
                    .ThenInclude(b => b.SupplierProfile)
            .FirstOrDefaultAsync(t => t.Id == model.TenderId);

        if (tender == null || tender.EvaluationReport == null)
        {
            return NotFound();
        }

        var winningBid = tender.EvaluationReport.RecommendedBid;
        var currentProfileId = await GetCurrentProfileIdAsync();

        // 1. Update Tender status to Awarded
        tender.Status = "Awarded";

        // 2. Update Winning Bid to Accepted, and other bids to Rejected
        foreach (var bid in tender.Bids)
        {
            if (bid.Id == winningBid.Id)
            {
                bid.Status = "Accepted";
            }
            else
            {
                bid.Status = "Rejected";
            }
        }

        // 3. Create Notification for winning supplier
        _context.Notifications.Add(new Notification
        {
            SupplierProfileId = winningBid.SupplierProfileId,
            Title = "Tender Award Notice: Winning Bid Selected",
            Message = $"Congratulations! Your company '{winningBid.SupplierProfile.CompanyName}' has officially been awarded Tender '{tender.Title}' (Contract Ref: {model.ContractReferenceNumber}).",
            Link = $"/Bids/Details/{winningBid.Id}",
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        });

        // 4. Create Notification for requesting department employee if exists
        if (tender.PurchaseRequisition?.CreatedByEmployeeProfileId != null)
        {
            _context.Notifications.Add(new Notification
            {
                EmployeeProfileId = tender.PurchaseRequisition.CreatedByEmployeeProfileId,
                Title = "Tender Awarded & Finalized",
                Message = $"Tender '{tender.Title}' for your requisition '{tender.PurchaseRequisition.Title}' has been awarded to '{winningBid.SupplierProfile.CompanyName}'.",
                Link = $"/Awards/Details/{tender.Id}",
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            });
        }

        // 5. Create Audit Log entry
        _context.AuditLogs.Add(new AuditLog
        {
            EmployeeProfileId = currentProfileId,
            Action = "Tender Award Confirmed",
            EntityName = "Tender",
            EntityId = tender.Id,
            Details = $"Tender #{tender.Id} awarded to '{winningBid.SupplierProfile.CompanyName}'. Contract Ref: {model.ContractReferenceNumber}. Total Value: {winningBid.TotalPrice} {winningBid.Currency}. Notes: {model.AwardNotes}",
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = $"Tender #{tender.Id} has been successfully awarded to {winningBid.SupplierProfile.CompanyName}!";
        return RedirectToAction(nameof(Details), new { id = tender.Id });
    }

    private async Task<int> GetCurrentProfileIdAsync()
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

        var fallback = await _context.EmployeeProfiles.FirstOrDefaultAsync(p => p.IsActive)
                       ?? await _context.EmployeeProfiles.FirstOrDefaultAsync();

        return fallback?.Id ?? 1;
    }
}
