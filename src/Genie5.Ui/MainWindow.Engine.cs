using System;
using Genie4.Core.Aliases;
using Genie4.Core.Commanding;
using Genie4.Core.Config;
using Genie4.Core.Gsl;
using Genie4.Core.Highlights;
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
    internal readonly GslParser    _gslParser    = new();
    internal readonly GslGameState _gslGameState = new();

    private LocalDirectoryService _dirService = null!;
    private readonly PersistenceService _persistence = new();
    internal readonly ProfileStore _profiles = new();

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

        LoadData();

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

                var plainText = string.Concat(segments.Select(s => s.Text));
                if (string.IsNullOrWhiteSpace(plainText)) return;

                // Route to sub-stream panel when in a named stream.
                // Unknown named streams (e.g. "room") are suppressed — their data
                // arrives via ComponentEvent / GslGameState, not as raw text.
                var stream = _gslGameState.CurrentStream;
                if (string.IsNullOrEmpty(stream))
                {
                    AppendSegments(segments);
                    _triggers.ProcessLine(plainText);
                }
                else if (_streamVms.TryGetValue(stream, out var streamVm))
                {
                    var line2 = new RenderLine();
                    foreach (var seg in segments)
                    {
                        var parsed = AnsiParser.Parse(seg.Text, seg.GslBold, seg.GslColor);
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

    internal void LoadData()
    {
        _profiles.Load(ProfilesPath);
        foreach (var m in _persistence.LoadAliases(DataPath("aliases.json")))
            _aliases.AddAlias(m.Name, m.Expansion, m.IsEnabled);

        foreach (var m in _persistence.LoadTriggers(DataPath("triggers.json")))
            _triggers.AddTrigger(m.Pattern, m.Action, m.CaseSensitive, m.IsEnabled);

        foreach (var m in _persistence.LoadHighlights(DataPath("highlights.json")))
            _highlights.AddRule(m.Pattern, m.ForegroundColor, m.IsRegex, m.CaseSensitive, m.IsEnabled);
    }

    internal void SaveData()
    {
        _persistence.SaveAliases(DataPath("aliases.json"), _aliases.Aliases);
        _persistence.SaveTriggers(DataPath("triggers.json"), _triggers.Triggers);
        _persistence.SaveHighlights(DataPath("highlights.json"), _highlights.Rules);
        _profiles.Save(ProfilesPath);
    }

    private sealed class UiHost : ICommandHost
    {
        private readonly MainWindow _window;

        public UiHost(MainWindow window) => _window = window;

        public void Echo(string text)
            => Avalonia.Threading.Dispatcher.UIThread.Post(() => _window.AppendOutput("[echo] " + text));

        public void SendToGame(string text, bool userInput = false, string origin = "")
            => _ = _window._client.SendAsync(text);

        public void RunScript(string text)
            => _window.AppendOutput("[script] " + text);
    }
}
