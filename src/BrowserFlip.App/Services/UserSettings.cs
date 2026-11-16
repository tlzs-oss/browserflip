using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BrowserFlip.App.Services;

/// <summary>Per-user preferences stored in %LOCALAPPDATA%\BrowserFlip\settings.json.</summary>
public sealed class UserSettings
{
    /// <summary>Default location of the settings file.</summary>
    public static readonly string DefaultFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BrowserFlip", "settings.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>Where this instance is saved; <c>null</c> disables saving (used by tests).</summary>
    [JsonIgnore]
    public string? FilePath { get; private set; }

    /// <summary>UI language ("en", "ja", "zh-CN") or empty to follow Windows.</summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>Creates in-memory settings that are never written to disk.</summary>
    public static UserSettings InMemory() => new();

    public static UserSettings Load(string? filePath = null)
    {
        var path = filePath ?? DefaultFilePath;
        UserSettings settings;
        try
        {
            settings = File.Exists(path)
                ? JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(path)) ?? new UserSettings()
                : new UserSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            settings = new UserSettings();
        }

        settings.FilePath = path;
        settings.Language ??= string.Empty;
        return settings;
    }

    public void Save()
    {
        if (FilePath is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Preferences are best-effort; failing to save must not break the app.
        }
    }
}
