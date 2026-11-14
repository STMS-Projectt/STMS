using Microsoft.EntityFrameworkCore;
using SourcingTenderSystem.Data;
using SourcingTenderSystem.Models;

namespace SourcingTenderSystem.Services;

public static class TenderRules
{
    public const string AwaitingReturn = "Cancelled - Awaiting Manager Handoff";
    public const string ReadyToRetender = "Cancelled - Ready to Retender";

    public static bool CanCreate(PurchaseRequisition request) =>
        request.Tenders.All(t => t.Status == "Cancelled") &&
        (request.Status == "Approved" || request.Status == "Converted to Tender" &&
            request.Tenders.OrderByDescending(t => t.Id).FirstOrDefault()?.EvaluationReport?.Status == ReadyToRetender);
    public static int Minimum(Tender tender) => tender.Counter >= 2 ? 2 : 3;
    public static IQueryable<Bid> Offers(ApplicationDbContext db, int id) =>
        db.Bids.Where(b => b.TenderId == id && b.SubmittedAt != null &&
            (b.Status == "Submitted" || b.Status == "Compliant" || b.Status == "Non-Compliant"));
}

public sealed class TenderClock(IServiceScopeFactory scopes, ILogger<TenderClock> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var now = DateTime.UtcNow;
                await db.Tenders.Where(t => (t.Status == "Published" || t.Status == "Open") && t.CloseDate <= now)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, "Closed"), stoppingToken);
                await db.Tenders.Where(t => t.Status == "Published" && t.OpenDate <= now && t.CloseDate > now)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, "Open"), stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            { logger.LogWarning(ex, "Could not advance scheduled tender statuses."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
