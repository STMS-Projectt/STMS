using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SourcingTenderSystem.Data;
using SourcingTenderSystem.Models;

namespace SourcingTenderSystem.Controllers;

[Authorize(Roles = "SourcingOfficer")]
public class TendersController : Controller
{
    private const string DraftStatus = "Draft";
    private const string EvaluationCommitteeType = "Evaluation";
    private const string EvaluationCommitteeRole = "EvaluationCommittee";

    // The committee can be changed until the evaluation starts.
    private static readonly string[] CommitteeEditableStatuses = { "Draft", "Published", "Open", "Closed" };

    private readonly ApplicationDbContext _context;
    private readonly UserManager<IdentityUser> _userManager;

    public TendersController(ApplicationDbContext context, UserManager<IdentityUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    // GET: /Tenders
    public async Task<IActionResult> Index()
    {
        var tenders = await _context.Tenders
            .AsNoTracking()
            .Include(t => t.PurchaseRequisition)
            .Include(t => t.CreatedByEmployeeProfile)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();

        return View(tenders);
    }

    // GET: /Tenders/Details/5
    public async Task<IActionResult> Details(int? id)
    {
        if (id == null) return NotFound();

        var tender = await _context.Tenders
            .AsNoTracking()
            .Include(t => t.PurchaseRequisition).ThenInclude(r => r.Department)
            .Include(t => t.CreatedByEmployeeProfile)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (tender == null) return NotFound();

        var counts = await _context.Tenders
            .Where(t => t.Id == id)
            .Select(t => new { Invitations = t.Invitations.Count, Bids = t.Bids.Count })
            .FirstAsync();

        ViewBag.InvitationCount = counts.Invitations;
        ViewBag.BidCount = counts.Bids;

        return View(tender);
    }

    // GET: /Tenders/Create
    public async Task<IActionResult> Create()
    {
        var model = new TenderFormViewModel
        {
            PurchaseRequests = await GetApprovedRequestsAsync()
        };
        return View(model);
    }

    // POST: /Tenders/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(TenderFormViewModel model)
    {
        PurchaseRequisition? requisition = null;
        if (model.PurchaseRequisitionId != null)
        {
            requisition = await _context.PurchaseRequisitions
                .Include(r => r.Tender)
                .FirstOrDefaultAsync(r => r.Id == model.PurchaseRequisitionId);

            if (requisition == null || requisition.Status != "Approved" || requisition.Tender != null)
            {
                ModelState.AddModelError(nameof(model.PurchaseRequisitionId),
                    "This purchase request is not available for a new tender.");
            }
        }

        var employee = await GetCurrentEmployeeAsync();
        if (employee == null)
        {
            ModelState.AddModelError(string.Empty, "Your account is not linked to an employee profile.");
        }

        if (!ModelState.IsValid)
        {
            model.PurchaseRequests = await GetApprovedRequestsAsync();
            return View(model);
        }

        var tender = new Tender
        {
            PurchaseRequisitionId = requisition!.Id,
            Title = model.Title.Trim(),
            Description = model.Description.Trim(),
            Terms = model.Terms.Trim(),
            OpenDate = model.OpenDate!.Value,
            CloseDate = model.CloseDate!.Value,
            Status = DraftStatus,
            CreatedByEmployeeProfileId = employee!.Id,
            CreatedAt = DateTime.UtcNow
        };

        requisition.Status = "Converted to Tender";
        _context.Tenders.Add(tender);
        await _context.SaveChangesAsync();

        AddAuditLog(employee.Id, "Create", tender.Id, "Tender created from the approved request.");
        await _context.SaveChangesAsync();

        TempData["Success"] = "Tender created successfully.";
        return RedirectToAction(nameof(Index));
    }

    // GET: /Tenders/Edit/5
    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null) return NotFound();

        var tender = await _context.Tenders
            .AsNoTracking()
            .Include(t => t.PurchaseRequisition)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (tender == null) return NotFound();

