using System.ComponentModel.DataAnnotations;
using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SourcingTenderSystem.Data;
using SourcingTenderSystem.Models;
using SourcingTenderSystem.Security;
using SourcingTenderSystem.Services;
using SourcingTenderSystem.ViewModels;

namespace SourcingTenderSystem.Controllers;

[Authorize, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ClarificationsController(ApplicationDbContext db, ClarificationRouting routing, SupplierFileStore files, BidDocumentAccess bidAccess) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? kind, int? recordId)
    {
        var actor = await routing.Current(User);
        var admin = User.IsInRole(Roles.Admin);
        if (actor is null && !admin) return Forbid();
        var query = Threads(actor, admin);
        if (recordId is int id)
            query = kind switch {
                "request" => query.Where(r => r.PurchaseRequisitionId == id),
                "tender" => query.Where(r => r.TenderId == id || r.Bid!.TenderId == id),
                "bid" => query.Where(r => r.BidId == id),
                "supplier" => query.Where(r => r.SupplierApplicationId == id),
                _ => query.Where(r => false)
            };
        var route = actor is not null && kind is not null && recordId is int target && CanStart(kind, actor)
            ? await routing.Get(kind, target, actor) : null;
        return View(new ClarificationInbox(await query.OrderByDescending(r => r.AnsweredAt ?? r.CreatedAt)
            .ThenByDescending(r => r.Id).ToListAsync(), kind, recordId, route, []));
    }

    [HttpPost, Authorize(Roles = Roles.SourcingOfficer)]
    public async Task<IActionResult> ClaimRequest(int id)
    {
        var actor = await routing.Current(User);
        if (actor?.Employee is not int officerId) return Forbid();
        var changed = await db.PurchaseRequisitions.Where(r => r.Id == id &&
            r.ReviewedByEmployeeProfileId == null && (r.Status == "Submitted" || r.Status == "Under Review"))
            .ExecuteUpdateAsync(set => set.SetProperty(r => r.ReviewedByEmployeeProfileId, officerId)
                .SetProperty(r => r.Status, "Under Review"));
        if (changed == 0)
            TempData["ClarificationError"] = "The request is already assigned or is no longer awaiting review.";
        return RedirectToAction(nameof(Index), new { kind = "request", recordId = id });
    }
    [HttpGet]
    public async Task<IActionResult> StartRequest(int id)
    {
        var actor = await routing.Current(User);
        if (actor is null) return Forbid();
        var route = await routing.Get("request", id, actor);
        return route is null
            ? RedirectToAction(nameof(Index), new { kind = "request", recordId = id })
            : Compose(new ClarificationInput { Kind = "request", RecordId = id }, route);
    }

    [HttpGet]
    public async Task<IActionResult> Create(string kind, int recordId)
    {
        var actor = await routing.Current(User);
        if (actor is null) return Forbid();
        if (!CanStart(kind, actor)) return Forbid();
        var route = await routing.Get(kind, recordId, actor);
        return route is null ? NotFound() : Compose(new() { Kind = kind, RecordId = recordId }, route,
            await RequestedTypes(kind, recordId, actor));
    }

    [HttpPost]
    public async Task<IActionResult> Create(ClarificationInput input)
    {
        var actor = await routing.Current(User);
        if (actor is null) return Forbid();
        if (!CanStart(input.Kind, actor)) return Forbid();
        int? tenderId = input.Kind == "bid" ? await db.Bids.Where(b => b.Id == input.RecordId)
            .Select(b => (int?)b.TenderId).SingleOrDefaultAsync() : null;
        await using var tx = tenderId is int key ? await TenderRules.LockAsync(db, key)
            : await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var route = await routing.Get(input.Kind, input.RecordId, actor);
        if (route is null) return NotFound();
        var recipient = route.Recipients.SingleOrDefault(p => p.Key == input.Recipient);
        if (recipient is null) ModelState.AddModelError(nameof(input.Recipient), "Select an eligible recipient.");
        var types = await RequestedTypes(input.Kind, input.RecordId, actor);
        if (input.Kind == "supplier" && input.RequestedDocumentType is null ||
            input.RequestedDocumentType is not null && (!types.Contains(input.RequestedDocumentType) || recipient?.Supplier is null))
            ModelState.AddModelError(nameof(input.RequestedDocumentType), "Select an available document type for this request.");
        if (input.RequestedDocumentType is not null && tenderId.HasValue &&
            await db.Bids.Where(b => b.Id == input.RecordId && b.ComplianceClarificationId != null)
                .AnyAsync(b => db.ClarificationRequests.Any(r => r.Id == b.ComplianceClarificationId && r.Status != "Resolved")))
            ModelState.AddModelError("", "Resolve the open compliance clarification before requesting a new document.");
        if (!ModelState.IsValid) return Compose(input, route, types);
        var row = new ClarificationRequest
        {
            SenderEmployeeProfileId = actor.Employee, SenderSupplierProfileId = actor.Supplier,
            ReceiverEmployeeProfileId = recipient!.Employee, ReceiverSupplierProfileId = recipient.Supplier,
            Subject = input.Subject.Trim(), Description = input.Description.Trim(), Status = "Pending",
            PurchaseRequisitionId = input.Kind == "request" ? input.RecordId : null,
            TenderId = input.Kind == "tender" ? input.RecordId : null,
            BidId = input.Kind == "bid" ? input.RecordId : null,
            SupplierApplicationId = input.Kind == "supplier" ? input.RecordId : null,
            RequestedDocumentType = input.RequestedDocumentType, CreatedAt = DateTime.UtcNow
        };
        db.ClarificationRequests.Add(row);
        if (tenderId is int tenderKey && actor.Employee is int officer && recipient.Supplier is not null)
        {
            var bid = await db.Bids.Include(b => b.Tender).SingleAsync(b => b.Id == input.RecordId);
            if (bid.Tender.Status == "Closed" && bid.Status is ("Submitted" or "Pending Clarification") &&
                (bid.ComplianceClarificationId == null || await db.ClarificationRequests.AnyAsync(r =>
                    r.Id == bid.ComplianceClarificationId && r.Status == "Resolved")))
            {
                bid.Status = "Pending Clarification";
                bid.ComplianceClarification = row;
                EvaluationRules.Audit(db, tenderKey, officer, "ComplianceClarificationRequested", $"Bid #{bid.Id}", row.CreatedAt);
            }
        }
        await db.SaveChangesAsync();
        Notify(recipient.Employee, recipient.Supplier, "New clarification request", row);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        TempData["Message"] = "Clarification sent.";
        return RedirectToAction(nameof(Details), new { id = row.Id });
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var actor = await routing.Current(User);
        var admin = User.IsInRole(Roles.Admin);
        if (actor is null && !admin) return Forbid();
        var row = await Threads(actor, admin).SingleOrDefaultAsync(r => r.Id == id);
        if (row is null) return NotFound();
        var participant = actor is not null && (IsSender(row, actor) || IsReceiver(row, actor));
        var canAnswer = actor is not null && row.Status == "Pending" && IsReceiver(row, actor) && await CanReply(row, actor);
        return View(new ClarificationThread(row, canAnswer, actor is not null && row.Status == "Answered" && IsSender(row, actor),
            actor is not null && await CanAddAttachment(row, actor), participant && row.AttachmentKey != null &&
            (row.BidId is null || row.Bid is not null && await bidAccess.CanRead(User, row.Bid))));
    }

    [HttpPost]
    [RequestSizeLimit(SupplierFileStore.MaxBytes + 65536)]
    public async Task<IActionResult> Answer(int id, [StringLength(2000)] string? response, IFormFile? attachment,
        DateTime? expiryDate, CancellationToken cancellationToken)
    {
        var actor = await routing.Current(User);
        if (actor is null) return Forbid();
        await using var tx = await LockThread(id, cancellationToken);
        var row = await db.ClarificationRequests.SingleOrDefaultAsync(r => r.Id == id);
        if (row is null || !IsReceiver(row, actor)) return NotFound();
        if (row.Status != "Pending" || !await CanReply(row, actor))
            return Conflict("This clarification can no longer be answered at the current stage.");
        var documentRequested = row.RequestedDocumentType != null;
        if (!ModelState.IsValid || (!documentRequested && (string.IsNullOrWhiteSpace(response) || attachment != null)) ||
            (documentRequested && (attachment is null || actor.Supplier is null)))
        {
            TempData["ClarificationError"] = documentRequested ? "Attach the requested PDF. Notes can be up to 2,000 characters."
                : "Enter a response between 1 and 2,000 characters. Attachments are allowed when the officer requests a document.";
            return RedirectToAction(nameof(Details), new { id });
        }
        var key = attachment is null ? null : await files.SaveAsync(attachment, cancellationToken, pending: true);
        if (attachment != null && key is null)
        {
            TempData["ClarificationError"] = "Choose a valid PDF no larger than 10 MB.";
            return RedirectToAction(nameof(Details), new { id });
        }
        row.Response = response?.Trim();
        row.AttachmentKey = key;
        row.AttachmentName = attachment is null ? null : SupplierFileStore.SafeName(attachment.FileName);
        row.AttachmentExpiryDate = row.SupplierApplicationId.HasValue ? expiryDate?.Date : null;
        row.AnsweredAt = DateTime.UtcNow;
        row.Status = "Answered";
        Notify(row.SenderEmployeeProfileId, row.SenderSupplierProfileId, "Clarification answered", row);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            if (key != null) files.Delete(key, pending: true);
            throw;
        }
        TempData["Message"] = "Response sent.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> DownloadAttachment(int id, [FromServices] ProtectedPdf pdf)
    {
        var actor = await routing.Current(User);
        if (actor is null) return Forbid();
        var row = await Threads(actor, false).SingleOrDefaultAsync(r => r.Id == id);
        if (row?.AttachmentKey is not { } key || row.BidId.HasValue && (row.Bid is null || !await bidAccess.CanRead(User, row.Bid)) ||
            files.Open(key, pending: true) is not { } stream) return NotFound();
        return row.BidId.HasValue
            ? await pdf.Serve(this, stream, row.AttachmentName ?? "clarification.pdf", preview: false)
            : File(stream, "application/pdf", row.AttachmentName ?? "clarification.pdf");
    }

    [HttpPost, Authorize(Roles = Roles.SourcingOfficer)]
    public async Task<IActionResult> AddAttachment(int id, CancellationToken cancellationToken)
    {
        var actor = await routing.Current(User);
        if (actor?.Employee is not int officer) return Forbid();
        await using var tx = await LockThread(id, cancellationToken);
        var row = await db.ClarificationRequests.SingleOrDefaultAsync(r => r.Id == id && r.SenderEmployeeProfileId == officer, cancellationToken);
        if (row is null) return NotFound();
        if (!await CanAddAttachment(row, actor)) return Conflict("This document cannot be added at the current stage.");
        var forBid = row.BidId.HasValue;
        var key = await files.CopyAttachmentAsync(row.AttachmentKey!, forBid, cancellationToken);
        if (key is null) return NotFound();
        try
        {
            var now = DateTime.UtcNow;
            if (row.BidId is int bidId)
                db.BidDocuments.Add(new BidDocument { BidId = bidId, FilePath = key, FileName = row.AttachmentName ?? "clarification.pdf",
                    DocumentType = row.RequestedDocumentType!, UploadedAt = now });
            else
                db.SupplierDocuments.Add(new SupplierDocument { SupplierProfileId = row.SupplierApplicationId!.Value,
                    FilePath = key, FileName = row.AttachmentName, DocumentType = row.RequestedDocumentType!,
                    ExpiryDate = row.AttachmentExpiryDate, UploadedAt = now });
            row.AttachmentAddedAt = row.ResolvedAt = now;
            row.Status = "Resolved";
            db.AuditLogs.Add(new AuditLog { EntityName = forBid ? nameof(Bid) : nameof(SupplierProfile),
                EntityId = row.BidId ?? row.SupplierApplicationId!.Value, EmployeeProfileId = officer,
                SupplierProfileId = row.ReceiverSupplierProfileId, Action = "ClarificationDocumentAdded",
                Details = $"Document from clarification #{row.Id}", CreatedAt = now });
            Notify(null, row.ReceiverSupplierProfileId, "Requested document added", row);
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            files.DeleteCopy(key, forBid);
            throw;
        }
        TempData["Message"] = forBid ? "Document approved and added to the bid." : "Document approved and added to the supplier application.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Resolve(int id)
    {
        var actor = await routing.Current(User);
        if (actor is null) return Forbid();
        var query = db.ClarificationRequests.Where(r => r.Id == id && r.Status == "Answered");
        query = actor.Employee is int e ? query.Where(r => r.SenderEmployeeProfileId == e)
            : query.Where(r => r.SenderSupplierProfileId == actor.Supplier);
        var now = DateTime.UtcNow;
        if (await query.ExecuteUpdateAsync(update => update.SetProperty(r => r.Status, "Resolved")
            .SetProperty(r => r.ResolvedAt, now)) == 0) return NotFound();
        TempData["Message"] = "Clarification resolved.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private IQueryable<ClarificationRequest> Threads(ClarificationPerson? actor, bool allThreads)
    {
        var query = db.ClarificationRequests.AsNoTracking();
        if (!allThreads)
            query = actor?.Employee is int employee
                ? query.Where(r => r.SenderEmployeeProfileId == employee || r.ReceiverEmployeeProfileId == employee)
                : actor?.Supplier is int supplier
                    ? query.Where(r => r.SenderSupplierProfileId == supplier || r.ReceiverSupplierProfileId == supplier)
                    : query.Where(r => false);
        return query.Include(r => r.SenderEmployeeProfile).Include(r => r.ReceiverEmployeeProfile)
            .Include(r => r.SenderSupplierProfile).Include(r => r.ReceiverSupplierProfile)
            .Include(r => r.PurchaseRequisition).Include(r => r.SupplierApplication).Include(r => r.Tender).Include(r => r.Bid).ThenInclude(b => b!.Tender);
    }

    private async Task<bool> CanReply(ClarificationRequest row, ClarificationPerson actor)
    {
        var kind = row.PurchaseRequisitionId.HasValue ? "request" : row.BidId.HasValue ? "bid" :
            row.SupplierApplicationId.HasValue ? "supplier" : "tender";
        var id = row.PurchaseRequisitionId ?? row.BidId ?? row.SupplierApplicationId ?? row.TenderId;
        if (id is null) return false;
        var route = await routing.Get(kind, id.Value, actor);
        var sender = new ClarificationPerson(row.SenderEmployeeProfileId, row.SenderSupplierProfileId, "");
        if (route?.Recipients.Any(p => p.Key == sender.Key) != true) return false;
        if (row.RequestedDocumentType == null) return true;
        if (actor.Supplier is null) return false;
        return row.BidId is not int bid || await CanReceiveBidSupplement(bid, actor.Supplier);
    }

    private static bool IsReceiver(ClarificationRequest row, ClarificationPerson person) =>
        person.Employee is int e ? row.ReceiverEmployeeProfileId == e : row.ReceiverSupplierProfileId == person.Supplier;
    private static bool IsSender(ClarificationRequest row, ClarificationPerson person) =>
        person.Employee is int e ? row.SenderEmployeeProfileId == e : row.SenderSupplierProfileId == person.Supplier;

    private bool CanStart(string kind, ClarificationPerson actor) => kind != "supplier" ||
        (actor.Employee.HasValue && User.IsInRole(Roles.SourcingOfficer));

    private Task<bool> CanReceiveBidSupplement(int bidId, int? supplierId = null) =>
        db.Bids.AnyAsync(b => b.Id == bidId && (supplierId == null || b.SupplierProfileId == supplierId) &&
            b.SubmittedAt != null && b.Tender.Status == "Closed" &&
            (b.Status == "Submitted" || b.Status == "Pending Clarification"));

    private async Task<IReadOnlyList<string>> RequestedTypes(string kind, int recordId, ClarificationPerson actor)
    {
        if (actor.Employee is null || !User.IsInRole(Roles.SourcingOfficer)) return [];
        if (kind == "supplier") return SupplierFileStore.DocumentTypes;
        return kind == "bid" && await CanReceiveBidSupplement(recordId) ? BidDocument.DocumentTypes : [];
    }

    private async Task<bool> CanAddAttachment(ClarificationRequest row, ClarificationPerson actor)
    {
        if (!User.IsInRole(Roles.SourcingOfficer) || actor.Employee is null || !IsSender(row, actor) ||
            row.Status != "Answered" || row.AttachmentKey == null || row.AttachmentAddedAt != null ||
            row.RequestedDocumentType == null || row.ReceiverSupplierProfileId is not int supplierId)
            return false;
        if (row.BidId is int bidId)
            return BidDocument.DocumentTypes.Contains(row.RequestedDocumentType) && await CanReceiveBidSupplement(bidId, supplierId)
                && (await routing.Get("bid", bidId, actor))?.Recipients.Any(p => p.Supplier == supplierId) == true;
        return row.SupplierApplicationId == supplierId && SupplierFileStore.DocumentTypes.Contains(row.RequestedDocumentType)
            && (await routing.Get("supplier", supplierId, actor))?.Recipients.Any(p => p.Supplier == supplierId) == true;
    }

    private async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> LockThread(int id, CancellationToken cancellationToken)
    {
        var tenderId = await db.ClarificationRequests.Where(r => r.Id == id && r.BidId.HasValue)
            .Select(r => (int?)r.Bid!.TenderId).SingleOrDefaultAsync(cancellationToken);
        return tenderId is int tender ? await TenderRules.LockAsync(db, tender, cancellationToken)
            : await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
    }

    private IActionResult Compose(ClarificationInput input, ClarificationRoute route, IReadOnlyList<string>? types = null)
    {
        ViewData["ClarificationRoute"] = route;
        ViewData["RequestedDocumentTypes"] = types;
        return View("Create", input);
    }

    private void Notify(int? employee, int? supplier, string title, ClarificationRequest row) =>
        db.Notifications.Add(new Notification { EmployeeProfileId = employee, SupplierProfileId = supplier,
            Title = title, Message = row.Subject, Link = Url.Action(nameof(Details), new { id = row.Id }), CreatedAt = DateTime.UtcNow });
}
