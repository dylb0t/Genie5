namespace Genie4.Core.Layout;

/// <summary>
/// Per-window display configuration: font, colours, timestamp prefix.
/// Mutable — LayoutPanel writes into it and calls NotifyChanged().
/// </summary>
public sealed class WindowSettings
{
    public string Id           { get; init; } = "";
    public string DefaultTitle { get; init; } = "";   // Read-only label shown in the list

    public string DisplayTitle { get; set; } = "";
    public string FontFamily   { get; set; } = "Cascadia Mono,Consolas,Courier New,monospace";
    public double FontSize     { get; set; } = 13;
    public string Foreground   { get; set; } = "Default";
    public string Background   { get; set; } = "";
    public bool   Timestamp    { get; set; } = false;

    public event Action? Changed;

    public void NotifyChanged() => Changed?.Invoke();
}
