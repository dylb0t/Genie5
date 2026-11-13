using System.Reactive.Linq;
using Avalonia.Controls.Documents;
using Genie.App.Highlighting;
using Genie.Core;
using Genie.Core.Events;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Genie.App.ViewModels;

public class RoomViewModel : ReactiveObject
{
    [Reactive] public string Title       { get; private set; } = "";
    [Reactive] public string Description { get; private set; } = "";
    [Reactive] public string Exits       { get; private set; } = "";
    [Reactive] public string Players     { get; private set; } = "";
    [Reactive] public string Objects     { get; private set; } = "";

    // Every field renders as styled inlines through the same
    // DefaultHighlights.Tokenize path the game streams use, so user highlight
    // rules, name colours (#154) and MonsterBold (#131) all apply in the Room
    // panel — previously only the objects line was tokenized, which is why
    // imported "room" highlights did nothing here. The plain string twins stay
    // for the IsVisible gates and copy. Unclaimed characters inherit the
    // window's own text colour (#424).
    [Reactive] public IReadOnlyList<Inline> TitleInlines       { get; private set; } = System.Array.Empty<Inline>();
    [Reactive] public IReadOnlyList<Inline> DescriptionInlines { get; private set; } = System.Array.Empty<Inline>();
    [Reactive] public IReadOnlyList<Inline> ExitsInlines       { get; private set; } = System.Array.Empty<Inline>();
    [Reactive] public IReadOnlyList<Inline> PlayersInlines     { get; private set; } = System.Array.Empty<Inline>();
    [Reactive] public IReadOnlyList<Inline> ObjectsInlines     { get; private set; } = System.Array.Empty<Inline>();

    // Last-seen content per component, so a highlight-rules change can repaint
    // the panel without waiting for the next room.
    private readonly Dictionary<string, (string Content, IReadOnlyList<BoldSpan>? Bold)> _last = new();

    public void Attach(GenieCore core)
    {
        core.GameEvents.OfType<ComponentEvent>()
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(e =>
            {
                switch (e.ComponentId)
                {
                    case "room title":
                    case "room desc":
                    case "room exits":
                    case "room players":
                    case "room objs":
                        _last[e.ComponentId] = (e.Content, e.BoldSpans);
                        Apply(e.ComponentId, e.Content, e.BoldSpans);
                        break;
                }
            });

        // Highlight/name rules edited mid-session repaint the current room in
        // place (the same seam the game window uses for its re-render).
        UserHighlights.RulesChanged += () =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                foreach (var (id, (content, bold)) in _last)
                    Apply(id, content, bold);
            });
    }

    internal void Apply(string componentId, string content, IReadOnlyList<BoldSpan>? boldSpans)
    {
        // The title paints through the roomname preset — same palette entry
        // the game window uses for its <style id="roomName"/> line (#174) —
        // and the description through roomdesc, as Genie 4's UpdateRoom
        // printed it (#424). ComponentEvents carry no preset spans, so
        // synthesize a full-length one; user highlight rules still win
        // (presets are the ??= base layer), and a "Default" preset leaves the
        // window's own text colour. Exits, players and objects have no preset
        // in Genie 4 either: they take the window's text colour.
        var preset = componentId switch
        {
            "room title" => "roomName",
            "room desc"  => "roomDesc",
            _            => null,
        };
        var presetSpans = preset is not null && content.Length > 0
            ? new[] { new PresetSpan(0, content.Length, preset) }
            : null;
        var inlines = DefaultHighlights.Tokenize(content, links: null,
                                                 boldSpans: boldSpans, presetSpans: presetSpans, window: "room");
        switch (componentId)
        {
            case "room title":   Title       = content; TitleInlines       = inlines; break;
            case "room desc":    Description = content; DescriptionInlines = inlines; break;
            case "room exits":   Exits       = content; ExitsInlines       = inlines; break;
            case "room players": Players     = content; PlayersInlines     = inlines; break;
            case "room objs":    Objects     = content; ObjectsInlines     = inlines; break;
        }
    }
}
