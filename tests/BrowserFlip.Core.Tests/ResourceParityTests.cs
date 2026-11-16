using System.Xml.Linq;

namespace BrowserFlip.Core.Tests;

/// <summary>Every UI string must exist (non-empty) in English, Japanese and Simplified Chinese.</summary>
public class ResourceParityTests
{
    private static readonly string ResxDir = Path.Combine(AppContext.BaseDirectory, "Resx");

    private static Dictionary<string, string> Load(string file)
        => XDocument.Load(Path.Combine(ResxDir, file))
            .Root!.Elements("data")
            .ToDictionary(e => (string)e.Attribute("name")!, e => (string?)e.Element("value") ?? string.Empty, StringComparer.Ordinal);

    [Theory]
    [InlineData("Strings.ja.resx")]
    [InlineData("Strings.zh-CN.resx")]
    public void Translation_has_exactly_the_neutral_keys(string file)
    {
        var neutral = Load("Strings.resx");
        var translated = Load(file);

        Assert.Empty(neutral.Keys.Except(translated.Keys));
        Assert.Empty(translated.Keys.Except(neutral.Keys));
        Assert.All(translated, kv => Assert.False(string.IsNullOrWhiteSpace(kv.Value), $"{file}: '{kv.Key}' is empty"));
    }

    [Theory]
    [InlineData("Strings.ja.resx")]
    [InlineData("Strings.zh-CN.resx")]
    public void Format_placeholders_match(string file)
    {
        var neutral = Load("Strings.resx");
        var translated = Load(file);
        foreach (var (key, value) in neutral)
        {
            Assert.Equal(Placeholders(value), Placeholders(translated[key]));
        }
    }

    [Fact]
    public void Neutral_strings_are_present()
        => Assert.NotEmpty(Load("Strings.resx"));

    private static string[] Placeholders(string s)
        => [.. System.Text.RegularExpressions.Regex.Matches(s, @"\{\d+\}").Select(m => m.Value).Distinct().Order(StringComparer.Ordinal)];
}
