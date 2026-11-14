using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf.Signatures;

namespace SourcingTenderSystem.Services;

public sealed class ProtectedPdf(IConfiguration config, UserManager<IdentityUser> users,
    SignInManager<IdentityUser> signIn, ILogger<ProtectedPdf> logger)
{
    public bool SigningEnabled => !string.IsNullOrWhiteSpace(config["DocumentSecurity:CertificatePath"]);

    public static void ConfigureFonts(IConfiguration config)
    {
        var custom = config["DocumentSecurity:FontPath"];
        if (!string.IsNullOrWhiteSpace(custom)) GlobalFontSettings.FontResolver = new WatermarkFont(custom);
        else if (OperatingSystem.IsWindows()) GlobalFontSettings.UseWindowsFontsUnderWindows = true;
    }

    private sealed class WatermarkFont(string path) : IFontResolver
    {
        public byte[] GetFont(string faceName) => System.IO.File.ReadAllBytes(path);
        public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic) => new("watermark");
    }

    public async Task<bool> Confirm(ClaimsPrincipal principal, string? password)
    {
        var user = await users.GetUserAsync(principal);
        return user is not null && !string.IsNullOrWhiteSpace(password) &&
            (await signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true)).Succeeded;
    }

    public async Task<IActionResult> Serve(Controller controller, Stream source, string filename, bool preview,
        bool sign = false, string? password = null)
    {
        await using var input = source;
        if (sign && (!SigningEnabled || !await Confirm(controller.User, password)))
            return new ObjectResult(SigningEnabled ? "Could not confirm your password." : "Document signing is not configured.")
                { StatusCode = 400 };
        try
        {
            using var document = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
            AddWatermark(document, controller.User, DateTime.UtcNow);
            var output = await Render(document, sign ? "Document copy signed for " + Identifier(controller.User) : null);
            controller.Response.Headers.CacheControl = "private, no-store";
            controller.Response.Headers["X-Content-Type-Options"] = "nosniff";
            return preview && !sign ? new FileContentResult(output, "application/pdf") :
                new FileContentResult(output, "application/pdf") { FileDownloadName = sign ? "signed-" + filename : filename };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not produce a protected PDF copy.");
            return new ObjectResult("Could not prepare this protected PDF. The original was not changed.") { StatusCode = 422 };
        }
    }

    public async Task<IActionResult> Decision(Controller controller, int id, IEnumerable<(string Label, string Value)> fields)
    {
        try
        {
            using var document = new PdfDocument();
            var page = document.AddPage();
            var gfx = XGraphics.FromPdfPage(page);
            try
            {
                var navy = new XSolidBrush(XColor.FromArgb(19, 46, 67));
                gfx.DrawRectangle(navy, 0, 0, page.Width.Point, 125);
                gfx.DrawString("SOURCING DESK", new XFont("Arial", 12), XBrushes.White, 35, 40);
                gfx.DrawString("Official tender decision", new XFont("Arial", 25), XBrushes.White, 35, 75);
                gfx.DrawString($"Decision #{id}", new XFont("Arial", 12), XBrushes.White, 35, 100);
                var y = 161d;
                var font = new XFont("Arial", 11);
                void Line(string text, XFont style)
                {
                    if (y > page.Height.Point - 60)
                    {
                        gfx.Dispose();
                        page = document.AddPage();
                        gfx = XGraphics.FromPdfPage(page);
                        y = 48;
                    }
                    gfx.DrawString(text, style, navy, 35, y);
                    y += 16;
                }
                foreach (var (label, value) in fields)
                {
                    Line(label, new XFont("Arial", 11, XFontStyleEx.Bold));
                    foreach (var line in Wrap(gfx, font, value, page.Width.Point - 70).ToArray()) Line(line, font);
                    y += 16;
                }
            }
            finally { gfx.Dispose(); }
            AddWatermark(document, controller.User, DateTime.UtcNow);
            var bytes = await Render(document, $"Certified copy of recorded tender decision #{id}");
            controller.Response.Headers.CacheControl = "private, no-store";
            return new FileContentResult(bytes, "application/pdf") { FileDownloadName = $"signed-decision-{id}.pdf" };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not certify a tender decision.");
            return new ObjectResult("Could not prepare the signed decision PDF.") { StatusCode = 422 };
        }
    }

    private static IEnumerable<string> Wrap(XGraphics gfx, XFont font, string value, double width)
    {
        foreach (var paragraph in value.Replace("\r", "").Split('\n'))
        {
            var line = "";
            foreach (var c in paragraph.Where(c => !char.IsControl(c)))
            {
                if (line.Length > 0 && gfx.MeasureString(line + c, font).Width > width)
                {
                    yield return line;
                    line = "";
                }
                line += c;
            }
            yield return line;
        }
    }
    public void AddWatermark(PdfDocument document, ClaimsPrincipal user, DateTime now)
    {
        var label = Clean(config["DocumentSecurity:WatermarkLabel"] ?? "CONFIDENTIAL | SOURCING DESK", 65);
        var identity = Clean(Identifier(user), 130);
        foreach (var page in document.Pages)
        {
            var width = page.Width.Point;
            var height = page.Height.Point;
            using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            var state = gfx.Save();
            gfx.TranslateTransform(width / 2, height / 2);
            gfx.RotateTransform(-32);
            var ink = new XSolidBrush(XColor.FromArgb(55, 24, 63, 83));
            gfx.DrawString(label, Fit(gfx, label, width * .8, 30), ink, new XRect(-width / 2, -33, width, 32), XStringFormats.Center);
            gfx.DrawString(identity, Fit(gfx, identity, width * .8, 14), ink, new XRect(-width / 2, 1, width, 21), XStringFormats.Center);
            gfx.Restore(state);
            var trace = $"Accessed by {identity} | {now:yyyy-MM-dd HH:mm} UTC";
            gfx.DrawString(trace, Fit(gfx, trace, width - 28, 9), new XSolidBrush(XColor.FromArgb(175, 24, 63, 83)),
                new XRect(14, height - 22, width - 28, 14), XStringFormats.Center);
        }
    }

    public async Task<byte[]> Render(PdfDocument document, string? signatureReason)
    {
        using var output = new MemoryStream();
        if (signatureReason is null) await document.SaveAsync(output, closeStream: false);
        else
        {
            using var cert = X509CertificateLoader.LoadPkcs12FromFile(config["DocumentSecurity:CertificatePath"]!,
                config["DocumentSecurity:CertificatePassword"], X509KeyStorageFlags.EphemeralKeySet);
            if (!cert.HasPrivateKey || DateTime.UtcNow < cert.NotBefore.ToUniversalTime() || DateTime.UtcNow > cert.NotAfter.ToUniversalTime())
                throw new InvalidOperationException("The signing certificate is unavailable or has expired.");
            DigitalSignatureHandler.ForDocument(document, new PdfSharpDefaultSigner(cert, PdfMessageDigestType.SHA256),
                new DigitalSignatureOptions { Reason = Clean(signatureReason, 200), AppName = "Sourcing Desk" });
            await document.SaveAsync(output, closeStream: false);
        }
        return output.ToArray();
    }

    public static XFont Fit(XGraphics gfx, string value, double width, double size)
    {
        var font = new XFont("Arial", size);
        var measured = gfx.MeasureString(value, font).Width;
        return measured > width && width > 0 ? new XFont("Arial", size * width / measured) : font;
    }

    private static string Identifier(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.Email)
        ?? user.Identity?.Name ?? user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "Authenticated user";
    private static string Clean(string value, int max) => new(value.Where(c => !char.IsControl(c)).Take(max).ToArray());
}
