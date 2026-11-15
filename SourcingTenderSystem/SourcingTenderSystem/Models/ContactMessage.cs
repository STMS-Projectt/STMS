using System.ComponentModel.DataAnnotations;

namespace SourcingTenderSystem.Models;

public sealed class ContactMessage
{
    public int Id { get; set; }
    public Guid Receipt { get; set; }
    [MaxLength(100)] public string Name { get; set; } = "";
    [MaxLength(254)] public string Email { get; set; } = "";
    [MaxLength(40)] public string Topic { get; set; } = "";
    [MaxLength(4000)] public string Message { get; set; } = "";
    public DateTime ReceivedAtUtc { get; set; }
    [MaxLength(4000)] public string? Reply { get; set; }
    public DateTime? RepliedAtUtc { get; set; }
    [MaxLength(450)] public string? RepliedByUserId { get; set; }
}
