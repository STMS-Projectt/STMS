using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderSystem.Data;
using SourcingTenderSystem.Models;

namespace SourcingTenderSystem.Controllers;

// دوال مشتركة حتى لا نكرر التحقق وجلب تفاصيل الطلب في كل كنترولر.
public abstract class RequisitionBaseController : Controller
{
    protected readonly ApplicationDbContext Db;

    protected RequisitionBaseController(ApplicationDbContext db) { Db = db; }

    protected Task<EmployeeProfile?> CurrentEmployeeAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Db.EmployeeProfiles.Include(x => x.Department)
            .SingleOrDefaultAsync(x => x.UserId == userId && x.IsActive);
    }

    protected static bool HasDepartment(EmployeeProfile? employee)
    {
        if (employee == null || employee.DepartmentId == null) return false;
        return employee.Department != null && employee.Department.IsActive;
    }

    protected static bool CanEdit(PurchaseRequisition request)
    {
        return request.Status == "Draft" || request.Status == "Returned";
    }

    protected static bool Owns(PurchaseRequisition request, EmployeeProfile employee)
    {
        return request.CreatedByEmployeeProfileId == employee.Id && request.DepartmentId == employee.DepartmentId;
    }

    // تجهيز بيانات صفحة التفاصيل: الطلب وعناصره والقسم وصاحب الطلب والمراجع.
    protected IQueryable<PurchaseRequisition> RequestDetails()
    {
        return Db.PurchaseRequisitions.Include(x => x.Items).Include(x => x.Department)
            .Include(x => x.CreatedByEmployeeProfile).Include(x => x.ReviewedByEmployeeProfile);
    }

    protected void Log(PurchaseRequisition request, EmployeeProfile employee, string action, string? details = null)
    {
        Db.AuditLogs.Add(new AuditLog
        {
            EmployeeProfileId = employee.Id, EntityName = nameof(PurchaseRequisition),
            EntityId = request.Id, Action = action, Details = details, CreatedAt = DateTime.UtcNow
        });
    }
}
