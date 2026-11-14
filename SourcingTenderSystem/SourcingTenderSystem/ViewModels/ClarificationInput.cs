using System.ComponentModel.DataAnnotations;
using SourcingTenderSystem.Models;
using SourcingTenderSystem.Services;

namespace SourcingTenderSystem.ViewModels;

public sealed class ClarificationInput
{
    [Required, RegularExpression("^(request|tender|bid)$")] public string Kind { get; set; } = "";
    [Range(1, int.MaxValue)] public int RecordId { get; set; }
    [Required, StringLength(32)] public string Recipient { get; set; } = "";
    [Required, StringLength(150)] public string Subject { get; set; } = "";
    [Required, StringLength(2000)] public string Description { get; set; } = "";
}
public sealed record ClarificationThread(ClarificationRequest Record, bool CanAnswer, bool CanResolve);
public sealed record ClarificationInbox(IReadOnlyList<ClarificationRequest> Rows, string? Kind, int? RecordId,
    ClarificationRoute? Route, IReadOnlyList<ClarificationTarget> Requests);
