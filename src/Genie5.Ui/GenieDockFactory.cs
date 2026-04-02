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
    private readonly GameOutputViewModel _gameOutputVm;
    private IRootDock? _rootDock;
    private IDocumentDock? _documentDock;

    public GenieDockFactory(GameOutputViewModel gameOutputVm)
    {
        _gameOutputVm = gameOutputVm;
    }

    public override IRootDock CreateLayout()
    {
        var documentDock = new DocumentDock
        {
            Id = "Documents",
            Title = "Documents",
            IsCollapsable = false,
            CanCreateDocument = false,
            VisibleDockables = CreateList<IDockable>(_gameOutputVm),
            ActiveDockable = _gameOutputVm
        };
        _documentDock = documentDock;

        var rootDock = CreateRootDock();
        rootDock.Id = "Root";
        rootDock.Title = "Root";
        rootDock.IsCollapsable = false;
        rootDock.VisibleDockables = CreateList<IDockable>(documentDock);
        rootDock.DefaultDockable = documentDock;
        rootDock.ActiveDockable = documentDock;
        _rootDock = rootDock;

        return rootDock;
    }

    public override void InitLayout(IDockable layout)
    {
        ContextLocator = new Dictionary<string, Func<object?>>
        {
            [_gameOutputVm.Id] = () => _gameOutputVm
        };

        DockableLocator = new Dictionary<string, Func<IDockable?>>
        {
            ["Root"]      = () => _rootDock,
            ["Documents"] = () => _documentDock
        };

        HostWindowLocator = new Dictionary<string, Func<IHostWindow?>>
        {
            [nameof(IDockWindow)] = () => new HostWindow()
        };

        base.InitLayout(layout);
    }
}
