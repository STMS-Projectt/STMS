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

    private readonly IWebHostEnvironment _environment;

    // All photos of one item in one request, plus room for the other form fields
    private const long MaxUploadBytes = BidItem.MaxImages * BidFiles.MaxBytes + 1024 * 1024;

    public BidItemsController(ApplicationDbContext context, UserManager<IdentityUser> userManager, IWebHostEnvironment environment)
    {
        _context = context;
        _userManager = userManager;
        _environment = environment;
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
    [RequestSizeLimit(MaxUploadBytes), RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
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

        // The sample photos (optional) must be real JPG/PNG files, at most BidItem.MaxImages
        var newImages = await CheckImagesAsync(model, 0);

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
        await AddImagesAsync(bidItem, newImages);

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
    [RequestSizeLimit(MaxUploadBytes), RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
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

        // Photos ticked for removal (only this item's own photos), then the new photos within the limit
        var removed = bidItem.Images.Where(i => model.RemoveImageIds.Contains(i.Id)).ToList();
        var newImages = await CheckImagesAsync(model, bidItem.Images.Count - removed.Count);

        if (!ModelState.IsValid)
        {
            ViewBag.BidItem = bidItem;
            return View(model);
        }

        // Only price and notes change. Quantity is always the requested quantity.
        bidItem.UnitPrice = model.UnitPrice;
        bidItem.Notes = model.Notes;
        bidItem.Quantity = bidItem.PurchaseRequisitionItem.Quantity;
        _context.BidItemImages.RemoveRange(removed);
        await AddImagesAsync(bidItem, newImages);
        await _context.SaveChangesAsync();

        await RecalculateTotalAsync(bidItem.Bid);

        await tx.CommitAsync();
        foreach (var image in removed) BidFiles.Delete(_environment, image.FilePath);
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
        var images = bidItem.Images.Select(i => i.FilePath).ToList();

        _context.BidItems.Remove(bidItem);
        await _context.SaveChangesAsync();

        await RecalculateTotalAsync(bid);

        await tx.CommitAsync();
        foreach (var image in images) BidFiles.Delete(_environment, image);
        TempData["Msg"] = $"Price removed for {itemName}.";
        return BackToBid(bid.Id);
    }

    // ----------------- helper methods -----------------

    // The chosen sample photos (empty file inputs ignored). Each must be a real JPG/PNG and
    // keptCount + new photos may not pass BidItem.MaxImages. Errors go into ModelState.
    private async Task<List<IFormFile>> CheckImagesAsync(BidItemFormViewModel model, int keptCount)
    {
        var ui = new UiText(); // messages are built here, so translate them here
        var files = model.SampleImages.Where(f => f != null && !string.IsNullOrEmpty(f.FileName)).ToList();
        if (keptCount + files.Count > BidItem.MaxImages)
        {
            ModelState.AddModelError(nameof(model.SampleImages), ui.F("An item can have at most {0} photos.", BidItem.MaxImages));
        }
        foreach (var file in files)
        {
            if (await BidFiles.CheckImageAsync(file) is string error)
            {
                ModelState.AddModelError(nameof(model.SampleImages), $"{Path.GetFileName(file.FileName)}: {ui.T(error)}");
            }
        }
        return files;
    }

    // Saves the photos to disk and adds them to the item (saved with the next SaveChanges)
    private async Task AddImagesAsync(BidItem bidItem, List<IFormFile> files)
    {
        foreach (var file in files)
        {
            bidItem.Images.Add(new BidItemImage
            {
                FilePath = await BidFiles.SaveAsync(_environment, file),
                FileName = Path.GetFileName(file.FileName),
                UploadedAt = DateTime.UtcNow
            });
        }
    }

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
            .Include(bi => bi.Images.OrderBy(i => i.Id))
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
