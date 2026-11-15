using System.ComponentModel.DataAnnotations;

namespace SourcingTenderSystem.Models;

public class Category
{
    public int Id { get; set; }
    [MaxLength(120)] public string Name { get; set; } = "";
    public ICollection<SupplierProfile> Suppliers { get; set; } = new List<SupplierProfile>();
}
