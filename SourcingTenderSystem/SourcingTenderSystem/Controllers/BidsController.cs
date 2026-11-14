using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SourcingTenderSystem.Data;
using SourcingTenderSystem.Models;
using SourcingTenderSystem.Security;
using SourcingTenderSystem.ViewModels;

namespace SourcingTenderSystem.Controllers;

// Supplier side of the bids module: list, create, edit, view, submit and withdraw my bids.
[Authorize(Roles = Roles.Supplier)]
public class BidsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<IdentityUser> _userManager;

    // Fixed list of currencies the supplier can choose from
    private static readonly string[] Currencies = { "JOD", "USD", "EUR" };

    public BidsController(ApplicationDbContext context, UserManager<IdentityUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    // GET: /Bids/MyBids
    [HttpGet]
    public async Task<IActionResult> MyBids()
    {
        var supplier = await GetCurrentSupplierAsync();
        if (supplier == null || supplier.Status != "Approved")
        {
            return NotApprovedView(supplier);
        }

        // (b) All my bids, newest first
        var myBids = await _context.Bids
            .Include(b => b.Tender)
            .Where(b => b.SupplierProfileId == supplier.Id)
            .OrderByDescending(b => b.Id)
            .ToListAsync();

        // Ids of the tenders I already have a bid on (any status, even Withdrawn)
        var tenderIdsWithBid = new List<int>();
        foreach (var bid in myBids)
        {
            tenderIdsWithBid.Add(bid.TenderId);
        }

        var now = DateTime.UtcNow;

        // (a) Accepted invitations for open tenders where I have no bid yet
        var waitingInvitations = await _context.Invitations
            .Include(i => i.Tender)
            .Where(i => i.SupplierProfileId == supplier.Id
                     && i.Status == "Accepted"
                     && i.Tender.Status == "Open"
                     && i.Tender.OpenDate <= now && i.Tender.CloseDate > now
                     && !tenderIdsWithBid.Contains(i.TenderId))
            .OrderBy(i => i.Tender.CloseDate)
            .ToListAsync();

        var model = new MyBidsViewModel
        {
            WaitingInvitations = waitingInvitations,
            MyBids = myBids
        };
        return View(model);
    }

    // GET: /Bids/Create?tenderId=5
    [HttpGet]
    public async Task<IActionResult> Create(int tenderId)
    {
        var supplier = await GetCurrentSupplierAsync();
        if (supplier == null || supplier.Status != "Approved")
        {
            return NotApprovedView(supplier);
        }

        var tender = await _context.Tenders.FindAsync(tenderId);
        if (tender == null)
        {
            return NotFound();
        }

        string? error = await CheckCanCreateBidAsync(supplier.Id, tender);
        if (error != null)
        {
            TempData["Error"] = error;
            return RedirectToAction(nameof(MyBids));
        }

        ViewBag.Tender = tender;
        ViewBag.Currencies = GetCurrencyList();
        return View(new BidFormViewModel());
    }

    // POST: /Bids/Create?tenderId=5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(int tenderId, BidFormViewModel model)
    {
        var supplier = await GetCurrentSupplierAsync();
        if (supplier == null || supplier.Status != "Approved")
        {
            return NotApprovedView(supplier);
        }

        var tender = await _context.Tenders.FindAsync(tenderId);
        if (tender == null)
        {
            return NotFound();
        }

        // Check the rules again on POST (never trust that the GET page was used)
        string? error = await CheckCanCreateBidAsync(supplier.Id, tender);
        if (error != null)
        {
            TempData["Error"] = error;
            return RedirectToAction(nameof(MyBids));
        }

        // The currency must be one of our fixed list
        if (!Currencies.Contains(model.Currency))
        {
            ModelState.AddModelError(nameof(model.Currency), "Please choose a currency from the list.");
        }

        if (!ModelState.IsValid)
        {
            ViewBag.Tender = tender;
            ViewBag.Currencies = GetCurrencyList();
            return View(model);
        }

        // Sensitive values come from the server, not from the form
        var bid = new Bid
        {
            TenderId = tender.Id,
            SupplierProfileId = supplier.Id,
            Currency = model.Currency,
            DeliveryDays = model.DeliveryDays,
            ValidityDays = model.ValidityDays,
            Status = "Draft",
            TotalPrice = 0,
            SubmittedAt = null
        };

        _context.Bids.Add(bid);
        await _context.SaveChangesAsync();

        TempData["Msg"] = "Draft bid created. Add a price for every item and upload at least one document, then submit.";
        return RedirectToAction(nameof(Details), new { id = bid.Id });
    }

    // GET: /Bids/Edit/5
    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var supplier = await GetCurrentSupplierAsync();
        if (supplier == null || supplier.Status != "Approved")
        {
            return NotApprovedView(supplier);
        }

        var bid = await GetMyBidAsync(id, supplier.Id);
        if (bid == null)
        {
            return NotFound();
        }

        if (bid.Status != "Draft" || !IsOpenForBidding(bid.Tender))
        {
            TempData["Error"] = "You can only edit a draft bid while the tender is still open.";
            return RedirectToAction(nameof(Details), new { id = bid.Id });
        }

        // Copy the current values into the form
        var model = new BidFormViewModel
        {
            Currency = bid.Currency,
            DeliveryDays = bid.DeliveryDays,
            ValidityDays = bid.ValidityDays
        };

        ViewBag.Bid = bid;
        ViewBag.Currencies = GetCurrencyList();
        return View(model);
    }

    // POST: /Bids/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, BidFormViewModel model)
    {
        var supplier = await GetCurrentSupplierAsync();
        if (supplier == null || supplier.Status != "Approved")
        {
            return NotApprovedView(supplier);
        }

        var bid = await GetMyBidAsync(id, supplier.Id);
        if (bid == null)
        {
            return NotFound();
        }

        if (bid.Status != "Draft" || !IsOpenForBidding(bid.Tender))
        {
            TempData["Error"] = "You can only edit a draft bid while the tender is still open.";
            return RedirectToAction(nameof(Details), new { id = bid.Id });
        }

        if (!Currencies.Contains(model.Currency))
        {
            ModelState.AddModelError(nameof(model.Currency), "Please choose a currency from the list.");
        }

        if (!ModelState.IsValid)
        {
            ViewBag.Bid = bid;
            ViewBag.Currencies = GetCurrencyList();
            return View(model);
        }

        // Only the 3 allowed fields change. EF is already tracking "bid", so SaveChangesAsync is enough.
        bid.Currency = model.Currency;
        bid.DeliveryDays = model.DeliveryDays;
        bid.ValidityDays = model.ValidityDays;
        await _context.SaveChangesAsync();

        TempData["Msg"] = "Bid information updated.";
        return RedirectToAction(nameof(Details), new { id = bid.Id });
    }

    // GET: /Bids/Details/5
    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var supplier = await GetCurrentSupplierAsync();
        if (supplier == null || supplier.Status != "Approved")
        {
            return NotApprovedView(supplier);
        }

        // Only my bid: the Id AND the owner must match
        var bid = await _context.Bids
            .Include(b => b.Tender)
            .Include(b => b.BidItems)
            .Include(b => b.BidDocuments)
            .FirstOrDefaultAsync(b => b.Id == id && b.SupplierProfileId == supplier.Id);
        if (bid == null)
        {
            return NotFound();
        }

        var requestedItems = await GetRequestedItemsAsync(bid.Tender.PurchaseRequisitionId);

        var model = new BidDetailsViewModel();
        model.Bid = bid;
        model.TenderIsOpen = IsOpenForBidding(bid.Tender);
        model.MissingPrices = CountMissingPrices(requestedItems, bid);

        // One row per requested item, with my price if I already added one
        foreach (var requestedItem in requestedItems)
        {
            var bidItem = bid.BidItems.FirstOrDefault(bi => bi.PurchaseRequisitionItemId == requestedItem.Id);
            model.Rows.Add(new BidItemRow { RequestedItem = requestedItem, BidItem = bidItem });
        }

        // Which buttons to show
        model.CanChange = bid.Status == "Draft" && model.TenderIsOpen;
        model.CanSubmit = model.CanChange
                          && requestedItems.Count > 0
                          && model.MissingPrices == 0
                          && bid.BidDocuments.Count > 0;
        model.CanWithdraw = (bid.Status == "Draft" || bid.Status == "Submitted") && model.TenderIsOpen;

        return View(model);
    }

    // POST: /Bids/Submit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(int id)
    {
        var supplier = await GetCurrentSupplierAsync();
        if (supplier == null || supplier.Status != "Approved")
        {
            return NotApprovedView(supplier);
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var bid = await _context.Bids
            .Include(b => b.Tender)
            .Include(b => b.BidItems)
            .Include(b => b.BidDocuments)
            .FirstOrDefaultAsync(b => b.Id == id && b.SupplierProfileId == supplier.Id);
        if (bid == null)
        {
            return NotFound();
        }

        // Rule 1: only a draft can be submitted
        if (bid.Status != "Draft")
        {
            TempData["Error"] = "Only a draft bid can be submitted.";
            return RedirectToAction(nameof(Details), new { id = bid.Id });
        }

        // Rule 2: the tender must be Open and before CloseDate
        if (!IsOpenForBidding(bid.Tender))
        {
            TempData["Error"] = "The tender is closed. You can't submit this bid any more.";
            return RedirectToAction(nameof(Details), new { id = bid.Id });
        }

        // Rule 3: every requested item must have a price
        var requestedItems = await GetRequestedItemsAsync(bid.Tender.PurchaseRequisitionId);
        if (requestedItems.Count == 0)
        {
            TempData["Error"] = "This tender has no requested items. Please contact the sourcing officer.";
            return RedirectToAction(nameof(Details), new { id = bid.Id });
        }

        int missingPrices = CountMissingPrices(requestedItems, bid);
        if (missingPrices > 0)
        {
            TempData["Error"] = $"You still have {missingPrices} item(s) without a price.";
            return RedirectToAction(nameof(Details), new { id = bid.Id });
        }

        // Rule 4: at least one document
        if (bid.BidDocuments.Count == 0)
        {
            TempData["Error"] = "Upload at least one document before submitting.";
            return RedirectToAction(nameof(Details), new { id = bid.Id });
        }

        // Recalculate the total from the database (UnitPrice * Quantity of every item)
        bid.TotalPrice = await _context.BidItems
            .Where(bi => bi.BidId == bid.Id)
            .SumAsync(bi => bi.UnitPrice * bi.Quantity);

        bid.Status = "Submitted";
        bid.SubmittedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        await transaction.CommitAsync();
        // TODO: audit log + notification (team mechanism) - bid submitted

        TempData["Msg"] = "Your bid was submitted successfully.";
        return RedirectToAction(nameof(Details), new { id = bid.Id });
    }

    // POST: /Bids/Withdraw/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Withdraw(int id)
    {
        var supplier = await GetCurrentSupplierAsync();
        if (supplier == null || supplier.Status != "Approved")
        {
            return NotApprovedView(supplier);
        }

        var bid = await GetMyBidAsync(id, supplier.Id);
        if (bid == null)
        {
            return NotFound();
        }

        if (bid.Status != "Draft" && bid.Status != "Submitted")
        {
            TempData["Error"] = "Only a draft or submitted bid can be withdrawn.";
            return RedirectToAction(nameof(Details), new { id = bid.Id });
        }

        if (!IsOpenForBidding(bid.Tender))
        {
            TempData["Error"] = "The tender is closed. You can't withdraw this bid any more.";
            return RedirectToAction(nameof(Details), new { id = bid.Id });
        }

        bid.Status = "Withdrawn";
        await _context.SaveChangesAsync();

        // TODO: audit log + notification (team mechanism) - bid withdrawn

        TempData["Msg"] = "Your bid was withdrawn. You can't create a new bid on this tender.";
        return RedirectToAction(nameof(MyBids));
    }

    // ----------------- helper methods -----------------

    // The supplier profile of the logged-in user (or null)
    private async Task<SupplierProfile?> GetCurrentSupplierAsync()
    {
        string? userId = _userManager.GetUserId(User);
        return await _context.SupplierProfiles.FirstOrDefaultAsync(s => s.UserId == userId);
    }

    // Page shown when the supplier is not "Approved"
    private IActionResult NotApprovedView(SupplierProfile? supplier)
    {
        ViewBag.SupplierStatus = supplier?.Status ?? "No supplier profile";
        return View("NotApproved");
    }

    // One of MY bids (with its tender), or null if it doesn't exist or belongs to someone else
    private async Task<Bid?> GetMyBidAsync(int bidId, int supplierProfileId)
    {
        return await _context.Bids
            .Include(b => b.Tender)
            .FirstOrDefaultAsync(b => b.Id == bidId && b.SupplierProfileId == supplierProfileId);
    }

    // Bidding is allowed only while the tender is "Open" and before CloseDate
    private bool IsOpenForBidding(Tender tender)
    {
        var now = DateTime.UtcNow;
        return tender.Status == "Open" && tender.OpenDate <= now && now < tender.CloseDate;
    }

    // Returns an error message, or null if the supplier may create a bid on this tender
    private async Task<string?> CheckCanCreateBidAsync(int supplierProfileId, Tender tender)
    {
        bool hasAcceptedInvitation = await _context.Invitations.AnyAsync(i =>
            i.TenderId == tender.Id
            && i.SupplierProfileId == supplierProfileId
            && i.Status == "Accepted");
        if (!hasAcceptedInvitation)
        {
            return "You don't have an accepted invitation for this tender.";
        }

        if (!IsOpenForBidding(tender))
        {
            return "This tender is not open for bidding.";
        }

        // One bid per supplier per tender (there is also a unique index in the database)
        bool alreadyHasBid = await _context.Bids.AnyAsync(b =>
            b.TenderId == tender.Id && b.SupplierProfileId == supplierProfileId);
        if (alreadyHasBid)
        {
            return "You already have a bid on this tender. Only one bid per tender is allowed (even after withdrawing).";
        }

        return null;
    }

    // The items the department requested (from the tender's purchase requisition)
    private async Task<List<PurchaseRequisitionItem>> GetRequestedItemsAsync(int purchaseRequisitionId)
    {
        return await _context.PurchaseRequisitionItems
            .Where(p => p.PurchaseRequisitionId == purchaseRequisitionId)
            .OrderBy(p => p.Id)
            .ToListAsync();
    }

    // How many requested items have no BidItem with a price > 0 (bid.BidItems must be loaded)
    private int CountMissingPrices(List<PurchaseRequisitionItem> requestedItems, Bid bid)
    {
        int missing = 0;
        foreach (var requestedItem in requestedItems)
        {
            bool hasPrice = bid.BidItems.Any(bi => bi.PurchaseRequisitionItemId == requestedItem.Id && bi.UnitPrice > 0);
            if (!hasPrice)
            {
                missing++;
            }
        }
        return missing;
    }

    // Currency list for the dropdown
    private List<SelectListItem> GetCurrencyList()
    {
        var list = new List<SelectListItem>();
        foreach (var currency in Currencies)
        {
            list.Add(new SelectListItem { Value = currency, Text = currency });
        }
        return list;
    }
}
