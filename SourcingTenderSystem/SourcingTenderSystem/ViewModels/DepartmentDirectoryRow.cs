namespace SourcingTenderSystem.ViewModels;

public sealed record DepartmentDirectoryRow(int Id, string Name, string? Description, bool IsActive, int EmployeeCount);
