using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderSystem.Data;
using SourcingTenderSystem.Models;
using SourcingTenderSystem.ViewModels;

namespace SourcingTenderSystem.Controllers;

[Authorize(Roles = "RequestingDepartment")]
public class RequisitionItemsController : RequisitionBaseController
{
    public RequisitionItemsController(ApplicationDbContext db) : base(db) { }

    [HttpGet]
    public async Task<IActionResult> Create(int requestId)
    {
        var request = await EditableRequestAsync(requestId);
        if (request == null) return NotFound();
        ViewBag.RequestId = requestId;
        return View(new RequisitionItemInput());
    }

    [HttpPost]
    public async Task<IActionResult> Create(int requestId, RequisitionItemInput input)
    {
        await using var transaction = await Db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var request = await EditableRequestAsync(requestId);
        if (request == null) return NotFound();
        ViewBag.RequestId = requestId;
        if (!ModelState.IsValid) return View(input);
        var item = new PurchaseRequisitionItem { PurchaseRequisitionId = requestId };
        Apply(item, input);
        Db.PurchaseRequisitionItems.Add(item);
        Log(request, (await CurrentEmployeeAsync())!, "Item added", input.ItemName.Trim());
        await Db.SaveChangesAsync();
        await transaction.CommitAsync();
        return RedirectToAction("Details", "PurchaseRequisitions", new { id = requestId });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var item = await Db.PurchaseRequisitionItems.FindAsync(id);
        if (item == null || await EditableRequestAsync(item.PurchaseRequisitionId) == null) return NotFound();
        ViewBag.RequestId = item.PurchaseRequisitionId;
        ViewBag.ItemId = id;
        return View(new RequisitionItemInput
        {
            ItemName = item.ItemName, Quantity = item.Quantity, Unit = item.Unit, Specifications = item.Specifications
        });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, RequisitionItemInput input)
    {
        await using var transaction = await Db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var item = await Db.PurchaseRequisitionItems.FindAsync(id);
        if (item == null) return NotFound();
        var request = await EditableRequestAsync(item.PurchaseRequisitionId);
        if (request == null) return NotFound();
        ViewBag.RequestId = item.PurchaseRequisitionId;
        ViewBag.ItemId = id;
        if (!ModelState.IsValid) return View(input);
        Apply(item, input);
        Log(request, (await CurrentEmployeeAsync())!, "Item edited", item.ItemName);
        await Db.SaveChangesAsync();
        await transaction.CommitAsync();
        return RedirectToAction("Details", "PurchaseRequisitions", new { id = item.PurchaseRequisitionId });
    }

    [HttpGet]
    public async Task<IActionResult> Delete(int id)
    {
        var item = await Db.PurchaseRequisitionItems.FindAsync(id);
        if (item == null || await EditableRequestAsync(item.PurchaseRequisitionId) == null) return NotFound();
        return View(item);
    }

    [HttpPost, ActionName("Delete")]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        await using var transaction = await Db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var item = await Db.PurchaseRequisitionItems.FindAsync(id);
        if (item == null) return NotFound();
        var request = await EditableRequestAsync(item.PurchaseRequisitionId);
        if (request == null) return NotFound();
        if (await Db.BidItems.AnyAsync(x => x.PurchaseRequisitionItemId == id))
            return Conflict("This item is already linked to a supplier bid.");
        Db.PurchaseRequisitionItems.Remove(item);
        Log(request, (await CurrentEmployeeAsync())!, "Item deleted", item.ItemName);
        await Db.SaveChangesAsync();
        await transaction.CommitAsync();
        return RedirectToAction("Details", "PurchaseRequisitions", new { id = item.PurchaseRequisitionId });
    }

    // لا نسمح بتغيير العناصر إلا لصاحب الطلب وهو مسودة أو معاد للتعديل.
    private async Task<PurchaseRequisition?> EditableRequestAsync(int id)
    {
        var employee = await CurrentEmployeeAsync();
        if (!HasDepartment(employee)) return null;
        var request = await Db.PurchaseRequisitions.FindAsync(id);
        if (request == null) return null;
        if (!Owns(request, employee!)) return null;
        if (!CanEdit(request)) return null;
        return request;
    }

    // نفس الحقول تستخدم عند الإضافة والتعديل.
    private static void Apply(PurchaseRequisitionItem item, RequisitionItemInput input)
    {
        item.ItemName = input.ItemName.Trim();
        item.Quantity = input.Quantity;
        item.Unit = input.Unit.Trim();
        item.Specifications = input.Specifications.Trim();
    }
}
