using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderSystem.Data;
using SourcingTenderSystem.Models;
using SourcingTenderSystem.ViewModels;

namespace SourcingTenderSystem.Controllers;

[Authorize(Roles = "SourcingOfficer")]
public class RequisitionReviewsController : RequisitionBaseController
{
    public RequisitionReviewsController(ApplicationDbContext db) : base(db) { }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        if (await CurrentEmployeeAsync() == null) return Forbid();
        var requests = await Db.PurchaseRequisitions.AsNoTracking().Include(x => x.Department)
            .Include(x => x.CreatedByEmployeeProfile)
            .OrderBy(x => x.CreatedAt).ToListAsync();
        return View(requests);
    }

    [HttpGet]
    public async Task<IActionResult> Pending()
    {
        if (await CurrentEmployeeAsync() == null) return Forbid();
        return View(await Db.PurchaseRequisitions.AsNoTracking().Include(x => x.Department)
            .Include(x => x.CreatedByEmployeeProfile)
            .Where(x => x.Status == "Submitted" || x.Status == "Under Review")
            .OrderBy(x => x.CreatedAt).ToListAsync());
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        if (await CurrentEmployeeAsync() == null) return Forbid();
        var request = await ReviewableRequests().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (request == null) return NotFound();
        ViewBag.Request = request;
        return View(new RequisitionReviewInput());
    }

    [HttpPost]
    public Task<IActionResult> Approve(int id, RequisitionReviewInput input)
    {
        return DecideAsync(id, input, "Approved");
    }

    [HttpPost]
    public Task<IActionResult> Return(int id, RequisitionReviewInput input)
    {
        return DecideAsync(id, input, "Returned");
    }

    [HttpPost]
    public Task<IActionResult> Reject(int id, RequisitionReviewInput input)
    {
        return DecideAsync(id, input, "Rejected");
    }

    // القرارات الثلاثة لها نفس التحقق والحفظ؛ الاختلاف فقط في الحالة الجديدة.
    private async Task<IActionResult> DecideAsync(int id, RequisitionReviewInput input, string status)
    {
        var employee = await CurrentEmployeeAsync();
        if (employee == null) return Forbid();
        await using var transaction = await Db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var request = await ReviewableRequests().SingleOrDefaultAsync(x => x.Id == id);
        if (request == null) return NotFound();
        if (request.CreatedByEmployeeProfileId == employee.Id) return Forbid();
        if (request.Status != "Submitted" && request.Status != "Under Review")
            return Conflict("This request has already been decided. Refresh the pending list.");
        if (status == "Approved" && !request.Items.Any())
            ModelState.AddModelError("", "A request must contain items before approval.");
        if (!ModelState.IsValid)
        {
            ViewBag.Request = request;
            return View("Details", input);
        }
        request.Status = status;
        request.ReviewNotes = input.Notes.Trim();
        request.ReviewedByEmployeeProfileId = employee.Id;
        Log(request, employee, status, request.ReviewNotes);
        Db.Notifications.Add(new Notification
        {
            EmployeeProfileId = request.CreatedByEmployeeProfileId,
            Title = "Purchase request " + status,
            Message = request.Title + ": " + request.ReviewNotes,
            Link = "/PurchaseRequisitions/Details/" + request.Id, CreatedAt = DateTime.UtcNow
        });
        await Db.SaveChangesAsync();
        await transaction.CommitAsync();
        TempData["Message"] = "Request " + status.ToLowerInvariant() + ".";
        return RedirectToAction(nameof(Pending));
    }

    private IQueryable<PurchaseRequisition> ReviewableRequests()
    {
        return RequestDetails().Where(x => x.Status == "Submitted" || x.Status == "Under Review" ||
            x.Status == "Approved" || x.Status == "Returned" || x.Status == "Rejected" ||
            x.Status == "Converted to Tender");
    }
}
