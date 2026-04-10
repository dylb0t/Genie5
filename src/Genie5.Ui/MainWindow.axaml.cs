using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Dock.Model.Core;
using Genie4.Core.Gsl;
using Genie4.Core.Persistence;
using Genie4.Core.Profiles;
using Genie4.Core.Sge;

namespace Genie5.Ui;

public partial class MainWindow : Window
{
    private readonly ScrollbackBuffer _scrollback = new();
    private readonly GameOutputViewModel _gameOutputVm;
    private readonly GameOutputViewModel _rawOutputVm;
    private readonly GameOutputViewModel _logVm;
    private readonly RoomViewModel _roomVm = new();
    private MapWindow?         _mapWindow;
    private GenieDockFactory   _factory  = null!;

    // Sub-stream panels keyed by GSL stream id
    private readonly Dictionary<string, GameOutputViewModel> _streamVms = new();

    // Tracks normal-state geometry so we don't persist maximized dimensions.
    private int _restoreX, _restoreY;
    private double _restoreWidth = 1280, _restoreHeight = 800;

    private readonly List<string> _history = new();
    private int _historyIndex = -1;

    private string  _lastHost     = string.Empty;
    private int     _lastPort     = 4000;
    private string? _lastGameCode = "DR";

    public MainWindow()
    {
        InitializeComponent();

        _gameOutputVm = new GameOutputViewModel();
        _rawOutputVm  = new GameOutputViewModel("RawOutput", "Raw",  canClose: true);
        _logVm        = new GameOutputViewModel("Log",       "Log",  canClose: true);

        // Create a dockable panel for each sub-stream
        var streams = new (string id, string title)[]
        {
            ("thoughts",   "Thoughts"),
            ("logons",     "Arrivals"),
            ("death",      "Deaths"),
            ("combat",     "Combat"),
            ("inv",        "Inventory"),
            ("familiar",   "Familiar"),
            ("percWindow", "Perception"),
        };
        var streamVmArray = new GameOutputViewModel[streams.Length + 1];
        for (int i = 0; i < streams.Length; i++)
        {
            var vm = new GameOutputViewModel(streams[i].id, streams[i].title, canClose: true);
            _streamVms[streams[i].id] = vm;
            streamVmArray[i] = vm;
        }
        streamVmArray[streams.Length] = _logVm;

        InitializeEngines();

        // Attach window settings after all VMs exist so Settings props are set before any rendering.
        var streamTuples = streams.Select((s, i) => (s.id, s.title, streamVmArray[i]));
        AttachWindowSettings(_gameOutputVm, _rawOutputVm, _logVm, streamTuples);

        EnableMapperMenuItem.IsChecked = _mapperEngine.IsEnabled;

        _factory = new GenieDockFactory(_gameOutputVm, _rawOutputVm, streamVmArray, _roomVm);
        var layout = _factory.CreateLayout();
        _factory.InitLayout(layout);

        MainDockControl.Factory = _factory;
        MainDockControl.Layout  = layout;

        StatusBar.Attach(_gslGameState);
        _roomVm.Attach(_gslGameState);

        BuildWindowsMenu();

        InputBox.KeyDown += OnInputKeyDown;

        // Restore saved layout (window geometry + dock proportions).
        var savedLayout = _persistence.LoadLayout(DataPath("layout.json"));
        if (savedLayout != null) RestoreLayout(savedLayout);

        // Track restore-state geometry whenever the window is in Normal state
        // so we never persist maximized dimensions as the restore size.
        PositionChanged += (_, _) =>
        {
            if (WindowState == WindowState.Normal)
            {
                _restoreX = Position.X;
                _restoreY = Position.Y;
            }
        };
        SizeChanged += (_, args) =>
        {
            if (WindowState == WindowState.Normal)
            {
                _restoreWidth  = args.NewSize.Width;
                _restoreHeight = args.NewSize.Height;
            }
        };

        Closing += (_, _) => SaveData();

        // Demo output
        AppendOutput("Normal text");
        AppendOutput("\x1b[31mRed text\x1b[0m back to normal");
        AppendOutput("\x1b[32mGreen\x1b[0m and \x1b[34mBlue\x1b[0m");
        AppendOutput("\x1b[1mBold text\x1b[0m");

        // Auto-connect: if a profile is flagged, connect after the window is shown.
        var autoProfile = _profiles.GetAutoConnectProfile();
        if (autoProfile != null)
        {
            // Defer until the window is rendered so the UI is ready.
            Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
            {
                var password = _profiles.GetPassword(autoProfile);
                AppendOutput($"[auto-connecting: {autoProfile.Name}]");
                await ConnectProfileAsync(autoProfile, password);
            }, Avalonia.Threading.DispatcherPriority.Background);
        }
    }

    // Called for plain-text lines (demo output, echo, connection messages)
    internal void AppendOutput(string text)
    {
        var parsed = AnsiParser.Parse(text);
        ApplyHighlightAndAppend(parsed);
    }

    // Called for GSL-parsed lines with styled segments
    internal void AppendSegments(IReadOnlyList<GslSegment> segments)
    {
        var line = new RenderLine();
        foreach (var seg in segments)
        {
            var parsed = AnsiParser.Parse(seg.Text, seg.GslBold, seg.GslColor, seg.GslBackground);
            foreach (var span in parsed.Spans)
                line.Spans.Add(span);
        }

        // If any segment belongs to a preset with HighlightLine, colour the whole line.
        foreach (var seg in segments)
        {
            if (!string.IsNullOrEmpty(seg.GslColor) && _presets.GetHighlightLine(seg.GslColor))
            {
                var hlBg = _presets.GetBackground(seg.GslColor);
                line = ApplyRangeHighlight(line,
                    [(0, line.PlainText.Length)], seg.GslColor, hlBg);
                break;
            }
        }

        ApplyHighlightAndAppend(line);
    }

    private void ApplyHighlightAndAppend(RenderLine line)
    {
        var rule = _highlights.Match(line.PlainText);
        if (rule != null)
        {
            var ranges = rule.GetHighlightRanges(line.PlainText);
            line = ApplyRangeHighlight(line, ranges, rule.ForegroundColor, rule.BackgroundColor);
        }

        _scrollback.Add(line);
        _gameOutputVm.AppendLine(line);
    }

    // Colours only the characters within the given ranges; everything else keeps
    // its original colour.  If ranges covers the entire line the result is the
    // same as coloring every span (preserves the existing whole-line behaviour).
    private static RenderLine ApplyRangeHighlight(
        RenderLine line, IReadOnlyList<(int Start, int Length)> ranges, string color, string bgColor = "")
    {
        if (ranges.Count == 0) return line;

        var plain = line.PlainText;

        // Build a per-character boolean map of which positions should be coloured.
        var isHl = new bool[plain.Length];
        foreach (var (start, length) in ranges)
            for (int i = start; i < start + length && i < plain.Length; i++)
                isHl[i] = true;

        // Walk spans, splitting at highlight-boundary transitions.
        var result = new RenderLine();
        int pos = 0;

        foreach (var span in line.Spans)
        {
            int spanEnd = pos + span.Text.Length;
            int segStart = pos;
            bool segHl = pos < plain.Length && isHl[pos];

            for (int i = pos; i <= spanEnd; i++)
            {
                bool curHl = i < plain.Length && isHl[i];
                if (i == spanEnd || curHl != segHl)
                {
                    if (i > segStart)
                    {
                        result.Spans.Add(new AnsiSpan
                        {
                            Text       = span.Text.Substring(segStart - pos, i - segStart),
                            Foreground = segHl ? color : span.Foreground,
                            Background = segHl && !string.IsNullOrEmpty(bgColor) ? bgColor : span.Background,
                            Bold       = span.Bold,
                        });
                    }
                    segStart = i;
                    segHl    = curHl;
                }
            }

            pos = spanEnd;
        }

        return result;
    }

    // ── Layout persistence ────────────────────────────────────────────────────

    private void SaveLayout() => SaveLayoutTo(DataPath("layout.json"));

    private void SaveLayoutTo(string path)
    {
        var props = new Dictionary<string, double>();
        CollectProportions(MainDockControl.Layout, props);

        var state = new LayoutState
        {
            WindowState     = WindowState.ToString(),
            WindowX         = _restoreX,
            WindowY         = _restoreY,
            WindowWidth     = _restoreWidth,
            WindowHeight    = _restoreHeight,
            DockProportions = props,
        };

        _persistence.SaveLayout(path, state);
    }

    private void RestoreLayout(LayoutState state)
    {
        // Apply geometry first so the OS restore-bounds are correct.
        Position = new PixelPoint(state.WindowX, state.WindowY);
        Width    = state.WindowWidth;
        Height   = state.WindowHeight;

        if (Enum.TryParse<WindowState>(state.WindowState, out var ws))
            WindowState = ws;

        ApplyProportions(MainDockControl.Layout, state.DockProportions);
    }

    private static void CollectProportions(IDockable? dockable, Dictionary<string, double> result)
    {
        if (dockable is null) return;
        if (!string.IsNullOrEmpty(dockable.Id) && dockable.Proportion > 0)
            result[dockable.Id] = dockable.Proportion;
        if (dockable is IDock dock)
            foreach (var child in dock.VisibleDockables ?? [])
                CollectProportions(child, result);
    }

    private static void ApplyProportions(IDockable? dockable, Dictionary<string, double> saved)
    {
        if (dockable is null) return;
        if (!string.IsNullOrEmpty(dockable.Id) && saved.TryGetValue(dockable.Id, out var p))
            dockable.Proportion = p;
        if (dockable is IDock dock)
            foreach (var child in dock.VisibleDockables ?? [])
                ApplyProportions(child, saved);
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Return:
                SendInput();
                e.Handled = true;
                break;

            case Key.Up:
                if (_history.Count == 0) break;
                if (_historyIndex < 0) _historyIndex = _history.Count - 1;
                else if (_historyIndex > 0) _historyIndex--;
                InputBox.Text = _history[_historyIndex];
                InputBox.CaretIndex = InputBox.Text.Length;
                e.Handled = true;
                break;

            case Key.Down:
                if (_historyIndex < 0) break;
                _historyIndex++;
                if (_historyIndex >= _history.Count)
                {
                    _historyIndex = -1;
                    InputBox.Text = string.Empty;
                }
                else
                {
                    InputBox.Text = _history[_historyIndex];
                    InputBox.CaretIndex = InputBox.Text.Length;
                }
                e.Handled = true;
                break;
        }
    }

    private void SendInput()
    {
        var input = InputBox.Text ?? string.Empty;
        InputBox.Text = string.Empty;
        _historyIndex = -1;

        if (string.IsNullOrWhiteSpace(input)) return;

        if (_history.Count == 0 || _history[^1] != input)
            _history.Add(input);

        if (_variables.TryProcess(input)) return;

        var expanded = _variables.Expand(input);

        var echoLine = new RenderLine();
        echoLine.Spans.Add(new AnsiSpan
        {
            Text       = expanded,
            Foreground = _presets.GetForeground("inputuser"),
            Background = _presets.GetBackground("inputuser"),
        });
        ApplyHighlightAndAppend(echoLine);

        if (_mapper.TryHandleGoto(expanded)) return;
        if (TryHandleScriptCommand(expanded)) return;

        if (!_aliases.TryProcess(expanded))
            _engine.ProcessInput(expanded);
    }

    private async void OnMenuConnectProfile(object? sender, RoutedEventArgs e)
    {
        var dialog = new DialogProfileConnect(_profiles, ProfilesPath);
        var ok = await dialog.ShowDialog<bool>(this);
        if (!ok || dialog.SelectedProfile is null) return;

        var p        = dialog.SelectedProfile;
        var password = _profiles.GetPassword(p);
        await ConnectProfileAsync(p, password);
    }

    private async void OnMenuConnect(object? sender, RoutedEventArgs e)
    {
        var dialog = new DialogConnect(_lastGameCode, _lastHost, _lastPort, _profiles, ProfilesPath);
        var ok = await dialog.ShowDialog<bool>(this);
        if (!ok) return;

        _lastGameCode = dialog.IsSimutronics ? dialog.ResultGameCode : null;
        _scripts.Globals["game"] = _lastGameCode ?? string.Empty;
        _lastHost     = dialog.ResultHost;
        _lastPort     = dialog.ResultPort;

        if (dialog.IsSimutronics)
            await ConnectSgeAsync(dialog.ResultGameCode, dialog.ResultCharacter,
                                  dialog.ResultAccount, dialog.ResultPassword);
        else
            await ConnectDirectAsync(dialog.ResultHost, dialog.ResultPort);
    }

    private void OnMenuDisconnect(object? sender, RoutedEventArgs e) => _client.Disconnect();

    // Connect using a saved profile
    private async Task ConnectProfileAsync(ConnectionProfile p, string password)
    {
        if (p.IsSimutronics)
            await ConnectSgeAsync(p.GameCode, p.CharacterName, p.AccountName, password);
        else
            await ConnectDirectAsync(p.Host, p.Port);
    }

    // Simutronics SGE auth → game server
    private async Task ConnectSgeAsync(string gameCode, string character,
                                        string account, string password)
    {
        _lastGameCode = gameCode;
        _scripts.Globals["game"] = gameCode;
        AppendOutput($"[authenticating as {account} for {gameCode}...]");

        SgeLoginResult result;
        try
        {
            result = await new SgeClient().AuthenticateAsync(account, password, gameCode, character);
        }
        catch (Exception ex)
        {
            AppendOutput($"[authentication error: {ex.Message}]");
            return;
        }

        if (!result.Success)
        {
            AppendOutput($"[authentication failed: {result.Error}]");
            return;
        }

        // No character specified — show picker
        if (result.Characters.Count > 0 && string.IsNullOrEmpty(result.Key))
        {
            var picked = await PickCharacterAsync(result.Characters);
            if (picked is null) return;
            await ConnectSgeAsync(gameCode, picked, account, password);
            return;
        }

        AppendOutput($"[connecting to {result.GameHost}:{result.GamePort}]");
        try
        {
            await _client.ConnectWithKeyAsync(result.GameHost, result.GamePort, result.Key);
        }
        catch (Exception ex)
        {
            AppendOutput($"[connection failed: {ex.Message}]");
        }
    }

    // Direct TCP (non-Simutronics MUD)
    private async Task ConnectDirectAsync(string host, int port)
    {
        AppendOutput($"[connecting to {host}:{port}]");
        try
        {
            await _client.ConnectAsync(new Genie4.Core.Networking.GameConnectionOptions
                { Host = host, Port = port });
        }
        catch (Exception ex)
        {
            AppendOutput($"[connection failed: {ex.Message}]");
        }
    }

    // Simple character picker dialog (inline window)
    private async Task<string?> PickCharacterAsync(IReadOnlyList<string> characters)
    {
        var win = new Window
        {
            Title = "Select Character",
            Width = 280, Height = 260,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var list   = new ListBox { ItemsSource = characters, Margin = new(8) };
        var btn    = new Avalonia.Controls.Button
            { Content = "Connect", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
              Margin = new(8) };
        var panel  = new Avalonia.Controls.StackPanel();
        panel.Children.Add(list);
        panel.Children.Add(btn);
        win.Content = panel;

        string? picked = null;
        btn.Click += (_, _) =>
        {
            picked = list.SelectedItem as string;
            win.Close(picked is not null);
        };

        await win.ShowDialog<bool>(this);
        return picked;
    }

    private void OnSend(object? sender, RoutedEventArgs e) => SendInput();

    // Intercepts ".scriptname [args...]" launches and "#kill"/"#kill name" stops.
    private bool TryHandleScriptCommand(string input)
    {
        var t = input.TrimStart();
        if (t.Length == 0) return false;

        if (t[0] == '.')
        {
            var rest   = t[1..].Trim();
            if (rest.Length == 0) return false;
            var parts  = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var name   = parts[0];
            var args   = parts.Skip(1).ToList();
            _scripts.TryStart(name, args);
            return true;
        }

        if (t.StartsWith("#kill", StringComparison.OrdinalIgnoreCase))
        {
            var rest = t.Length > 5 ? t[5..].Trim() : string.Empty;
            if (string.IsNullOrEmpty(rest)) _scripts.StopAll();
            else                            _scripts.Stop(rest);
            return true;
        }

        return false;
    }

    private void OnSettings(object? sender, RoutedEventArgs e)
    {
        var config = new FormConfig(_aliases, _triggers, _highlights, _presets, _windowSettings);
        config.Show();
    }

    // All panels that can be toggled, paired with the dock that owns them.
    // Populated in BuildWindowsMenu after the factory is created.
    private readonly List<(Dock.Model.Core.IDockable vm, Dock.Model.Core.IDock owner)> _toggleablePanels = new();

    private void BuildWindowsMenu()
    {
        // Main-output dock panels
        foreach (var vm in new Dock.Model.Core.IDockable[] { _gameOutputVm, _rawOutputVm, _logVm })
        {
            _toggleablePanels.Add((vm, _factory.StreamsDock!.Owner as Dock.Model.Core.IDock ?? _factory.StreamsDock));
            var item = new MenuItem { Header = ((Dock.Model.Core.IDockable)vm).Title, Tag = vm };
            item.Click += OnWindowItemClick;
            WindowsMenu.Items.Add(item);
        }

        WindowsMenu.Items.Add(new Separator());

        // Stream panels
        foreach (var vm in _streamVms.Values)
        {
            _toggleablePanels.Add((vm, _factory.StreamsDock!));
            var item = new MenuItem { Header = vm.Title, Tag = vm };
            item.Click += OnWindowItemClick;
            WindowsMenu.Items.Add(item);
        }

        WindowsMenu.Items.Add(new Separator());

        // Right-panel dockables (Room)
        foreach (var vm in new Dock.Model.Core.IDockable[] { _roomVm })
        {
            _toggleablePanels.Add((vm, vm.Owner as Dock.Model.Core.IDock ?? _factory.StreamsDock!));
            var item = new MenuItem { Header = vm.Title, Tag = vm };
            item.Click += OnWindowItemClick;
            WindowsMenu.Items.Add(item);
        }
    }

    private void OnWindowsMenuOpened(object? sender, RoutedEventArgs e)
    {
        foreach (var menuItem in WindowsMenu.Items.OfType<MenuItem>())
        {
            if (menuItem.Tag is not Dock.Model.Core.IDockable vm) continue;
            var isVisible = vm.Owner is Dock.Model.Core.IDock dock &&
                            dock.VisibleDockables?.Contains(vm) == true;
            menuItem.Icon = new Avalonia.Controls.TextBlock
            {
                Text       = isVisible ? "✓" : " ",
                Foreground = Avalonia.Media.Brushes.White,
                Width      = 14,
            };
        }
    }

    private void OnWindowItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item) return;
        if (item.Tag is not Dock.Model.Core.IDockable vm) return;

        var isVisible = vm.Owner is Dock.Model.Core.IDock ownerDock &&
                        ownerDock.VisibleDockables?.Contains(vm) == true;

        if (isVisible)
        {
            _factory!.RemoveDockable(vm, collapse: false);
        }
        else
        {
            // Restore to the correct dock based on which panel this belongs to
            Dock.Model.Core.IDock targetDock;
            if (vm == _gameOutputVm || vm == _rawOutputVm || vm == _logVm)
                targetDock = _factory!.Find(d => d.Id == "MainOutput") as Dock.Model.Core.IDock
                             ?? _factory!.StreamsDock!;
            else if (vm == _roomVm)
                targetDock = _factory!.Find(d => d.Id == "RoomPanel") as Dock.Model.Core.IDock
                             ?? _factory!.StreamsDock!;
            else
                targetDock = _factory!.StreamsDock!;

            _factory!.AddDockable(targetDock, vm);
            _factory!.SetActiveDockable(vm);
        }
    }

    private async void OnMenuSaveLayoutAs(object? sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title           = "Save Layout As",
            DefaultExtension = "json",
            Filters          = [new FileDialogFilter { Name = "Layout files", Extensions = ["json"] }],
            InitialFileName  = "layout.json",
        };

        var path = await dialog.ShowAsync(this);
        if (string.IsNullOrEmpty(path)) return;

        SaveLayoutTo(path);
    }

    private async void OnMenuLoadLayout(object? sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title       = "Load Layout",
            AllowMultiple = false,
            Filters       = [new FileDialogFilter { Name = "Layout files", Extensions = ["json"] }],
        };

        var paths = await dialog.ShowAsync(this);
        if (paths is null || paths.Length == 0) return;

        var state = _persistence.LoadLayout(paths[0]);
        if (state != null) RestoreLayout(state);
    }

    private async void OnMenuImportMaps(object? sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select folder containing Genie4 .xml map files" };
        var dir = await dialog.ShowAsync(this);
        if (string.IsNullOrEmpty(dir)) return;

        var count = _mapper.ImportGenie4Maps(dir);
        if (count == 0)
            AppendOutput("[mapper] No .xml map files found in that folder.");
        else
            AppendOutput($"[mapper] Imported {count} map(s) to {DataPath("Maps")}");
    }

    private void OnMenuToggleMapper(object? sender, RoutedEventArgs e)
    {
        _mapperEngine.IsEnabled = EnableMapperMenuItem.IsChecked;
        AppendOutput($"[mapper] Automapper {(_mapperEngine.IsEnabled ? "enabled" : "disabled")}");
    }

    private void OnMenuShowMapWindow(object? sender, RoutedEventArgs e)
    {
        if (_mapWindow is null)
        {
            _mapWindow = new MapWindow();
            _mapWindow.Initialize(
                _mapperEngine,
                DataPath("Maps"),
                cmd => _engine.ProcessInput(cmd),
                _mapper.CurrentZonePath);
            _mapWindow.CurrentZonePathChanged = p => _mapper.SetCurrentZonePath(p);
            _mapWindow.Closed += (_, _) => _mapWindow = null;
            _mapWindow.Show();
        }
        else
        {
            _mapWindow.RefreshZoneList(_mapper.CurrentZonePath);
            _mapWindow.Activate();
        }
    }

    private void OnMenuExit(object? sender, RoutedEventArgs e) => Close();

    private void OnMenuGitHub(object? sender, RoutedEventArgs e)
    {
        var url = "https://github.com/dylb0t/Genie5";
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
        {
            UseShellExecute = true
        });
    }
}
