namespace Genie4.Core.Presets;

public sealed class PresetEngine
{
    private readonly Dictionary<string, PresetRule> _presets = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, PresetRule> Presets => _presets;

    public PresetEngine()
    {
        SetDefaults();
    }

    public void SetDefaults()
    {
        Set("roomdesc",    "Silver",      highlightLine: false);
        Set("roomname",    "Khaki",       highlightLine: false);
        Set("speech",      "Yellow",      highlightLine: false);
        Set("whispers",    "Magenta",     highlightLine: false);
        Set("thoughts",    "Cyan",        highlightLine: false);
        Set("creatures",   "Cyan",        highlightLine: true);
        Set("familiar",    "PaleGreen",   highlightLine: false);
        Set("inputuser",   "Yellow",      highlightLine: false);
        Set("inputother",  "GreenYellow", highlightLine: false);
        Set("scriptecho",  "Cyan",        highlightLine: false);
        Set("health",      "IndianRed",   highlightLine: false);
        Set("mana",        "CornflowerBlue", highlightLine: false);
        Set("stamina",     "LightGreen",  highlightLine: false);
        Set("spirit",      "Orchid",      highlightLine: false);
        Set("concentration", "LightGray", highlightLine: false);
        Set("roundtime",   "CornflowerBlue", highlightLine: false);
        Set("castbar",     "Orchid",      highlightLine: false);
    }

    private void Set(string id, string fg, string bg = "", bool highlightLine = false)
        => _presets[id] = new PresetRule { Id = id, ForegroundColor = fg, BackgroundColor = bg, HighlightLine = highlightLine };

    public void Apply(PresetRule rule)
        => _presets[rule.Id] = rule;

    public PresetRule? Get(string id)
        => _presets.TryGetValue(id, out var r) ? r : null;

    public string GetForeground(string id)
        => _presets.TryGetValue(id, out var r) ? r.ForegroundColor : "Default";

    public string GetBackground(string id)
        => _presets.TryGetValue(id, out var r) ? r.BackgroundColor : string.Empty;

    public bool GetHighlightLine(string id)
        => _presets.TryGetValue(id, out var r) && r.HighlightLine;
}
