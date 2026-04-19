using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Genie5.Ui;

// Loads a Genie4 pixel-art icon (black-background PNG) into an Avalonia
// bitmap with pure-black pixels mapped to transparent, mirroring what
// WinForms Bitmap.MakeTransparent(Color.Black) did in the original client.
internal static class IconLoader
{
    public static WriteableBitmap LoadBlackTransparent(Stream stream)
    {
        using var src = new Bitmap(stream);
        var size = src.PixelSize;
        var wb = new WriteableBitmap(size, src.Dpi, PixelFormat.Bgra8888, AlphaFormat.Unpremul);

        using var fb = wb.Lock();
        var byteCount = fb.RowBytes * size.Height;
        src.CopyPixels(new PixelRect(0, 0, size.Width, size.Height), fb.Address, byteCount, fb.RowBytes);

        var bytes = new byte[byteCount];
        Marshal.Copy(fb.Address, bytes, 0, byteCount);

        for (int y = 0; y < size.Height; y++)
        {
            int row = y * fb.RowBytes;
            for (int x = 0; x < size.Width; x++)
            {
                int i = row + x * 4;
                if (bytes[i] == 0 && bytes[i + 1] == 0 && bytes[i + 2] == 0)
                    bytes[i + 3] = 0;
            }
        }

        Marshal.Copy(bytes, 0, fb.Address, byteCount);
        return wb;
    }
}
