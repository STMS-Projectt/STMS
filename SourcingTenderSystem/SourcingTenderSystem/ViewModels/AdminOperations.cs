using System.ComponentModel.DataAnnotations;

namespace SourcingTenderSystem.ViewModels;

public sealed class AdminPasswordInput
{
    [Required, DataType(DataType.Password)] public string Password { get; set; } = "";
    [Required, DataType(DataType.Password), Compare(nameof(Password))]
    public string ConfirmPassword { get; set; } = "";
}

public sealed class AdminRecord
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string? Context { get; init; }
    public string? Status { get; init; }
    public DateTime? Date { get; init; }
    public int? TenderId { get; init; }
}
public sealed record AdminRegister(string Title, string Description, string Action, string? Query,
    IReadOnlyList<AdminRecord> Rows, int Page, bool HasNext, string? Status, IReadOnlyList<string> Statuses);
public sealed record AdminTenderView(Models.Tender Tender, IReadOnlyList<AdminRecord> Invitations, IReadOnlyList<AdminRecord> Bids);
