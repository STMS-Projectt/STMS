using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SourcingTenderSystem.Data;
using SourcingTenderSystem.Models;

namespace SourcingTenderSystem.Services;

public static class TenderRules
{
    public const string AwaitingReturn = "Cancelled - Awaiting Manager Handoff";
    public const string ReadyToRetender = "Cancelled - Ready to Retender";
    public const int MaxRounds = 3;
    public const int NormalMinimum = 3;

    public static bool CanCreate(PurchaseRequisition request) =>
        request.Tenders.All(t => t.Status == "Cancelled") &&
        (request.Status == "Approved" || request.Status == "Converted to Tender" &&
            request.Tenders.OrderByDescending(t => t.Id).FirstOrDefault()?.EvaluationReport?.Status == ReadyToRetender);

    public static int Minimum(Tender tender) => tender.Counter == MaxRounds ? 1 : NormalMinimum;
    public static bool CanEvaluate(Tender tender, int count) => tender.Status == "Closed" &&
        tender.Counter is >= 1 and <= MaxRounds && count >= Minimum(tender);
    public static bool CanReopen(Tender tender, int count) => tender.Status == "Closed" &&
        tender.Counter >= 1 && tender.Counter < MaxRounds && count < NormalMinimum;
    public static bool IsOpen(Tender tender, DateTime now) =>
        tender.Status == "Open" && tender.OpenDate <= now && now < tender.CloseDate;
    public static IQueryable<Bid> Offers(ApplicationDbContext db, int id) =>
        db.Bids.Where(b => b.TenderId == id && b.SubmittedAt != null &&
            (b.Status == "Submitted" || b.Status == "Pending Clarification" || b.Status == "Compliant"));
    public static IQueryable<int> EligibleSuppliers(ApplicationDbContext db, int id) =>
        Offers(db, id).Select(b => b.SupplierProfileId).Distinct();
    public static IQueryable<int> PreviouslySubmitted(ApplicationDbContext db, int id) =>
        db.Bids.Where(b => b.TenderId == id && b.SubmittedAt != null).Select(b => b.SupplierProfileId).Distinct();

    // Unattributed legacy attempts lack defensible attribution. Preserve their existing access
    // only while the migrated legacy round is current.
    public static IQueryable<Invitation> CurrentInvitations(ApplicationDbContext db, Tender tender) =>
        db.Invitations.Where(i => i.TenderId == tender.Id &&
            (i.RoundNumber == tender.Counter || (i.RoundNumber == null && db.TenderRounds.Any(r =>
                r.TenderId == tender.Id && r.Number == tender.Counter && r.Legacy))));

    public static DateTime AsUtc(DateTime date) => date.Kind == DateTimeKind.Local
        ? date.ToUniversalTime() : DateTime.SpecifyKind(date, DateTimeKind.Utc);

    // Every lifecycle operation acquires the same transaction-owned lock before loading state.
    public static async Task<IDbContextTransaction> LockAsync(ApplicationDbContext db, int tenderId,
        CancellationToken cancellationToken = default)
    {
        var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var resource = $"STMS:Tender:{tenderId}";
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive',
                    @LockOwner = 'Transaction', @LockTimeout = 15000;
                IF @result < 0 THROW 51012, 'Could not acquire the tender workflow lock.', 1;
                """, cancellationToken);
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    public static TenderRound NewRound(ApplicationDbContext db, Tender tender, int officerId, DateTime now, string? reason = null)
    {
        var round = new TenderRound { TenderId = tender.Id, Number = tender.Counter,
            OpensAt = tender.OpenDate, ClosesAt = tender.CloseDate, OfficerId = officerId,
            CreatedAt = now, ReopenReason = reason };
        db.TenderRounds.Add(round);
        return round;
    }

    public static async Task CloseRoundAsync(ApplicationDbContext db, Tender tender, DateTime now, int? officerId,
        bool early, CancellationToken cancellationToken = default)
    {
        var round = await db.TenderRounds.SingleAsync(r => r.TenderId == tender.Id && r.Number == tender.Counter, cancellationToken);
        tender.Status = "Closed";
        round.ClosedAt = now;
        round.ClosingOutcome = early ? "ClosedEarly" : "DeadlineReached";
        round.EligibleSuppliersAtClose = await EligibleSuppliers(db, tender.Id).CountAsync(cancellationToken);
        Audit(db, tender, officerId, early ? "CloseEarly" : "CloseAtDeadline", now,
            $"{round.ClosingOutcome}; eligible suppliers: {round.EligibleSuppliersAtClose}.");
    }

    public static void Audit(ApplicationDbContext db, Tender tender, int? officerId, string action, DateTime now, string detail) =>
        db.AuditLogs.Add(new AuditLog { EmployeeProfileId = officerId, EntityName = nameof(Tender),
            EntityId = tender.Id, Action = action, CreatedAt = now, Details = $"Round {tender.Counter}: {detail}" });
}

public sealed class TenderClock(IServiceScopeFactory scopes, ILogger<TenderClock> logger) : BackgroundService
{
    public static async Task AdvanceAsync(ApplicationDbContext db, CancellationToken token = default)
    {
        var scanTime = DateTime.UtcNow;
        var ids = await db.Tenders.Where(t => (t.Status == "Published" && t.OpenDate <= scanTime) ||
                ((t.Status == "Open" || t.Status == "Published") && t.CloseDate <= scanTime))
            .Select(t => t.Id).ToListAsync(token);
        foreach (var id in ids)
        {
            await using var tx = await TenderRules.LockAsync(db, id, token);
            var tender = await db.Tenders.SingleOrDefaultAsync(t => t.Id == id, token);
            var now = DateTime.UtcNow;
            if (tender is not null && tender.Status is ("Open" or "Published") && tender.CloseDate <= now)
                await TenderRules.CloseRoundAsync(db, tender, now, null, false, token);
            else if (tender?.Status == "Published" && tender.OpenDate <= now)
            {
                tender.Status = "Open";
                TenderRules.Audit(db, tender, null, "OpenScheduled", now, "Bidding opened.");
            }
            await db.SaveChangesAsync(token);
            await tx.CommitAsync(token);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await AdvanceAsync(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(), stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            { logger.LogWarning(ex, "Could not advance scheduled tender statuses."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
