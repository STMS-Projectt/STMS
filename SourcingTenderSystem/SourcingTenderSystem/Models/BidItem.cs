using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace SourcingTenderSystem.Models;

[Table("BidItems")]
public class BidItem
{
    public int Id { get; set; }
    public int BidId { get; set; }
    public int PurchaseRequisitionItemId { get; set; }
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public string? Notes { get; set; }

    public Bid Bid { get; set; } = null!;
    public PurchaseRequisitionItem PurchaseRequisitionItem { get; set; } = null!;
}
