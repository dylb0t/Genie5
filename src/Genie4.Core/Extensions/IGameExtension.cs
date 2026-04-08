namespace Genie4.Core.Extensions;

/// <summary>
/// Contract for an in-process Genie5 extension. Built-in trackers (EXP, info)
/// implement this; future user-loadable plugins will use the same interface
/// once a load context is added.
///
/// Lifecycle:
///   - <see cref="Initialize"/> is called once, after the host is wired up.
///   - <see cref="OnGameLine"/> fires for every parsed game line.
///   - <see cref="OnCommandSent"/> fires for every command the user/script sends.
///   - <see cref="OnPrompt"/> fires once per server prompt.
///   - <see cref="Shutdown"/> is called when the engine is torn down.
///
/// Extensions must be safe to call from the UI thread; they may not block.
/// </summary>
public interface IGameExtension
{
    string Name        { get; }
    string Version     { get; }
    string Description { get; }

    bool   Enabled     { get; set; }

    void Initialize(IExtensionHost host);
    void OnGameLine(string line);
    void OnCommandSent(string command);
    void OnPrompt();
    void Shutdown();
}