        if (tender.Status != DraftStatus)
        {
            TempData["Error"] = "Only draft tenders can be edited.";
            return RedirectToAction(nameof(Index));
        }

        var model = new TenderFormViewModel
        {
            Id = tender.Id,
            PurchaseRequisitionId = tender.PurchaseRequisitionId,
            PurchaseRequisitionTitle = tender.PurchaseRequisition.Title,
            Title = tender.Title,
            Description = tender.Description,
            Terms = tender.Terms,
            OpenDate = tender.OpenDate,
            CloseDate = tender.CloseDate
        };
        return View(model);
    }

    // POST: /Tenders/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, TenderFormViewModel model)
    {
        if (id != model.Id) return NotFound();

        // The purchase request cannot be changed after the tender is created.
        ModelState.Remove(nameof(TenderFormViewModel.PurchaseRequisitionId));

        var tender = await _context.Tenders
            .Include(t => t.PurchaseRequisition)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (tender == null) return NotFound();

        if (tender.Status != DraftStatus)
        {
            TempData["Error"] = "Only draft tenders can be edited.";
            return RedirectToAction(nameof(Index));
        }

        if (!ModelState.IsValid)
        {
            model.PurchaseRequisitionId = tender.PurchaseRequisitionId;
            model.PurchaseRequisitionTitle = tender.PurchaseRequisition.Title;
            return View(model);
        }

        tender.Title = model.Title.Trim();
        tender.Description = model.Description.Trim();
        tender.Terms = model.Terms.Trim();
        tender.OpenDate = model.OpenDate!.Value;
        tender.CloseDate = model.CloseDate!.Value;

        var employee = await GetCurrentEmployeeAsync();
        AddAuditLog(employee?.Id, "Update", tender.Id, "Tender details updated.");
        await _context.SaveChangesAsync();

        TempData["Success"] = "Tender updated successfully.";
        return RedirectToAction(nameof(Index));
    }

    // POST: /Tenders/Delete/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var tender = await _context.Tenders
            .Include(t => t.PurchaseRequisition)
            .Include(t => t.EvaluationCriteria)
            .Include(t => t.CommitteeMembers)
            .Include(t => t.Invitations)
            .Include(t => t.Bids)
            .Include(t => t.ClarificationRequests)
            .AsSplitQuery()
            .FirstOrDefaultAsync(t => t.Id == id);

        if (tender == null) return NotFound();

        if (tender.Status != DraftStatus)
        {
            TempData["Error"] = "Only draft tenders can be deleted.";
            return RedirectToAction(nameof(Index));
        }

        if (tender.Invitations.Any() || tender.Bids.Any() || tender.ClarificationRequests.Any())
        {
            TempData["Error"] = "This tender already has invitations, bids or clarification requests and cannot be deleted.";
            return RedirectToAction(nameof(Index));
        }

        // Remove the tender's own setup data, then give the purchase request back to the Sourcing Officer.
        _context.RemoveRange(tender.EvaluationCriteria);
        _context.RemoveRange(tender.CommitteeMembers);
        tender.PurchaseRequisition.Status = "Approved";
        _context.Tenders.Remove(tender);

        var employee = await GetCurrentEmployeeAsync();
        AddAuditLog(employee?.Id, "Delete", tender.Id, "Draft tender deleted. The purchase request returned to Approved.");
        await _context.SaveChangesAsync();

        TempData["Success"] = "Tender deleted.";
        return RedirectToAction(nameof(Index));
    }

    // GET: /Tenders/AssignCommittee/5
    public async Task<IActionResult> AssignCommittee(int? id)
    {
        if (id == null) return NotFound();

        var tender = await _context.Tenders
            .AsNoTracking()
            .Include(t => t.CommitteeMembers)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (tender == null) return NotFound();

        if (!CommitteeEditableStatuses.Contains(tender.Status))
        {
            TempData["Error"] = "The committee cannot be changed after the evaluation has started.";
            return RedirectToAction(nameof(Index));
        }

        var members = await GetEligibleMembersAsync();
        foreach (var current in tender.CommitteeMembers.Where(m => m.CommitteeType == EvaluationCommitteeType))
        {
            var option = members.FirstOrDefault(m => m.EmployeeProfileId == current.EmployeeProfileId);
            if (option == null) continue;
            option.IsSelected = true;
            option.MemberRole = current.MemberRole;
        }

        return View(new AssignCommitteeViewModel
        {
            TenderId = tender.Id,
            TenderTitle = tender.Title,
            Members = members
        });
    }

    // POST: /Tenders/AssignCommittee/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignCommittee(int id, AssignCommitteeViewModel model)
    {
        if (id != model.TenderId) return NotFound();

        var tender = await _context.Tenders
            .Include(t => t.CommitteeMembers)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (tender == null) return NotFound();

        if (!CommitteeEditableStatuses.Contains(tender.Status))
        {
            TempData["Error"] = "The committee cannot be changed after the evaluation has started.";
            return RedirectToAction(nameof(Index));
        }

        var eligible = await GetEligibleMembersAsync();
        var selected = (model.Members ?? new List<CommitteeMemberOption>())
            .Where(m => m.IsSelected)
            .ToList();

        if (selected.Count != AssignCommitteeViewModel.Roles.Length)
        {
            ModelState.AddModelError(string.Empty, "Select exactly 4 committee members.");
        }
        else if (selected.Any(s => eligible.All(e => e.EmployeeProfileId != s.EmployeeProfileId)))
        {
            ModelState.AddModelError(string.Empty, "One of the selected employees cannot be assigned to the committee.");
        }
        else if (selected.Select(s => s.MemberRole).Distinct().Count() != AssignCommitteeViewModel.Roles.Length
                 || selected.Any(s => s.MemberRole == null || !AssignCommitteeViewModel.Roles.Contains(s.MemberRole)))
        {
            ModelState.AddModelError(string.Empty,
                "Give each of the 4 members a different role: Sourcing Manager, Department Representative, Financial Representative and Technical Representative.");
        }

        if (!ModelState.IsValid)
        {
            // Keep what the user already ticked and show the form again.
            foreach (var option in eligible)
            {
                var posted = model.Members?.FirstOrDefault(m => m.EmployeeProfileId == option.EmployeeProfileId);
                if (posted == null) continue;
                option.IsSelected = posted.IsSelected;
                option.MemberRole = posted.MemberRole;
            }
            model.TenderTitle = tender.Title;
            model.Members = eligible;
            return View(model);
        }

        // Update the Evaluation committee only (Top Committee rows are not touched).
        var existing = tender.CommitteeMembers.Where(m => m.CommitteeType == EvaluationCommitteeType).ToList();

        foreach (var member in existing.Where(m => selected.All(s => s.EmployeeProfileId != m.EmployeeProfileId)))
        {
            _context.Remove(member);
        }

        foreach (var s in selected)
        {
            var current = existing.FirstOrDefault(m => m.EmployeeProfileId == s.EmployeeProfileId);
            if (current != null)
            {
                current.MemberRole = s.MemberRole!;
            }
            else
            {
                tender.CommitteeMembers.Add(new TenderCommitteeMember
                {
                    EmployeeProfileId = s.EmployeeProfileId,
                    CommitteeType = EvaluationCommitteeType,
                    MemberRole = s.MemberRole!
                });
            }
        }

        var employee = await GetCurrentEmployeeAsync();
        AddAuditLog(employee?.Id, "AssignCommittee", tender.Id, "Evaluation committee assigned (4 members).");
        await _context.SaveChangesAsync();

        TempData["Success"] = "Evaluation committee assigned successfully.";
        return RedirectToAction(nameof(Index));
    }

    // ---------------------------------------------------------------- helpers

    // Active employees that have the EvaluationCommittee role.
    private async Task<List<CommitteeMemberOption>> GetEligibleMembersAsync()
    {
        var users = await _userManager.GetUsersInRoleAsync(EvaluationCommitteeRole);
        var userIds = users.Select(u => u.Id).ToList();

        return await _context.EmployeeProfiles
            .AsNoTracking()
            .Where(e => e.IsActive && userIds.Contains(e.UserId))
            .OrderBy(e => e.FullName)
            .Select(e => new CommitteeMemberOption
            {
                EmployeeProfileId = e.Id,
                FullName = e.FullName,
                JobTitle = e.JobTitle,
                Department = e.Department != null ? e.Department.Name : null
            })
            .ToListAsync();
    }

    private async Task<EmployeeProfile?> GetCurrentEmployeeAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return null;
        return await _context.EmployeeProfiles.FirstOrDefaultAsync(e => e.UserId == userId);
    }

    // Only approved requests that do not have a tender yet can be turned into a tender.
    private async Task<List<SelectListItem>> GetApprovedRequestsAsync()
    {
        var requests = await _context.PurchaseRequisitions
            .AsNoTracking()
            .Where(r => r.Status == "Approved" && r.Tender == null)
            .OrderBy(r => r.Title)
            .Select(r => new { r.Id, r.Title, Department = r.Department.Name })
            .ToListAsync();

        return requests
            .Select(r => new SelectListItem($"{r.Title} ({r.Department})", r.Id.ToString()))
            .ToList();
    }

    private void AddAuditLog(int? employeeId, string action, int tenderId, string details)
    {
        _context.AuditLogs.Add(new AuditLog
        {
            EmployeeProfileId = employeeId,
            Action = action,
            EntityName = "Tender",
            EntityId = tenderId,
            Details = details,
            CreatedAt = DateTime.UtcNow
        });
    }
}

