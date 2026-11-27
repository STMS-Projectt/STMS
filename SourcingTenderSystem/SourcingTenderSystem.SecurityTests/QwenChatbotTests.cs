using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SourcingTenderSystem.Services;
using Xunit;

public sealed class QwenChatbotTests
{
    private static AiData Sample() => new(
        [
            new AiTenderRow(7, "Laptops", "Awarded", "IT", "Hardware", new DateTime(2026, 1, 1), new DateTime(2026, 2, 1),
                2, 900m, 1200m, 1050m, "JOD", "Alpha", "Beta", "Alpha", 12),
            new AiTenderRow(8, "Chairs", "Open", "HR", "Furniture", new DateTime(2026, 3, 1), new DateTime(2026, 4, 1),
                0, null, null, null, "JOD", null, null, null, null)
        ],
        [
            new AiBidRow(7, "Laptops", "Beta", 1200m, "JOD", 10, 60, "Unsuccessful"),
            new AiBidRow(7, "Laptops", "Alpha", 900m, "JOD", 14, 90, "Awarded")
        ]);

    [Fact]
    public void ParseAnswer_reads_json_and_drops_thinking_fences_and_bad_shapes()
    {
        const string reply = """
            <think>let me compute</think>
            ```json
            { "answer": "Alpha is the cheapest.",
              "cards": [ { "label": "Lowest", "value": "900.00 JOD" }, { "label": "", "value": "x" } ],
              "table": { "columns": ["Supplier", "Price"], "rows": [["Alpha", "900.00 JOD"], ["Beta"]] },
              "chart": { "title": "Prices", "kind": "columns", "labels": ["Alpha", "Beta"], "values": [900, "1,200.50 JOD"] },
              "recommendations": ["Check delivery.", ""] }
            ```
            """;
        var answer = QwenChatbot.ParseAnswer(reply)!;
        Assert.Equal("Alpha is the cheapest.", answer.Answer);
        Assert.True(answer.UsedAiModel);
        Assert.Single(answer.Cards);
        Assert.Equal(["Beta", ""], answer.Table!.Rows[1]);
        Assert.Equal("columns", answer.Chart!.Kind);
        Assert.Equal([900m, 1200.50m], answer.Chart.Values);
        Assert.Equal(["Check delivery."], answer.Recommendations);

        Assert.Null(QwenChatbot.ParseAnswer("Sorry, I cannot help."));
        // A question outside the system: flagged, with nothing else to show
        var offTopic = QwenChatbot.ParseAnswer("{ \"offTopic\": true, \"answer\": \"JavaScript is a language\" }")!;
        Assert.True(offTopic.OffTopic);
        Assert.Equal("", offTopic.Answer);
        Assert.Null(offTopic.Table);
        Assert.Null(QwenChatbot.ParseAnswer("{ \"cards\": [] }"));
    }

    [Fact]
    public void Facts_are_precalculated_and_focus_on_one_tender()
    {
        var all = JsonSerializer.SerializeToNode(AiInsights.Facts(Sample()))!;
        Assert.Equal(2, all["summary"]!["totalTenders"]!.GetValue<int>());
        var bids = all["bids"]!.AsArray();
        Assert.Equal("Alpha", bids[0]!["Supplier"]!.GetValue<string>());
        Assert.Equal(1, bids[0]!["rankByPrice"]!.GetValue<int>());
        Assert.Equal(33.3m, bids[1]!["percentAboveLowest"]!.GetValue<decimal>());
        var supplier = all["suppliers"]!.AsArray().Single(s => s!["name"]!.GetValue<string>() == "Alpha")!;
        Assert.Equal(1, supplier["wins"]!.GetValue<int>());
        Assert.Equal(1, supplier["lowestOfferIn"]!.GetValue<int>());

        var data = Sample();
        var one = JsonSerializer.SerializeToNode(AiInsights.Facts(data, data.Tenders[1]))!;
        Assert.Single(one["tenders"]!.AsArray());
        Assert.Empty(one["bids"]!.AsArray());
    }

    [Fact]
    public async Task Local_ollama_gets_prompt_data_file_and_history_with_thinking_off()
    {
        var handler = new FakeHandler("http://localhost:11434/api/chat", ollama: true);
        var bot = Bot(handler, null, provider: null);
        Assert.True(bot.Local);
        var answer = await bot.GenerateAsync("Who is cheapest?", AiInsights.Facts(Sample()), "File name: a.csv\nx,y",
            [new AiChatTurn("user", "hi"), new AiChatTurn("assistant", "hello")], arabic: true);

        Assert.Equal("Alpha.", answer!.Answer);
        Assert.Equal(2, handler.Requests.Count);   // first request rejected (400), second without the JSON format option
        var first = handler.Requests[0];
        Assert.Null(first.Auth);
        Assert.Equal("qwen3.5:9b", first.Body["model"]!.GetValue<string>());
        Assert.False(first.Body["think"]!.GetValue<bool>());
        Assert.Equal("json", first.Body["format"]!.GetValue<string>());
        Assert.Equal(16384, first.Body["options"]!["num_ctx"]!.GetValue<int>());
        Assert.Null(handler.Requests[1].Body["format"]);
        Assert.False(handler.Requests[1].Body["think"]!.GetValue<bool>());
        var messages = first.Body["messages"]!.AsArray();
        Assert.Contains("Arabic", messages[0]!["content"]!.GetValue<string>());
        Assert.Contains("did NOT ask for recommendations", messages[0]!["content"]!.GetValue<string>());
        Assert.Contains("offTopic", messages[0]!["content"]!.GetValue<string>());
        Assert.Equal("hi", messages[1]!["content"]!.GetValue<string>());
        var user = messages[^1]!["content"]!.GetValue<string>();
        Assert.Contains("\"lowestSupplier\":\"Alpha\"", user);
        Assert.Contains("FILE CONTENT", user);
        Assert.Contains("QUESTION: Who is cheapest?", user);
    }

