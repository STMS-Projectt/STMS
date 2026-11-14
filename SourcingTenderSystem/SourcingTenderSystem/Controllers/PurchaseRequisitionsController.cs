using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderSystem.Data;
using SourcingTenderSystem.Models;
using SourcingTenderSystem.ViewModels;

namespace SourcingTenderSystem.Controllers;

[Authorize(Roles = "RequestingDepartment")]
public class PurchaseRequisitionsController : RequisitionBaseController
{
    public PurchaseRequisitionsController(ApplicationDbContext db) : base(db) { }

    private static readonly string[] Statuses =
        ["Draft", "Submitted", "Under Review", "Returned", "Approved", "Rejected", "Converted to Tender"];

    [HttpGet]
    public IActionResult Index() => RedirectToAction(nameof(MyRequests));

    [HttpGet]
    public async Task<IActionResult> Dashboard()
    {
        var employee = await CurrentEmployeeAsync();
        if (!HasDepartment(employee)) return Forbid();
        var requests = DepartmentRequests(employee!);
        var counts = await requests.GroupBy(x => x.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count);
        var recent = await RequestRows(requests).Take(5).ToListAsync();
        return View(new DepartmentDashboard(employee!.Department!.Name, counts, recent));
    }

    [HttpGet]
    public async Task<IActionResult> MyRequests(string? status, string? q)
    {
        var employee = await CurrentEmployeeAsync();
        if (!HasDepartment(employee)) return Forbid();
        var requests = DepartmentRequests(employee!);
        status = Statuses.Contains(status) ? status : null;
        q = q?.Trim();
        if (status != null) requests = requests.Where(x => x.Status == status);
        if (!string.IsNullOrEmpty(q)) requests = requests.Where(x => x.Title.Contains(q));
        return View("Index", new DepartmentRequestList(employee!.Department!.Name, Statuses,
            status, q, await RequestRows(requests).ToListAsync()));
    }

