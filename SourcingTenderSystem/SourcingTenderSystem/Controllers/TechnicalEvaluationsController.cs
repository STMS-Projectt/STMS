using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SourcingTenderSystem.Data;
using SourcingTenderSystem.Models;
namespace SourcingTenderSystem.Controllers
{
    [Authorize]
    public class TechnicalEvaluationsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public TechnicalEvaluationsController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var evaluations =await _context.TechnicalEvaluations
           .Include(e => e.Bid)
           .Include(e => e.EvaluationCriterion)
           .Include(e => e.EvaluatorEmployeeProfile)
           .ToListAsync();

            return View(evaluations);
        }
        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var evaluation = await _context.TechnicalEvaluations
                .Include(e => e.Bid)
                .Include(e => e.EvaluationCriterion)
                .Include(e => e.EvaluatorEmployeeProfile)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (evaluation == null)
            {
                return NotFound();
            }

            return View(evaluation);
        }
        [HttpGet]
        public async Task<IActionResult> Create(int? bidId)
        {
            ViewBag.Bids = await _context.Bids
                .Where(b => b.Status == "Compliant")
                .Include(b => b.SupplierProfile)
                .ToListAsync();

            if (bidId.HasValue)
            {
                var bid = await _context.Bids
                    .Include(b => b.SupplierProfile)
                    .Include(b => b.Tender)
                    .Include(b => b.BidItems)
                        .ThenInclude(i => i.PurchaseRequisitionItem)
                    .FirstOrDefaultAsync(b => b.Id == bidId.Value &&
                                              b.Status == "Compliant");

                if (bid == null) return NotFound();

                ViewBag.SelectedBid = bid;

                ViewBag.Criteria = await _context.EvaluationCriteria
                    .Where(c => c.TenderId == bid.TenderId &&
                                c.Type == "Technical")
                    .ToListAsync();
            }

            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Create(int bidId)
        {
            var employee = await _context.EmployeeProfiles
                .FirstOrDefaultAsync(e => e.User.UserName == User.Identity!.Name);

            if (employee == null) return Forbid();

            var bid = await _context.Bids.FindAsync(bidId);

            if (bid == null || bid.Status != "Compliant")
                return BadRequest("Invalid bid.");

            var criteria = await _context.EvaluationCriteria
                .Where(c => c.TenderId == bid.TenderId &&
                            c.Type == "Technical")
                .ToListAsync();

            foreach (var criterion in criteria)
            {
                var notes = Request.Form[$"notes_{criterion.Id}"].ToString().Trim();

                if (string.IsNullOrWhiteSpace(notes))
                    continue;

                var alreadyEvaluated = await _context.TechnicalEvaluations.AnyAsync(e =>
                    e.BidId == bidId &&
                    e.EvaluationCriteriaId == criterion.Id &&
                    e.EvaluatorEmployeeProfileId == employee.Id);

                if (alreadyEvaluated)
                    continue;

                _context.TechnicalEvaluations.Add(new TechnicalEvaluation
                {
                    BidId = bidId,
                    EvaluationCriteriaId = criterion.Id,
                    EvaluatorEmployeeProfileId = employee.Id,
                    Notes = notes,
                    Score = 0,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var evaluation = await _context.TechnicalEvaluations
                .Include(e => e.EvaluationCriterion)
                .Include(e => e.Bid)
                    .ThenInclude(b => b.SupplierProfile)
                .Include(e => e.Bid)
                    .ThenInclude(b => b.Tender)
                .Include(e => e.Bid)
                    .ThenInclude(b => b.BidItems)
                        .ThenInclude(i => i.PurchaseRequisitionItem)
                .FirstOrDefaultAsync(e => e.Id == id);

            if (evaluation == null) return NotFound();

            return View(evaluation);
        }
        [HttpPost]
        public async Task<IActionResult> Edit(int id, decimal score, string? notes)
        {
            var evaluation = await _context.TechnicalEvaluations
                .Include(e => e.EvaluationCriterion)
                .FirstOrDefaultAsync(e => e.Id == id);

            if (evaluation == null) return NotFound();

            if (score < 0 || score > evaluation.EvaluationCriterion.MaxScore)
                return BadRequest("Invalid score.");

            evaluation.Score = score;
            evaluation.Notes = notes;

            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }
    }
}
