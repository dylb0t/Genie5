using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Genie4.Core.Gsl;

namespace Genie5.Ui;

// Authentic Genie4 compass: a base rose (compass.png) with per-direction
// overlays layered on top for each active exit. Images are drawn pixel-perfect
// with nearest-neighbor scaling, matching the original client's look.
public sealed class CompassView : Control
{
    private static readonly Bitmap BaseBmp = Load("compass.png");

    private static readonly (string Key, Bitmap Img)[] Overlays =
    {
        ("n",    Load("compass_north.png")),
        ("ne",   Load("compass_northeast.png")),
        ("e",    Load("compass_east.png")),
        ("se",   Load("compass_southeast.png")),
        ("s",    Load("compass_south.png")),
        ("sw",   Load("compass_southwest.png")),
        ("w",    Load("compass_west.png")),
        ("nw",   Load("compass_northwest.png")),
        ("up",   Load("compass_up.png")),
        ("down", Load("compass_down.png")),
        ("out",  Load("compass_out.png")),
    };

    private HashSet<string> _exits = new(StringComparer.OrdinalIgnoreCase);

    public CompassView()
    {
        // Match the authentic 28×32 source art at 1:1 to preserve pixel clarity.
        Width  = 28;
        Height = 32;
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
        var bw = BaseBmp.PixelSize.Width;
        var bh = BaseBmp.PixelSize.Height;
        var scale = Math.Min(Bounds.Width / bw, Bounds.Height / bh);
        var dw = bw * scale;
        var dh = bh * scale;
        var dx = (Bounds.Width  - dw) / 2;
        var dy = (Bounds.Height - dh) / 2;
        var dst = new Rect(dx, dy, dw, dh);

        using (ctx.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None }))
        {
            ctx.DrawImage(BaseBmp, dst);
            foreach (var (key, img) in Overlays)
                if (_exits.Contains(key))
                    ctx.DrawImage(img, dst);
        }
    }

    private static Bitmap Load(string name)
    {
        var uri = new Uri($"avares://Genie/Assets/Icons/{name}");
        using var stream = AssetLoader.Open(uri);
        return IconLoader.LoadBlackTransparent(stream);
    }
}
