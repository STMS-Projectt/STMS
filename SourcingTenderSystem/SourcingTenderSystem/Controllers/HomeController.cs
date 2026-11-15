using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using SourcingTenderSystem.Data;
using SourcingTenderSystem.ViewModels;
using Microsoft.AspNetCore.Mvc;
using SourcingTenderSystem.Models;
using System.Diagnostics;

namespace SourcingTenderSystem.Controllers
{
    public class HomeController(ApplicationDbContext db) : Controller
    {
        public IActionResult Index()
        {
            return View(new ContactInput());
        }


        [HttpPost, EnableRateLimiting("public-contact"), RequestSizeLimit(65536)]
        public async Task<IActionResult> Contact(ContactInput input, CancellationToken cancellationToken)
        {
            if (!input.Consent) ModelState.AddModelError(nameof(input.Consent), "Agree to be contacted before sending.");
            if (!ContactInput.Topics.Contains(input.Topic)) ModelState.AddModelError(nameof(input.Topic), "Select a valid topic.");
            if (!ModelState.IsValid)
            {
                Response.StatusCode = StatusCodes.Status400BadRequest;
                return View(nameof(Index), input);
            }

            var message = new ContactMessage
            {
                Receipt = Guid.NewGuid(), Name = input.Name.Trim(), Email = input.Email.Trim(),
                Topic = input.Topic, Message = input.Message.Trim(), ReceivedAtUtc = DateTime.UtcNow
            };
            db.ContactMessages.Add(message);
            await db.SaveChangesAsync(cancellationToken);
            return RedirectToAction(nameof(ContactStatus), new { receipt = message.Receipt });
        }

        [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> ContactStatus(Guid receipt, CancellationToken cancellationToken)
        {
            Response.Headers["Referrer-Policy"] = "no-referrer";
            var message = await db.ContactMessages.AsNoTracking().Where(x => x.Receipt == receipt)
                .Select(x => new ContactReceipt(x.Topic, x.ReceivedAtUtc, x.Reply, x.RepliedAtUtc))
                .SingleOrDefaultAsync(cancellationToken);
            return message is null ? NotFound() : View(message);
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
