using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace SourcingTenderSystem.Services;

public sealed class UiText
{
    private static readonly Lazy<Dictionary<string, string>> Catalog = new(() =>
    {
        using var stream = typeof(UiText).Assembly.GetManifestResourceStream("STMS.Ui.ar")
            ?? throw new InvalidOperationException("Arabic UI catalog is missing.");
        using var reader = new StreamReader(stream);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (reader.ReadLine() is { } line)
        {
            var tab = line.IndexOf('|');
            if (tab > 0) result[line[..tab]] = line[(tab + 1)..];
        }
        return result;
    });
    private static readonly Lazy<string> Client = new(() => JsonSerializer.Serialize(
        Catalog.Value.ToDictionary(pair => pair.Key.ToLowerInvariant(), pair => pair.Value)));
    private static readonly CultureInfo ArabicDisplay = CultureInfo.GetCultureInfo("ar-JO");
    private readonly string? language;
    public UiText() { }
    internal UiText(string language) => this.language = language;
    public bool Arabic => (language ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName) == "ar";
    public CultureInfo DisplayCulture => Arabic ? ArabicDisplay : CultureInfo.CurrentCulture;
    public string Language => Arabic ? "ar" : "en";
    public string Direction => Arabic ? "rtl" : "ltr";
    public string T(string? text) => text is null ? "" : Arabic ? Catalog.Value.GetValueOrDefault(text, text) : text;
    public string F(string format, params object?[] args) => string.Format(CultureInfo.CurrentCulture, T(format), args);
    // Only call for system-owned messages. User-entered titles, notes and filenames stay verbatim.
    public string SystemMessage(string? text)
    {
        if (string.IsNullOrEmpty(text) || !Arabic) return text ?? "";
        if (Catalog.Value.TryGetValue(text, out var translated)) return translated;
        foreach (var format in new[] { "Tender invitation · Round {0}", "Round {0} requires {1} eligible supplier bids." })
        {
            var pattern = string.Concat(Regex.Split(format, @"(\{\d+\})")
                .Select(part => part.StartsWith('{') ? @"(\d+)" : Regex.Escape(part)));
            var match = Regex.Match(text, "^" + pattern + "$", RegexOptions.CultureInvariant);
            if (match.Success) return F(format, match.Groups.Values.Skip(1).Select(g => (object)g.Value).ToArray());
        }
        return text;
    }
    public string Error(string text)
    {
        if (!Arabic) return text;
        if (Catalog.Value.TryGetValue(text, out var value)) return value;
        foreach (var format in new[] { "The {0} field is required.", "The {0} field is not a valid e-mail address.",
            "The {0} field is not a valid phone number.", "The field {0} must be a string with a maximum length of {1}.",
            "The field {0} must be between {1} and {2}." })
        {
            var pattern = string.Concat(Regex.Split(format, @"(\{\d+\})")
                .Select(part => part.StartsWith('{') ? "(.+?)" : Regex.Escape(part)));
            var match = Regex.Match(text, "^" + pattern + "$", RegexOptions.CultureInvariant);
            if (match.Success) return F(format, match.Groups.Values.Skip(1).Select(g => (object)T(g.Value)).ToArray());
        }
        return text;
    }
    public string ClientCatalog => Arabic ? Client.Value : "{}";
}

public sealed class UiValidationFilter : IResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        var ui = new UiText();
        if (!ui.Arabic || context.Result is not ViewResult view) return;
        foreach (var entry in view.ViewData.ModelState.Values)
            for (var i = 0; i < entry.Errors.Count; i++)
            {
                var error = entry.Errors[i];
                if (error.ErrorMessage.Length != 0)
                    entry.Errors[i] = new Microsoft.AspNetCore.Mvc.ModelBinding.ModelError(ui.Error(error.ErrorMessage));
            }
    }
    public void OnResultExecuted(ResultExecutedContext context) { }
}

[HtmlTargetElement("input", Attributes = "asp-for")]
[HtmlTargetElement("textarea", Attributes = "asp-for")]
[HtmlTargetElement("select", Attributes = "asp-for")]
public sealed class UiValidationTagHelper(UiText ui) : TagHelper
{
    public override int Order => int.MaxValue;
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (!ui.Arabic) return;
        foreach (var attribute in output.Attributes.Where(a => a.Name.StartsWith("data-val-", StringComparison.Ordinal) &&
            !a.Name["data-val-".Length..].Contains('-')).ToArray())
            output.Attributes.SetAttribute(attribute.Name, ui.Error(System.Net.WebUtility.HtmlDecode(attribute.Value?.ToString()) ?? ""));
    }
}

public sealed class UiDisplayMetadata : IDisplayMetadataProvider
{
    public void CreateDisplayMetadata(DisplayMetadataProviderContext context)
    {
        var name = context.DisplayMetadata.DisplayName;
        var property = context.Key.Name;
        if (name is not null || property is not null)
            context.DisplayMetadata.DisplayName = () =>
            {
                var original = name?.Invoke() ?? property;
                return new UiText().T(original);
            };
    }
}
