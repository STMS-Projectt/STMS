using SourcingTenderSystem.ViewModels;
using System.Data;
using SourcingTenderSystem.Services;
using SourcingTenderSystem.Security;
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
    private const string DepartmentRepresentativeRole = "Department Representative";

    // The committee can be changed until the evaluation starts.
    private static readonly string[] CommitteeEditableStatuses = { "Closed" };

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
        ViewBag.OfferCount = await TenderRules.EligibleSuppliers(_context, tender.Id).CountAsync();
        ViewBag.Rounds = await _context.TenderRounds.AsNoTracking().Where(r => r.TenderId == tender.Id)
            .OrderBy(r => r.Number).ToListAsync();
        var submitted = TenderRules.PreviouslySubmitted(_context, tender.Id);
        ViewBag.SubmittedSuppliers = await _context.SupplierProfiles.Where(p => submitted.Contains(p.Id))
            .Select(p => p.CompanyName).OrderBy(n => n).ToListAsync();
        var currentInvitations = TenderRules.CurrentInvitations(_context, tender);
        ViewBag.InvitableSuppliers = await _context.SupplierProfiles.Where(p => p.Status == SupplierStatus.Approved &&
            !submitted.Contains(p.Id) && !currentInvitations.Any(i => i.SupplierProfileId == p.Id))
            .Select(p => p.CompanyName).OrderBy(n => n).ToListAsync();

        return View(tender);
    }

    // GET: /Tenders/Create
    [HttpGet]
    public async Task<IActionResult> Create(int? purchaseRequisitionId)
    {
        var model = new TenderFormViewModel
        {
            PurchaseRequests = await GetApprovedRequestsAsync()
        };
        if (purchaseRequisitionId.HasValue)
        {
            var option = model.PurchaseRequests.SingleOrDefault(r => r.Value == purchaseRequisitionId.Value.ToString());
            if (option == null)
            {
                ModelState.AddModelError(nameof(model.PurchaseRequisitionId), "This purchase request is not available for a new tender.");
            }
            else
            {
                model.PurchaseRequisitionId = purchaseRequisitionId;
            }
        }
        return View(model);
    }

    // POST: /Tenders/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(TenderFormViewModel model)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        PurchaseRequisition? requisition = null;
        if (model.PurchaseRequisitionId != null)
        {
            requisition = await _context.PurchaseRequisitions
                .Include(r => r.Tenders).ThenInclude(t => t.EvaluationReport)
                .Include(r => r.CreatedByEmployeeProfile).ThenInclude(e => e.Department)
                .FirstOrDefaultAsync(r => r.Id == model.PurchaseRequisitionId);

            if (requisition == null || !TenderRules.CanCreate(requisition))
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
            OpenDate = TenderRules.AsUtc(model.OpenDate!.Value),
            CloseDate = TenderRules.AsUtc(model.CloseDate!.Value),
            Status = DraftStatus,
            CreatedByEmployeeProfileId = employee!.Id,
            CreatedAt = DateTime.UtcNow
        };

        requisition.Status = "Converted to Tender";
        _context.Tenders.Add(tender);
        await _context.SaveChangesAsync();

        AddAuditLog(employee.Id, "Create", tender.Id, "Tender created from the approved request.");
        await _context.SaveChangesAsync();

        await transaction.CommitAsync();
        TempData["Success"] = "Tender draft created. Submit it when ready to publish.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Publish(int id)
    {
        var employee = await GetCurrentEmployeeAsync();
        if (employee is null) return Forbid();
        await using var tx = await TenderRules.LockAsync(_context, id);
        var tender = await _context.Tenders.FindAsync(id);
        if (tender is null) return NotFound();
        var now = DateTime.UtcNow;
        if (tender.Status != DraftStatus || tender.CloseDate <= now || tender.CloseDate <= tender.OpenDate)
        {
            TempData["Error"] = "Use a draft with a future closing deadline.";
            return RedirectToAction(nameof(Details), new { id });
        }
        if (!EvaluationRules.CriteriaValid(await _context.EvaluationCriteria.Where(c => c.TenderId == id).ToListAsync()))
        {
            TempData["Error"] = "Configure valid criteria totaling 100% before publishing.";
            return RedirectToAction(nameof(Criteria), new { id });
        }
        var round = await _context.TenderRounds.FindAsync(id, tender.Counter)
            ?? TenderRules.NewRound(_context, tender, employee.Id, now);
        round.OpensAt = tender.OpenDate;
        round.ClosesAt = tender.CloseDate;
        round.CreatedAt = now;
        round.OfficerId = employee.Id;
        tender.Status = tender.OpenDate <= now ? "Open" : "Published";
        TenderRules.Audit(_context, tender, employee.Id, "Publish", now, "Initial round published.");
        await _context.SaveChangesAsync();
        await tx.CommitAsync();
        TempData["Success"] = "Tender submitted for publication.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> Criteria(int id)
    {
        var tender = await _context.Tenders.AsNoTracking().Include(t => t.EvaluationCriteria).SingleOrDefaultAsync(t => t.Id == id);
        return tender == null ? NotFound() : View(tender);
    }

    [HttpPost]
    public async Task<IActionResult> Criterion(int id, CommitteeCriterionInput input, int? remove)
    {
        if (await GetCurrentEmployeeAsync() is not { } employee) return Forbid();
        await using var tx = await TenderRules.LockAsync(_context, id);
        var tender = await _context.Tenders.Include(t => t.EvaluationCriteria).SingleOrDefaultAsync(t => t.Id == id);
        if (tender is null) return NotFound();
        if (tender.Status != DraftStatus || await _context.Bids.AnyAsync(b => b.TenderId == id))
            return Conflict("Criteria are frozen once published.");
        if (remove is int key)
        {
            var criterion = tender.EvaluationCriteria.SingleOrDefault(c => c.Id == key);
            if (criterion is null) return NotFound();
            _context.Remove(criterion);
        }
        else if (ModelState.IsValid && !string.IsNullOrWhiteSpace(input.Name) &&
            input.WeightPercent == EvaluationRules.Round(input.WeightPercent) &&
            tender.EvaluationCriteria.Sum(c => c.WeightPercent) + input.WeightPercent <= 100 &&
            !tender.EvaluationCriteria.Any(c => string.Equals(c.Name, input.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
            tender.EvaluationCriteria.Add(new EvaluationCriterion { Name = input.Name.Trim(), Type = input.Type,
                MaxScore = input.MaxScore, WeightPercent = input.WeightPercent });
        else
        {
            TempData["Error"] = "Enter a unique valid criterion. Total weight cannot exceed 100%.";
            return RedirectToAction(nameof(Criteria), new { id });
        }
        EvaluationRules.Audit(_context, id, employee.Id, "DraftCriteriaChanged", remove is null ? input.Name : $"Removed criterion {remove}", DateTime.UtcNow);
        await _context.SaveChangesAsync();
        await tx.CommitAsync();
        return RedirectToAction(nameof(Criteria), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> Reopen(int id)
    {
        var tender = await _context.Tenders.FindAsync(id);
        if (tender is null) return NotFound();
        return View(tender);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Reopen(int id, DateTime closeDate, string? reason)
    {
        var employee = await GetCurrentEmployeeAsync();
        if (employee is null) return Forbid();
        await using var tx = await TenderRules.LockAsync(_context, id);
        var tender = await _context.Tenders.FindAsync(id);
        if (tender is null) return NotFound();
        var now = DateTime.UtcNow;
        closeDate = TenderRules.AsUtc(closeDate);
        reason = reason?.Trim();
        var count = await TenderRules.EligibleSuppliers(_context, id).CountAsync();
        if (!ModelState.IsValid || closeDate <= now || reason is null || reason.Length is < 1 or > 500)
            TempData["Error"] = "Enter a future closing deadline (UTC) and a reopening reason (1–500 characters).";
        else if (!TenderRules.CanReopen(tender, count))
            TempData["Error"] = "Reopening requires a closed round below Round 3 with fewer than three eligible suppliers.";
        else
        {
            tender.Counter++;
            tender.Status = "Open";
            tender.OpenDate = now;
            tender.CloseDate = closeDate;
            TenderRules.NewRound(_context, tender, employee.Id, now, reason);
            TenderRules.Audit(_context, tender, employee.Id, "Reopen", now, reason);
            await _context.SaveChangesAsync();
            await tx.CommitAsync();
            TempData["Success"] = $"Round {tender.Counter} opened. Invite approved suppliers who have never submitted a bid.";
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Close(int id)
    {
        var employee = await GetCurrentEmployeeAsync();
        if (employee is null) return Forbid();
        await using var tx = await TenderRules.LockAsync(_context, id);
        var tender = await _context.Tenders.FindAsync(id);
        if (tender is null) return NotFound();
        var now = DateTime.UtcNow;
        if (tender.Status is ("Open" or "Published") && tender.CloseDate <= now)
        {
            await TenderRules.CloseRoundAsync(_context, tender, now, employee.Id, false);
            await _context.SaveChangesAsync();
            await tx.CommitAsync();
            TempData["Success"] = "Tender closed and available for compliance review.";
        }
        else TempData["Error"] = "An active tender can be closed here after its bidding deadline.";
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

        await using var tx = await TenderRules.LockAsync(_context, id);
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
        tender.OpenDate = TenderRules.AsUtc(model.OpenDate!.Value);
        tender.CloseDate = TenderRules.AsUtc(model.CloseDate!.Value);
        var planned = await _context.TenderRounds.FindAsync(tender.Id, tender.Counter);
        if (planned != null)
        {
            planned.OpensAt = tender.OpenDate;
            planned.ClosesAt = tender.CloseDate;
        }

        var employee = await GetCurrentEmployeeAsync();
        AddAuditLog(employee?.Id, "Update", tender.Id, "Tender details updated.");
        await _context.SaveChangesAsync();

        await tx.CommitAsync();
        TempData["Success"] = "Tender updated successfully.";
        return RedirectToAction(nameof(Index));
    }

    // POST: /Tenders/Delete/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        await using var tx = await TenderRules.LockAsync(_context, id);
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
        tender.PurchaseRequisition.Status = await _context.Tenders.AnyAsync(t => t.PurchaseRequisitionId == tender.PurchaseRequisitionId && t.Id != tender.Id)
            ? "Converted to Tender" : "Approved";
        var draftRound = await _context.TenderRounds.FindAsync(tender.Id, tender.Counter);
        if (draftRound != null) _context.TenderRounds.Remove(draftRound);
        _context.Tenders.Remove(tender);

        var employee = await GetCurrentEmployeeAsync();
        AddAuditLog(employee?.Id, "Delete", tender.Id, "Draft tender deleted; purchase request history retained.");
        await _context.SaveChangesAsync();

        await tx.CommitAsync();
        TempData["Success"] = "Tender deleted.";
        return RedirectToAction(nameof(Index));
    }

    // GET: /Tenders/AssignCommittee/5
    public async Task<IActionResult> AssignCommittee(int? id)
    {
        if (id == null) return NotFound();

        var tender = await TendersWithCommitteeData()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id);

        if (tender == null) return NotFound();

        if (!CommitteeEditableStatuses.Contains(tender.Status))
        {
            TempData["Error"] = "The committee is assigned after the tender closes.";
            return RedirectToAction(nameof(Index));
        }

        var (slots, problems) = await BuildCommitteeAsync(tender.PurchaseRequisition.CreatedByEmployeeProfile);
        if (!TenderRules.CanEvaluate(tender, await TenderRules.EligibleSuppliers(_context, tender.Id).CountAsync()))
            problems.Add($"Round {tender.Counter} requires {TenderRules.Minimum(tender)} eligible supplier bids.");
        else if (await EvaluationRules.Unresolved(_context, tender.Id).AnyAsync())
            problems.Add("Complete the compliance checks before assigning the committee.");

        return View(new AssignCommitteeViewModel
        {
            TenderId = tender.Id,
            TenderTitle = tender.Title,
            DepartmentName = tender.PurchaseRequisition.Department.Name,
            AlreadyAssigned = tender.CommitteeMembers.Count(m => m.CommitteeType == EvaluationCommitteeType) == 4,
            IsOpen = tender.Status == "Open",
            Members = slots,
            Problems = problems
        });
    }

    // POST: /Tenders/AssignCommittee/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignCommittee(int id)
    {
        var employee = await GetCurrentEmployeeAsync();
        if (employee == null) return Forbid();

        await using var transaction = await TenderRules.LockAsync(_context, id);
        var tender = await TendersWithCommitteeData().FirstOrDefaultAsync(t => t.Id == id);

        if (tender == null) return NotFound();

        if (!CommitteeEditableStatuses.Contains(tender.Status))
        {
            TempData["Error"] = "The committee is assigned after the tender closes.";
            return RedirectToAction(nameof(Index));
        }

        var (slots, problems) = await BuildCommitteeAsync(tender.PurchaseRequisition.CreatedByEmployeeProfile);
        if (problems.Any())
        {
            // The page shows the problems and keeps the button disabled.
            return RedirectToAction(nameof(AssignCommittee), new { id });
        }

        if (!await _context.EvaluationCriteria.AnyAsync(c => c.TenderId == id) ||
            !EvaluationRules.CriteriaValid(await _context.EvaluationCriteria.Where(c => c.TenderId == id).ToListAsync()) ||
            !TenderRules.CanEvaluate(tender, await TenderRules.EligibleSuppliers(_context, id).CountAsync()) ||
            await EvaluationRules.Unresolved(_context, id).AnyAsync() ||
            !await _context.Bids.AnyAsync(b => b.TenderId == id && b.Status == "Compliant"))
        {
            TempData["Error"] = "Meet the round's minimum offer count and complete compliance before assigning the committee.";
            return RedirectToAction(nameof(AssignCommittee), new { id });
        }
        // Update the Evaluation committee only (Top Committee rows are not touched).
        var existing = tender.CommitteeMembers.Where(m => m.CommitteeType == EvaluationCommitteeType).ToList();

        foreach (var member in existing.Where(m => slots.All(s => s.EmployeeProfileId != m.EmployeeProfileId)))
        {
            _context.Remove(member);
        }

        foreach (var slot in slots)
        {
            var matching = existing.Where(m => m.EmployeeProfileId == slot.EmployeeProfileId).OrderBy(m => m.Id).ToList();
            var current = matching.FirstOrDefault();
            _context.RemoveRange(matching.Skip(1));
            if (current != null)
            {
                current.MemberRole = slot.Role;
            }
            else
            {
                tender.CommitteeMembers.Add(new TenderCommitteeMember
                {
                    EmployeeProfileId = slot.EmployeeProfileId,
                    CommitteeType = EvaluationCommitteeType,
                    MemberRole = slot.Role
                });
            }
        }

        tender.Status = "Under Evaluation";
        AddAuditLog(employee.Id, "AssignCommittee", tender.Id, "Four evaluation committee members assigned.");
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        TempData["Success"] = "Committee assigned. Tender is under evaluation.";
        return RedirectToAction(nameof(Index));
    }

    // ---------------------------------------------------------------- helpers

    private IQueryable<Tender> TendersWithCommitteeData()
    {
        return _context.Tenders
            .Include(t => t.CommitteeMembers)
            .Include(t => t.PurchaseRequisition).ThenInclude(r => r.Department)
            .Include(t => t.PurchaseRequisition).ThenInclude(r => r.CreatedByEmployeeProfile).ThenInclude(e => e.Department);
    }

    // 3 fixed members + the employee who requested the purchase = the 4 committee seats.
    private async Task<(List<CommitteeSlot> Slots, List<string> Problems)> BuildCommitteeAsync(EmployeeProfile requester)
    {
        var slots = new List<CommitteeSlot>();
        var problems = new List<string>();

        foreach (var (role, seat) in SourcingRoster.Seats)
        {
            var users = await _userManager.GetUsersInRoleAsync(role);
            var ids = users.Select(u => u.Id).ToList();
            var employees = await _context.EmployeeProfiles.AsNoTracking().Include(e => e.Department)
                .Where(e => e.IsActive && ids.Contains(e.UserId)).Take(2).ToListAsync();
            if (employees.Count != 1)
                problems.Add($"Configure exactly one active employee with the {role} role.");
            else
                slots.Add(ToSlot(seat, employees[0], isFixed: true));
        }
        if (!requester.IsActive)
        {
            problems.Add($"The employee who created the purchase request ({requester.FullName}) is not active.");
        }
        else
        {
            slots.Add(ToSlot(DepartmentRepresentativeRole, requester, isFixed: false));
        }

        if (slots.Select(s => s.EmployeeProfileId).Distinct().Count() != slots.Count)
        {
            problems.Add("The same person cannot hold two seats. The requester is already one of the fixed committee members.");
        }

        return (slots, problems);
    }

    private static CommitteeSlot ToSlot(string role, EmployeeProfile employee, bool isFixed)
    {
        return new CommitteeSlot
        {
            EmployeeProfileId = employee.Id,
            Role = role,
            FullName = employee.FullName,
            JobTitle = employee.JobTitle,
            Department = employee.Department?.Name,
            IsFixed = isFixed
        };
    }

    private async Task<EmployeeProfile?> GetCurrentEmployeeAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return null;
        return await _context.EmployeeProfiles.FirstOrDefaultAsync(e => e.UserId == userId && e.IsActive);
    }

    // Only approved requests that do not have a tender yet can be turned into a tender.
    private async Task<List<SelectListItem>> GetApprovedRequestsAsync()
    {
        var requests = await _context.PurchaseRequisitions
            .AsNoTracking()
            .Where(r => !r.Tenders.Any(t => t.Status != "Cancelled") &&
                (r.Status == "Approved" || r.Status == "Converted to Tender" &&
                    r.Tenders.OrderByDescending(t => t.Id).Select(t => t.EvaluationReport!.Status).FirstOrDefault() == TenderRules.ReadyToRetender))
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

// Model for the "Assign" page (the committee is built automatically, nothing is posted back).
public class AssignCommitteeViewModel
{
    public int TenderId { get; set; }
    public string TenderTitle { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public bool AlreadyAssigned { get; set; }
    public bool IsOpen { get; set; }
    public List<CommitteeSlot> Members { get; set; } = new();
    public List<string> Problems { get; set; } = new();
}

public class CommitteeSlot
{
    public int EmployeeProfileId { get; set; }
    public string Role { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? JobTitle { get; set; }
    public string? Department { get; set; }
    public bool IsFixed { get; set; }
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
    [Display(Name = "Open date (UTC)")]
    public DateTime? OpenDate { get; set; }

    [Required(ErrorMessage = "Close date is required.")]
    [DataType(DataType.DateTime)]
    [Display(Name = "Close date (UTC)")]
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
