namespace SourcingTenderSystem.Services;

// Checks and saves the files of a bid (PDF documents and item sample images) in App_Data/BidDocuments.
// Files are saved with a GUID name, never with the user's file name.
public static class BidFiles
{
    public const long MaxBytes = 10 * 1024 * 1024; // 10 MB

    public static string Folder(IWebHostEnvironment environment) =>
        Path.Combine(environment.ContentRootPath, "App_Data", "BidDocuments");

    // Path.GetFileName keeps only the name, so a saved path can never point outside the folder.
    public static string FullPath(IWebHostEnvironment environment, string storedName) =>
        Path.Combine(Folder(environment), Path.GetFileName(storedName));

    public static bool IsImage(string? storedName) =>
        Path.GetExtension(storedName ?? "").ToLowerInvariant() is ".jpg" or ".jpeg" or ".png";

    public static string ImageContentType(string storedName) =>
        Path.GetExtension(storedName).ToLowerInvariant() == ".png" ? "image/png" : "image/jpeg";

    // Returns an error message, or null if the file is a real PDF.
    public static async Task<string?> CheckPdfAsync(IFormFile file)
    {
        if (file.Length == 0) return "The file is empty.";
        if (file.Length > MaxBytes) return "The file is too big. The maximum size is 10 MB.";
        if (Path.GetExtension(file.FileName).ToLowerInvariant() != ".pdf") return "Only PDF files are allowed.";
        var head = await ReadHeadAsync(file, 5);
        return System.Text.Encoding.ASCII.GetString(head) == "%PDF-" ? null : "This file is not a real PDF.";
    }

    // Returns an error message, or null if the file is a real JPG/PNG image.
    public static async Task<string?> CheckImageAsync(IFormFile file)
    {
        if (file.Length == 0) return "The file is empty.";
        if (file.Length > MaxBytes) return "The file is too big. The maximum size is 10 MB.";
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension is not (".jpg" or ".jpeg" or ".png")) return "Sample images must be JPG or PNG files.";
        var h = await ReadHeadAsync(file, 4);
        bool jpg = h.Length >= 3 && h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF;
        bool png = h.Length == 4 && h[0] == 0x89 && h[1] == 0x50 && h[2] == 0x4E && h[3] == 0x47;
        return (extension == ".png" ? png : jpg) ? null : "This file is not a real JPG/PNG image.";
    }

    // Saves the file and returns its stored (GUID) name.
    public static async Task<string> SaveAsync(IWebHostEnvironment environment, IFormFile file)
    {
        var extension = IsImage(file.FileName) ? Path.GetExtension(file.FileName).ToLowerInvariant() : ".pdf";
        var storedName = Guid.NewGuid().ToString("N") + extension;
        Directory.CreateDirectory(Folder(environment));
        await using var stream = new FileStream(FullPath(environment, storedName), FileMode.CreateNew);
        await file.CopyToAsync(stream);
        return storedName;
    }

    public static void Delete(IWebHostEnvironment environment, string? storedName)
    {
        if (string.IsNullOrEmpty(storedName)) return;
        var path = FullPath(environment, storedName);
        if (File.Exists(path)) File.Delete(path);
    }

    private static async Task<byte[]> ReadHeadAsync(IFormFile file, int count)
    {
        var buffer = new byte[count];
        await using var stream = file.OpenReadStream();
        var read = await stream.ReadAsync(buffer.AsMemory(0, count));
        return buffer[..read];
    }
}
