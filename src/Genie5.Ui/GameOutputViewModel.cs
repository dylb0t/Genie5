using System.Collections.ObjectModel;
using Dock.Model.Mvvm.Controls;
using Genie4.Core.Layout;

namespace Genie5.Ui;

public sealed class GameOutputViewModel : Document
{
    private const int MaxLines = 2000;

    public ObservableCollection<RenderLine> Lines { get; } = new();

    /// <summary>Display settings (font, colour, timestamp). Set by MainWindow after store is loaded.</summary>
    public WindowSettings? Settings { get; set; }

    public GameOutputViewModel() : this("GameOutput", "Game Output") { }

    public GameOutputViewModel(string id, string title, bool canClose = false)
    {
        Id = id;
        Title = title;
        CanClose = canClose;
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
