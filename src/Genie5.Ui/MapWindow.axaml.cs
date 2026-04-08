using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Genie4.Core.Mapper;

namespace Genie5.Ui;

public partial class MapWindow : Window
{
    private readonly MapZoneRepository _repo = new();
    private AutoMapperEngine? _engine;
    private string _mapsDir = string.Empty;
    private Action<string>? _sendCommand;
    private bool _suppressSelection;

    /// <summary>Invoked by the host to update the loaded zone path on disk.</summary>
    public Action<string>? CurrentZonePathChanged { get; set; }

    public MapWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    public void Initialize(AutoMapperEngine engine, string mapsDir, Action<string> sendCommand, string? currentZonePath)
    {
        _engine      = engine;
        _mapsDir     = mapsDir;
        _sendCommand = sendCommand;

        var mapView = this.FindControl<MapView>("MapHost")!;
        var vm      = new MapViewModel(engine);
        mapView.DataContext = vm;
        mapView.Attach(vm);
        mapView.SendCommand = sendCommand;

        RefreshZoneList(currentZonePath);
    }

    public void RefreshZoneList(string? selectedPath = null)
    {
        var box = this.FindControl<ComboBox>("ZoneBox")!;
        var files = Directory.Exists(_mapsDir)
            ? Directory.GetFiles(_mapsDir, "*.json").OrderBy(f => f).ToList()
            : new System.Collections.Generic.List<string>();

        _suppressSelection = true;
        box.ItemsSource = files.Select(Path.GetFileNameWithoutExtension).ToList();

        if (!string.IsNullOrEmpty(selectedPath))
        {
            var name = Path.GetFileNameWithoutExtension(selectedPath);
            var idx = files.FindIndex(f => Path.GetFileNameWithoutExtension(f) == name);
            if (idx >= 0) box.SelectedIndex = idx;
        }
        _suppressSelection = false;
    }

    private void OnZoneSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection || _engine is null) return;
        var box = (ComboBox)sender!;
        if (box.SelectedItem is not string name) return;
        var path = Path.Combine(_mapsDir, name + ".json");
        var zone = _repo.Load(path);
        if (zone is null) return;
        _engine.LoadZone(zone);
        CurrentZonePathChanged?.Invoke(path);
    }

    private void OnCenter(object? sender, RoutedEventArgs e)
        => this.FindControl<MapView>("MapHost")?.CentreOnCurrent();
}
