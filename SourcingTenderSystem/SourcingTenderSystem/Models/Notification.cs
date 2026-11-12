using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace SourcingTenderSystem.Models;

[Table("Notifications")]
public class Notification
{
    public int Id { get; set; }
    public int? EmployeeProfileId { get; set; }
    public int? SupplierProfileId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Link { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }

    public EmployeeProfile? EmployeeProfile { get; set; }
    public SupplierProfile? SupplierProfile { get; set; }
}
