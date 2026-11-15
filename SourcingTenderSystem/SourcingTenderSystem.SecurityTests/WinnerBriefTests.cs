using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using PdfSharp.Fonts;
using PdfSharp.Pdf.IO;
using SourcingTenderSystem.Services;
using Xunit;

public sealed class WinnerBriefTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("winner-brief-").FullName;

    static WinnerBriefTests()
    {
        // PDFsharp needs a font on Linux CI; the app does the same through DocumentSecurity:FontPath.
        const string font = "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf";
        if (GlobalFontSettings.FontResolver is null && File.Exists(font))
            ProtectedPdf.ConfigureFonts(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["DocumentSecurity:FontPath"] = font }).Build());
    }

    public void Dispose() => Directory.Delete(root, true);

    [Fact]
    public async Task Falls_back_to_recorded_facts_when_ollama_is_down_and_keeps_the_committee_winner()
    {
        var ollama = new FakeOllama(_ => throw new HttpRequestException("refused"));
        var brief = await Service(ollama).GetOrCreateAsync(Input());

        Assert.False(brief.AiGenerated);
        Assert.Contains("Ollama", brief.Notice);
        Assert.Equal("Acme Supplies", brief.Winner.Supplier);
        Assert.Contains(brief.Output.WhyWon, r => r.Contains("86.5"));
        Assert.Contains(brief.Output.Differentiators, r => r.Contains("Lowest total price"));
        AssertPdf(brief);

        // A fallback is not cached: the next request asks Ollama again.
        await Service(ollama).GetOrCreateAsync(Input());
        Assert.Equal(2, ollama.Calls);
    }

    [Fact]
    public async Task Uses_the_ollama_narrative_and_caches_it_per_bid_set()
    {
        var answer = JsonSerializer.Serialize(new
        {
            supplierDescription = "Acme Supplies is an IT hardware supplier based in Amman.",
            offerSummary = "Offers 10 laptops for 9,000 JOD with delivery in 14 days.",
            whyWon = new[] { "Highest committee score of 86.5." },
            differentiators = new[] { "1,000 JOD cheaper than Beta Trading." },
            pointsToVerify = new[] { "Confirm the warranty period." }
        });
        var ollama = new FakeOllama(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { message = new { content = answer } }), Encoding.UTF8, "application/json")
        });
        var brief = await Service(ollama).GetOrCreateAsync(Input());

        Assert.True(brief.AiGenerated);
        Assert.Equal("Acme Supplies is an IT hardware supplier based in Amman.", brief.Output.SupplierDescription);
        Assert.Single(brief.Output.PointsToVerify);
        Assert.Contains("\"model\":\"llama3.1\"", ollama.LastBody);
        AssertPdf(brief);

        // Read back from the disk cache by a fresh instance, even after the vote moves the report on.
        var again = await Service(ollama).GetOrCreateAsync(Input() with { ReportStatus = "Awarded" });
        Assert.Equal(brief.Output.SupplierDescription, again.Output.SupplierDescription);
        Assert.Equal(1, ollama.Calls);
    }

    private static void AssertPdf(WinnerBrief brief)
    {
        if (GlobalFontSettings.FontResolver is null) return;
        using var document = WinnerBriefPdf.Build(brief);
        using var stream = new MemoryStream();
        document.Save(stream, false);
        stream.Position = 0;
        using var reopened = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        Assert.InRange(reopened.PageCount, 1, 3);
    }

    private WinnerAiBriefing Service(FakeOllama ollama) => new(new Factory(ollama),
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Ollama:Model"] = "llama3.1" }).Build(),
        new Host(root), NullLogger<WinnerAiBriefing>.Instance);

    private static WinnerBriefInput Input() => new(7, "Office laptops", "Ten laptops for the finance team.", "Pending Top Committee",
        "Best technical score and lowest price.", 11,
        new WinnerSupplierProfile("Acme Supplies", "Amman", ["IT hardware"], new DateTime(2024, 3, 1), 2),
        [new AiRequestedItem(1, "Laptop", 10, "pcs", "16 GB RAM, 512 GB SSD")],
        [new AiCriterion("Quality", "Technical", 60, 10), new AiCriterion("Price", "Financial", 40, 10)],
        [
            new WinnerBriefBid(11, "Acme Supplies", 9000, "JOD", null, 14, 90, 52.5m, 34m, 86.5m, 1, 1,
                [new AiBidLine(1, "Laptop", 10, 900, "3-year warranty")], ["Quotation: acme.pdf"]),
            new WinnerBriefBid(12, "Beta Trading", 10000, "JOD", null, 21, 60, 48m, 30m, 78m, 2, 1,
                [new AiBidLine(1, "Laptop", 10, 1000, null)], ["Quotation: beta.pdf"])
        ]);

    private sealed class FakeOllama(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        public int Calls;
        public string LastBody = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return reply(request);
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false) { BaseAddress = new Uri("http://localhost:11434/") };
    }

    private sealed class Host(string root) : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = root;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = root;
        public string EnvironmentName { get; set; } = "Testing";
    }
}
