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

// Supplier uploads and deletes the PDF documents of a DRAFT bid.
// Download is allowed for the owner supplier and for the staff roles that review bids.
// Files are saved in App_Data/BidDocuments (outside wwwroot, so they are not public) with a GUID file name.
[Authorize]
public class BidDocumentsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly IWebHostEnvironment _environment;

    // Who may call Download (a supplier is also checked to be the owner, inside the action)
    private const string DownloadRoles = Roles.Supplier + ",SourcingOfficer,EvaluationCommittee,SourcingManager,TopCommittee";

    private const long MaxFileBytes = 10 * 1024 * 1024; // 10 MB

    // Fixed list of document types
    private static readonly string[] DocumentTypes = { "Quotation", "Technical Offer", "Certificate", "Other" };

    private const string CannotChangeMessage = "You can only change documents of a draft bid while the tender is still open.";

    public BidDocumentsController(ApplicationDbContext context, UserManager<IdentityUser> userManager,
                                  IWebHostEnvironment environment)
    {
        _context = context;
        _userManager = userManager;
        _environment = environment;
    }

    // GET: /BidDocuments/Upload?bidId=7
    [HttpGet]
    [Authorize(Roles = Roles.Supplier)]
    public async Task<IActionResult> Upload(int bidId)
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

        ViewBag.Bid = bid;
        ViewBag.DocumentTypes = GetDocumentTypeList();
        return View(new BidDocumentUploadViewModel());
    }

    // POST: /BidDocuments/Upload?bidId=7
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.Supplier)]
    public async Task<IActionResult> Upload(int bidId, BidDocumentUploadViewModel model)
    {
        var supplier = await GetCurrentSupplierAsync();
        if (supplier == null || supplier.Status != "Approved")
        {
            return NotApprovedView(supplier);
        }

        // Check everything again on POST (never trust that the GET page was used)
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

        // The document type must be one of our fixed list
        if (!DocumentTypes.Contains(model.DocumentType))
        {
            ModelState.AddModelError(nameof(model.DocumentType), "Please choose a document type from the list.");
        }

        // Check the file: type, size and content
        string? fileError = await CheckPdfFileAsync(model.File);
        if (fileError != null)
        {
            ModelState.AddModelError(nameof(model.File), fileError);
        }

        if (!ModelState.IsValid)
        {
            ViewBag.Bid = bid;
            ViewBag.DocumentTypes = GetDocumentTypeList();
            return View(model);
        }

        // 1) Save the file on the disk with a new GUID name (never with the user's file name)
        string storedFileName = Guid.NewGuid().ToString("N") + ".pdf";
        string folder = GetUploadFolder();
        Directory.CreateDirectory(folder); // creates the folder the first time (does nothing if it exists)

        string fullPath = Path.Combine(folder, storedFileName);
        using (var stream = new FileStream(fullPath, FileMode.Create))
        {
            await model.File.CopyToAsync(stream);
        }

        // 2) Save a row in BidDocuments
        var document = new BidDocument
        {
            BidId = bid.Id,
            DocumentType = model.DocumentType,
            FileName = Path.GetFileName(model.File.FileName), // original name: only to show it and as the download name
            FilePath = storedFileName,                          // the GUID name inside App_Data/BidDocuments
            UploadedAt = DateTime.UtcNow
        };

        _context.BidDocuments.Add(document);
        await _context.SaveChangesAsync();

        TempData["Msg"] = $"Document \"{document.FileName}\" uploaded.";
        return BackToBid(bid.Id);
    }

    // GET: /BidDocuments/Download/3
    [HttpGet]
    [Authorize(Roles = DownloadRoles)]
    public async Task<IActionResult> Download(int id)
    {
        var document = await _context.BidDocuments
            .Include(d => d.Bid)
            .FirstOrDefaultAsync(d => d.Id == id);
        if (document == null)
        {
            return NotFound();
        }

        if (User.IsInRole(Roles.Supplier))
        {
            // A supplier can download only the documents of their own bids
            var supplier = await GetCurrentSupplierAsync();
            if (supplier == null || document.Bid.SupplierProfileId != supplier.Id)
            {
                return NotFound();
            }
        }
        else
        {
            // Staff can't see a draft: it is still private to the supplier
            if (document.Bid.Status == "Draft")
            {
                return NotFound();
            }
        }

        string fullPath = GetFullPath(document);

        // The file may be missing (for example, the demo rows have fake paths)
        if (!System.IO.File.Exists(fullPath))
        {
            return NotFound();
        }

        return PhysicalFile(fullPath, "application/pdf", document.FileName);
    }

    // POST: /BidDocuments/Delete/3
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.Supplier)]
    public async Task<IActionResult> Delete(int id)
    {
        var supplier = await GetCurrentSupplierAsync();
        if (supplier == null || supplier.Status != "Approved")
        {
            return NotApprovedView(supplier);
        }

        // Only a document of MY bid
        var document = await _context.BidDocuments
            .Include(d => d.Bid.Tender)
            .FirstOrDefaultAsync(d => d.Id == id && d.Bid.SupplierProfileId == supplier.Id);
        if (document == null)
        {
            return NotFound();
        }

        if (!CanChangeBid(document.Bid))
        {
            TempData["Error"] = CannotChangeMessage;
            return BackToBid(document.BidId);
        }

        // Keep what we need before the row is removed
        int bidId = document.BidId;
        string fileName = document.FileName;
        string fullPath = GetFullPath(document);

        // 1) Remove the row
        _context.BidDocuments.Remove(document);
        await _context.SaveChangesAsync();

        // 2) Remove the file from the disk (if it is there)
        if (System.IO.File.Exists(fullPath))
        {
            System.IO.File.Delete(fullPath);
        }

        TempData["Msg"] = $"Document \"{fileName}\" deleted.";
        return BackToBid(bidId);
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

    // Documents can change only in a Draft bid while the tender is Open and before CloseDate
    private bool CanChangeBid(Bid bid)
    {
        return bid.Status == "Draft"
               && bid.Tender.Status == "Open"
               && DateTime.UtcNow < bid.Tender.CloseDate;
    }

    // <project folder>/App_Data/BidDocuments
    private string GetUploadFolder()
    {
        return Path.Combine(_environment.ContentRootPath, "App_Data", "BidDocuments");
    }

    // Full path of a document's file. Path.GetFileName keeps only the name,
    // so a path saved in the database can never point outside our folder.
    private string GetFullPath(BidDocument document)
    {
        return Path.Combine(GetUploadFolder(), Path.GetFileName(document.FilePath));
    }

    // Returns an error message, or null if the file is a real PDF with an allowed size
    private async Task<string?> CheckPdfFileAsync(IFormFile? file)
    {
        if (file == null)
        {
            return null; // no file chosen: [Required] in the ViewModel already shows an error
        }

        if (file.Length == 0)
        {
            return "The file is empty.";
        }

        if (file.Length > MaxFileBytes)
        {
            return "The file is too big. The maximum size is 10 MB.";
        }

        string extension = Path.GetExtension(file.FileName).ToLower();
        if (extension != ".pdf")
        {
            return "Only PDF files are allowed.";
        }

        // A real PDF file always starts with the 5 characters "%PDF-"
        var firstBytes = new byte[5];
        int bytesRead;
        using (var stream = file.OpenReadStream())
        {
            bytesRead = await stream.ReadAsync(firstBytes, 0, 5);
        }

        string start = System.Text.Encoding.ASCII.GetString(firstBytes, 0, bytesRead);
        if (start != "%PDF-")
        {
            return "This file is not a real PDF.";
        }

        return null;
    }

    // Document type list for the dropdown
    private List<SelectListItem> GetDocumentTypeList()
    {
        var list = new List<SelectListItem>();
        foreach (var type in DocumentTypes)
        {
            list.Add(new SelectListItem { Value = type, Text = type });
        }
        return list;
    }

    // Go back to the bid page (Details action in BidsController)
    private IActionResult BackToBid(int bidId)
    {
        return RedirectToAction(nameof(BidsController.Details), "Bids", new { id = bidId });
    }
}
