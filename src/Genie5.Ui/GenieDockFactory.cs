using System;
using System.Collections.Generic;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;

namespace Genie5.Ui;

public sealed class GenieDockFactory : Factory
{
    private readonly GameOutputViewModel   _mainVm;
    private readonly GameOutputViewModel   _rawVm;
    private readonly GameOutputViewModel[] _streamVms;
    private readonly RoomViewModel         _roomVm;

    private IRootDock?   _rootDock;
    public  DocumentDock? StreamsDock { get; private set; }

    public GenieDockFactory(GameOutputViewModel mainVm,
                            GameOutputViewModel rawVm,
                            GameOutputViewModel[] streamVms,
                            RoomViewModel roomVm)
    {
        _mainVm    = mainVm;
        _rawVm     = rawVm;
        _streamVms = streamVms;
        _roomVm    = roomVm;
    }

    public override IRootDock CreateLayout()
    {
        // Centre-left: main game output (top) + streams tabs (bottom)
        var mainDock = new DocumentDock
        {
            Id                = "MainOutput",
            Title             = "Main",
            IsCollapsable     = false,
            CanCreateDocument = false,
            Proportion        = 0.65,
            VisibleDockables  = CreateList<IDockable>(_mainVm, _rawVm),
            ActiveDockable    = _mainVm
        };

        var streamDockables = new List<IDockable>();
        foreach (var vm in _streamVms)
            streamDockables.Add(vm);

        StreamsDock = new DocumentDock
        {
            Id                = "Streams",
            Title             = "Streams",
            IsCollapsable     = false,
            CanCreateDocument = false,
            Proportion        = 0.35,
            VisibleDockables  = streamDockables,
            ActiveDockable    = _streamVms.Length > 0 ? _streamVms[0] : null
        };
        var streamsDock = StreamsDock;

        var leftPanel = new ProportionalDock
        {
            Id               = "LeftPanel",
            Orientation      = Orientation.Vertical,
            IsCollapsable    = false,
            Proportion       = 0.72,
            VisibleDockables = CreateList<IDockable>(
                mainDock,
                new ProportionalDockSplitter { Id = "MainStreamSplitter" },
                streamsDock)
        };

        // Right panel: room info only (map is a floating window)
        var roomDock = new DocumentDock
        {
            Id                = "RoomPanel",
            Title             = "Room",
            IsCollapsable     = false,
            CanCreateDocument = false,
            Proportion        = 1.0,
            VisibleDockables  = CreateList<IDockable>(_roomVm),
            ActiveDockable    = _roomVm
        };

        var rightPanel = new ProportionalDock
        {
            Id               = "RightPanel",
            Orientation      = Orientation.Vertical,
            IsCollapsable    = false,
            Proportion       = 0.28,
            VisibleDockables = CreateList<IDockable>(roomDock)
        };

        var rootLayout = new ProportionalDock
        {
            Id               = "RootLayout",
            Orientation      = Orientation.Horizontal,
            IsCollapsable    = false,
            VisibleDockables = CreateList<IDockable>(
                leftPanel,
                new ProportionalDockSplitter { Id = "RoomSplitter" },
                rightPanel)
        };

        var root = CreateRootDock();
        root.Id               = "Root";
        root.IsCollapsable    = false;
        root.VisibleDockables = CreateList<IDockable>(rootLayout);
        root.DefaultDockable  = rootLayout;
        root.ActiveDockable   = rootLayout;
        _rootDock             = root;

        return root;
    }

    public override void InitLayout(IDockable layout)
    {
        ContextLocator = new Dictionary<string, Func<object?>>
        {
            [_mainVm.Id] = () => _mainVm,
            [_rawVm.Id]  = () => _rawVm,
            [_roomVm.Id] = () => _roomVm,
        };
        foreach (var vm in _streamVms)
            ContextLocator[vm.Id] = () => vm;

        DockableLocator = new Dictionary<string, Func<IDockable?>>
        {
            ["Root"] = () => _rootDock
        };

        HostWindowLocator = new Dictionary<string, Func<IHostWindow?>>
        {
            [nameof(IDockWindow)] = () => new HostWindow()
        };

        base.InitLayout(layout);
    }
}
