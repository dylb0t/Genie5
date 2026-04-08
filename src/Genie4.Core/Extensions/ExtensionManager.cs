namespace Genie4.Core.Extensions;

/// <summary>
/// Owns the live set of extensions and dispatches game events to each
/// enabled one. Has no awareness of which extensions exist — they're
/// registered by the bootstrap code at startup.
/// </summary>
public sealed class ExtensionManager
{
    private readonly List<IGameExtension> _extensions = new();
    private readonly IExtensionHost       _host;

    public ExtensionManager(IExtensionHost host) { _host = host; }

    public IReadOnlyList<IGameExtension> Extensions => _extensions;

    public void Register(IGameExtension ext)
    {
        if (_extensions.Any(e => e.Name.Equals(ext.Name, StringComparison.OrdinalIgnoreCase)))
            return;
        _extensions.Add(ext);
        try { ext.Initialize(_host); }
        catch (Exception ex)
        { _host.Echo($"[ext] {ext.Name}: init failed — {ex.Message}"); }
    }

    public void Shutdown()
    {
        foreach (var e in _extensions)
        {
            try { e.Shutdown(); }
            catch { /* swallow — shutdown must not throw */ }
        }
        _extensions.Clear();
    }

    public void DispatchGameLine(string line)
    {
        for (int i = 0; i < _extensions.Count; i++)
        {
            var e = _extensions[i];
            if (!e.Enabled) continue;
            try { e.OnGameLine(line); }
            catch (Exception ex) { _host.Echo($"[ext] {e.Name}: {ex.Message}"); }
        }
    }

    public void DispatchCommand(string command)
    {
        for (int i = 0; i < _extensions.Count; i++)
        {
            var e = _extensions[i];
            if (!e.Enabled) continue;
            try { e.OnCommandSent(command); }
            catch (Exception ex) { _host.Echo($"[ext] {e.Name}: {ex.Message}"); }
        }
    }

    public void DispatchPrompt()
    {
        for (int i = 0; i < _extensions.Count; i++)
        {
            var e = _extensions[i];
            if (!e.Enabled) continue;
            try { e.OnPrompt(); }
            catch (Exception ex) { _host.Echo($"[ext] {e.Name}: {ex.Message}"); }
        }
    }
}