// Form model used by the "Assign" page.
public class AssignCommitteeViewModel
{
    // The four seats of the evaluation committee (one member per role).
    public static readonly string[] Roles =
    {
        "Sourcing Manager",
        "Department Representative",
        "Financial Representative",
        "Technical Representative"
    };

    public int TenderId { get; set; }
    public string? TenderTitle { get; set; }
    public List<CommitteeMemberOption> Members { get; set; } = new();
}

public class CommitteeMemberOption
{
    public int EmployeeProfileId { get; set; }
    public string? FullName { get; set; }
    public string? JobTitle { get; set; }
    public string? Department { get; set; }
    public bool IsSelected { get; set; }
    public string? MemberRole { get; set; }
}

// Form model used by Create and Edit (keeps the validation rules out of the database entity).
public class TenderFormViewModel : IValidatableObject
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Select an approved purchase request.")]
    [Display(Name = "Purchase Request")]
    public int? PurchaseRequisitionId { get; set; }

    // Display only (Edit page).
    public string? PurchaseRequisitionTitle { get; set; }

    [Required(ErrorMessage = "Title is required.")]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Description is required.")]
    [StringLength(2000)]
    public string Description { get; set; } = string.Empty;

    [Required(ErrorMessage = "Terms are required.")]
    [StringLength(4000)]
    public string Terms { get; set; } = string.Empty;

    [Required(ErrorMessage = "Open date is required.")]
    [DataType(DataType.DateTime)]
    [Display(Name = "Open Date")]
    public DateTime? OpenDate { get; set; }

    [Required(ErrorMessage = "Close date is required.")]
    [DataType(DataType.DateTime)]
    [Display(Name = "Close Date")]
    public DateTime? CloseDate { get; set; }

    [ValidateNever]
    public List<SelectListItem> PurchaseRequests { get; set; } = new();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (OpenDate.HasValue && CloseDate.HasValue && CloseDate.Value <= OpenDate.Value)
        {
            yield return new ValidationResult(
                "Close date must be after the open date.",
                new[] { nameof(CloseDate) });
        }
    }
}
