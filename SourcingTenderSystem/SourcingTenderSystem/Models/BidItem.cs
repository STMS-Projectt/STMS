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

    // Photo of the sample for this item (JPG/PNG in App_Data/BidDocuments)
    public string? SampleImagePath { get; set; }
    public string? SampleImageName { get; set; }

    public Bid Bid { get; set; } = null!;
    public PurchaseRequisitionItem PurchaseRequisitionItem { get; set; } = null!;
}
