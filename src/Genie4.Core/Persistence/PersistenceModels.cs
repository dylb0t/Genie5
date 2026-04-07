namespace Genie4.Core.Persistence;

public sealed class AliasPersistenceModel
{
    public string Name { get; set; } = string.Empty;
    public string Expansion { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
}

public sealed class TriggerPersistenceModel
{
    public string Pattern { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public bool CaseSensitive { get; set; }
    public bool IsEnabled { get; set; } = true;
}

public sealed class VariablePersistenceModel
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Scope { get; set; } = "User";
}

public sealed class HighlightPersistenceModel
{
    public string Pattern { get; set; } = string.Empty;
    public string ForegroundColor { get; set; } = "Yellow";
    public bool IsRegex { get; set; }
    public bool CaseSensitive { get; set; }
    public bool IsEnabled { get; set; } = true;
}

public sealed class PresetPersistenceModel
{
    public string Id              { get; set; } = string.Empty;
    public string ForegroundColor { get; set; } = "Default";
    public string BackgroundColor { get; set; } = string.Empty;
    public bool   HighlightLine   { get; set; } = false;
}

public sealed class LayoutState
{
    /// <summary>Avalonia WindowState name: Normal, Maximized, FullScreen.</summary>
    public string WindowState { get; set; } = "Normal";

    // Restore-position geometry (only meaningful when WindowState == Normal).
    public int    WindowX      { get; set; } = 100;
    public int    WindowY      { get; set; } = 100;
    public double WindowWidth  { get; set; } = 1280;
    public double WindowHeight { get; set; } = 800;

    /// <summary>Dock pane proportions keyed by pane Id.</summary>
    public Dictionary<string, double> DockProportions { get; set; } = new();
}
