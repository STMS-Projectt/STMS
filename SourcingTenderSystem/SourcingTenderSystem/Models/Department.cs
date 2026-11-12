using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace SourcingTenderSystem.Models;

[Table("Departments")]
public class Department
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }

    public ICollection<EmployeeProfile> EmployeeProfiles { get; set; } = new List<EmployeeProfile>();
    public ICollection<PurchaseRequisition> PurchaseRequisitions { get; set; } = new List<PurchaseRequisition>();
}
