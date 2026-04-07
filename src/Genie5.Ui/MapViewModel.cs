using Avalonia.Threading;
using Dock.Model.Mvvm.Controls;
using Genie4.Core.Mapper;

namespace Genie5.Ui;

public sealed class MapViewModel : Document
{
    private MapNode? _currentNode;

    public AutoMapperEngine Engine { get; }

    public MapNode? CurrentNode
    {
        get => _currentNode;
        private set => SetProperty(ref _currentNode, value);
    }

    public MapViewModel(AutoMapperEngine engine)
    {
        Id       = "Map";
        Title    = "Map";
        CanClose = true;
        CanPin   = false;
        Engine   = engine;

        engine.CurrentNodeChanged += () =>
            Dispatcher.UIThread.Post(() => CurrentNode = engine.CurrentNode);
    }
}
