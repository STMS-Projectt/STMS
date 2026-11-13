
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SourcingTenderSystem.Data;
using SourcingTenderSystem.Models;

public class InvitationsController : Controller
{
    private readonly ApplicationDbContext _context;

    public InvitationsController(ApplicationDbContext context)
    {
        _context = context;
    }

    // GET: INVITATIONS
    public async Task<IActionResult> Index()    
    {
        var invitations = await _context.Invitations
         .Include(i => i.Tender)            // جلب جدول التندر
         .Include(i => i.SupplierProfile)   // جلب جدول المورد
         .ToListAsync();

        return View(invitations);
    }

    // GET: INVITATIONS/Details/5
    public async Task<IActionResult> Details(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var invitation = await _context.Invitations
            .FirstOrDefaultAsync(m => m.Id == id);
        if (invitation == null)
        {
            return NotFound();
        }

        return View(invitation);
    }

    // GET: INVITATIONS/Create
    public async Task<IActionResult> Create()
    {
        ViewData["TenderId"] = new SelectList(_context.Tenders, "Id", "Title");

        ViewBag.Suppliers = await _context.SupplierProfiles.ToListAsync();
        return View();
    }

    // POST: INVITATIONS/Create
    // To protect from overposting attacks, enable the specific properties you want to bind to.
    // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("TenderId,Status,ExpiresAt")] Invitation invitation, List<int> selectedSuppliers)
    {
        if (selectedSuppliers != null && selectedSuppliers.Count > 0 && invitation.TenderId > 0)
        {
            foreach (var supplierId in selectedSuppliers)
            {
                var newInvitation = new Invitation
                {
                    TenderId = invitation.TenderId,               // ربط الدعوة برقم العطاء (لتخزين وإحضار داتا التندر كاملة لاحقاً)
                    SupplierProfileId = supplierId,               // المورد الحالي في الحلقة
                    Status = string.IsNullOrEmpty(invitation.Status) ? "Pending" : invitation.Status,
                    SentAt = DateTime.Now,                        // تاريخ اليوم تلقائياً وقت الإرسال
                    ExpiresAt = invitation.ExpiresAt,             // تاريخ انتهاء الدعوة اللي دخله المستخدم
                    DeclineReason = invitation.DeclineReason
                };

                _context.Add(newInvitation);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        // إذا صار خطأ أو ما اخترت موردين/تندر، يعيد إرسال القوائم عشان ما تضرب الصفحة
        ViewData["TenderId"] = new SelectList(_context.Tenders, "Id", "Title", invitation.TenderId);
        ViewBag.Suppliers = await _context.SupplierProfiles.ToListAsync();
        ModelState.AddModelError("", "Please select a Tender and at least one Supplier.");
        return View(invitation);
    }

    // GET: INVITATIONS/Edit/5////////////////////////////////////////////////
    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var invitation = await _context.Invitations.FindAsync(id);
        if (invitation == null)
        {
            return NotFound();
        }

        ViewData["TenderId"] = new SelectList(_context.Tenders, "Id", "Title", invitation.TenderId);
        ViewData["SupplierProfileId"] = new SelectList(_context.SupplierProfiles, "Id", "CompanyName", invitation.SupplierProfileId);
        return View(invitation);
    }

    // POST: INVITATIONS/Edit/5
    // To protect from overposting attacks, enable the specific properties you want to bind to.
    // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int? id, [Bind("Id,TenderId,SupplierProfileId,Status,SentAt,ExpiresAt,")] Invitation invitation)
    {
        if (id != invitation.Id)
        {
            return NotFound();
        }

        if (ModelState.IsValid)
        {
            try
            {
                _context.Update(invitation);
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!InvitationExists(invitation.Id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }
            return RedirectToAction(nameof(Index));
        }
        return View(invitation);
    }

    // GET: INVITATIONS/Delete/5
    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var invitation = await _context.Invitations
            .FirstOrDefaultAsync(m => m.Id == id);
        if (invitation == null)
        {
            return NotFound();
        }

        ViewData["TenderId"] = new SelectList(_context.Tenders, "Id", "Title", invitation.TenderId);
        ViewData["SupplierProfileId"] = new SelectList(_context.SupplierProfiles, "Id", "CompanyName", invitation.SupplierProfileId);
        return View(invitation);
    }

    // POST: INVITATIONS/Delete/5
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int? id)
    {
        var invitation = await _context.Invitations.FindAsync(id);
        if (invitation != null)
        {
            _context.Invitations.Remove(invitation);
        }

        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    private bool InvitationExists(int id)
    {
        return _context.Invitations.Any(e => e.Id == id);
    }
}
