using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Genie4.Core.Gsl;

namespace Genie5.Ui;

/// <summary>
/// Simple human-silhouette control.  Body zones are coloured by wound severity.
/// Currently shows global bleeding state; per-zone wounds tracked in todo.
/// </summary>
public sealed class BodyView : Control
{
    private static readonly IBrush NormalFill   = new SolidColorBrush(Color.Parse("#2A2A2A"));
    private static readonly IBrush BleedFill    = new SolidColorBrush(Color.Parse("#7A1515"));
    private static readonly IBrush OutlineBrush = new SolidColorBrush(Color.Parse("#555555"));
    private static readonly Pen    OutlinePen   = new(OutlineBrush, 1);
    private static readonly Pen    BleedPen     = new(new SolidColorBrush(Color.Parse("#AA2020")), 1);

    private bool _bleeding;

    public BodyView()
    {
        Width  = 32;
        Height = 52;
    }

    public void Attach(GslGameState state)
    {
        state.StateChanged += () =>
        {
            _bleeding = state.Bleeding;
            InvalidateVisual();
        };
    }

    public override void Render(DrawingContext ctx)
    {
        var fill = _bleeding ? BleedFill : NormalFill;
        var pen  = _bleeding ? BleedPen  : OutlinePen;

        double w  = Bounds.Width;
        double cx = w / 2;

        // Head
        ctx.DrawEllipse(fill, pen, new Point(cx, 6), 5, 5);

        // Neck
        ctx.DrawRectangle(fill, pen, new Rect(cx - 2, 11, 4, 3));

        // Torso
        ctx.DrawRectangle(fill, pen, new Rect(cx - 8, 14, 16, 16));

        // Left arm
        ctx.DrawRectangle(fill, pen, new Rect(cx - 14, 14, 5, 13));

        // Right arm
        ctx.DrawRectangle(fill, pen, new Rect(cx + 9, 14, 5, 13));

        // Left leg
        ctx.DrawRectangle(fill, pen, new Rect(cx - 8, 30, 6, 18));

        // Right leg
        ctx.DrawRectangle(fill, pen, new Rect(cx + 2, 30, 6, 18));
    }
}
