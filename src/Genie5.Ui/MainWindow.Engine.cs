using System;
using Genie4.Core.Aliases;
using Genie4.Core.Commanding;
using Genie4.Core.Config;
using Genie4.Core.Gsl;
using Genie4.Core.Highlights;
using Genie4.Core.Layout;
using Genie4.Core.Mapper;
using Genie4.Core.Networking;
using Genie4.Core.Persistence;
using Genie4.Core.Profiles;
using Genie4.Core.Queue;
using Genie4.Core.Runtime;
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
    internal Genie4.Core.Presets.PresetEngine _presets = null!;
    internal GslParser    _gslParser    = null!;
    internal readonly GslGameState _gslGameState = new();

    private LocalDirectoryService _dirService = null!;
    private readonly PersistenceService _persistence = new();
    internal readonly ProfileStore _profiles = new();
    internal AutoMapperEngine _mapperEngine = null!;
    private readonly MapZoneRepository _mapRepo = new();
    internal string _currentZonePath = string.Empty;
    internal readonly WindowSettingsStore _windowSettings = new();

    // Prompt suppression: only show the prompt when output or a command preceded it.
    private bool _outputSinceLastPrompt  = false;
    private bool _commandSinceLastPrompt = false;

    private string DataPath(string file)
        => Path.Combine(_dirService.Current.BasePath, file);

    private void InitializeEngines()
    {
        var baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Genie5");
        Directory.CreateDirectory(baseDir);

        _dirService = new LocalDirectoryService("Genie5", baseDir);

        var config = new GenieConfig(_dirService);
        var queue  = new CommandQueue();
        var events = new EventQueue();

        _engine     = new CommandEngine(config, queue, events, new UiHost(this));
        _triggers   = new TriggerEngineFinal(new UiHost(this), _engine);
        _aliases    = new AliasEngine(_engine);
        _variables  = new VariableEngine(_engine);
        _highlights = new HighlightEngine();
        _presets    = new Genie4.Core.Presets.PresetEngine();
        _gslParser  = new GslParser(_presets);

        LoadData();

        var defaultZonePath = DataPath(Path.Combine("maps", "default.json"));
        _currentZonePath = defaultZonePath;
        var defaultZone = _mapRepo.Load(defaultZonePath) ?? new MapZone { Name = "Default" };
        _mapperEngine = new AutoMapperEngine(defaultZone);
        _mapperEngine.Attach(_gslGameState);

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

                // Duplicate speech to the Log panel (without suppressing main output).
                foreach (var ev in events)
                {
                    if (ev is PresetEvent { PresetId: "speech" or "whispers" } pe)
                    {
                        var logColor = pe.PresetId == "speech" ? "Yellow" : "Cyan";
                        var logLine  = new RenderLine();
                        logLine.Spans.Add(new AnsiSpan { Text = pe.Text, Foreground = logColor });
                        _logVm.AppendLine(logLine);
                    }
                }

                var plainText = string.Concat(segments.Select(s => s.Text));
                if (string.IsNullOrWhiteSpace(plainText)) return;

                // Suppress repeated prompts: only show the prompt line when output
                // or a command has occurred since the last one.
                bool isPrompt = events.Any(ev => ev is PromptEvent);
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

    internal string ProfilesPath => DataPath("profiles.json");

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
        foreach (var m in _persistence.LoadWindowSettings(DataPath("window_settings.json")))
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
        foreach (var m in _persistence.LoadAliases(DataPath("aliases.json")))
            _aliases.AddAlias(m.Name, m.Expansion, m.IsEnabled);

        foreach (var m in _persistence.LoadTriggers(DataPath("triggers.json")))
            _triggers.AddTrigger(m.Pattern, m.Action, m.CaseSensitive, m.IsEnabled);

        foreach (var m in _persistence.LoadHighlights(DataPath("highlights.json")))
        {
            HighlightMatchType matchType;
            if (!string.IsNullOrEmpty(m.MatchType) &&
                Enum.TryParse<HighlightMatchType>(m.MatchType, ignoreCase: true, out var parsed))
                matchType = parsed;
            else
                matchType = m.IsRegex ? HighlightMatchType.Regex : HighlightMatchType.String;

            _highlights.AddRule(m.Pattern, m.ForegroundColor, m.BackgroundColor,
                                matchType, m.CaseSensitive, m.IsEnabled);
        }

        foreach (var m in _persistence.LoadPresets(DataPath("presets.json")))
            _presets.Apply(new Genie4.Core.Presets.PresetRule
            {
                Id              = m.Id,
                ForegroundColor = m.ForegroundColor,
                BackgroundColor = m.BackgroundColor,
                HighlightLine   = m.HighlightLine,
            });
    }

    internal void SaveData()
    {
        _persistence.SaveAliases(DataPath("aliases.json"), _aliases.Aliases);
        _persistence.SaveTriggers(DataPath("triggers.json"), _triggers.Triggers);
        _persistence.SaveHighlights(DataPath("highlights.json"), _highlights.Rules);
        _persistence.SavePresets(DataPath("presets.json"), _presets);
        _persistence.SaveWindowSettings(DataPath("window_settings.json"), _windowSettings);
        _profiles.Save(ProfilesPath);
        _mapRepo.Save(string.IsNullOrEmpty(_currentZonePath)
            ? DataPath(Path.Combine("maps", "default.json"))
            : _currentZonePath, _mapperEngine.ActiveZone);
        SaveLayout();
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
            _window._commandSinceLastPrompt = true;
            _ = _window._client.SendAsync(text);
        }

        public void RunScript(string text)
            => _window.AppendOutput("[script] " + text);
    }
}
