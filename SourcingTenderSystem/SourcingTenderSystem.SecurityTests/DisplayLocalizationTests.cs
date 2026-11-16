using System.Globalization;
using System.Text.RegularExpressions;
using SourcingTenderSystem.Services;
using Xunit;

public sealed class DisplayLocalizationTests
{
    private static string SourceRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
        "../../../../SourcingTenderSystem"));

    [Fact]
    public void Arabic_formats_messages_without_changing_business_values()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ar");
            var ui = new UiText();
            Assert.Equal("rtl", ui.Direction);
            Assert.Equal("العطاء رقم 12", ui.F("Tender #{0}", 12));
            Assert.Equal("دعوة عطاء · الجولة 3", ui.SystemMessage("Tender invitation · Round 3"));
            Assert.Contains("مطلوب", ui.Error("The Email field is required."));
            Assert.Equal("Customer-entered title", ui.T("Customer-entered title"));
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            Assert.Equal("Tender #12", ui.F("Tender #{0}", 12));
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    [Fact]
    public void Translations_preserve_format_placeholders_and_have_unique_keys()
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadLines(Path.Combine(SourceRoot, "Localization/ar.tsv")))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var pair = line.Split('|', 2);
            Assert.Equal(2, pair.Length);
            Assert.True(keys.Add(pair[0]), "Duplicate translation: " + pair[0]);
            Assert.False(string.IsNullOrWhiteSpace(pair[1]));
            string[] Tokens(string s) => Regex.Matches(s, @"\{\d+\}")
                .Select(m => m.Value).Order().ToArray();
            Assert.Equal(Tokens(pair[0]), Tokens(pair[1]));
        }
    }

    [Fact]
    public void Arabic_rendering_does_not_translate_route_or_filter_keys()
    {
        foreach (var file in Directory.EnumerateFiles(Path.Combine(SourceRoot, "Views"), "*.cshtml", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(file);
            Assert.False(Regex.IsMatch(source, "(?:asp-action|asp-controller|data-status|data-status-choice)=[\"']@L\\.[TF]\\("), file);
        }
        var criteria = File.ReadAllText(Path.Combine(SourceRoot, "Views/Tenders/Criteria.cshtml"));
        Assert.Contains("value=\"Technical\" selected=", criteria);
        Assert.Contains("value=\"Financial\" selected=", criteria);
    }

    [Fact]
    public void Literal_ui_keys_have_Arabic_translations()
    {
        var catalog = File.ReadLines(Path.Combine(SourceRoot, "Localization/ar.tsv"))
            .Where(l => l.Contains('|')).Select(l => l.Split('|', 2)[0]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in new[] { "Views", "wwwroot/js" })
        foreach (var file in Directory.EnumerateFiles(Path.Combine(SourceRoot, folder), "*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cshtml") || f.EndsWith(".js")))
        foreach (Match match in Regex.Matches(File.ReadAllText(file), "(?:L\\.[TF]|stmsT|stmsFormat)\\(\\s*\"([^\"\\r\\n]+)\""))
            Assert.True(catalog.Contains(match.Groups[1].Value), file + ": " + match.Groups[1].Value);
    }
}