    [HttpGet]
    public async Task<IActionResult> Committee()
    {
        var employee = await CurrentEmployeeAsync();
        if (!HasDepartment(employee)) return Forbid();
        var assignments = await Db.Tenders.AsNoTracking()
            .Where(t => t.PurchaseRequisition.DepartmentId == employee!.DepartmentId &&
                t.CommitteeMembers.Any(m => m.EmployeeProfileId == employee.Id &&
                    m.CommitteeType == "Evaluation" && m.MemberRole == "Department Representative"))
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new DepartmentAssignment(t.Id, t.Title, t.Status, t.PurchaseRequisitionId))
            .ToListAsync();
        return View(assignments);
    }

    private IQueryable<PurchaseRequisition> DepartmentRequests(EmployeeProfile employee) =>
        Db.PurchaseRequisitions.AsNoTracking().Where(x => x.DepartmentId == employee.DepartmentId);

    private static IOrderedQueryable<PurchaseRequisition> RequestRows(IQueryable<PurchaseRequisition> requests) =>
        requests.Include(x => x.CreatedByEmployeeProfile).OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id);
    [HttpGet]
    public async Task<IActionResult> Create()
    {
        if (!HasDepartment(await CurrentEmployeeAsync())) return Forbid();
        return View(new RequisitionInput());
    }

    [HttpPost]
    public async Task<IActionResult> Create(RequisitionInput input)
    {
        var employee = await CurrentEmployeeAsync();
        if (!HasDepartment(employee)) return Forbid();
        ValidateDate(input);
        if (!ModelState.IsValid) return View(input);

        // نحفظ الطلب وسجل إنشائه معًا؛ إذا فشل أحدهما لا نعتمد أيًا منهما.
        await using var transaction = await Db.Database.BeginTransactionAsync();
        var request = new PurchaseRequisition
        {
            Title = input.Title.Trim(), Reason = input.Reason.Trim(), Priority = input.Priority,
            TargetDate = input.TargetDate!.Value.Date, Status = "Draft",
            DepartmentId = employee!.DepartmentId!.Value, CreatedByEmployeeProfileId = employee.Id,
            CreatedAt = DateTime.UtcNow
        };
        Db.PurchaseRequisitions.Add(request);
        await Db.SaveChangesAsync();
        Log(request, employee, "Created");
        await Db.SaveChangesAsync();
        await transaction.CommitAsync();
        TempData["Message"] = "Draft created. Add items, then submit it for review.";
        return RedirectToAction(nameof(Details), new { id = request.Id });
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var employee = await CurrentEmployeeAsync();
        if (!HasDepartment(employee)) return Forbid();
        var request = await RequestDetails().Include(x => x.Tenders).AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.DepartmentId == employee!.DepartmentId);
        if (request == null) return NotFound();
        ViewBag.CanEdit = Owns(request, employee!) && CanEdit(request);
        return View(request);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var employee = await CurrentEmployeeAsync();
        if (!HasDepartment(employee)) return Forbid();
        var request = await Db.PurchaseRequisitions.FindAsync(id);
        if (request == null || !Owns(request, employee!)) return NotFound();
        if (!CanEdit(request)) return Conflict("Only draft or returned requests can be edited.");
        ViewBag.RequestId = id;
        return View(new RequisitionInput
        {
            Title = request.Title, Reason = request.Reason, Priority = request.Priority, TargetDate = request.TargetDate
        });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, RequisitionInput input)
    {
        var employee = await CurrentEmployeeAsync();
        if (!HasDepartment(employee)) return Forbid();
        // نحمي فحص الحالة والحفظ من التعارض مع إرسال الطلب في نفس الوقت.
        await using var transaction = await Db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var request = await Db.PurchaseRequisitions.FindAsync(id);
        if (request == null || !Owns(request, employee!)) return NotFound();
        if (!CanEdit(request)) return Conflict("Only draft or returned requests can be edited.");
        ViewBag.RequestId = id;
        ValidateDate(input);
        if (!ModelState.IsValid) return View(input);
        request.Title = input.Title.Trim();
        request.Reason = input.Reason.Trim();
        request.Priority = input.Priority;
        request.TargetDate = input.TargetDate!.Value.Date;
        Log(request, employee!, "Edited");
        await Db.SaveChangesAsync();
        await transaction.CommitAsync();
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Submit(int id)
    {
        var employee = await CurrentEmployeeAsync();
        if (!HasDepartment(employee)) return Forbid();
        await using var transaction = await Db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var request = await Db.PurchaseRequisitions.Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == id);
        if (request == null || !Owns(request, employee!)) return NotFound();
        if (!CanEdit(request)) return Conflict("This request has already been submitted or decided.");
        if (!ReadyToSubmit(request))
        {
            TempData["RequestError"] = "Add at least one valid item and use a target date of today or later before submitting.";
            return RedirectToAction(nameof(Details), new { id });
        }
        request.Status = "Under Review";
        // نبقي الملاحظات السابقة ظاهرة عند إعادة إرسال الطلب.
        Log(request, employee!, "Submitted");
        await Db.SaveChangesAsync();
        await transaction.CommitAsync();
        TempData["Message"] = "Request submitted for review.";
        return RedirectToAction(nameof(Details), new { id });
    }

    // نفحص كل عنصر بخطوات واضحة قبل السماح بإرسال الطلب.
    private static bool ReadyToSubmit(PurchaseRequisition request)
    {
        if (request.Items.Count == 0) return false;
        if (request.TargetDate.Date < DateTime.Today) return false;
        foreach (var item in request.Items)
        {
            if (item.Quantity <= 0) return false;
            if (string.IsNullOrWhiteSpace(item.ItemName)) return false;
            if (string.IsNullOrWhiteSpace(item.Unit)) return false;
            if (string.IsNullOrWhiteSpace(item.Specifications)) return false;
        }
        return true;
    }

    private void ValidateDate(RequisitionInput input)
    {
        if (input.TargetDate.HasValue && input.TargetDate.Value.Date < DateTime.Today)
            ModelState.AddModelError(nameof(input.TargetDate), "Target date must be today or later.");
    }
}