    [Theory]
    [InlineData("Supplier participation", false)]
    [InlineData("مين أقل سعر؟", false)]
    [InlineData("Give me recommendations for tender #7", true)]
    [InlineData("اعطيني توصيات", true)]
    [InlineData("شو بتنصحني؟", true)]
    [InlineData("بدي اقتراحات للموردين", true)]
    public void Recommendations_only_when_asked(string question, bool expected) =>
        Assert.Equal(expected, AiInsights.AsksForRecommendations(question));

    [Fact]
    public async Task Hugging_face_provider_sends_token_and_openai_request()
    {
        var handler = new FakeHandler("https://router.example/v1/chat/completions", ollama: false);
        var bot = Bot(handler, "hf_test", provider: "HuggingFace", model: "Qwen/Qwen3.5-397B-A17B");
        var answer = await bot.GenerateAsync("Who is cheapest?", AiInsights.Facts(Sample()), "File name: a.csv\nx,y",
            [new AiChatTurn("user", "hi"), new AiChatTurn("assistant", "hello")], arabic: true);

        Assert.Equal("Alpha.", answer!.Answer);
        Assert.Equal(2, handler.Requests.Count);   // first request rejected (400), second without extra options
        var first = handler.Requests[0];
        Assert.Equal("Bearer hf_test", first.Auth);
        Assert.Equal("Qwen/Qwen3.5-397B-A17B", first.Body["model"]!.GetValue<string>());
        Assert.False(first.Body["chat_template_kwargs"]!["enable_thinking"]!.GetValue<bool>());
        Assert.Null(handler.Requests[1].Body["chat_template_kwargs"]);
        var messages = first.Body["messages"]!.AsArray();
        Assert.Contains("Arabic", messages[0]!["content"]!.GetValue<string>());
        Assert.Equal("hi", messages[1]!["content"]!.GetValue<string>());
        var user = messages[^1]!["content"]!.GetValue<string>();
        Assert.Contains("\"lowestSupplier\":\"Alpha\"", user);
        Assert.Contains("FILE CONTENT", user);
        Assert.Contains("QUESTION: Who is cheapest?", user);
    }

    [Fact]
    public async Task GenerateAsync_returns_null_without_token()
    {
        var handler = new FakeHandler("https://router.example/v1/chat/completions", ollama: false);
        var previous = Environment.GetEnvironmentVariable("HF_TOKEN");
        Environment.SetEnvironmentVariable("HF_TOKEN", null);
        try { Assert.Null(await Bot(handler, null, provider: "HuggingFace").GenerateAsync("x", new { }, null, [], false)); }
        finally { Environment.SetEnvironmentVariable("HF_TOKEN", previous); }
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void Uploaded_excel_is_read_with_formula_results_and_sent_as_text()
    {
        using var stream = new MemoryStream();
        using (var book = new ClosedXML.Excel.XLWorkbook())
        {
            var sheet = book.Worksheets.Add("Offers");
            sheet.Cell(1, 1).Value = "Supplier"; sheet.Cell(1, 2).Value = "Price";
            sheet.Cell(2, 1).Value = "Alpha, Ltd"; sheet.Cell(2, 2).Value = 900;
            sheet.Cell(3, 1).Value = "Beta"; sheet.Cell(3, 2).FormulaA1 = "B2+300";
            book.SaveAs(stream);
        }
        stream.Position = 0;
        var file = new Microsoft.AspNetCore.Http.FormFile(stream, 0, stream.Length, "file", "offers.xlsx");
        var table = AiInsights.ReadFile(file);
        Assert.Equal(["Supplier", "Price"], table.Columns);
        Assert.Equal(["Beta", "1200"], table.Rows[1]);
        Assert.Contains("\"Alpha, Ltd\",900", AiInsights.FileText("offers.xlsx", table));
    }

    private static QwenChatbot Bot(HttpMessageHandler handler, string? key, string? provider, string? model = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Qwen:ApiKey"] = key, ["Qwen:Provider"] = provider, ["Qwen:Model"] = model }).Build();
        var baseUrl = provider == "HuggingFace" ? "https://router.example/v1/" : "http://localhost:11434/";
        return new QwenChatbot(new Factory(handler, baseUrl), configuration, NullLogger<QwenChatbot>.Instance);
    }

    private sealed class Factory(HttpMessageHandler handler, string baseUrl) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false) { BaseAddress = new Uri(baseUrl) };
    }

    private sealed class FakeHandler(string url, bool ollama) : HttpMessageHandler
    {
        public List<(string? Auth, JsonNode Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(url, request.RequestUri!.ToString());
            Requests.Add((request.Headers.Authorization?.ToString(), JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!));
            if (Requests.Count == 1) return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("{}") };
            var message = new JsonObject { ["content"] = "{\"answer\":\"Alpha.\",\"cards\":[],\"table\":null,\"chart\":null}" };
            var reply = ollama ? new JsonObject { ["message"] = message }
                : new JsonObject { ["choices"] = new JsonArray(new JsonObject { ["message"] = message }) };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(reply.ToJsonString(), Encoding.UTF8, "application/json") };
        }
    }
}
