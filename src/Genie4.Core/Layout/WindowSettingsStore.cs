namespace Genie4.Core.Layout;

/// <summary>
/// Holds WindowSettings for every named output panel.
/// Keyed by the window Id used in GameOutputViewModel.
/// </summary>
public sealed class WindowSettingsStore
{
    private readonly Dictionary<string, WindowSettings> _settings = new();

    public IReadOnlyDictionary<string, WindowSettings> All => _settings;

    public WindowSettings Get(string id) => _settings.TryGetValue(id, out var s) ? s : Fallback;

    private static readonly WindowSettings Fallback = new()
    {
        Id = "", DefaultTitle = "", DisplayTitle = "",
        FontFamily = "Cascadia Mono,Consolas,Courier New,monospace",
        FontSize = 13, Foreground = "Default", Background = "", Timestamp = false
    };

    /// <summary>Register a window slot with its default title.</summary>
    public WindowSettings Register(string id, string defaultTitle)
    {
        var s = new WindowSettings
        {
            Id           = id,
            DefaultTitle = defaultTitle,
            DisplayTitle = defaultTitle,
            FontFamily   = "Cascadia Mono,Consolas,Courier New,monospace",
            FontSize     = 13,
            Foreground   = "Default",
            Background   = "",
            Timestamp    = false,
        };
        _settings[id] = s;
        return s;
    }

    /// <summary>Apply a persisted model over an already-registered slot.</summary>
    public void Apply(WindowSettingsPersistenceModel m)
    {
        if (!_settings.TryGetValue(m.Id, out var s)) return;
        s.DisplayTitle = string.IsNullOrEmpty(m.DisplayTitle) ? s.DefaultTitle : m.DisplayTitle;
        s.FontFamily   = string.IsNullOrEmpty(m.FontFamily) ? s.FontFamily : m.FontFamily;
        s.FontSize     = m.FontSize > 0 ? m.FontSize : s.FontSize;
        s.Foreground   = string.IsNullOrEmpty(m.Foreground) ? s.Foreground : m.Foreground;
        s.Background   = m.Background;
        s.Timestamp    = m.Timestamp;
    }
}

// Keep model here to avoid a circular reference between Layout and Persistence projects.
public sealed class WindowSettingsPersistenceModel
{
    public string Id           { get; set; } = "";
    public string DisplayTitle { get; set; } = "";
    public string FontFamily   { get; set; } = "";
    public double FontSize     { get; set; } = 0;
    public string Foreground   { get; set; } = "";
    public string Background   { get; set; } = "";
    public bool   Timestamp    { get; set; } = false;
}
