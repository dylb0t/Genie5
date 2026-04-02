using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace Genie4.Core.Gsl;

/// <summary>
/// Parses a single line of Simutronics game server output.
/// Strips XML tags from the display text and emits typed GslEvents
/// for each tag encountered.
/// </summary>
public sealed class GslParser
{
    // Carry partial XML across lines (server may split tags across newlines)
    private readonly StringBuilder _xmlCarry = new();

    // Tracks open compass block so we collect <dir> children
    private bool _inCompass;
    private readonly List<string> _compassDirs = new();

    // Tracks open preset block
    private bool _inPreset;
    private string _presetId = string.Empty;

    /// <summary>
    /// Parse one raw server line. Returns the plain text to display
    /// and populates <paramref name="events"/> with any game state events.
    /// </summary>
    public string ParseLine(string rawLine, List<GslEvent> events)
    {
        // DR sends "< " and "> " at start of lines which are NOT XML tags
        if (rawLine.StartsWith("< "))  rawLine = rawLine.Replace("< ", "&lt; ");
        if (rawLine.StartsWith("> "))  rawLine = rawLine.Replace("> ", "&gt; ");

        var text      = new StringBuilder();
        var xmlBuf    = new StringBuilder();
        int depth     = 0;
        bool inHtml   = false;
        var htmlBuf   = new StringBuilder();
        char prev     = '\0';

        // Prepend any carry from previous incomplete line
        if (_xmlCarry.Length > 0)
        {
            xmlBuf.Append(_xmlCarry);
            _xmlCarry.Clear();
            depth = 1;
        }

        foreach (char c in rawLine)
        {
            if (c == '<')
            {
                depth++;
                xmlBuf.Append(c);
            }
            else if (c == '>')
            {
                xmlBuf.Append(c);

                bool selfClose = prev == '/';
                bool endTag    = xmlBuf.Length > 1 && xmlBuf[1] == '/';

                if (selfClose || endTag)
                    depth--;
                else
                    depth--; // normal close still reduces depth after we've collected the tag

                if (depth <= 0)
                {
                    depth = 0;
                    var fragment = xmlBuf.ToString();
                    xmlBuf.Clear();
                    ProcessFragment(fragment, text, events);
                }
            }
            else if (depth > 0)
            {
                // Track end-tag marker
                if (c == '/' && prev == '<') { /* end tag — already captured '<' */ }
                xmlBuf.Append(c);
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
                    text.Append(TranslateHtmlEntity(htmlBuf.ToString()));
                    htmlBuf.Clear();
                    inHtml = false;
                }
                else if (htmlBuf.Length > 8)
                {
                    // Not a real entity — flush as-is
                    text.Append(htmlBuf);
                    htmlBuf.Clear();
                    inHtml = false;
                }
            }
            else if (c != '\r' && c != (char)28) // GSL skip char
            {
                text.Append(c);
            }

            prev = c;
        }

        // If XML tag ran to end of line without closing, carry it forward
        if (depth > 0 && xmlBuf.Length > 0)
            _xmlCarry.Append(xmlBuf);

        if (inHtml && htmlBuf.Length > 0)
            text.Append(htmlBuf); // flush partial entity

        return text.ToString();
    }

    private void ProcessFragment(string fragment, StringBuilder text, List<GslEvent> events)
    {
        // Wrap in a root so XmlDocument can parse it
        XmlDocument doc;
        try
        {
            doc = new XmlDocument();
            doc.LoadXml("<r>" + fragment + "</r>");
        }
        catch
        {
            // Malformed — treat as plain text
            text.Append(StripTags(fragment));
            return;
        }

        var node = doc.DocumentElement?.FirstChild;
        if (node is null) return;

        switch (node.Name)
        {
            case "roundTime":
                if (int.TryParse(Attr(node, "value"), out int rt))
                    events.Add(new RoundTimeEvent(rt));
                break;

            case "castTime":
                if (int.TryParse(Attr(node, "value"), out int ct))
                    events.Add(new CastTimeEvent(ct));
                break;

            case "prompt":
                var promptText = node.InnerText;
                int.TryParse(Attr(node, "time"), out int gt);
                events.Add(new PromptEvent(promptText, gt));
                text.Append(promptText);
                break;

            case "spell":
                events.Add(new SpellEvent(node.InnerText));
                break;

            case "indicator":
                events.Add(new IndicatorEvent(
                    Attr(node, "id"),
                    Attr(node, "visible") == "y"));
                break;

            case "compass":
                _inCompass = true;
                _compassDirs.Clear();
                foreach (XmlNode child in node.ChildNodes)
                    if (child.Name == "dir")
                        _compassDirs.Add(Attr(child, "value"));
                events.Add(new CompassEvent(_compassDirs.ToList()));
                _inCompass = false;
                break;

            case "pushStream":
                events.Add(new PushStreamEvent(Attr(node, "id")));
                break;

            case "popStream":
                events.Add(new PopStreamEvent());
                break;

            case "output":
                events.Add(new OutputModeEvent(Attr(node, "class") == "mono"));
                break;

            case "pushBold":
                events.Add(new PushBoldEvent());
                break;

            case "popBold":
                events.Add(new PopBoldEvent());
                break;

            case "preset":
                var pid  = Attr(node, "id");
                var ptxt = node.InnerText;
                events.Add(new PresetEvent(pid, ptxt));
                text.Append(ptxt);
                break;

            case "streamWindow":
                if (Attr(node, "id") == "room")
                {
                    var subtitle = Attr(node, "subtitle");
                    if (subtitle.StartsWith(" - ")) subtitle = subtitle[3..];
                    if (subtitle.StartsWith("["))   subtitle = subtitle[1..^1];
                    events.Add(new RoomTitleEvent(subtitle.Trim()));
                }
                break;

            case "a":   // Hyperlink — show inner text
                text.Append(node.InnerText);
                break;

            // Tags that carry no display text and no state we track yet
            case "style":
            case "component":
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
                break;

            default:
                // Unknown tag — emit its inner text so nothing is silently dropped
                var inner = node.InnerText;
                if (!string.IsNullOrEmpty(inner))
                    text.Append(inner);
                break;
        }
    }

    private static string Attr(XmlNode node, string name)
        => node.Attributes?.GetNamedItem(name)?.Value ?? string.Empty;

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
