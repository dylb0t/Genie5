using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Genie4.Core.Presets;

namespace Genie4.Core.Gsl;

/// <summary>
/// Parses a single line of Simutronics game server output.
/// Returns styled GslSegments (text + bold + colour override) and GslEvents.
/// </summary>
public sealed class GslParser
{
    private readonly StringBuilder _xmlCarry = new();

    // Accumulates <dir> children between <compass> … </compass> fragments.
    private List<string>? _pendingCompassDirs;

    // Accumulates inner text between <component id="…"> … </component> across line boundaries.
    private string?       _pendingComponentId;
    private readonly StringBuilder _pendingComponentText = new();

    // Stores the time attribute from <prompt time="…"> until </prompt> closes it.
    private int _pendingPromptTime;

    private readonly PresetEngine _presets;

    public GslParser(PresetEngine presets)
    {
        _presets = presets;
    }

    public (IReadOnlyList<GslSegment> Segments, IReadOnlyList<GslEvent> Events) ParseLine(string rawLine)
    {
        if (rawLine.StartsWith("< ")) rawLine = rawLine.Replace("< ", "&lt; ");
        if (rawLine.StartsWith("> ")) rawLine = rawLine.Replace("> ", "&gt; ");

        var ctx = new ParseContext();

        if (_xmlCarry.Length > 0)
        {
            ctx.XmlBuf.Append(_xmlCarry);
            _xmlCarry.Clear();
            ctx.Depth = 1;
        }

        bool inHtml  = false;
        var  htmlBuf = new StringBuilder();
        char prev    = '\0';

        foreach (char c in rawLine)
        {
            if (c == '<')
            {
                ctx.Depth++;
                ctx.XmlBuf.Append(c);
            }
            else if (c == '>')
            {
                ctx.XmlBuf.Append(c);
                ctx.Depth--;

                if (ctx.Depth <= 0)
                {
                    ctx.Depth = 0;
                    var fragment = ctx.XmlBuf.ToString();
                    ctx.XmlBuf.Clear();
                    ProcessFragment(fragment, ctx);
                }
            }
            else if (ctx.Depth > 0)
            {
                ctx.XmlBuf.Append(c);
            }
            else if (c == '&')
            {
                inHtml = true;
                htmlBuf.Clear();
                htmlBuf.Append(c);
            }
            else if (inHtml)
            {
                htmlBuf.Append(c);
                if (c == ';')
                {
                    ctx.Text.Append(TranslateHtmlEntity(htmlBuf.ToString()));
                    htmlBuf.Clear();
                    inHtml = false;
                }
                else if (htmlBuf.Length > 8)
                {
                    ctx.Text.Append(htmlBuf);
                    htmlBuf.Clear();
                    inHtml = false;
                }
            }
            else if (c != '\r' && c != (char)28)
            {
                // Route plain text into the component buffer when inside a <component> block
                if (_pendingComponentId is not null)
                    _pendingComponentText.Append(c);
                else
                    ctx.Text.Append(c);
            }

            prev = c;
        }

        if (ctx.Depth > 0 && ctx.XmlBuf.Length > 0)
            _xmlCarry.Append(ctx.XmlBuf);

        if (inHtml && htmlBuf.Length > 0)
            ctx.Text.Append(htmlBuf);

        ctx.Flush();

        return (ctx.Segments, ctx.Events);
    }

