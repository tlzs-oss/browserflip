using System.Text.Json;
using System.Text.Json.Serialization;

namespace BrowserFlip.UI.Platform;

/// <summary>
/// Cross-platform settings store: one JSON file in the per-user configuration directory that
/// each OS designates. Writes go to a temporary file first and are then moved into place, so an
/// interrupted write cannot leave a half-written file behind.
/// </summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public JsonSettingsStore() : this(DefaultLocation())
    {
    }

    public JsonSettingsStore(string location) => Location = location;

    public string Location { get; }

    /// <summary>
    /// macOS: <c>~/Library/Application Support/BrowserFlip</c>;
    /// Windows: <c>%APPDATA%\BrowserFlip</c>;
    /// Linux: <c>$XDG_CONFIG_HOME/BrowserFlip</c> (<c>~/.config/BrowserFlip</c>).
    /// All three are what <see cref="Environment.SpecialFolder.ApplicationData"/> resolves to.
    /// </summary>
    public static string DefaultLocation()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(root))
        {
            root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        }

        return Path.Combine(root, "BrowserFlip", "prototype-settings.json");
    }

    public PrototypeSettings Load()
    {
        try
        {
            if (!File.Exists(Location))
            {
                return PrototypeSettings.Default;
            }

            return JsonSerializer.Deserialize<PrototypeSettings>(File.ReadAllText(Location), Options)
                   ?? PrototypeSettings.Default;
        }
#pragma warning disable CA1031 // Unreadable or corrupt settings must fall back to defaults, never crash.
        catch (Exception)
#pragma warning restore CA1031
        {
            return PrototypeSettings.Default;
        }
    }

    public bool Save(PrototypeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        try
        {
            var directory = Path.GetDirectoryName(Location);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temp = Location + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
            File.Move(temp, Location, overwrite: true);
            return true;
        }
#pragma warning disable CA1031 // A read-only or full disk must not crash the app.
        catch (Exception)
#pragma warning restore CA1031
        {
            return false;
        }
    }
}
