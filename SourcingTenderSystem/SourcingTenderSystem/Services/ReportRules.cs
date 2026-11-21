using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SourcingTenderSystem.Data;
using SourcingTenderSystem.Models;

namespace SourcingTenderSystem.Services;

// Shared rules for the reports and the analytics dashboard, so both always show the same numbers.
public static class ReportRules
{
    // A tender is "delayed" when it waits more than this many days at one evaluation stage.
    public const int EvaluationDelayDays = 1;

    // Supplier documents that expire within this many days are flagged on the reports.
    public const int DocumentWarningDays = 30;

    public static readonly string[] ApprovedRequestStatuses = new string[] { "Approved", "Converted to Tender" };
    public static readonly string[] PendingRequestStatuses = new string[] { "Submitted", "Under Review" };

    public class DelayedTender
    {
        public int TenderId { get; set; }
        public string Title { get; set; } = "";
        public string Stage { get; set; } = "";
        public DateTime Since { get; set; }
        public double Days { get; set; }
    }

    // Tenders that wait more than EvaluationDelayDays at their current stage.
    public static async Task<List<DelayedTender>> DelayedEvaluationsAsync(ApplicationDbContext db, DateTime now)
    {
        var tenders = await db.Tenders
            .Include(t => t.EvaluationReport)
            .ThenInclude(r => r!.TenderApprovals)
            .Where(t => t.Status == "Closed" || t.Status == "Under Evaluation" ||
                        t.Status == "Awaiting Manager Review" || t.Status == "Awaiting Committee Decision")
            .ToListAsync();

        var delayed = new List<DelayedTender>();
        foreach (var tender in tenders)
        {
            DateTime since;
            string stage;
            if (tender.EvaluationReport == null)
            {
                // No evaluation report yet: waiting since the bidding closed.
                since = tender.CloseDate;
                if (tender.Status == "Closed")
                    stage = "Compliance check";
                else
                    stage = "Committee evaluation";
            }
            else
            {
                // The report waits for an approval: waiting since it was prepared or since the last decision on it.
                since = tender.EvaluationReport.CreatedAt;
                foreach (var approval in tender.EvaluationReport.TenderApprovals)
                {
                    if (approval.DecidedAt > since) since = approval.DecidedAt;
                }
                stage = tender.EvaluationReport.Status;
            }

            var days = (now - since).TotalDays;
            if (days > EvaluationDelayDays)
            {
                delayed.Add(new DelayedTender { TenderId = tender.Id, Title = tender.Title, Stage = stage, Since = since, Days = days });
            }
        }
        return delayed.OrderByDescending(d => d.Days).ToList();
    }

    // Time from "Submitted" to "Approved" for each purchase request, read from the audit log.
    // Key = request id. Requests that were never approved are not in the result.
    public static async Task<Dictionary<int, TimeSpan>> ApprovalDurationsAsync(ApplicationDbContext db)
    {
        var logs = await db.AuditLogs
            .Where(a => a.EntityName == "PurchaseRequisition" && (a.Action == "Submitted" || a.Action == "Approved"))
            .OrderBy(a => a.CreatedAt)
            .ToListAsync();

        var lastSubmitted = new Dictionary<int, DateTime>();
        var durations = new Dictionary<int, TimeSpan>();
        foreach (var log in logs)
        {
            if (log.Action == "Submitted")
            {
                // A returned request can be submitted again: keep the latest submission.
                lastSubmitted[log.EntityId] = log.CreatedAt;
            }
            else if (!durations.ContainsKey(log.EntityId) && lastSubmitted.ContainsKey(log.EntityId))
            {
                durations[log.EntityId] = log.CreatedAt - lastSubmitted[log.EntityId];
            }
        }
        return durations;
    }

    // Number of different suppliers invited to a tender (the same supplier can be invited again in a new round).
    public static int InvitedSuppliers(Tender tender)
    {
        var supplierIds = new List<int>();
        foreach (var invitation in tender.Invitations)
        {
            if (!supplierIds.Contains(invitation.SupplierProfileId)) supplierIds.Add(invitation.SupplierProfileId);
        }
        return supplierIds.Count;
    }

    public static int AcceptedSuppliers(Tender tender)
    {
        var supplierIds = new List<int>();
        foreach (var invitation in tender.Invitations)
        {
            if (invitation.Status == "Accepted" && !supplierIds.Contains(invitation.SupplierProfileId))
                supplierIds.Add(invitation.SupplierProfileId);
        }
        return supplierIds.Count;
    }

    // Number of different suppliers who submitted a bid (drafts and withdrawn bids do not count).
    public static int BiddingSuppliers(Tender tender)
    {
        var supplierIds = new List<int>();
        foreach (var bid in tender.Bids)
        {
            if (bid.SubmittedAt != null && bid.Status != "Withdrawn" && !supplierIds.Contains(bid.SupplierProfileId))
                supplierIds.Add(bid.SupplierProfileId);
        }
        return supplierIds.Count;
    }

    public static TimeSpan? Average(List<TimeSpan> spans)
    {
        if (spans.Count == 0) return null;
        long totalTicks = 0;
        foreach (var span in spans)
        {
            totalTicks += span.Ticks;
        }
        return TimeSpan.FromTicks(totalTicks / spans.Count);
    }

    public static string Percent(int part, int whole)
    {
        if (whole == 0) return "—";
        double percent = 100.0 * part / whole;
        return percent.ToString("0.#", CultureInfo.InvariantCulture) + "%";
    }

    public static string Duration(TimeSpan? span, UiText text)
    {
        if (span == null) return "—";
        var value = span.Value;
        if (value.TotalHours < 24)
            return text.F("{0} hours", value.TotalHours.ToString("0.#", CultureInfo.InvariantCulture));
        return text.F("{0} days", value.TotalDays.ToString("0.#", CultureInfo.InvariantCulture));
    }

    // How one table cell is written on the page and in the PDF.
    public static string Cell(object? value, CultureInfo culture)
    {
        if (value == null) return "—";
        if (value is int) return ((int)value).ToString("N0", culture);
        if (value is decimal) return ((decimal)value).ToString("N2", culture);
        if (value is double) return ((double)value).ToString("0.#", culture);
        if (value is DateTime) return ((DateTime)value).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return value.ToString() ?? "";
    }
}