    private void ProcessFragment(string fragment, ParseContext ctx)
    {
        // ── Closing tags ─────────────────────────────────────────────────────
        // These cannot be wrapped in <r>…</r> (invalid XML), so handle first.
        if (fragment.StartsWith("</"))
        {
            var tagName = fragment[2..].TrimEnd('>').Trim();
            switch (tagName)
            {
                case "compass" when _pendingCompassDirs is not null:
                    ctx.Events.Add(new CompassEvent(_pendingCompassDirs));
                    _pendingCompassDirs = null;
                    break;
                case "component" when _pendingComponentId is not null:
                    ctx.Events.Add(new ComponentEvent(_pendingComponentId, _pendingComponentText.ToString().Trim()));
                    _pendingComponentId = null;
                    _pendingComponentText.Clear();
                    break;
                case "prompt":
                    // Text content arrived via the char loop; emit the event now.
                    ctx.Events.Add(new PromptEvent(ctx.Text.ToString(), _pendingPromptTime));
                    _pendingPromptTime = 0;
                    // Leave ctx.Text so the prompt char ">." renders in the output.
                    break;
            }
            return;
        }

        // ── Bare opening tags (not self-closing) ─────────────────────────────
        // A bare opening tag like <component id='room objs'> has no matching
        // close inside the fragment, so wrapping it in <r>…</r> is invalid XML
        // and XmlDocument.Load throws.  Extract the tag name and attributes
        // directly and handle the state transitions here.
        if (!fragment.TrimEnd().EndsWith("/>"))
        {
            var nameMatch = Regex.Match(fragment, @"^<([\w:-]+)");
            if (!nameMatch.Success) return;

            switch (nameMatch.Groups[1].Value)
            {
                case "pushBold":
                    ctx.Flush();
                    ctx.Bold = true;
                    break;
                case "popBold":
                    ctx.Flush();
                    ctx.Bold = false;
                    break;
                case "compass":
                    _pendingCompassDirs = new List<string>();
                    break;
                case "component":
                    _pendingComponentId = AttrFromRaw(fragment, "id");
                    _pendingComponentText.Clear();
                    break;
                case "prompt":
                    int.TryParse(AttrFromRaw(fragment, "time"), out _pendingPromptTime);
                    break;
                case "pushStream":
                    ctx.Events.Add(new PushStreamEvent(AttrFromRaw(fragment, "id")));
                    break;
                case "popStream":
                    ctx.Events.Add(new PopStreamEvent());
                    break;
                case "a":
                    // Text content comes through the char loop; nothing to do here.
                    break;
            }
            return;
        }

        // ── Self-closing tags — safe to parse with XmlDocument ───────────────
        XmlDocument doc;
        try
        {
            doc = new XmlDocument();
            doc.LoadXml("<r>" + fragment + "</r>");
        }
        catch
        {
            ctx.Text.Append(StripTags(fragment));
            return;
        }

        var node = doc.DocumentElement?.FirstChild;
        if (node is null) return;

        switch (node.Name)
        {
            case "pushBold":
                ctx.Flush();
                ctx.Bold = true;
                break;

            case "popBold":
                ctx.Flush();
                ctx.Bold = false;
                break;

            case "preset":
            {
                var pid  = Attr(node, "id").ToLower();
                var ptxt = node.InnerText;
                if (pid == "whisper") pid = "whispers";
                if (pid == "thought") pid = "thoughts";
                ctx.Flush();
                var rule  = _presets.Get(pid);
                var color = rule?.ForegroundColor ?? string.Empty;
                ctx.Events.Add(new PresetEvent(pid, ptxt));
                ctx.Segments.Add(new GslSegment(ptxt, ctx.Bold, color));
                break;
            }

            case "roundTime":
                if (int.TryParse(Attr(node, "value"), out int rt))
                    ctx.Events.Add(new RoundTimeEvent(rt));
                break;

            case "castTime":
                if (int.TryParse(Attr(node, "value"), out int ct))
                    ctx.Events.Add(new CastTimeEvent(ct));
                break;

            case "prompt":
                // Self-closing prompt (rare) — emit directly.
                int.TryParse(Attr(node, "time"), out int gt);
                ctx.Events.Add(new PromptEvent(node.InnerText, gt));
                if (!string.IsNullOrEmpty(node.InnerText))
                    ctx.Text.Append(node.InnerText);
                break;

            case "spell":
                ctx.Events.Add(new SpellEvent(node.InnerText));
                break;

            case "indicator":
                ctx.Events.Add(new IndicatorEvent(Attr(node, "id"), Attr(node, "visible") == "y"));
                break;

            case "compass":
                // Self-closing <compass/> — no exits.
                ctx.Events.Add(new CompassEvent(new List<string>()));
                break;

            case "dir":
                _pendingCompassDirs?.Add(Attr(node, "value"));
                break;

            case "pushStream":
                ctx.Events.Add(new PushStreamEvent(Attr(node, "id")));
                break;

            case "popStream":
                ctx.Events.Add(new PopStreamEvent());
                break;

            case "output":
                ctx.Events.Add(new OutputModeEvent(Attr(node, "class") == "mono"));
                break;

            case "streamWindow":
                if (Attr(node, "id") == "room")
                {
                    var sub = Attr(node, "subtitle");
                    if (sub.StartsWith(" - ")) sub = sub[3..];
                    if (sub.StartsWith("["))   sub = sub[1..^1];
                    ctx.Events.Add(new RoomTitleEvent(sub.Trim()));
                }
                break;

            case "a":
                AppendText(ctx, node.InnerText);
                break;

            case "progressBar":
            {
                var pid = Attr(node, "id");
                if (!string.IsNullOrEmpty(pid) && int.TryParse(Attr(node, "value"), out int pv))
                    ctx.Events.Add(new VitalsEvent(pid, pv));
                break;
            }

            case "left":
                ctx.Events.Add(new ComponentEvent("lhand", node.InnerText.Trim()));
                break;

            case "right":
                ctx.Events.Add(new ComponentEvent("rhand", node.InnerText.Trim()));
                break;

            case "component":
                // Self-closing <component id="..."/> — empty value, still emit event.
                ctx.Events.Add(new ComponentEvent(Attr(node, "id"), string.Empty));
                break;

            case "style":
            {
                ctx.Flush();
                var sid = Attr(node, "id").ToLowerInvariant();
                // Normalise camelCase ids sent by the server to match preset keys
                sid = sid switch
                {
                    "roomname"        => "roomname",
                    "roomdesc"        => "roomdesc",
                    "roomobjs"        => "roomobjs",
                    "roomplayers"     => "roomplayers",
                    "roomextras"      => "roomextras",
                    _ => sid
                };
                if (string.IsNullOrEmpty(sid))
                    ctx.PresetColor = string.Empty;
                else
                {
                    var fg = _presets.Get(sid)?.ForegroundColor ?? string.Empty;
                    ctx.PresetColor = fg == "Default" ? string.Empty : fg;
                }
                break;
            }

            case "resource":
            case "dialogData":
            case "openDialog":
            case "closeDialog":
            case "clearStream":
            case "nav":
            case "mode":
            case "settingsInfo":
            case "settings":
            case "vars":
            case "launchURL":
            case "inv":
                break;

            default:
                var inner = node.InnerText;
                if (!string.IsNullOrEmpty(inner)) AppendText(ctx, inner);
                break;
        }
    }

