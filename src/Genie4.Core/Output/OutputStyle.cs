namespace Genie4.Core.Output;

public sealed class OutputStyle
{
    public string Foreground { get; set; } = "Default";
    public string Background { get; set; } = "Default";
    public bool Bold { get; set; }
    public bool Underline { get; set; }

    public OutputStyle Clone()
    {
        return new OutputStyle
        {
            Foreground = Foreground,
            Background = Background,
            Bold = Bold,
            Underline = Underline
        };
    }
}
