using System.ComponentModel.DataAnnotations.Schema;

namespace SourcingTenderSystem.Models;

// One photo of the sample a supplier offers for a bid item.
// The file lives in App_Data/BidDocuments under a GUID name (FilePath); FileName is the original name, for display only.
[Table("BidItemImages")]
public class BidItemImage
{
    public int Id { get; set; }
    public int BidItemId { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; }

    public BidItem BidItem { get; set; } = null!;
}
