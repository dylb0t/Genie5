using System.Collections.ObjectModel;
using Dock.Model.Mvvm.Controls;

namespace Genie5.Ui;

public sealed class GameOutputViewModel : Document
{
    private const int MaxLines = 2000;

    public ObservableCollection<RenderLine> Lines { get; } = new();

    public GameOutputViewModel() : this("GameOutput", "Game Output") { }

    public GameOutputViewModel(string id, string title)
    {
        Id = id;
        Title = title;
        CanClose = false;
        CanPin = false;
    }

    // Must be called on the UI thread (callers use Dispatcher.UIThread.Post)
    public void AppendLine(RenderLine line)
    {
        Lines.Add(line);
        while (Lines.Count > MaxLines)
            Lines.RemoveAt(0);
    }
}
