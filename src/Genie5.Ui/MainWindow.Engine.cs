using System;
using Genie4.Core.Aliases;
using Genie4.Core.Classes;
using Genie4.Core.Commanding;
using Genie4.Core.Config;
using Genie4.Core.Gags;
using Genie4.Core.Gsl;
using Genie4.Core.Highlights;
using Genie4.Core.Layout;
using Genie4.Core.Macros;
using Genie4.Core.Mapper;
using Genie4.Core.Networking;
using Genie4.Core.Persistence;
using Genie4.Core.Profiles;
using Genie4.Core.Queue;
using Genie4.Core.Runtime;
using Genie4.Core.Scripting;
using Genie4.Core.Substitutes;
using Genie4.Core.Triggers;
using Genie4.Core.Variables;

namespace Genie5.Ui;

public partial class MainWindow
{
    private readonly TcpGameClient _client = new();
    private CommandEngine _engine = null!;
    private TriggerEngineFinal _triggers = null!;
    private AliasEngine _aliases = null!;
    private VariableEngine _variables = null!;
    internal HighlightEngine _highlights = null!;
    internal NameHighlightEngine _nameHighlights = null!;
    internal SubstituteEngine _substitutes = null!;
    internal GagEngine        _gags        = null!;
    internal MacroEngine      _macros      = null!;
    internal ClassEngine      _classes     = null!;
    internal Genie4.Core.Presets.PresetEngine _presets = null!;
    internal GslParser    _gslParser    = null!;
    internal readonly GslGameState _gslGameState = new();

    private LocalDirectoryService _dirService = null!;
    private readonly PersistenceService _persistence = new();
    internal readonly ProfileStore _profiles = new();
    internal AutoMapperEngine _mapperEngine = null!;
    private readonly MapZoneRepository _mapRepo = new();
    internal MapperController _mapper = null!;
    internal readonly WindowSettingsStore _windowSettings = new();
    internal readonly TypeAheadSession _typeAhead = new();
    internal ScriptEngine _scripts = null!;

    // Prompt suppression: only show the prompt when output or a command preceded it.
    private bool _outputSinceLastPrompt  = false;
    private bool _commandSinceLastPrompt = false;

    private string DataPath(string file)
        => Path.Combine(_dirService.Current.BasePath, file);

