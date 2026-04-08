namespace Genie4.Core.Extensions;

/// <summary>
/// The slice of Genie5 that an extension is allowed to touch. Deliberately
/// minimal: globals, echo, and command sending. Extensions should not reach
/// into the network client, the GSL parser, or any UI.
/// </summary>
public interface IExtensionHost
{
    /// <summary>Session-wide global variables, accessible from scripts as <c>$Name</c>.</summary>
    IDictionary<string, string> Globals { get; }

    /// <summary>Print a line to the game window (or status output).</summary>
    void Echo(string text);

    /// <summary>
    /// Queue a command to the game server, respecting the same type-ahead
    /// budget the script engine and mapper use.
    /// </summary>
    void SendCommand(string command);
}