    private sealed class ParseContext
    {
        public List<GslSegment> Segments      { get; } = new();
        public List<GslEvent>   Events        { get; } = new();
        public StringBuilder    Text          { get; } = new();
        public StringBuilder    XmlBuf        { get; } = new();
        public int  Depth { get; set; }
        public bool Bold  { get; set; }
        public string PresetColor { get; set; } = string.Empty;

        public void Flush()
        {
            if (Text.Length == 0) return;
            Segments.Add(new GslSegment(Text.ToString(), Bold, PresetColor));
            Text.Clear();
        }
    }

    // Appends text to the component accumulator when inside a <component> block,
    // otherwise to the normal output segments.
    private void AppendText(ParseContext ctx, string text)
    {
        if (_pendingComponentId is not null)
            _pendingComponentText.Append(text);
        else
            ctx.Text.Append(text);
    }

    private static string Attr(XmlNode node, string name)
        => node.Attributes?.GetNamedItem(name)?.Value ?? string.Empty;

    private static string AttrFromRaw(string fragment, string name)
    {
        var m = Regex.Match(fragment, name + @"\s*=\s*[""']([^""']*)[""']");
        return m.Success ? m.Groups[1].Value : string.Empty;
    }

    private static string StripTags(string s)
        => Regex.Replace(s, "<[^>]*>", string.Empty);

    private static string TranslateHtmlEntity(string entity) => entity switch
    {
        "&amp;"  => "&",
        "&lt;"   => "<",
        "&gt;"   => ">",
        "&quot;" => "\"",
        "&apos;" => "'",
        "&nbsp;" => " ",
        _        => entity
    };
}
