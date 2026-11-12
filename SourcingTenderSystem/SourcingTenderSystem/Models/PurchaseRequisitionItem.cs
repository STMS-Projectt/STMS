using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace SourcingTenderSystem.Models;

[Table("PurchaseRequisitionItems")]
public class PurchaseRequisitionItem
{
    public int Id { get; set; }
    public int PurchaseRequisitionId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string Specifications { get; set; } = string.Empty;

    public PurchaseRequisition PurchaseRequisition { get; set; } = null!;
    public ICollection<BidItem> BidItems { get; set; } = new List<BidItem>();
}
