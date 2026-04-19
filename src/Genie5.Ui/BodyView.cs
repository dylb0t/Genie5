using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Genie4.Core.Gsl;

namespace Genie5.Ui;

// Draws the authentic Genie4 posture sprite (standing/sitting/kneeling/prone/dead)
// from Assets/Icons. Black pixels are made transparent at load time so the sprite
// blends cleanly onto any background.
public sealed class BodyView : Control
{
    private enum Posture { Standing, Sitting, Kneeling, Prone, Dead }

    private static readonly Bitmap StandingBmp = Load("standing.png");
    private static readonly Bitmap SittingBmp  = Load("sitting.png");
    private static readonly Bitmap KneelingBmp = Load("kneeling.png");
    private static readonly Bitmap ProneBmp    = Load("prone.png");
    private static readonly Bitmap DeadBmp     = Load("dead.png");

    private Posture _posture = Posture.Standing;

    public BodyView()
    {
        // Match the authentic 28×32 sprite at 1:1 to preserve pixel clarity.
        Width  = 28;
        Height = 32;
    }

    public void Attach(GslGameState state)
    {
        state.StateChanged += () =>
        {
            _posture =
                state.Dead     ? Posture.Dead     :
                state.Prone    ? Posture.Prone    :
                state.Kneeling ? Posture.Kneeling :
                state.Sitting  ? Posture.Sitting  :
                                 Posture.Standing;
            InvalidateVisual();
        };
    }

    public override void Render(DrawingContext ctx)
    {
        var bmp = _posture switch
        {
            Posture.Dead     => DeadBmp,
            Posture.Prone    => ProneBmp,
            Posture.Kneeling => KneelingBmp,
            Posture.Sitting  => SittingBmp,
            _                => StandingBmp,
        };

        var bw = bmp.PixelSize.Width;
        var bh = bmp.PixelSize.Height;
        var scale = Math.Min(Bounds.Width / bw, Bounds.Height / bh);
        var dw = bw * scale;
        var dh = bh * scale;
        var dx = (Bounds.Width  - dw) / 2;
        var dy = (Bounds.Height - dh) / 2;
        var dst = new Rect(dx, dy, dw, dh);

        using (ctx.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None }))
            ctx.DrawImage(bmp, dst);
    }

    private static Bitmap Load(string name)
    {
        var uri = new Uri($"avares://Genie/Assets/Icons/{name}");
        using var stream = AssetLoader.Open(uri);
        return IconLoader.LoadBlackTransparent(stream);
    }
}
