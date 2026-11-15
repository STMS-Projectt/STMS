using System.ComponentModel.DataAnnotations;

namespace SourcingTenderSystem.ViewModels;

public sealed class ContactInput
{
    public static IReadOnlyList<string> Topics { get; } = ["Becoming a supplier", "Buying wholesale", "Partnership", "Other"];
    [Required, StringLength(100)] public string Name { get; set; } = "";
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
    [Required, StringLength(40)] public string Topic { get; set; } = Topics[0];
    [Required, StringLength(4000)] public string Message { get; set; } = "";
    public bool Consent { get; set; }
}

public sealed class ContactReplyInput
{
    [Required, StringLength(4000)] public string Reply { get; set; } = "";
}

public sealed record ContactReceipt(string Topic, DateTime ReceivedAtUtc, string? Reply, DateTime? RepliedAtUtc);
public sealed record ContactInbox(IReadOnlyList<Models.ContactMessage> Messages, int Page, bool HasNext);
