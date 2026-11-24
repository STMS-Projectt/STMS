using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SourcingTenderSystem.Services;

/// <summary>One earlier message of the chat, sent back so the model understands follow-up questions.</summary>
public record AiChatTurn(string Role, string Content);

/// <summary>
/// The chatbot brain: a Qwen model.
/// - Default: Qwen3.5 9B running LOCALLY on this computer's GPU with Ollama ("ollama pull qwen3.5:9b"),
///   with thinking turned OFF for fast answers.
/// - Optional: the big Qwen3.5-397B-A17B on Hugging Face (set Qwen:Provider = "HuggingFace" and a token).
///
/// Everything happens in ONE function: <see cref="GenerateAsync"/>.
/// It receives the user's prompt + the data from the database (+ an uploaded file), and returns a ready answer:
/// text, analysis cards, a table, a chart and recommendations.
/// </summary>
public class QwenChatbot(IHttpClientFactory httpClients, IConfiguration configuration, ILogger<QwenChatbot> logger)
{
    public const string HttpClientName = "Qwen";
    public const string DefaultModel = "qwen3.5:9b";

    /// <summary>True = local Ollama (default). False = Hugging Face Inference Providers.</summary>
    public bool Local => !string.Equals(configuration["Qwen:Provider"], "HuggingFace", StringComparison.OrdinalIgnoreCase);
    private string? ApiKey => configuration["Qwen:ApiKey"] is { Length: > 0 } key ? key : Environment.GetEnvironmentVariable("HF_TOKEN");
    private string Model => configuration["Qwen:Model"] is { Length: > 0 } m ? m : DefaultModel;

    /// <summary>The local model needs no key; Hugging Face needs a token.</summary>
    public bool Enabled => Local || !string.IsNullOrWhiteSpace(ApiKey);

    // How much database data is sent. A small local model on an 8 GB GPU gets less, so it stays fast and accurate.
    public int MaxTenders => configuration.GetValue("Qwen:MaxTenders", Local ? 40 : 150);
    public int MaxBids => configuration.GetValue("Qwen:MaxBids", Local ? 200 : 600);

    private const string Instructions = """
        You are the AI analyst of a Sourcing & Tender Management System (STMS).
        You help employees compare tenders, analyse supplier offers, find the lowest and highest prices,
        analyse supplier participation, and explain tender status and results. You can also analyse an uploaded Excel or PDF file.

        RULES FOR ACCURATE ANSWERS
        - Use ONLY the DATABASE DATA and the FILE CONTENT given in the message. Never invent tenders, suppliers, prices or dates.
        - The numbers in the data are already calculated by the system (lowest, highest, average, rank, percentAboveLowest, wins ...).
          Copy them exactly. Only do simple maths yourself when the data has no ready field for it, and double-check it.
        - Never add or compare prices in different currencies as if they were the same currency.
        - Bid prices of tenders that are still Draft/Published/Open are sealed and are NOT in the data. Say so if asked.
        - If the data does not contain the answer, say that clearly instead of guessing.
        - You only advise. You cannot approve, award, edit or delete anything.

        HOW TO ANSWER
        Reply with ONE JSON object only (no markdown, no text outside the JSON), in this exact shape:
        {
          "answer": "short clear answer, max 6 sentences",
          "cards": [ { "label": "Lowest price", "value": "1,200.00 JOD" } ],
          "table": { "columns": ["Supplier", "Price"], "rows": [["ABC Ltd", "1,200.00 JOD"]] },
          "chart": { "title": "Bid prices", "kind": "bars", "labels": ["ABC Ltd"], "values": [1200] },
          "recommendations": ["advice sentence"]
        }
        - "cards": 0 to 6 key numbers (analysis cards). "table": use it when listing or comparing several items (max 50 rows).
        - "chart": use it when a comparison of numbers helps. "kind" is "bars" (compare items) or "columns" (values over time).
          "values" must be plain numbers in the same order as "labels" (max 15 points). Use null for table/chart when not useful.
        - "recommendations": 0 to 4 short practical advice sentences.
        - Every table cell and card value is a string. Format money as "1,234.50 JOD".
        - Write "answer", labels, table text and recommendations in the language requested below.
        """;

