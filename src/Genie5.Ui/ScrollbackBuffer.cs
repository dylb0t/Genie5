namespace Genie5.Ui;

public sealed class ScrollbackBuffer
{
    private readonly LinkedList<RenderLine> _lines = new();
    private readonly int _max;

    public ScrollbackBuffer(int max = 2000)
    {
        _max = max;
    }

    public void Add(RenderLine line)
    {
        _lines.AddLast(line);
        while (_lines.Count > _max)
            _lines.RemoveFirst();
    }
}