    /// <summary>
    /// Path for JSON config files. All application configuration lives under
    /// <c>BasePath/Config</c> to keep the app support root tidy (Maps, Scripts,
    /// Logs and the Config dir sit side-by-side). The directory is created on
    /// first access.
    /// </summary>
    internal string ConfigPath(string file)
    {
        var dir = Path.Combine(_dirService.Current.BasePath, "Config");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, file);
    }

    internal string ConfigDir
    {
        get
        {
            var dir = Path.Combine(_dirService.Current.BasePath, "Config");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>
    /// One-time migration: if any known *.json config file still lives at the
    /// old location (BasePath root), move it into BasePath/Config. Safe to run
    /// on every launch — no-op once the move has happened.
    /// </summary>
    // private void MigrateConfigFiles()
    // {
    //     var baseDir   = _dirService.Current.BasePath;
    //     var configDir = ConfigDir;

    //     // Fixed-name files we have always written.
    //     var names = new[]
    //     {
    //         "aliases.json",
    //         "autolog.json",
    //         "clientstate.json",
    //         "highlights.json",
    //         "layout.json",
    //         "presets.json",
    //         "profiles.json",
    //         "triggers.json",
    //         "window_settings.json",
    //     };
    //     foreach (var name in names)
    //         TryMove(Path.Combine(baseDir, name), Path.Combine(configDir, name));

    //     // Per-profile layouts: layout_<profile>.json
    //     try
    //     {
    //         foreach (var path in Directory.EnumerateFiles(baseDir, "layout_*.json",
    //                                                       SearchOption.TopDirectoryOnly))
    //         {
    //             var name = Path.GetFileName(path);
    //             TryMove(path, Path.Combine(configDir, name));
    //         }
    //     }
    //     catch { /* ignore enumeration errors */ }

    //     static void TryMove(string src, string dst)
    //     {
    //         if (!File.Exists(src)) return;
    //         if (File.Exists(dst))
    //         {
    //             // Destination wins — leave source in place so the user can
    //             // reconcile by hand rather than silently losing data.
    //             return;
    //         }
    //         try { File.Move(src, dst); } catch { /* ignore — best effort */ }
    //     }
    // }

    private void InitializeEngines()
    {
        var baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Genie5");
        Directory.CreateDirectory(baseDir);

        _dirService = new LocalDirectoryService("Genie5", baseDir);

        // Move any stale JSON files from the base directory into Config/.
        // One-shot — no-op once everything has been relocated.
        //MigrateConfigFiles();

        var config = new GenieConfig(_dirService);
        var queue  = new CommandQueue();
        var events = new EventQueue();

        _engine     = new CommandEngine(config, queue, events, new UiHost(this));
        _triggers   = new TriggerEngineFinal(new UiHost(this), _engine);
        _aliases    = new AliasEngine(_engine);
        _variables  = new VariableEngine(_engine);
        _highlights     = new HighlightEngine();
        _nameHighlights = new NameHighlightEngine();
        _substitutes    = new SubstituteEngine();
        _gags           = new GagEngine();
        _macros         = new MacroEngine();
        _classes        = new ClassEngine();
        _presets        = new Genie4.Core.Presets.PresetEngine();
        _gslParser  = new GslParser(_presets);

        // Hook the class gate into every rule engine that supports it so
        // inactive classes suppress matches across the board.
        _triggers.Classes    = _classes;
        _highlights.Classes  = _classes;
        _substitutes.Classes = _classes;
        _gags.Classes        = _classes;

        LoadData();

        var mapsDir = DataPath("Maps");
        var defaultZonePath = Path.Combine(mapsDir, "default.json");
        var defaultZone = _mapRepo.Load(defaultZonePath) ?? new MapZone { Name = "Default" };
        _mapperEngine = new AutoMapperEngine(defaultZone);
        _mapperEngine.Attach(_gslGameState);

        _mapper = new MapperController(
            _mapperEngine,
            _mapRepo,
            _gslGameState,
            mapsDir,
            AppendOutput,
            cmd => _engine.ProcessInput(cmd),
            _typeAhead);

        var scriptsDir = DataPath("Scripts");
        _scripts = new ScriptEngine(
            scriptsDir,
            _typeAhead,
            cmd =>
            {
                _mapperEngine?.OnCommandSent(cmd);
                _commandSinceLastPrompt = true;
                _ = _client.SendAsync(cmd);
            },
            AppendOutput,
            hashCmd => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                // Route # commands from scripts through the same path as
                // user-typed input: mapper (#goto), script control (#script),
                // and eventually other UI commands.
                if (_mapper.TryHandleGoto(hashCmd)) return;
                if (TryHandleScriptCommand(hashCmd)) return;
                if (TryHandleClassCommand(hashCmd)) return;
                // Unknown # commands are silently dropped.
            }));

        // Seed script globals with any user-defined variables loaded earlier.
        foreach (var v in _variables.Store.GetAll().Values)
            _scripts.Globals[v.Name] = v.Value;

        // Echo commands sent by scripts to the game window using the scriptecho preset.
        _scripts.EchoCommand = (scriptName, cmd) =>
        {
            var line = new RenderLine();
            line.Spans.Add(new AnsiSpan
            {
                Text       = $"[{scriptName}]: {cmd}",
                Foreground = _presets.GetForeground("scriptecho"),
                Background = _presets.GetBackground("scriptecho"),
            });
            ApplyHighlightAndAppend(line);
        };

        // Roundtime check for pause/wait commands.
        _scripts.InRoundtime = () => _gslGameState.RoundTimeRemaining > 0;

        // Timer-driven tick so delay/pause unblock without waiting for server events.
        _scripts.ScheduleTick = delay =>
        {
            var timer = new Avalonia.Threading.DispatcherTimer { Interval = delay };
            timer.Tick += (_, _) => { timer.Stop(); _scripts.Tick(); };
            timer.Start();
        };

        // Directed echo: route #echo >Window #color messages to the right panel.
        _scripts.EchoTo = (msg, window, color) =>
        {
            GameOutputViewModel? target = null;
            if (window != null)
            {
                if (window.Equals("Log", StringComparison.OrdinalIgnoreCase))
                    target = _logVm;
                else
                    _streamVms.TryGetValue(window, out target);
            }
            target ??= _gameOutputVm;

            var line = new RenderLine();
            line.Spans.Add(new AnsiSpan
            {
                Text       = msg,
                Foreground = color ?? "Default",
            });
            target.AppendLine(line);
        };

        // Wire mapper → script pipeline so synthetic lines like
        // "YOU HAVE ARRIVED!" flow through match/waitfor.
        _mapper.RoundTimeRemaining = () => _gslGameState.RoundTimeRemaining;
        _mapper.EmitGameLine = line =>
        {
            AppendOutput(line);
            _scripts?.OnGameLine(line);
        };
        _mapper.RunScript = (name, args) =>
        {
            _scripts?.TryStart(name, args);
        };

        // When the automapper script finishes, check if we arrived or need to replan.
        _scripts.ScriptFinished += name =>
        {
            if (!name.Equals("automapper", StringComparison.OrdinalIgnoreCase)) return;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => _mapper.OnAutoMapperScriptFinished());
        };

        ScriptBar.Attach(_scripts, _mapper);

        // Push mapper state into script globals so scripts can read $zoneid /
        // $roomid / $zonename like Genie4. Refreshed whenever the engine
        // recognises a new room (read-only, no auto-resolve).
        _mapperEngine.CurrentNodeChanged += () => RefreshMapperGlobals(autoResolve: false);
        _mapper.SetInitialZonePath(defaultZonePath);
        _mapper.ZoneChanged += () => Avalonia.Threading.Dispatcher.UIThread.Post(
            () => _mapWindow?.RefreshZoneList(_mapper.CurrentZonePath));

        _client.LineReceived += line =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                // Raw tab: unprocessed server data
                var rawLine = new RenderLine();
                rawLine.Spans.Add(new AnsiSpan { Text = line, Foreground = "#AAAAAA" });
                _rawOutputVm.AppendLine(rawLine);

                var (segments, events) = _gslParser.ParseLine(line);

                _gslGameState.Apply(events);

                // Push hand state into script globals so scripts can read
                // $righthand / $lefthand / $righthandnoun / $lefthandnoun.
                foreach (var ev in events)
                {
                    if (ev is ComponentEvent { Id: "rhand" or "lhand" })
                    {
                        var rh = string.IsNullOrEmpty(_gslGameState.RightHand) ? "Empty" : _gslGameState.RightHand;
                        var lh = string.IsNullOrEmpty(_gslGameState.LeftHand)  ? "Empty" : _gslGameState.LeftHand;
                        _scripts.Globals["righthand"]     = rh;
                        _scripts.Globals["lefthand"]      = lh;
                        _scripts.Globals["righthandnoun"] = ExtractNoun(rh);
                        _scripts.Globals["lefthandnoun"]  = ExtractNoun(lh);
                        break; // only need to refresh once per batch
                    }
                }

                // Route container/inv events to the inv stream window.
                foreach (var ev in events)
                {
                    if (ev is ClearContainerEvent && _streamVms.TryGetValue("inv", out var clrVm))
                        clrVm.Lines.Clear();
                    else if (ev is InvLineEvent inv && _streamVms.TryGetValue("inv", out var invVm))
                    {
                        var invLine = new RenderLine();
                        invLine.Spans.Add(new AnsiSpan { Text = inv.Text, Foreground = "Default" });
                        invVm.AppendLine(invLine);
                    }
                }

                // Duplicate speech/whispers to the Log panel using the full line text.
                // The game sends speech twice (pushStream + popStream); only route
                // when outside a named stream to avoid duplicates.  Talk routing is
                // handled by the game's own pushStream id="talk".
                if (string.IsNullOrEmpty(_gslGameState.CurrentStream)
                    && events.Any(ev => ev is PresetEvent { PresetId: "speech" or "whispers" }))
                {
                    var presetId = events.OfType<PresetEvent>()
                        .First(ev => ev.PresetId is "speech" or "whispers").PresetId;
                    var logColor = presetId == "speech" ? "Yellow" : "Cyan";
                    var logLine = new RenderLine();
                    foreach (var seg in segments)
                        logLine.Spans.Add(new AnsiSpan { Text = seg.Text, Foreground = logColor });
                    _logVm.AppendLine(logLine);
                }

                var plainText = string.Concat(segments.Select(s => s.Text));
                if (string.IsNullOrWhiteSpace(plainText)) return;

                // Let the mapper observe lines for type-ahead errors.
                _mapper.OnGameLine(plainText);
                RefreshMapperGlobals();
                _scripts.OnGameLine(plainText);

                // Suppress repeated prompts: only show the prompt line when output
                // or a command has occurred since the last one.
                bool isPrompt = events.Any(ev => ev is PromptEvent);
                if (isPrompt) { _mapper.OnPrompt(); _scripts.OnPrompt(); }
                if (isPrompt)
                {
                    var show = _outputSinceLastPrompt || _commandSinceLastPrompt;
                    _outputSinceLastPrompt  = false;
                    _commandSinceLastPrompt = false;
                    if (!show) return;
                }

                // Route to sub-stream panel when in a named stream.
                // Unknown named streams (e.g. "room") are suppressed — their data
                // arrives via ComponentEvent / GslGameState, not as raw text.
                var stream = _gslGameState.CurrentStream;
                if (string.IsNullOrEmpty(stream))
                {
                    if (!isPrompt) _outputSinceLastPrompt = true;
                    AppendSegments(segments);
                    _triggers.ProcessLine(plainText);
                }
                else if (_streamVms.TryGetValue(stream, out var streamVm))
                {
                    var line2 = new RenderLine();
                    foreach (var seg in segments)
                    {
                        var parsed = AnsiParser.Parse(seg.Text, seg.GslBold, seg.GslColor, seg.GslBackground);
                        foreach (var span in parsed.Spans)
                            line2.Spans.Add(span);
                    }
                    streamVm.AppendLine(line2);
                }
                // else: named stream with no panel (e.g. "room") — suppress
            });
        };

        _client.Connected += () =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => AppendOutput("[connected]"));

        _client.Disconnected += () =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => AppendOutput("[disconnected]"));
    }

    internal string ProfilesPath => ConfigPath("profiles.json");

    /// <summary>
    /// Mirror the mapper's view of the current room into script globals.
    /// Called both from <c>CurrentNodeChanged</c> and opportunistically from
    /// the line pipeline so scripts that start mid-session see sane values.
    /// </summary>
    private bool _refreshingMapperGlobals;
    private void RefreshMapperGlobals(bool autoResolve = true)
    {
        if (_scripts is null || _mapperEngine is null) return;
        if (_refreshingMapperGlobals) return;
        _refreshingMapperGlobals = true;
        try {
        // If the mapper hasn't yet identified our room (e.g. the script is
        // starting while the default empty zone is loaded), ask the
        // controller to scan imported zones for one that contains us.
        // Only do this from the post-Apply line pipeline — never from inside
        // a CurrentNodeChanged handler, as ResolveCurrentNode → Recalculate
        // will re-fire the event and recurse.
        if (autoResolve && _mapperEngine.CurrentNode is null)
            _mapper?.ResolveCurrentNode();
        var node = _mapperEngine.CurrentNode;
        var zone = _mapperEngine.ActiveZone;
        _scripts.Globals["zoneid"]   = zone?.Genie4Id ?? string.Empty;
        _scripts.Globals["zonename"] = zone?.Name     ?? string.Empty;
        _scripts.Globals["roomid"]   = node?.Id.ToString() ?? "0";
        _scripts.Globals["roomname"] = node?.Title  ?? string.Empty;
        } finally { _refreshingMapperGlobals = false; }
    }

    // Called after all VMs are created so Settings can be attached.
    internal void AttachWindowSettings(
        GameOutputViewModel gameVm,
        GameOutputViewModel rawVm,
        GameOutputViewModel logVm,
        IEnumerable<(string id, string title, GameOutputViewModel vm)> streams)
    {
        // Register in display order (matches LayoutPanel list)
        gameVm.Settings = _windowSettings.Register(gameVm.Id, gameVm.Title);
        rawVm.Settings  = _windowSettings.Register(rawVm.Id,  rawVm.Title);
        logVm.Settings  = _windowSettings.Register(logVm.Id,  logVm.Title);
        foreach (var (id, title, vm) in streams)
            vm.Settings = _windowSettings.Register(id, title);

        // Apply persisted overrides
        foreach (var m in _persistence.LoadWindowSettings(ConfigPath("window_settings.json")))
            _windowSettings.Apply(m);

        // Push loaded titles back to document titles so tab headers update
        foreach (var s in _windowSettings.All.Values)
        {
            if (!string.IsNullOrEmpty(s.DisplayTitle))
            {
                if (s.Id == gameVm.Id) gameVm.Title = s.DisplayTitle;
                else if (s.Id == rawVm.Id) rawVm.Title = s.DisplayTitle;
                else if (s.Id == logVm.Id) logVm.Title = s.DisplayTitle;
            }
        }
    }

    internal void LoadData()
    {
        _profiles.Load(ProfilesPath);

        // Classes first so Ensure() calls from rule loaders below don't clobber
        // persisted active/inactive state.
        foreach (var m in _persistence.LoadClasses(ConfigPath("classes.json")))
            _classes.Set(m.Name, m.IsActive);

        foreach (var m in _persistence.LoadAliases(ConfigPath("aliases.json")))
            _aliases.AddAlias(m.Name, m.Expansion, m.IsEnabled);

        foreach (var m in _persistence.LoadTriggers(ConfigPath("triggers.json")))
            _triggers.AddTrigger(m.Pattern, m.Action, m.CaseSensitive, m.IsEnabled, m.ClassName);

        foreach (var m in _persistence.LoadHighlights(ConfigPath("highlights.json")))
        {
            HighlightMatchType matchType;
            if (!string.IsNullOrEmpty(m.MatchType) &&
                Enum.TryParse<HighlightMatchType>(m.MatchType, ignoreCase: true, out var parsed))
                matchType = parsed;
            else
                matchType = m.IsRegex ? HighlightMatchType.Regex : HighlightMatchType.String;

            _highlights.AddRule(m.Pattern, m.ForegroundColor, m.BackgroundColor,
                                matchType, m.CaseSensitive, m.IsEnabled, m.ClassName);
        }

        foreach (var m in _persistence.LoadNames(ConfigPath("names.json")))
            _nameHighlights.Add(m.Name, m.ForegroundColor, m.BackgroundColor);

        foreach (var m in _persistence.LoadSubstitutes(ConfigPath("substitutes.json")))
            _substitutes.AddRule(m.Pattern, m.Replacement, m.CaseSensitive, m.IsEnabled, m.ClassName);

        foreach (var m in _persistence.LoadGags(ConfigPath("gags.json")))
            _gags.AddRule(m.Pattern, m.CaseSensitive, m.IsEnabled, m.ClassName);

        foreach (var m in _persistence.LoadMacros(ConfigPath("macros.json")))
            _macros.Add(m.Key, m.Action);

        foreach (var m in _persistence.LoadPresets(ConfigPath("presets.json")))
            _presets.Apply(new Genie4.Core.Presets.PresetRule
            {
                Id              = m.Id,
                ForegroundColor = m.ForegroundColor,
                BackgroundColor = m.BackgroundColor,
                HighlightLine   = m.HighlightLine,
            });

        foreach (var m in _persistence.LoadVariables(ConfigPath("variables.json")))
            _variables.Store.Set(m.Name, m.Value);
    }

    internal void SaveData()
    {
        _persistence.SaveAliases(ConfigPath("aliases.json"), _aliases.Aliases);
        _persistence.SaveTriggers(ConfigPath("triggers.json"), _triggers.Triggers);
        _persistence.SaveHighlights(ConfigPath("highlights.json"), _highlights.Rules);
        _persistence.SaveNames(ConfigPath("names.json"), _nameHighlights.Rules);
        _persistence.SaveSubstitutes(ConfigPath("substitutes.json"), _substitutes.Rules);
        _persistence.SaveGags(ConfigPath("gags.json"), _gags.Rules);
        _persistence.SaveMacros(ConfigPath("macros.json"), _macros.Rules);
        _persistence.SaveClasses(ConfigPath("classes.json"), _classes);
        _persistence.SavePresets(ConfigPath("presets.json"), _presets);
        _persistence.SaveVariables(ConfigPath("variables.json"), _variables.Store);
        _persistence.SaveWindowSettings(ConfigPath("window_settings.json"), _windowSettings);
        _profiles.Save(ProfilesPath);
        _mapper.SaveActiveZone();
        SaveLayout();
        SaveClientState();
        CloseAutoLog();
    }

    private sealed class UiHost : ICommandHost
    {
        private readonly MainWindow _window;

        public UiHost(MainWindow window) => _window = window;

        public void Echo(string text)
            => Avalonia.Threading.Dispatcher.UIThread.Post(() => _window.AppendOutput("[echo] " + text));

        public void SendToGame(string text, bool userInput = false, string origin = "")
        {
            _window._mapperEngine?.OnCommandSent(text);
            _window._scripts?.Extensions.DispatchCommand(text);
            _window._commandSinceLastPrompt = true;
            _ = _window._client.SendAsync(text);
        }

        public void RunScript(string text)
            => _window.AppendOutput("[script] " + text);
    }

    /// <summary>
    /// Extract the last word (noun) from a hand item string.
    /// "polished steel longsword" → "longsword", "Empty" → "Empty".
    /// </summary>
    private static string ExtractNoun(string itemName)
    {
        if (string.IsNullOrEmpty(itemName)) return string.Empty;
        var idx = itemName.LastIndexOf(' ');
        return idx < 0 ? itemName : itemName[(idx + 1)..];
    }
}
