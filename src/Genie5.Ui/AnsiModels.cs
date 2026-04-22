namespace Genie5.Ui;

public sealed class AnsiSpan
{
    public string Text { get; set; } = "";
    public string Foreground { get; set; } = "Default";
    public string Background { get; set; } = "";
    public bool Bold { get; set; }
    public bool Monospace { get; set; }
}

public sealed class RenderLine
{
    public List<AnsiSpan> Spans { get; } = new();

    public string PlainText => string.Concat(Spans.Select(s => s.Text));
}