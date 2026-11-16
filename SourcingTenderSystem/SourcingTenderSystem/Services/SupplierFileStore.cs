namespace SourcingTenderSystem.Services;

public sealed class SupplierFileStore(IWebHostEnvironment environment)
{
    public const int MaxBytes = 10 * 1024 * 1024;
    public static IReadOnlyList<string> DocumentTypes { get; } = Array.AsReadOnly(new[]
    {
        "Commercial registration", "Tax certificate", "Business license", "Other"
    });

    private string Root => Path.Combine(environment.ContentRootPath, "App_Data", "SupplierDocuments");
    private string PendingRoot => Path.Combine(environment.ContentRootPath, "App_Data", "ClarificationAttachments");
    private string BidRoot => Path.Combine(environment.ContentRootPath, "App_Data", "BidDocuments");

    public static string SafeName(string original)
    {
        var name = new string(Path.GetFileName(original.Replace('\\', '/')).Where(c => !char.IsControl(c)).ToArray());
        return name.Length > 255 ? name[..251] + ".pdf" : name;
    }

    public async Task<string?> SaveAsync(IFormFile file, CancellationToken cancellationToken, bool pending = false)
    {
        if (file.Length is < 5 or > MaxBytes ||
            !string.Equals(Path.GetExtension(file.FileName), ".pdf", StringComparison.OrdinalIgnoreCase))
            return null;

        await using var input = file.OpenReadStream();
        var signature = new byte[5];
        await input.ReadExactlyAsync(signature, cancellationToken);
        if (!signature.AsSpan().SequenceEqual("%PDF-"u8)) return null;

        var folder = pending ? PendingRoot : Root;
        Directory.CreateDirectory(folder);
        var key = Guid.NewGuid().ToString("N");
        try
        {
            await using var output = new FileStream(Path.Combine(folder, key), FileMode.CreateNew);
            await output.WriteAsync(signature, cancellationToken);
            await input.CopyToAsync(output, cancellationToken);
        }
        catch
        {
            Delete(key, pending);
            throw;
        }
        return key;
    }

    public void Delete(string key, bool pending = false) => File.Delete(Path.Combine(pending ? PendingRoot : Root, key));

    public FileStream? Open(string key, bool pending = false)
    {
        if (!Guid.TryParseExact(key, "N", out _)) return null;
        try { return File.OpenRead(Path.Combine(pending ? PendingRoot : Root, key)); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    public async Task<string?> CopyAttachmentAsync(string sourceKey, bool forBid, CancellationToken cancellationToken)
    {
        await using var input = Open(sourceKey, pending: true);
        if (input is null) return null;
        var folder = forBid ? BidRoot : Root;
        var key = Guid.NewGuid().ToString("N") + (forBid ? ".pdf" : "");
        Directory.CreateDirectory(folder);
        try
        {
            await using var output = new FileStream(Path.Combine(folder, key), FileMode.CreateNew);
            await input.CopyToAsync(output, cancellationToken);
            return key;
        }
        catch
        {
            DeleteCopy(key, forBid);
            throw;
        }
    }

    public void DeleteCopy(string key, bool forBid) => File.Delete(Path.Combine(forBid ? BidRoot : Root, key));
}
