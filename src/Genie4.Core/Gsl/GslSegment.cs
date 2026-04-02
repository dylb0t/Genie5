namespace Genie4.Core.Gsl;

/// <summary>
/// A styled text segment emitted by GslParser.
/// Text may still contain ANSI escape codes — caller should run those
/// through AnsiParser and then layer GslBold / GslColor on top.
/// </summary>
public sealed record GslSegment(
    string Text,
    bool   GslBold,
    string GslColor   // "" = no override, otherwise a colour name e.g. "Cyan"
);
