using BrowserFlip.Core.Detection;

namespace BrowserFlip.Core.Tests;

public class RegistryValueParserTests
{
    [Theory]
    [InlineData("\"C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe\"", "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe")]
    [InlineData("\"C:\\Program Files\\Mozilla Firefox\\firefox.exe\" -osint -url \"%1\"", "C:\\Program Files\\Mozilla Firefox\\firefox.exe")]
    [InlineData("C:\\Program Files\\App Dir\\app.exe -url %1", "C:\\Program Files\\App Dir\\app.exe")]
    [InlineData("C:\\Apps\\APP.EXE", "C:\\Apps\\APP.EXE")]
    [InlineData("  \"C:\\x\\y.exe\"  ", "C:\\x\\y.exe")]
    [InlineData("\"C:\\unterminated\\y.exe", "C:\\unterminated\\y.exe")]
    [InlineData("notepad", "notepad")]
    [InlineData("tool --flag", "tool")]
    public void ParseExecutable_extracts_path(string command, string expected)
        => Assert.Equal(expected, RegistryValueParser.ParseExecutable(command));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"\"")]
    public void ParseExecutable_returns_null_for_empty(string? command)
        => Assert.Null(RegistryValueParser.ParseExecutable(command));

    [Theory]
    [InlineData("\"C:\\Program Files\\App\\app.exe\",0", "C:\\Program Files\\App\\app.exe", 0)]
    [InlineData("C:\\App\\app.exe,-101", "C:\\App\\app.exe", -101)]
    [InlineData("C:\\App\\app.exe, 3", "C:\\App\\app.exe", 3)]
    [InlineData("C:\\App\\app.ico", "C:\\App\\app.ico", 0)]
    [InlineData("\"C:\\A,B\\app.exe\"", "C:\\A,B\\app.exe", 0)]
    public void ParseIconLocation_splits_path_and_index(string value, string path, int index)
    {
        var (p, i) = RegistryValueParser.ParseIconLocation(value);
        Assert.Equal(path, p);
        Assert.Equal(index, i);
    }

    [Fact]
    public void ParseIconLocation_handles_empty()
        => Assert.Equal((null, 0), RegistryValueParser.ParseIconLocation(null));

    [Theory]
    [InlineData("\\Software\\Clients\\X\\", "Software\\Clients\\X")]
    [InlineData("Software/Clients/X", "Software\\Clients\\X")]
    public void NormalizeKeyPath_normalizes(string input, string expected)
        => Assert.Equal(expected, RegistryValueParser.NormalizeKeyPath(input));
}
