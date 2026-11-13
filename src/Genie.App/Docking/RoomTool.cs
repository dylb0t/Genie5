using Avalonia.Media;
using Dock.Model.Mvvm.Controls;
using Genie.App.Controls;
using Genie.App.ViewModels;
using Genie.Core.Layout;

namespace Genie.App.Docking;

/// <summary>
/// Room tool: a snapshot of the current room (title, description, exits,
/// players, objects) redrawn on every room change, not an appending stream —
/// as Genie 4's <c>UpdateRoom</c> did. Like Genie 4's Room window it is drawn
/// with the window's own font, size and colours (#424); the title and
/// description take the <c>roomname</c> / <c>roomdesc</c> presets
/// (<see cref="RoomViewModel"/>).
/// </summary>
public class RoomTool : ActivityTool, IWindowMenuHost
{
    public RoomViewModel ViewModel { get; }

    /// <summary>Right-click window menu (Close), built by <see cref="GenieDockFactory"/>.</summary>
    public WindowMenuModel? WindowMenu { get; set; }

    // Per-window appearance, resolved the same way as StreamTool: an empty
    // font / non-positive size / "Default" colour falls back to the global
    // game font, size and colour.
    private FontFamily _toolFontFamily = WindowSettingsResolver.ResolveFontFamily("");
    public  FontFamily ToolFontFamily { get => _toolFontFamily; private set => SetProperty(ref _toolFontFamily, value); }

    private double     _toolFontSize = WindowSettingsResolver.ResolveFontSize(0);
    public  double     ToolFontSize { get => _toolFontSize; private set => SetProperty(ref _toolFontSize, value); }

    private IBrush?    _toolForeground = WindowSettingsResolver.ResolveForeground("");
    public  IBrush?    ToolForeground { get => _toolForeground; private set => SetProperty(ref _toolForeground, value); }

    private IBrush?    _toolBackground;
    public  IBrush?    ToolBackground { get => _toolBackground; private set => SetProperty(ref _toolBackground, value); }

    public RoomTool(RoomViewModel vm, WindowSettings? settings = null)
    {
        ViewModel = vm;
        Id        = "room";
        Title     = "Room";

        if (settings is not null)
        {
            ApplySettings(settings);
            settings.Changed += () => ApplySettings(settings);
        }

        ActivitySettings = settings;

        // Unread-activity flash: [Reactive] props raise only on real changes,
        // so an unchanged `look` stays silent. Inlines twins are skipped —
        // they change in lockstep with these.
        WireActivity(vm,
            nameof(RoomViewModel.Title),
            nameof(RoomViewModel.Description),
            nameof(RoomViewModel.Exits),
            nameof(RoomViewModel.Players),
            nameof(RoomViewModel.Objects));
    }

    private void ApplySettings(WindowSettings s)
    {
        Title          = string.IsNullOrEmpty(s.DisplayTitle) ? s.DefaultTitle : s.DisplayTitle;
        ToolFontFamily = WindowSettingsResolver.ResolveFontFamily(s.FontFamily);
        ToolFontSize   = WindowSettingsResolver.ResolveFontSize(s.FontSize);
        ToolForeground = WindowSettingsResolver.ResolveForeground(s.Foreground);
        ToolBackground = WindowSettingsResolver.ResolveBackground(s.Background);
    }
}
