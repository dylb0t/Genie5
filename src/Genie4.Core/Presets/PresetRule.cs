namespace Genie4.Core.Presets;

public sealed class PresetRule
{
    public string Id              { get; set; } = string.Empty;
    public string ForegroundColor { get; set; } = "Default";
    public string BackgroundColor { get; set; } = string.Empty;
    public bool   HighlightLine   { get; set; } = false;
}
