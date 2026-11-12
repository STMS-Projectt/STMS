namespace SourcingTenderSystem.Services;

public sealed class SupplierFileStore(IWebHostEnvironment environment)
{
    public const int MaxBytes = 10 * 1024 * 1024;
    public static IReadOnlyList<string> DocumentTypes { get; } = Array.AsReadOnly(new[]
    {
        "Commercial registration", "Tax certificate", "Business license", "Other"
    });

    private string Root => Path.Combine(environment.ContentRootPath, "App_Data", "SupplierDocuments");

    public async Task<string?> SaveAsync(IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length is < 5 or > MaxBytes ||
            !string.Equals(Path.GetExtension(file.FileName), ".pdf", StringComparison.OrdinalIgnoreCase))
            return null;

        await using var input = file.OpenReadStream();
        var signature = new byte[5];
        await input.ReadExactlyAsync(signature, cancellationToken);
        if (!signature.AsSpan().SequenceEqual("%PDF-"u8)) return null;

        Directory.CreateDirectory(Root);
        var key = Guid.NewGuid().ToString("N");
        try
        {
            await using var output = new FileStream(Path.Combine(Root, key), FileMode.CreateNew);
            await output.WriteAsync(signature, cancellationToken);
            await input.CopyToAsync(output, cancellationToken);
        }
        catch
        {
            Delete(key);
            throw;
        }
        return key;
    }

    public void Delete(string key) => File.Delete(Path.Combine(Root, key));

    public FileStream? Open(string key)
    {
        if (!Guid.TryParseExact(key, "N", out _)) return null;
        try { return File.OpenRead(Path.Combine(Root, key)); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }
}
