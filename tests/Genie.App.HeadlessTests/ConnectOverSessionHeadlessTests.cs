using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Genie.App.ViewModels;
using Genie.Core;
using Genie.Core.Connection;
using Xunit;

namespace Genie.App.HeadlessTests;

/// <summary>
/// Connecting over an open session (character switch, <c>#connect</c>, a script
/// or inbox login) closes the old link first. Its Disconnected event used to
/// land while the new login was under way, and Genie took it for the session
/// ending: a "disconnected" marker in the middle of the login and the "You have
/// been disconnected" popup while connected. Genie 4 closed the old socket
/// silently (<c>Connection.Connect</c>). A real end of session still shows both.
/// </summary>
public class ConnectOverSessionHeadlessTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "genie_connover_" + Guid.NewGuid().ToString("N"));

    // Connecting pushes the session's config into these statics; put them back
    // so later tests in the assembly see the defaults.
    private readonly bool _links      = Highlighting.DefaultHighlights.LinksEnabled;
    private readonly bool _monsterBold = Highlighting.DefaultHighlights.MonsterBoldEnabled;

    public ConnectOverSessionHeadlessTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        Highlighting.DefaultHighlights.LinksEnabled       = _links;
        Highlighting.DefaultHighlights.MonsterBoldEnabled = _monsterBold;
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static int FreeTcpPort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private (DevReplayServer server, ConnectionConfig cfg) Server(string name)
    {
        var replay = Path.Combine(_dir, name + ".xml");
        File.WriteAllText(replay, "<settingsInfo/>\n");
        var port   = FreeTcpPort();
        var server = new DevReplayServer(replay, port: port, speed: 0.0, hangAfterStream: true);
        server.Start();
        return (server, new ConnectionConfig
        {
            Mode          = ConnectionMode.DevReplay,
            LichProxyHost = "127.0.0.1",
            LichProxyPort = port,
            CharacterName = name,
        });
    }

    /// <summary>Let posted connection events reach the view-model.</summary>
    private static async Task Settle()
    {
        for (int i = 0; i < 20; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(25);
        }
    }

    private static int DisconnectedMarkers(MainWindowViewModel vm)
        => vm.GameText.Lines.Count(l => l.Text.EndsWith("disconnected", StringComparison.Ordinal));

    [AvaloniaFact]
    public async Task Connecting_over_an_open_session_shows_no_disconnect()
    {
        var (a, cfgA) = Server("Alpha");
        var (b, cfgB) = Server("Beta");
        await using var _a = a;
        await using var _b = b;

        var vm = new MainWindowViewModel(startup: null, dataDirectoryOverride: _dir);
        int popups = 0;
        using var h = vm.ShowDisconnectNotice.RegisterHandler(ctx => { popups++; ctx.SetOutput(Unit.Default); });

        await vm.ReplaceSessionAsync(cfgA, profile: null);
        await Settle();
        Assert.True(vm.IsConnected);

        await vm.ReplaceSessionAsync(cfgB, profile: null);     // connect over Alpha
        await Settle();

        Assert.True(vm.IsConnected);
        Assert.False(vm.IsReplacingSession);
        Assert.Equal(0, popups);
        Assert.Equal(0, DisconnectedMarkers(vm));
    }

    [AvaloniaFact]
    public async Task A_real_end_of_session_still_shows_the_disconnect()
    {
        var (a, cfgA) = Server("Alpha");     // disposed below: that is the "server goes away"

        var vm = new MainWindowViewModel(startup: null, dataDirectoryOverride: _dir);
        vm.Display.ShowDisconnectPopup = true;
        int popups = 0;
        using var h = vm.ShowDisconnectNotice.RegisterHandler(ctx => { popups++; ctx.SetOutput(Unit.Default); });

        await vm.ReplaceSessionAsync(cfgA, profile: null);
        await Settle();

        await a.DisposeAsync();                                 // the server goes away
        await Settle();
        await Task.Delay(500);
        await Settle();

        Assert.False(vm.IsConnected);
        Assert.Equal(1, popups);
        Assert.Equal(1, DisconnectedMarkers(vm));
    }
}
