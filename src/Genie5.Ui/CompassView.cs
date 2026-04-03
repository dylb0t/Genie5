using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Genie4.Core.Gsl;

namespace Genie5.Ui;

/// <summary>
/// Compass rose that lights up exits received from the GSL game state.
/// Drawn entirely via Render() — no XAML template required.
/// </summary>
public sealed class CompassView : Control
{
    // Pairs of (exit key, bearing in degrees — 0=right/East, clockwise)
    private static readonly (string Key, double Angle)[] Cardinals =
    {
        ("n",  270), ("ne", 315), ("e",  0),
        ("se",  45), ("s",  90), ("sw", 135),
        ("w",  180), ("nw", 225),
    };

    // Brushes & pens — created once, shared across all render calls
    private static readonly IBrush ActiveFill    = new SolidColorBrush(Color.Parse("#E8C84A"));
    private static readonly IBrush InactiveFill  = new SolidColorBrush(Color.Parse("#242420"));
    private static readonly IBrush BackgroundFill = new SolidColorBrush(Color.Parse("#141414"));
    private static readonly Pen    ActivePen     = new(new SolidColorBrush(Color.Parse("#9A7B10")), 0.8);
    private static readonly Pen    InactivePen   = new(new SolidColorBrush(Color.Parse("#1C1C1C")), 0.8);
    private static readonly Pen    BorderPen     = new(new SolidColorBrush(Color.Parse("#363636")), 1);

    private HashSet<string> _exits = new(StringComparer.OrdinalIgnoreCase);

    public CompassView()
    {
        Width  = 68;
        Height = 82;
    }

    public void Attach(GslGameState state)
    {
        state.StateChanged += () =>
        {
            _exits = new HashSet<string>(state.Exits, StringComparer.OrdinalIgnoreCase);
            InvalidateVisual();
        };
    }

    public override void Render(DrawingContext ctx)
    {
        double w  = Bounds.Width;
        double h  = Bounds.Height;

        // Dark rounded background
        ctx.DrawRectangle(BackgroundFill, BorderPen, new Rect(0, 0, w, h), 4, 4);

        // Rose centre — shifted slightly down to leave room for Up chevron
        double cx = w / 2;
        double cy = h / 2 + 5;
        double r  = w * 0.30;   // arrow tip radius

        // 8 directional arrows
        foreach (var (key, deg) in Cardinals)
            DrawArrow(ctx, cx, cy, deg, r, _exits.Contains(key));

        // Centre circle = Out
        double cr = r * 0.30;
        ctx.DrawEllipse(
            _exits.Contains("out") ? ActiveFill : InactiveFill,
            _exits.Contains("out") ? ActivePen  : InactivePen,
            new Point(cx, cy), cr, cr);

        // Up chevron at the top
        DrawChevron(ctx, cx, 5, 8, true, _exits.Contains("up"));

        // Down chevron at the bottom
        DrawChevron(ctx, cx, h - 5, 8, false, _exits.Contains("down"));
    }

    // Isoceles triangle pointing outward from (cx,cy) along bearing deg.
    private static void DrawArrow(DrawingContext ctx,
        double cx, double cy, double deg, double radius, bool active)
    {
        double rad      = deg * Math.PI / 180.0;
        double tipDist  = radius;
        double baseDist = radius * 0.35;
        double halfBase = radius * 0.32;

        double tx = cx + tipDist  * Math.Cos(rad);
        double ty = cy + tipDist  * Math.Sin(rad);
        double bx = cx + baseDist * Math.Cos(rad);
        double by = cy + baseDist * Math.Sin(rad);

        double px = Math.Cos(rad + Math.PI / 2);
        double py = Math.Sin(rad + Math.PI / 2);

        Triangle(ctx,
            tx, ty,
            bx + halfBase * px, by + halfBase * py,
            bx - halfBase * px, by - halfBase * py,
            active);
    }

    // Small up/down pointing chevron for Up and Down exits.
    // tipY is the y of the pointed end; the base is `size` px away.
    private static void DrawChevron(DrawingContext ctx,
        double cx, double tipY, double size, bool pointingUp, bool active)
    {
        double baseY = pointingUp ? tipY + size : tipY - size;
        Triangle(ctx,
            cx, tipY,
            cx - size * 0.75, baseY,
            cx + size * 0.75, baseY,
            active);
    }

    private static void Triangle(DrawingContext ctx,
        double x1, double y1,
        double x2, double y2,
        double x3, double y3,
        bool active)
    {
        var geo = new StreamGeometry();
        using (var gc = geo.Open())
        {
            gc.BeginFigure(new Point(x1, y1), true);
            gc.LineTo(new Point(x2, y2));
            gc.LineTo(new Point(x3, y3));
            gc.EndFigure(true);
        }
        ctx.DrawGeometry(active ? ActiveFill : InactiveFill,
                         active ? ActivePen  : InactivePen,
                         geo);
    }
}
