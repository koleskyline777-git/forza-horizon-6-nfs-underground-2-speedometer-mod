using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Nfsu2ForzaHud.Hud;

public static class ImageUtil
{
    public static BitmapSource Load(string path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.UriSource = new Uri(path, UriKind.Absolute);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    /// <summary>
    /// Tint a white/greyscale mask. Near-black becomes fully transparent.
    /// </summary>
    public static BitmapSource Tint(BitmapSource source, Color color, byte lumaCutoff = 20)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int w = converted.PixelWidth;
        int h = converted.PixelHeight;
        int stride = w * 4;
        var pixels = new byte[h * stride];
        converted.CopyPixels(pixels, stride, 0);

        for (int i = 0; i < pixels.Length; i += 4)
        {
            byte b = pixels[i];
            byte g = pixels[i + 1];
            byte r = pixels[i + 2];
            byte a = pixels[i + 3];
            int luma = Math.Max(r, Math.Max(g, b));

            // Alpha-only masks: treat alpha as intensity.
            if (luma < lumaCutoff && a > lumaCutoff)
                luma = a;

            if (luma < lumaCutoff)
            {
                pixels[i] = pixels[i + 1] = pixels[i + 2] = pixels[i + 3] = 0;
                continue;
            }

            double t = luma / 255.0;
            pixels[i] = (byte)(color.B * t);
            pixels[i + 1] = (byte)(color.G * t);
            pixels[i + 2] = (byte)(color.R * t);
            pixels[i + 3] = (byte)Math.Clamp(Math.Max(a, luma) * (color.A / 255.0), 0, 255);
        }

        var result = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        result.Freeze();
        return result;
    }

    public static string AssetPath(params string[] parts)
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        return Path.GetFullPath(Path.Combine(new[] { baseDir }.Concat(parts).ToArray()));
    }
}
