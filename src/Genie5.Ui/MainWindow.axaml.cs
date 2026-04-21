using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Dock.Model.Core;
using Genie4.Core.Gsl;
using Genie4.Core.Import;
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

    // ── Layout mode ─────────────────────────────────────────────────────────
    private bool _isMdiMode;

    // ── Auto-log ────────────────────────────────────────────────────────────
    private bool          _autoLogEnabled;
    private StreamWriter? _autoLogWriter;
    private string        _autoLogProfileName = string.Empty;
    private string        _autoLogDate        = string.Empty; // yyyy-MM-dd of the open file

    public MainWindow()
    {
        InitializeComponent();

        _gameOutputVm = new GameOutputViewModel("GameOutput", "Game Output", canClose: true);
        _rawOutputVm  = new GameOutputViewModel("RawOutput", "Raw",  canClose: true);
        _logVm        = new GameOutputViewModel("Log",       "Log",  canClose: true);

        // Create a dockable panel for each sub-stream
        var streams = new (string id, string title, bool hidden)[]
        {
            ("inv",           "Inventory",      false),
            ("familiar",      "Familiar",       false),
            ("thoughts",      "Thoughts",       false),
            ("logons",        "Arrivals",        false),
            ("death",         "Deaths",          false),
            ("talk",          "Talk",            true),
            ("whispers",      "Whispers",        true),
            ("conversation",  "Conversation",    true),
            ("assess",        "Assess",          true),
            ("group",         "Group",           true),
            ("atmospherics",  "Atmospherics",    true),
            ("ooc",           "OOC",             true),
            ("activeSpells",  "Active Spells",   true),
            ("expMods",       "ExpMods",         true),
            ("itemLog",       "ItemLog",         true),
            ("chatter",       "Chatter",         true),
            ("combat",        "Combat",          false),
            ("debug",         "Debug",           true),
            ("portrait",      "Portrait",        true),
            ("percWindow",    "Perception",      false),
        };
        var defaultHidden = new HashSet<string>();
        var streamVmArray = new GameOutputViewModel[streams.Length + 1];
        for (int i = 0; i < streams.Length; i++)
        {
            var vm = new GameOutputViewModel(streams[i].id, streams[i].title, canClose: true);
            _streamVms[streams[i].id] = vm;
            streamVmArray[i] = vm;
            if (streams[i].hidden) defaultHidden.Add(streams[i].id);
        }
        streamVmArray[streams.Length] = _logVm;

        InitializeEngines();

        // Attach window settings after all VMs exist so Settings props are set before any rendering.
        var streamTuples = streams.Select((s, i) => (id: s.id, title: s.title, vm: streamVmArray[i]));
        AttachWindowSettings(_gameOutputVm, _rawOutputVm, _logVm, streamTuples);

        LoadClientState();

        UpdateLayoutMenuChecks();

        _factory = new GenieDockFactory(_gameOutputVm, _rawOutputVm, streamVmArray, _roomVm);
        _factory.IsMdiMode = _isMdiMode;
        var layout = _factory.CreateLayout();
        _factory.InitLayout(layout);

        MainDockControl.Factory = _factory;
        MainDockControl.Layout  = layout;

        foreach (var id in defaultHidden)
            if (_streamVms.TryGetValue(id, out var hiddenVm))
                _factory.RemoveDockable(hiddenVm, collapse: false);

        StatusBar.Attach(_gslGameState);
        StatusBar.ApplyPresets(_presets);
        _roomVm.Attach(_gslGameState);

        BuildWindowsMenu();

        InputBox.KeyDown += OnInputKeyDown;

        // Focus the input box on first show and whenever the window regains
        // activation, so typing always lands there without an explicit click.
        Opened    += (_, _) => InputBox.Focus();
        Activated += (_, _) => InputBox.Focus();

        // Restore saved layout (window geometry + dock proportions).
        // At startup no profile is connected, so this loads the default layout.
        var savedLayout = _persistence.LoadLayout(DefaultLayoutPath());
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
        // Substitutes: rewrite plain text before highlighting. If any rule
        // changes the text we flatten to a single default-colored span, since
        // the replaced text no longer aligns to the original ANSI/GSL spans.
        if (_substitutes.Rules.Count > 0)
        {
            var original = line.PlainText;
            var replaced = _substitutes.Apply(original);
            if (!ReferenceEquals(replaced, original) && replaced != original)
            {
                line = new RenderLine();
                line.Spans.Add(new AnsiSpan { Text = replaced, Foreground = "Default" });
            }
        }

        // Gags: suppress the line entirely if any enabled rule matches.
        if (_gags.ShouldGag(line.PlainText)) return;

        var rule = _highlights.Match(line.PlainText);
        if (rule != null)
        {
            var ranges = rule.GetHighlightRanges(line.PlainText);
            line = ApplyRangeHighlight(line, ranges, rule.ForegroundColor, rule.BackgroundColor);
        }

        // Name highlights win over generic ones: apply per-name so each name
        // keeps its own color.
        foreach (var group in _nameHighlights.MatchAll(line.PlainText)
                                             .GroupBy(m => (m.Rule.ForegroundColor, m.Rule.BackgroundColor)))
        {
            var ranges = group.Select(m => (m.Start, m.Length)).ToList();
            line = ApplyRangeHighlight(line, ranges, group.Key.ForegroundColor, group.Key.BackgroundColor);
        }

        _scrollback.Add(line);
        _gameOutputVm.AppendLine(line);
        AutoLog(line.PlainText);
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

    /// <summary>Active profile name (used to pick per-profile layout file).</summary>
    private string _currentProfileName = string.Empty;

    private string DefaultLayoutPath() => ConfigPath("Layout/layout.json");
    private string ProfileLayoutPath(string profile) =>
        ConfigPath($"Layout/{SanitizeFilename(profile)}.json");
    private string CurrentLayoutPath() =>
        string.IsNullOrEmpty(_currentProfileName)
            ? DefaultLayoutPath()
            : ProfileLayoutPath(_currentProfileName);

    private static string SanitizeFilename(string s)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var chars = s.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars);
    }

    /// <summary>Returns every window ViewModel managed by the factory (order matters).</summary>
    private IEnumerable<Dock.Model.Core.IDockable> AllDockables()
    {
        yield return _gameOutputVm;
        yield return _rawOutputVm;
        yield return _logVm;
        foreach (var vm in _streamVms.Values) yield return vm;
        yield return _roomVm;
    }

    private void SaveLayout() => SaveLayoutTo(CurrentLayoutPath());

    private void SaveLayoutTo(string path)
    {
        var props = new Dictionary<string, double>();
        CollectProportions(MainDockControl.Layout, props);

        var mdiBounds   = new Dictionary<string, MdiWindowBounds>();
        var hidden      = new List<string>();
        foreach (var vm in AllDockables())
        {
            if (string.IsNullOrEmpty(vm.Id)) continue;

            // MDI bounds (only meaningful when the VM has been laid out in MDI mode).
            if (vm is Dock.Model.Controls.IMdiDocument mdi)
            {
                var r = mdi.MdiBounds;
                if (r.Width > 0 && r.Height > 0)
                {
                    mdiBounds[vm.Id] = new MdiWindowBounds
                    {
                        X      = r.X,
                        Y      = r.Y,
                        Width  = r.Width,
                        Height = r.Height,
                        State  = mdi.MdiState.ToString(),
                    };
                }
            }

            // Visibility: a VM is "hidden" if its owner dock does not contain it.
            bool visible = vm.Owner is Dock.Model.Core.IDock d &&
                           d.VisibleDockables?.Contains(vm) == true;
            if (!visible) hidden.Add(vm.Id);
        }

        var state = new LayoutState
        {
            WindowState     = WindowState.ToString(),
            WindowX         = _restoreX,
            WindowY         = _restoreY,
            WindowWidth     = _restoreWidth,
            WindowHeight    = _restoreHeight,
            DockProportions = props,
            LayoutMode      = _isMdiMode ? "Mdi" : "Tabbed",
            MdiBounds       = mdiBounds,
            HiddenDockables = hidden,
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

        // Switch layout mode if the saved state differs from current.
        bool wantMdi = string.Equals(state.LayoutMode, "Mdi", StringComparison.OrdinalIgnoreCase);
        if (wantMdi != _isMdiMode)
            SwitchLayoutMode(wantMdi);

        ApplyProportions(MainDockControl.Layout, state.DockProportions);

        // Restore MDI bounds & state per dockable.
        if (state.MdiBounds is { Count: > 0 })
        {
            foreach (var vm in AllDockables())
            {
                if (string.IsNullOrEmpty(vm.Id)) continue;
                if (!state.MdiBounds.TryGetValue(vm.Id, out var b)) continue;
                if (vm is Dock.Model.Controls.IMdiDocument mdi)
                {
                    mdi.MdiBounds = new Dock.Model.Core.DockRect(b.X, b.Y, b.Width, b.Height);
                    if (Enum.TryParse<Dock.Model.Core.MdiWindowState>(b.State, out var mst))
                        mdi.MdiState = mst;
                }
            }
        }

        // Apply hidden-dockables list: any VM in the list but currently visible → hide it.
        // Any VM currently visible but NOT in the list → keep visible (no action needed).
        // Any VM hidden but not in the list → restore (add back).
        var hiddenSet = new HashSet<string>(state.HiddenDockables ?? new List<string>());
        foreach (var vm in AllDockables())
        {
            if (string.IsNullOrEmpty(vm.Id)) continue;

            bool isVisible = vm.Owner is Dock.Model.Core.IDock d &&
                             d.VisibleDockables?.Contains(vm) == true;

            if (hiddenSet.Contains(vm.Id) && isVisible)
            {
                _factory.RemoveDockable(vm, collapse: false);
            }
            else if (!hiddenSet.Contains(vm.Id) && !isVisible)
            {
                // Re-add to the correct dock based on mode.
                Dock.Model.Core.IDock? target = _isMdiMode
                    ? _factory.MdiDock
                    : (vm == _gameOutputVm || vm == _rawOutputVm || vm == _logVm
                        ? _factory.Find(x => x.Id == "MainOutput") as Dock.Model.Core.IDock
                          ?? _factory.StreamsDock
                        : vm == _roomVm
                            ? _factory.Find(x => x.Id == "RoomPanel") as Dock.Model.Core.IDock
                              ?? _factory.StreamsDock
                            : _factory.StreamsDock);
                if (target != null) _factory.AddDockable(target, vm);
            }
        }
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

    private void UpdateLayoutMenuChecks()
    {
        LayoutTabbedMenuItem.Icon = new Avalonia.Controls.TextBlock
        {
            Text = _isMdiMode ? " " : "✓", Foreground = Avalonia.Media.Brushes.White, Width = 14,
        };
        LayoutMdiMenuItem.Icon = new Avalonia.Controls.TextBlock
        {
            Text = _isMdiMode ? "✓" : " ", Foreground = Avalonia.Media.Brushes.White, Width = 14,
        };
    }

    private void SwitchLayoutMode(bool mdi)
    {
        if (_isMdiMode == mdi) return;
        _isMdiMode = mdi;

        UpdateLayoutMenuChecks();

        // Rebuild the dock layout from scratch.
        _factory.IsMdiMode = mdi;
        var layout = _factory.CreateLayout();
        _factory.InitLayout(layout);

        MainDockControl.Layout = layout;

        // Rebuild the Windows menu to point at the new dock containers.
        _toggleablePanels.Clear();
        WindowsMenu.Items.Clear();
        BuildWindowsMenu();

        SaveClientState();
        AppendOutput($"[layout] Switched to {(mdi ? "MDI" : "Tabbed")} mode");
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (TryRunMacro(e)) { e.Handled = true; return; }

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

    // Returns true if the key event matched a configured macro and was sent.
    // Mirrors Genie4's macros.cfg key-string format: the .NET Keys enum name,
    // with modifiers prepended as "Modifier, Key" (e.g. "F1", "Control, F2").
    private bool TryRunMacro(KeyEventArgs e)
    {
        if (_macros is null || _macros.Rules.Count == 0) return false;
        var keyName = e.Key.ToString();
        if (string.IsNullOrEmpty(keyName) || keyName == "None") return false;

        var mods = e.KeyModifiers;
        var ctrl  = (mods & KeyModifiers.Control) != 0;
        var alt   = (mods & KeyModifiers.Alt)     != 0;
        var shift = (mods & KeyModifiers.Shift)   != 0;

        // Don't hijack plain letter/digit/space/enter/nav keys with no modifier.
        if (!ctrl && !alt && !shift && IsTypingKey(e.Key)) return false;

        // Genie4 canonical form: "Mod1, Mod2, Key"
        var parts = new List<string>();
        if (ctrl)  parts.Add("Control");
        if (alt)   parts.Add("Alt");
        if (shift) parts.Add("Shift");
        parts.Add(keyName);
        var canonical = string.Join(", ", parts);

        var rule = _macros.Get(canonical);
        // Also accept modifier-less "F1" style for Avalonia key names.
        if (rule is null && parts.Count == 1) rule = _macros.Get(keyName);
        if (rule is null) return false;

        if (!string.IsNullOrEmpty(rule.Action))
            _engine.ProcessInput(rule.Action);
        return true;
    }

    private static bool IsTypingKey(Key k)
    {
        if (k >= Key.A && k <= Key.Z) return true;
        if (k >= Key.D0 && k <= Key.D9) return true;
        if (k == Key.Space || k == Key.Back || k == Key.Delete) return true;
        if (k == Key.Left || k == Key.Right || k == Key.Home || k == Key.End) return true;
        if (k == Key.Tab) return true;
        return false;
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
        if (TryHandleClassCommand(expanded)) return;

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
        {
            SetAutoLogProfile($"{dialog.ResultCharacter}{dialog.ResultGameCode}");
            await ConnectSgeAsync(dialog.ResultGameCode, dialog.ResultCharacter,
                                  dialog.ResultAccount, dialog.ResultPassword);
        }
        else
        {
            SetAutoLogProfile($"{dialog.ResultHost}_{dialog.ResultPort}");
            await ConnectDirectAsync(dialog.ResultHost, dialog.ResultPort);
        }
    }

    private void OnMenuDisconnect(object? sender, RoutedEventArgs e) => _client.Disconnect();

    // Connect using a saved profile
    private async Task ConnectProfileAsync(ConnectionProfile p, string password)
    {
        SetAutoLogProfile(p.Name);
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

    // Handles Genie4 #class commands: listing, toggling, and bulk activation.
    //   #class                         → list all classes
    //   #class <name>                  → show single class state
    //   #class <name> on|off|true|false|1|0|yes|no
    //   #class all on|off|...          → activate/deactivate every class
    //   #class +name +other -name      → batch +/- syntax (+ activates, - deactivates)
    //   #class +all | -all             → all on / all off via +/- syntax
    private bool TryHandleClassCommand(string input)
    {
        var t = input.TrimStart();
        if (!t.StartsWith("#class", StringComparison.OrdinalIgnoreCase)) return false;
        // Only treat as a class command when the directive is "#class" or
        // "#classes" (not some other "#classXyz" we haven't planned for).
        int dirLen;
        if (t.StartsWith("#classes", StringComparison.OrdinalIgnoreCase)) dirLen = 8;
        else                                                              dirLen = 6;
        if (t.Length > dirLen && t[dirLen] != ' ' && t[dirLen] != '\t') return false;

        var rest = t.Length > dirLen ? t[dirLen..].Trim() : string.Empty;
        var tokens = rest.Length == 0
            ? Array.Empty<string>()
            : rest.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length == 0)
        {
            EchoClassList(null);
            return true;
        }

        // Batch +/- syntax: all tokens must start with + or -.
        if (tokens[0].StartsWith('+') || tokens[0].StartsWith('-'))
        {
            int changed = 0;
            foreach (var tok in tokens)
            {
                if (tok.Length < 2 || (tok[0] != '+' && tok[0] != '-')) continue;
                var name = tok[1..];
                bool activate = tok[0] == '+';
                if (name.Equals("all", StringComparison.OrdinalIgnoreCase))
                {
                    if (activate) _classes.ActivateAll(); else _classes.DeactivateAll();
                }
                else
                {
                    _classes.Set(name, activate);
                }
                changed++;
            }
            SaveClasses();
            AppendOutput($"[class] {changed} change(s) applied.");
            return true;
        }

        if (tokens.Length == 1)
        {
            EchoClassList(tokens[0]);
            return true;
        }

        var first    = tokens[0];
        var stateRaw = tokens[1].ToLowerInvariant();
        bool? active = stateRaw switch
        {
            "on"  or "true"  or "1" or "yes" => true,
            "off" or "false" or "0" or "no"  => false,
            _                                 => null,
        };

        if (active is null)
        {
            AppendOutput($"[class] Unknown state '{tokens[1]}'. Use on|off.");
            return true;
        }

        if (first.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            if (active.Value) _classes.ActivateAll(); else _classes.DeactivateAll();
            SaveClasses();
            AppendOutput($"[class] all {(active.Value ? "on" : "off")}");
            return true;
        }

        _classes.Set(first, active.Value);
        SaveClasses();
        AppendOutput($"[class] {first} {(active.Value ? "on" : "off")}");
        return true;
    }

    private void EchoClassList(string? filter)
    {
        var all = _classes.GetAll();
        if (all.Count == 0)
        {
            AppendOutput("[class] (no classes)");
            return;
        }
        foreach (var kv in all)
        {
            if (!string.IsNullOrEmpty(filter) &&
                !kv.Key.Contains(filter, StringComparison.OrdinalIgnoreCase))
                continue;
            AppendOutput($"[class] {kv.Key} = {(kv.Value ? "on" : "off")}");
        }
    }

    private void OnSettings(object? sender, RoutedEventArgs e)
    {
        var config = new FormConfig(_aliases, _triggers, _highlights, _nameHighlights,
                                    _presets, _windowSettings,
                                    _variables.Store,
                                    _substitutes, _gags, _macros,
                                    _classes,
                                    () => ConfigPath("names.json"),
                                    SyncVariablesToScriptGlobals,
                                    SaveNames,
                                    () => StatusBar.ApplyPresets(_presets),
                                    SaveSubstitutes,
                                    SaveGags,
                                    SaveMacros,
                                    SaveClasses);
        config.Show();
    }

    private void SaveNames()
        => _persistence.SaveNames(ConfigPath("names.json"), _nameHighlights.Rules);

    private void SaveSubstitutes()
        => _persistence.SaveSubstitutes(ConfigPath("substitutes.json"), _substitutes.Rules);

    private void SaveGags()
        => _persistence.SaveGags(ConfigPath("gags.json"), _gags.Rules);

    private void SaveMacros()
        => _persistence.SaveMacros(ConfigPath("macros.json"), _macros.Rules);

    private void SaveClasses()
        => _persistence.SaveClasses(ConfigPath("classes.json"), _classes);

    // Mirror user-defined variables into the script engine's Globals dict so
    // scripts can read them as $name alongside engine-set values (roomid,
    // righthand, etc.).
    private void SyncVariablesToScriptGlobals()
    {
        foreach (var v in _variables.Store.GetAll().Values)
            _scripts.Globals[v.Name] = v.Value;
    }

    // All panels that can be toggled, paired with the dock that owns them.
    // Populated in BuildWindowsMenu after the factory is created.
    private readonly List<(Dock.Model.Core.IDockable vm, Dock.Model.Core.IDock owner)> _toggleablePanels = new();

    private void BuildWindowsMenu()
    {
        if (_isMdiMode)
        {
            // MDI: all windows live in MdiDock
            var mdiDock = _factory.MdiDock!;
            foreach (var vm in new Dock.Model.Core.IDockable[] { _gameOutputVm, _rawOutputVm, _logVm })
            {
                _toggleablePanels.Add((vm, mdiDock));
                var item = new MenuItem { Header = ((Dock.Model.Core.IDockable)vm).Title, Tag = vm };
                item.Click += OnWindowItemClick;
                WindowsMenu.Items.Add(item);
            }
            WindowsMenu.Items.Add(new Separator());
            foreach (var vm in _streamVms.Values)
            {
                _toggleablePanels.Add((vm, mdiDock));
                var item = new MenuItem { Header = vm.Title, Tag = vm };
                item.Click += OnWindowItemClick;
                WindowsMenu.Items.Add(item);
            }
            WindowsMenu.Items.Add(new Separator());
            _toggleablePanels.Add((_roomVm, mdiDock));
            var roomItem = new MenuItem { Header = _roomVm.Title, Tag = _roomVm };
            roomItem.Click += OnWindowItemClick;
            WindowsMenu.Items.Add(roomItem);
        }
        else
        {
            // Tabbed: panels split across MainOutput, Streams, RoomPanel
            foreach (var vm in new Dock.Model.Core.IDockable[] { _gameOutputVm, _rawOutputVm, _logVm })
            {
                _toggleablePanels.Add((vm, _factory.StreamsDock!.Owner as Dock.Model.Core.IDock ?? _factory.StreamsDock));
                var item = new MenuItem { Header = ((Dock.Model.Core.IDockable)vm).Title, Tag = vm };
                item.Click += OnWindowItemClick;
                WindowsMenu.Items.Add(item);
            }
            WindowsMenu.Items.Add(new Separator());
            foreach (var vm in _streamVms.Values)
            {
                _toggleablePanels.Add((vm, _factory.StreamsDock!));
                var item = new MenuItem { Header = vm.Title, Tag = vm };
                item.Click += OnWindowItemClick;
                WindowsMenu.Items.Add(item);
            }
            WindowsMenu.Items.Add(new Separator());
            foreach (var vm in new Dock.Model.Core.IDockable[] { _roomVm })
            {
                _toggleablePanels.Add((vm, vm.Owner as Dock.Model.Core.IDock ?? _factory.StreamsDock!));
                var item = new MenuItem { Header = vm.Title, Tag = vm };
                item.Click += OnWindowItemClick;
                WindowsMenu.Items.Add(item);
            }
        }
    }

    private void OnWindowsMenuOpened(object? sender, RoutedEventArgs e)
    {
        foreach (var menuItem in WindowsMenu.Items.OfType<MenuItem>())
        {
            if (menuItem.Tag is not Dock.Model.Core.IDockable vm) continue;
            bool isVisible;
            if (_isMdiMode)
            {
                isVisible = _factory.MdiDock?.VisibleDockables?.Contains(vm) == true;
            }
            else
            {
                isVisible = vm.Owner is Dock.Model.Core.IDock dock &&
                            dock.VisibleDockables?.Contains(vm) == true;
            }
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
            Dock.Model.Core.IDock targetDock;
            if (_isMdiMode)
            {
                targetDock = _factory!.MdiDock!;
            }
            else
            {
                // Restore to the correct dock based on which panel this belongs to
                if (vm == _gameOutputVm || vm == _rawOutputVm || vm == _logVm)
                    targetDock = _factory!.Find(d => d.Id == "MainOutput") as Dock.Model.Core.IDock
                                 ?? _factory!.StreamsDock!;
                else if (vm == _roomVm)
                    targetDock = _factory!.Find(d => d.Id == "RoomPanel") as Dock.Model.Core.IDock
                                 ?? _factory!.StreamsDock!;
                else
                    targetDock = _factory!.StreamsDock!;
            }

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
            Directory        = ConfigDir,
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
            Directory   = ConfigDir,
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

    private async void OnMenuImportGenie4Config(object? sender, RoutedEventArgs e)
    {
        var dialog = new DialogImportGenie4();
        var result = await dialog.ShowDialog<DialogImportGenie4.Result?>(this);
        if (result is null) return;

        var ctx = new Genie4ImportContext
        {
            Aliases     = _aliases,
            Triggers    = _triggers,
            Highlights  = _highlights,
            Substitutes = _substitutes,
            Gags        = _gags,
            Macros      = _macros,
            Names       = _nameHighlights,
            Presets     = _presets,
            Variables   = _variables.Store,
            Classes     = _classes,
        };

        var agg = new ImportAllResult();

        foreach (var (type, overridePath) in result.IndividualFiles)
        {
            if (overridePath is not null)
                ImportSingle(type, overridePath, ctx, result.Mode, agg);
        }

        // Bulk dir import for any selected types that didn't have a file override
        var dirTypes = Genie4ImportTypes.None;
        foreach (var (type, overridePath) in result.IndividualFiles)
            if (overridePath is null) dirTypes |= type;

        if (dirTypes != Genie4ImportTypes.None && Directory.Exists(result.Directory))
        {
            var dirResult = Genie4Importer.ImportDirectory(result.Directory, ctx, result.Mode, dirTypes);
            MergeInto(agg, dirResult);
        }

        SaveData();
        SyncVariablesToScriptGlobals();
        StatusBar.ApplyPresets(_presets);

        AppendOutput(FormatImportSummary(agg));
    }

    private static void ImportSingle(Genie4ImportTypes type, string path,
                                     Genie4ImportContext ctx, ImportMode mode, ImportAllResult agg)
    {
        switch (type)
        {
            case Genie4ImportTypes.Aliases:     agg.Aliases     = Genie4Importer.ImportAliases    (path, ctx.Aliases,     mode); break;
            case Genie4ImportTypes.Triggers:    agg.Triggers    = Genie4Importer.ImportTriggers   (path, ctx.Triggers,    mode); break;
            case Genie4ImportTypes.Highlights:  agg.Highlights  = Genie4Importer.ImportHighlights (path, ctx.Highlights,  mode); break;
            case Genie4ImportTypes.Substitutes: agg.Substitutes = Genie4Importer.ImportSubstitutes(path, ctx.Substitutes, mode); break;
            case Genie4ImportTypes.Gags:        agg.Gags        = Genie4Importer.ImportGags       (path, ctx.Gags,        mode); break;
            case Genie4ImportTypes.Macros:      agg.Macros      = Genie4Importer.ImportMacros     (path, ctx.Macros,      mode); break;
            case Genie4ImportTypes.Names:       agg.Names       = Genie4Importer.ImportNames      (path, ctx.Names,       mode); break;
            case Genie4ImportTypes.Presets:     agg.Presets     = Genie4Importer.ImportPresets    (path, ctx.Presets,     mode); break;
            case Genie4ImportTypes.Variables:   agg.Variables   = Genie4Importer.ImportVariables  (path, ctx.Variables,   mode); break;
            case Genie4ImportTypes.Classes:     agg.Classes     = Genie4Importer.ImportClasses    (path, ctx.Classes,     mode); break;
        }
    }

    private static void MergeInto(ImportAllResult a, ImportAllResult b)
    {
        a.Aliases     = new ImportResult(a.Aliases.Imported     + b.Aliases.Imported,     a.Aliases.Skipped     + b.Aliases.Skipped);
        a.Triggers    = new ImportResult(a.Triggers.Imported    + b.Triggers.Imported,    a.Triggers.Skipped    + b.Triggers.Skipped);
        a.Highlights  = new ImportResult(a.Highlights.Imported  + b.Highlights.Imported,  a.Highlights.Skipped  + b.Highlights.Skipped);
        a.Substitutes = new ImportResult(a.Substitutes.Imported + b.Substitutes.Imported, a.Substitutes.Skipped + b.Substitutes.Skipped);
        a.Gags        = new ImportResult(a.Gags.Imported        + b.Gags.Imported,        a.Gags.Skipped        + b.Gags.Skipped);
        a.Macros      = new ImportResult(a.Macros.Imported      + b.Macros.Imported,      a.Macros.Skipped      + b.Macros.Skipped);
        a.Names       = new ImportResult(a.Names.Imported       + b.Names.Imported,       a.Names.Skipped       + b.Names.Skipped);
        a.Presets     = new ImportResult(a.Presets.Imported     + b.Presets.Imported,     a.Presets.Skipped     + b.Presets.Skipped);
        a.Variables   = new ImportResult(a.Variables.Imported   + b.Variables.Imported,   a.Variables.Skipped   + b.Variables.Skipped);
        a.Classes     = new ImportResult(a.Classes.Imported     + b.Classes.Imported,     a.Classes.Skipped     + b.Classes.Skipped);
        a.MissingFiles.AddRange(b.MissingFiles);
    }

    private static string FormatImportSummary(ImportAllResult r)
    {
        var parts = new List<string>();
        void Add(string label, ImportResult x)
        {
            if (x.Imported == 0 && x.Skipped == 0) return;
            parts.Add(x.Skipped > 0
                ? $"{x.Imported} {label} ({x.Skipped} skipped)"
                : $"{x.Imported} {label}");
        }
        Add("aliases",     r.Aliases);
        Add("triggers",    r.Triggers);
        Add("highlights",  r.Highlights);
        Add("substitutes", r.Substitutes);
        Add("gags",        r.Gags);
        Add("macros",      r.Macros);
        Add("names",       r.Names);
        Add("presets",     r.Presets);
        Add("variables",   r.Variables);
        Add("classes",     r.Classes);

        var body = parts.Count == 0 ? "nothing imported" : string.Join(", ", parts);
        return $"[import] {body}.";
    }

    private void OnMenuPauseAllScripts(object? sender, RoutedEventArgs e) => _scripts.PauseAll();
    private void OnMenuResumeAllScripts(object? sender, RoutedEventArgs e) => _scripts.ResumeAll();
    private void OnMenuAbortAllScripts(object? sender, RoutedEventArgs e) => _scripts.StopAll();

    private async void OnMenuMapperScriptSettings(object? sender, RoutedEventArgs e)
    {
        var dialog = new DialogMapperScriptSettings(
            _mapper.UseScriptForGoto, _mapper.GotoScriptName);
        var ok = await dialog.ShowDialog<bool>(this);
        if (!ok) return;
        _mapper.UseScriptForGoto = dialog.ResultUseScript;
        _mapper.GotoScriptName   = dialog.ResultScriptName;
        SaveClientState();
        AppendOutput($"[mapper] Script mode: {(_mapper.UseScriptForGoto ? $"ON ({_mapper.GotoScriptName})" : "OFF (built-in engine)")}");
    }

    private void OnMenuToggleMapper(object? sender, RoutedEventArgs e)
    {
        _mapperEngine.IsEnabled = EnableMapperMenuItem.IsChecked;
        SaveClientState();
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

    private void OnMenuLayoutTabbed(object? sender, RoutedEventArgs e) => SwitchLayoutMode(mdi: false);
    private void OnMenuLayoutMdi(object? sender, RoutedEventArgs e)    => SwitchLayoutMode(mdi: true);

    private void OnMenuExit(object? sender, RoutedEventArgs e) => Close();

    private void OnMenuGitHub(object? sender, RoutedEventArgs e)
    {
        var url = "https://github.com/dylb0t/Genie5";
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
        {
            UseShellExecute = true
        });
    }

    // ── Client state (persisted UI toggles) ────────────────────────────────

    private string ClientStatePath => ConfigPath("clientstate.json");
    private string AutoLogDir      => DataPath("Logs");

    private void LoadClientState()
    {
        var state = _persistence.LoadClientState(ClientStatePath);
        _autoLogEnabled = state.AutoLogEnabled;
        AutoLogMenuItem.IsChecked = _autoLogEnabled;
        _mapperEngine.IsEnabled = state.MapperEnabled;
        EnableMapperMenuItem.IsChecked = state.MapperEnabled;
        _mapper.Debug = state.MapperDebug;
        _mapper.UseScriptForGoto = state.MapperUseScript;
        _mapper.GotoScriptName   = string.IsNullOrEmpty(state.MapperScriptName)
            ? "automapper" : state.MapperScriptName;
        _isMdiMode = string.Equals(state.LayoutMode, "Mdi", StringComparison.OrdinalIgnoreCase);
    }

    private void SaveClientState()
    {
        _persistence.SaveClientState(ClientStatePath, new Genie4.Core.Persistence.ClientState
        {
            AutoLogEnabled   = _autoLogEnabled,
            MapperEnabled    = _mapperEngine.IsEnabled,
            MapperDebug      = _mapper.Debug,
            MapperUseScript  = _mapper.UseScriptForGoto,
            MapperScriptName = _mapper.GotoScriptName,
            LayoutMode       = _isMdiMode ? "Mdi" : "Tabbed",
        });
    }

    // ── Auto-log ────────────────────────────────────────────────────────────

    private void OnMenuToggleAutoLog(object? sender, RoutedEventArgs e)
    {
        _autoLogEnabled = AutoLogMenuItem.IsChecked;
        SaveClientState();

        if (_autoLogEnabled)
            EnsureAutoLogOpen();
        else
            CloseAutoLog();

        AppendOutput($"[log] Auto-log {(_autoLogEnabled ? "enabled" : "disabled")}");
    }

    /// <summary>
    /// Set the active profile name for log filenames. Call this when a
    /// profile connects (before logging starts).  Also loads the profile's
    /// saved layout if one exists; otherwise leaves the current layout alone.
    /// </summary>
    private void SetAutoLogProfile(string profileName)
    {
        // Before switching profiles, persist the current (default or previous
        // profile) layout so we don't lose unsaved changes.
        SaveLayout();

        _autoLogProfileName = profileName;
        _currentProfileName = profileName;
        if (_autoLogEnabled) EnsureAutoLogOpen();

        // Load per-profile layout if one exists.
        var profileLayout = _persistence.LoadLayout(ProfileLayoutPath(profileName));
        if (profileLayout != null)
        {
            RestoreLayout(profileLayout);
            AppendOutput($"[layout] Loaded saved layout for profile '{profileName}'");
        }
    }

    private void EnsureAutoLogOpen()
    {
        if (string.IsNullOrEmpty(_autoLogProfileName)) return;
        var today = DateTime.Now.ToString("yyyy-MM-dd");

        // Already open for the right profile+date?
        if (_autoLogWriter != null && _autoLogDate == today) return;

        CloseAutoLog();

        Directory.CreateDirectory(AutoLogDir);
        var path = Path.Combine(AutoLogDir, $"{_autoLogProfileName}_{today}.txt");
        _autoLogDate = today;
        try
        {
            _autoLogWriter = new StreamWriter(path, append: true) { AutoFlush = true };
        }
        catch (Exception ex)
        {
            AppendOutput($"[log] Failed to open log file: {ex.Message}");
        }
    }

    private void CloseAutoLog()
    {
        _autoLogWriter?.Dispose();
        _autoLogWriter = null;
        _autoLogDate   = string.Empty;
    }

    /// <summary>Write a plain-text line to the auto-log if enabled.</summary>
    private void AutoLog(string plainText)
    {
        if (!_autoLogEnabled || _autoLogWriter == null) return;

        // Roll to a new file at midnight.
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        if (today != _autoLogDate)
            EnsureAutoLogOpen();

        try { _autoLogWriter?.WriteLine(plainText); }
        catch { /* best-effort; don't crash on I/O errors */ }
    }
}
