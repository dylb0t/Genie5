using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Genie4.Core.Mapper;

namespace Genie5.Ui;

public sealed class MapView : Control
{
    private MapViewModel? _vm;

    // Rendering constants
    private const double NodeW  = 14;
    private const double NodeH  = 10;
    private const double StubLen = 10; // pixels for an exit stub

    // Pan / zoom state
    private double _scale   = 24.0; // pixels per grid unit
    private double _offsetX = 0.0;
    private double _offsetY = 0.0;
    private Point  _dragStart;
    private double _offsetXAtDrag, _offsetYAtDrag;
    private bool   _isDragging;

    // Brushes (created once)
    private static readonly IBrush BgBrush      = new SolidColorBrush(Color.FromRgb(18,  20,  24));
    private static readonly IBrush NodeBrush     = new SolidColorBrush(Color.FromRgb(60,  80, 110));
    private static readonly IBrush NodeBorder    = new SolidColorBrush(Color.FromRgb(100, 140, 190));
    private static readonly IBrush CurrentBrush  = new SolidColorBrush(Color.FromRgb(40, 160,  80));
    private static readonly IBrush CurrentBorder = new SolidColorBrush(Color.FromRgb(80, 220, 120));
    private static readonly IBrush ExitBrush     = new SolidColorBrush(Color.FromRgb(130, 160, 200));
    private static readonly IBrush LinkedBrush   = new SolidColorBrush(Color.FromRgb(80, 120, 180));
    private static readonly IBrush TextBrush     = new SolidColorBrush(Color.FromRgb(200, 210, 220));
    private static readonly IBrush ZLabelBrush   = new SolidColorBrush(Color.FromRgb(150, 150, 150));

    public MapView()
    {
        ClipToBounds     = true;
        Focusable        = true;
        IsHitTestVisible = true;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MapViewModel vm)
            Attach(vm);
    }

    public void Attach(MapViewModel vm)
    {
        _vm = vm;
        vm.Engine.MapChanged         += () => Avalonia.Threading.Dispatcher.UIThread.Post(InvalidateVisual);
        vm.Engine.CurrentNodeChanged += () => Avalonia.Threading.Dispatcher.UIThread.Post(InvalidateVisual);
    }

    // ── Rendering ────────────────────────────────────────────────────────────

    public override void Render(DrawingContext ctx)
    {
        var bounds = new Rect(0, 0, Bounds.Width, Bounds.Height);
        ctx.FillRectangle(BgBrush, bounds);

        if (_vm is null) return;

        var zone        = _vm.Engine.ActiveZone;
        var currentNode = _vm.Engine.CurrentNode;
        var currentZ    = currentNode?.Z ?? 0;
        var cx          = Bounds.Width  / 2 + _offsetX;
        var cy          = Bounds.Height / 2 + _offsetY;

        // ── Draw exit lines first (behind nodes) ─────────────────────────────
        foreach (var node in zone.Nodes.Values)
        {
            if (node.Z != currentZ) continue;
            var (nx, ny) = NodeScreen(node, cx, cy);

            foreach (var exit in node.Exits)
            {
                if (!DirectionHelper.Angle.TryGetValue(exit.Direction, out var angleDeg)) continue;

                var rad = angleDeg * Math.PI / 180.0;
                var tx  = nx + Math.Cos(rad) * StubLen;
                var ty  = ny + Math.Sin(rad) * StubLen;

                IBrush lineBrush;
                if (exit.DestinationId.HasValue &&
                    zone.Nodes.TryGetValue(exit.DestinationId.Value, out var dest) &&
                    dest.Z == currentZ)
                {
                    // Draw full line to destination centre
                    var (dx, dy) = NodeScreen(dest, cx, cy);
                    lineBrush = LinkedBrush;
                    ctx.DrawLine(new Pen(lineBrush, 1.0), new Point(nx, ny), new Point(dx, dy));
                }
                else
                {
                    // Stub only
                    lineBrush = ExitBrush;
                    ctx.DrawLine(new Pen(lineBrush, 1.0), new Point(nx, ny), new Point(tx, ty));
                }
            }
        }

        // ── Draw nodes ────────────────────────────────────────────────────────
        foreach (var node in zone.Nodes.Values)
        {
            if (node.Z != currentZ) continue;
            var (nx, ny) = NodeScreen(node, cx, cy);
            var isCurrent = node.Id == currentNode?.Id;

            IBrush fill   = isCurrent ? CurrentBrush  : NodeBrush;
            IBrush border = isCurrent ? CurrentBorder : NodeBorder;

            if (!string.IsNullOrEmpty(node.Color) &&
                Color.TryParse(node.Color, out var customColor))
                fill = new SolidColorBrush(customColor);

            var rect = new Rect(nx - NodeW / 2, ny - NodeH / 2, NodeW, NodeH);
            ctx.FillRectangle(fill, rect);
            ctx.DrawRectangle(new Pen(border, isCurrent ? 1.5 : 1.0), rect);
        }

        // ── Z-level label ─────────────────────────────────────────────────────
        var zLabel = new FormattedText(
            $"Floor {currentZ}",
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("monospace"),
            11,
            ZLabelBrush);
        ctx.DrawText(zLabel, new Point(6, 4));

        // ── Node count ───────────────────────────────────────────────────────
        var countLabel = new FormattedText(
            $"{zone.Nodes.Count} rooms",
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("monospace"),
            11,
            ZLabelBrush);
        ctx.DrawText(countLabel, new Point(6, 18));
    }

    private (double x, double y) NodeScreen(MapNode node, double cx, double cy)
        => (cx + node.X * _scale, cy + node.Y * _scale);

    // ── Mouse interaction ────────────────────────────────────────────────────

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _isDragging    = true;
            _dragStart     = e.GetPosition(this);
            _offsetXAtDrag = _offsetX;
            _offsetYAtDrag = _offsetY;
            e.Pointer.Capture(this);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_isDragging) return;
        var pos = e.GetPosition(this);
        _offsetX = _offsetXAtDrag + (pos.X - _dragStart.X);
        _offsetY = _offsetYAtDrag + (pos.Y - _dragStart.Y);
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _isDragging = false;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var pos      = e.GetPosition(this);
        var cx       = Bounds.Width  / 2 + _offsetX;
        var cy       = Bounds.Height / 2 + _offsetY;
        var worldX   = (pos.X - cx) / _scale;
        var worldY   = (pos.Y - cy) / _scale;

        _scale = Math.Clamp(_scale * (e.Delta.Y > 0 ? 1.15 : 0.87), 6.0, 100.0);

        // Re-centre zoom around the cursor position
        _offsetX = pos.X - Bounds.Width  / 2 - worldX * _scale;
        _offsetY = pos.Y - Bounds.Height / 2 - worldY * _scale;
        InvalidateVisual();
    }

    /// <summary>Centre the view on the current node.</summary>
    public void CentreOnCurrent()
    {
        if (_vm?.Engine.CurrentNode is { } node)
        {
            _offsetX = -node.X * _scale;
            _offsetY = -node.Y * _scale;
            InvalidateVisual();
        }
    }
}
