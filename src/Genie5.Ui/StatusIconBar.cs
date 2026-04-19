using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Genie4.Core.Gsl;

namespace Genie5.Ui;

// Horizontal row of status-effect icons. Order and fixed slot layout match
// Genie4's ComponentIconBar (TabIndex 2..7): stunned, bleeding, invisible,
// hidden, joined, webbed. Each icon is drawn in its own slot only while the
// corresponding GslGameState flag is active; inactive slots stay blank.
public sealed class StatusIconBar : Control
{
    private static readonly (string File, Func<GslGameState, bool> Active)[] Icons =
    {
        ("stunned.png",   s => s.Stunned),
        ("bleeding.png",  s => s.Bleeding),
        ("invisible.png", s => s.Invisible),
        ("hidden.png",    s => s.Hidden),
        ("joined.png",    s => s.Joined),
        ("webbed.png",    s => s.Webbed),
    };

    private const double IconW = 28;
    private const double IconH = 32;
    private const double Gap   = 4;

    private static readonly Bitmap[] Sprites = LoadSprites();

    private GslGameState? _state;

    public StatusIconBar()
    {
        Width  = Icons.Length * IconW + (Icons.Length - 1) * Gap;
        Height = IconH;
    }

    public void Attach(GslGameState state)
    {
        _state = state;
        state.StateChanged += () => InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        if (_state is null) return;

        using (ctx.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None }))
        {
            for (int i = 0; i < Icons.Length; i++)
            {
                if (!Icons[i].Active(_state)) continue;
                var x = i * (IconW + Gap);
                ctx.DrawImage(Sprites[i], new Rect(x, 0, IconW, IconH));
            }
        }
    }

    private static Bitmap[] LoadSprites()
    {
        var arr = new Bitmap[Icons.Length];
        for (int i = 0; i < Icons.Length; i++)
        {
            var uri = new Uri($"avares://Genie/Assets/Icons/{Icons[i].File}");
            using var stream = AssetLoader.Open(uri);
            arr[i] = IconLoader.LoadBlackTransparent(stream);
        }
        return arr;
    }
}
