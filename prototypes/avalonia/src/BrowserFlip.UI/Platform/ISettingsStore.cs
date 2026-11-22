namespace BrowserFlip.UI.Platform;

/// <summary>Persists the prototype's settings in the platform's per-user configuration location.</summary>
public interface ISettingsStore
{
    /// <summary>Absolute path of the backing file (shown in the UI so the user can find it).</summary>
    string Location { get; }

    /// <summary>Loads the stored settings, or defaults when nothing is stored yet. Never throws.</summary>
    PrototypeSettings Load();

    /// <summary>Writes the settings atomically. Returns false when the write failed.</summary>
    bool Save(PrototypeSettings settings);
}