    /// <summary>
    /// Generates the chatbot answer with Qwen.
    /// </summary>
    /// <param name="prompt">The user's question.</param>
    /// <param name="databaseData">The data from the database the user is allowed to see (any object, sent as JSON).</param>
    /// <param name="fileContent">Text of an uploaded Excel/PDF file, or null.</param>
    /// <param name="history">The last messages of the chat (for follow-up questions).</param>
    /// <param name="arabic">Answer in Arabic.</param>
    /// <returns>The answer, or null when the model is not configured or not reachable (the caller then uses a simple answer).</returns>
    public async Task<AiAnswer?> GenerateAsync(string prompt, object databaseData, string? fileContent,
        IReadOnlyList<AiChatTurn> history, bool arabic, CancellationToken cancellationToken = default)
    {
        if (!Enabled) return null;

        // 1) Build the messages: rules, the earlier chat, then the data + the question.
        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "system", ["content"] = Instructions + "\nLanguage of the answer: " +
                (arabic ? "Arabic." : "the same language as the user's question.") }
        };
        foreach (var turn in history.TakeLast(6))
            messages.Add(new JsonObject { ["role"] = turn.Role == "user" ? "user" : "assistant", ["content"] = Trim(turn.Content, 1500) });

        var user = new StringBuilder();
        user.Append("DATABASE DATA (JSON):\n").Append(JsonSerializer.Serialize(databaseData, JsonOptions)).Append("\n\n");
        if (!string.IsNullOrWhiteSpace(fileContent))
            user.Append("FILE CONTENT:\n").Append(Trim(fileContent, 20000)).Append("\n\n");
        user.Append("QUESTION: ").Append(string.IsNullOrWhiteSpace(prompt) ? "Analyse the uploaded file and summarise it." : prompt);
        messages.Add(new JsonObject { ["role"] = "user", ["content"] = user.ToString() });

        // 2) Call the model and 3) turn its JSON reply into an AiAnswer.
        var content = await CallModelAsync(messages, cancellationToken);
        var answer = content is null ? null : ParseAnswer(content);
        if (content is not null && answer is null) logger.LogWarning("Qwen returned an answer that is not valid JSON.");
        return answer;
    }

    private async Task<string?> CallModelAsync(JsonArray messages, CancellationToken cancellationToken)
    {
        // Thinking is OFF by default: the numbers are already calculated, so the answer comes much faster.
        var thinking = configuration.GetValue("Qwen:EnableThinking", false);
        var maxTokens = configuration.GetValue("Qwen:MaxTokens", 2000);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            // attempt 0 asks for JSON output; attempt 1 is a plain request in case the server rejects that option.
            JsonObject payload;
            if (Local)
            {
                // Ollama's own API: "think": false really switches thinking off, num_ctx sets how much text fits.
                payload = new JsonObject
                {
                    ["model"] = Model,
                    ["messages"] = messages.DeepClone(),
                    ["stream"] = false,
                    ["think"] = thinking,
                    ["keep_alive"] = "30m",   // keep the model on the GPU between questions
                    ["options"] = new JsonObject
                    {
                        ["temperature"] = 0.2,
                        ["num_ctx"] = configuration.GetValue("Qwen:ContextLength", 16384),
                        ["num_predict"] = maxTokens
                    }
                };
                if (attempt == 0) payload["format"] = "json";
            }
            else
            {
                payload = new JsonObject
                {
                    ["model"] = Model,
                    ["messages"] = messages.DeepClone(),
                    ["temperature"] = 0.2,
                    ["max_tokens"] = maxTokens,
                    ["stream"] = false
                };
                if (attempt == 0)
                {
                    payload["response_format"] = new JsonObject { ["type"] = "json_object" };
                    payload["chat_template_kwargs"] = new JsonObject { ["enable_thinking"] = thinking };
                }
            }
            try
            {
                var client = httpClients.CreateClient(HttpClientName);
                using var request = new HttpRequestMessage(HttpMethod.Post, Local ? "api/chat" : "chat/completions")
                {
                    Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
                };
                if (!Local) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
                using var response = await client.SendAsync(request, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                if ((int)response.StatusCode is 400 or 422 && attempt == 0) continue;
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("Qwen request failed with {Status}: {Body}", (int)response.StatusCode, Trim(body, 300));
                    return null;
                }
                var reply = JsonNode.Parse(body);
                var text = (Local ? reply?["message"]?["content"] : reply?["choices"]?[0]?["message"]?["content"])?.GetValue<string>();
                return string.IsNullOrWhiteSpace(text) ? null : text;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
            {
                logger.LogInformation("Qwen is not available for the chatbot (is Ollama running?): {Message}", ex.Message);
                return null;
            }
        }
        return null;
    }

    // ------------------------------------------------------------ Reading the model's reply

    /// <summary>
    /// Turns the model's reply into an AiAnswer. Removes "thinking" text and ```json fences, and keeps only
    /// valid cards / table / chart (wrong shapes are dropped, sizes are limited). Returns null if there is no usable JSON.
    /// </summary>
    public static AiAnswer? ParseAnswer(string content)
    {
        content = Regex.Replace(content, "<think>.*?</think>", "", RegexOptions.Singleline).Trim();
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        JsonObject? json;
        try { json = JsonNode.Parse(content[start..(end + 1)]) as JsonObject; }
        catch (JsonException) { return null; }
        if (json is null || Text(json["answer"]) is not { Length: > 0 } text) return null;

        var answer = new AiAnswer { Answer = text, UsedAiModel = true };

        if (json["cards"] is JsonArray cards)
            answer.Cards = cards.OfType<JsonObject>()
                .Select(c => new AiCard(Text(c["label"]) ?? "", Text(c["value"]) ?? ""))
                .Where(c => c.Label.Length > 0 && c.Value.Length > 0).Take(6).ToList();

        if (json["table"] is JsonObject table && table["columns"] is JsonArray columns && table["rows"] is JsonArray rows)
        {
            var heads = columns.Select(c => Text(c) ?? "").ToList();
            var body = rows.OfType<JsonArray>().Take(50)
                .Select(r => Enumerable.Range(0, heads.Count).Select(i => i < r.Count ? Text(r[i]) ?? "" : "").ToList()).ToList();
            if (heads.Count is > 0 and <= 12 && body.Count > 0) answer.Table = new AiTable(heads, body);
        }

        if (json["chart"] is JsonObject chart && chart["labels"] is JsonArray labels && chart["values"] is JsonArray values)
        {
            var points = labels.Zip(values, (l, v) => (Label: Text(l) ?? "", Value: Number(v)))
                .Where(p => p.Value is not null).Take(15).ToList();
            if (points.Count > 0 && points.Count == Math.Min(15, Math.Min(labels.Count, values.Count)))
                answer.Chart = new AiChart(Text(chart["title"]) ?? "", points.Select(p => p.Label).ToList(),
                    points.Select(p => p.Value!.Value).ToList(), Text(chart["kind"]) == "columns" ? "columns" : "bars");
        }

        if (json["recommendations"] is JsonArray recommendations)
            answer.Recommendations = recommendations.Select(Text).OfType<string>().Where(r => r.Length > 0).Take(4).ToList();
        return answer;
    }

    private static string? Text(JsonNode? node) => node switch
    {
        null => null,
        JsonValue v when v.TryGetValue<string>(out var s) => s.Trim(),
        JsonValue v => v.ToJsonString().Trim('"'),
        _ => null
    };

    // Accepts 1200, "1200" and "1,200.50 JOD".
    private static decimal? Number(JsonNode? node)
    {
        if (node is JsonValue v && v.TryGetValue<decimal>(out var d)) return d;
        var text = Regex.Match(Text(node) ?? "", @"-?[\d,]*\.?\d+").Value.Replace(",", "");
        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static string Trim(string text, int max) => text.Length <= max ? text : text[..max] + "\n...(cut)";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
}
