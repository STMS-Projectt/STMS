using SourcingTenderSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderSystem.Data;
using SourcingTenderSystem.Models;
using SourcingTenderSystem.Security;
using SourcingTenderSystem.ViewModels;

namespace SourcingTenderSystem.Controllers;

// Supplier adds, edits and deletes the price of each requested item in a DRAFT bid.
// After every change the bid's TotalPrice is recalculated and the supplier goes back to Bids/Details.
[Authorize(Roles = Roles.Supplier)]
public class BidItemsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<IdentityUser> _userManager;

    private const string CannotChangeMessage = "You can only change prices of a draft bid while the tender is still open.";

    public BidItemsController(ApplicationDbContext context, UserManager<IdentityUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    // GET: /BidItems/Create?bidId=4&purchaseRequisitionItemId=3
    [HttpGet]
    public async Task<IActionResult> Create(int bidId, int purchaseRequisitionItemId)
    {
        var supplier = await GetCurrentSupplierAsync();
        if (supplier == null || supplier.Status != "Approved")
        {
            return NotApprovedView(supplier);
        }

        // Only my bid
        var bid = await GetMyBidAsync(bidId, supplier.Id);
        if (bid == null)
        {
            return NotFound();
        }

        if (!CanChangeBid(bid))
        {
            TempData["Error"] = CannotChangeMessage;
            return BackToBid(bid.Id);
        }

        // The item must be one of the items requested in THIS tender
        var requestedItem = await GetRequestedItemAsync(purchaseRequisitionItemId, bid.Tender.PurchaseRequisitionId);
        if (requestedItem == null)
        {
            return NotFound();
        }

        // Only one price per requested item
        if (await HasPriceAsync(bid.Id, requestedItem.Id))
        {
            TempData["Error"] = "This item already has a price. Use Edit to change it.";
            return BackToBid(bid.Id);
        }

        ViewBag.Bid = bid;
        ViewBag.RequestedItem = requestedItem;
        return View(new BidItemFormViewModel());
    }

    // POST: /BidItems/Create?bidId=4&purchaseRequisitionItemId=3
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(int bidId, int purchaseRequisitionItemId, BidItemFormViewModel model)
    {
        var supplier = await GetCurrentSupplierAsync();
        if (supplier == null || supplier.Status != "Approved")
        {
            return NotApprovedView(supplier);
        }

        // Check everything again on POST (never trust that the GET page was used)
        var tenderId = await _context.Bids.Where(b => b.Id == bidId && b.SupplierProfileId == supplier.Id).Select(b => (int?)b.TenderId).SingleOrDefaultAsync();
        if (tenderId is null) return NotFound();
        await using var tx = await TenderRules.LockAsync(_context, tenderId.Value);
        var bid = await GetMyBidAsync(bidId, supplier.Id);
        if (bid == null)
        {
            return NotFound();
        }

        if (!CanChangeBid(bid))
        {
            TempData["Error"] = CannotChangeMessage;
            return BackToBid(bid.Id);
        }

        var requestedItem = await GetRequestedItemAsync(purchaseRequisitionItemId, bid.Tender.PurchaseRequisitionId);
        if (requestedItem == null)
        {
            return NotFound();
        }

        if (await HasPriceAsync(bid.Id, requestedItem.Id))
        {
            TempData["Error"] = "This item already has a price. Use Edit to change it.";
            return BackToBid(bid.Id);
        }

        // The database column is decimal(18,2), so we allow max 2 decimal places
        if (model.UnitPrice != Math.Round(model.UnitPrice, 2))
        {
            ModelState.AddModelError(nameof(model.UnitPrice), "Unit price can have at most 2 decimal places.");
        }

        if (!ModelState.IsValid)
        {
            ViewBag.Bid = bid;
            ViewBag.RequestedItem = requestedItem;
            return View(model);
        }

        // BidId, item and Quantity come from the server. Quantity = the requested quantity.
        var bidItem = new BidItem
        {
            BidId = bid.Id,
            PurchaseRequisitionItemId = requestedItem.Id,
            Quantity = requestedItem.Quantity,
            UnitPrice = model.UnitPrice,
            Notes = model.Notes
        };

        _context.BidItems.Add(bidItem);
        await _context.SaveChangesAsync();

        await RecalculateTotalAsync(bid);

        await tx.CommitAsync();
        TempData["Msg"] = $"Price added for {requestedItem.ItemName}.";
        return BackToBid(bid.Id);
    }

    // GET: /BidItems/Edit/12
    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var supplier = await GetCurrentSupplierAsync();
        if (supplier == null || supplier.Status != "Approved")
        {
            return NotApprovedView(supplier);
        }

        var bidItem = await GetMyBidItemAsync(id, supplier.Id);
        if (bidItem == null)
        {
            return NotFound();
        }

        if (!CanChangeBid(bidItem.Bid))
        {
            TempData["Error"] = CannotChangeMessage;
            return BackToBid(bidItem.BidId);
        }

        // Copy the current values into the form
        var model = new BidItemFormViewModel
        {
            UnitPrice = bidItem.UnitPrice,
            Notes = bidItem.Notes
        };

        ViewBag.BidItem = bidItem;
        return View(model);
    }

    // POST: /BidItems/Edit/12
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, BidItemFormViewModel model)
    {
        var supplier = await GetCurrentSupplierAsync();
        if (supplier == null || supplier.Status != "Approved")
        {
            return NotApprovedView(supplier);
        }

        var tenderId = await _context.BidItems.Where(x => x.Id == id && x.Bid.SupplierProfileId == supplier.Id).Select(x => (int?)x.Bid.TenderId).SingleOrDefaultAsync();
        if (tenderId is null) return NotFound();
        await using var tx = await TenderRules.LockAsync(_context, tenderId.Value);
        var bidItem = await GetMyBidItemAsync(id, supplier.Id);
        if (bidItem == null)
        {
            return NotFound();
        }

        if (!CanChangeBid(bidItem.Bid))
        {
            TempData["Error"] = CannotChangeMessage;
            return BackToBid(bidItem.BidId);
        }

        if (model.UnitPrice != Math.Round(model.UnitPrice, 2))
        {
            ModelState.AddModelError(nameof(model.UnitPrice), "Unit price can have at most 2 decimal places.");
        }

        if (!ModelState.IsValid)
        {
            ViewBag.BidItem = bidItem;
            return View(model);
        }

        // Only price and notes change. Quantity is always the requested quantity.
        bidItem.UnitPrice = model.UnitPrice;
        bidItem.Notes = model.Notes;
        bidItem.Quantity = bidItem.PurchaseRequisitionItem.Quantity;
        await _context.SaveChangesAsync();

        await RecalculateTotalAsync(bidItem.Bid);

        await tx.CommitAsync();
        TempData["Msg"] = $"Price updated for {bidItem.PurchaseRequisitionItem.ItemName}.";
        return BackToBid(bidItem.BidId);
    }

    // POST: /BidItems/Delete/12
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var supplier = await GetCurrentSupplierAsync();
        if (supplier == null || supplier.Status != "Approved")
        {
            return NotApprovedView(supplier);
        }

        var tenderId = await _context.BidItems.Where(x => x.Id == id && x.Bid.SupplierProfileId == supplier.Id).Select(x => (int?)x.Bid.TenderId).SingleOrDefaultAsync();
        if (tenderId is null) return NotFound();
        await using var tx = await TenderRules.LockAsync(_context, tenderId.Value);
        var bidItem = await GetMyBidItemAsync(id, supplier.Id);
        if (bidItem == null)
        {
            return NotFound();
        }

        if (!CanChangeBid(bidItem.Bid))
        {
            TempData["Error"] = CannotChangeMessage;
            return BackToBid(bidItem.BidId);
        }

        // Keep what we need before the item is removed
        var bid = bidItem.Bid;
        string itemName = bidItem.PurchaseRequisitionItem.ItemName;

        _context.BidItems.Remove(bidItem);
        await _context.SaveChangesAsync();

        await RecalculateTotalAsync(bid);

        await tx.CommitAsync();
        TempData["Msg"] = $"Price removed for {itemName}.";
        return BackToBid(bid.Id);
    }

    // ----------------- helper methods -----------------

    // The supplier profile of the logged-in user (or null)
    private async Task<SupplierProfile?> GetCurrentSupplierAsync()
    {
        string? userId = _userManager.GetUserId(User);
        return await _context.SupplierProfiles.FirstOrDefaultAsync(s => s.UserId == userId);
    }

    // Same "not approved" page as BidsController (full path because it is in the Bids folder)
    private IActionResult NotApprovedView(SupplierProfile? supplier)
    {
        ViewBag.SupplierStatus = supplier?.Status ?? "No supplier profile";
        return View("~/Views/Bids/NotApproved.cshtml");
    }

    // One of MY bids (with its tender), or null if it doesn't exist or belongs to someone else
    private async Task<Bid?> GetMyBidAsync(int bidId, int supplierProfileId)
    {
        return await _context.Bids
            .Include(b => b.Tender)
            .FirstOrDefaultAsync(b => b.Id == bidId && b.SupplierProfileId == supplierProfileId);
    }

    // One of MY bid items, with its bid + tender and the requested item, or null
    private async Task<BidItem?> GetMyBidItemAsync(int bidItemId, int supplierProfileId)
    {
        return await _context.BidItems
            .Include(bi => bi.Bid.Tender)
            .Include(bi => bi.PurchaseRequisitionItem)
            .FirstOrDefaultAsync(bi => bi.Id == bidItemId && bi.Bid.SupplierProfileId == supplierProfileId);
    }

    // A requested item, only if it belongs to the purchase requisition of the bid's tender
    private async Task<PurchaseRequisitionItem?> GetRequestedItemAsync(int purchaseRequisitionItemId, int purchaseRequisitionId)
    {
        return await _context.PurchaseRequisitionItems
            .FirstOrDefaultAsync(p => p.Id == purchaseRequisitionItemId && p.PurchaseRequisitionId == purchaseRequisitionId);
    }

    // Does this bid already have a price for this requested item?
    private async Task<bool> HasPriceAsync(int bidId, int purchaseRequisitionItemId)
    {
        return await _context.BidItems.AnyAsync(bi =>
            bi.BidId == bidId && bi.PurchaseRequisitionItemId == purchaseRequisitionItemId);
    }

    // Prices can change only in a Draft bid while the tender is Open and before CloseDate
    private bool CanChangeBid(Bid bid)
    {
        return bid.Status == "Draft"
               && bid.Tender.Status == "Open"
               && DateTime.UtcNow < bid.Tender.CloseDate;
    }

    // Bid.TotalPrice = sum of (UnitPrice * Quantity) of all its items, read from the database
    private async Task RecalculateTotalAsync(Bid bid)
    {
        bid.TotalPrice = await _context.BidItems
            .Where(bi => bi.BidId == bid.Id)
            .SumAsync(bi => bi.UnitPrice * bi.Quantity);
        await _context.SaveChangesAsync();
    }

    // Go back to the bid page (Details action in BidsController)
    private IActionResult BackToBid(int bidId)
    {
        return RedirectToAction(nameof(BidsController.Details), "Bids", new { id = bidId });
    }
}
