using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace SourcingTenderSystem.Models;

[Table("AuditLogs")]
public class AuditLog
{
    public int Id { get; set; }
    public int? EmployeeProfileId { get; set; }
    public int? SupplierProfileId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public string? Details { get; set; }
    public DateTime CreatedAt { get; set; }

    public EmployeeProfile? EmployeeProfile { get; set; }
    public SupplierProfile? SupplierProfile { get; set; }
}
