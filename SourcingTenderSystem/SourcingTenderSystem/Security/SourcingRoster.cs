namespace SourcingTenderSystem.Security;

internal static class SourcingRoster
{
    internal static IReadOnlyList<(string Role, string Seat)> Seats { get; } =
    [
        (Roles.SourcingManager, "Sourcing Manager"),
        (Roles.TechnicalCommittee, "Technical Representative"),
        (Roles.FinancialCommittee, "Financial Representative")
    ];

    internal static IReadOnlyList<(string Seat, string Email, string Name)> FixedMembers { get; } =
    [
        ("Sourcing Manager", "tariq.salameh@demo.sourcing.jo", "Tariq Salameh"),
        ("Financial Representative", "rana.abuzaid@demo.sourcing.jo", "Rana Abuzaid"),
        ("Technical Representative", "yousef.qasem@demo.sourcing.jo", "Yousef Qasem")
    ];
}
