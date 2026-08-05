using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Nfsu2ForzaHud.Hud;

/// <summary>
/// Loads NFSU2HUD 3.0 (Assetto Corsa) textures.
/// AC treats near-black as transparent — we convert that on load.
/// </summary>
public sealed class AcHudAssets
{
    private readonly string _root;
    private readonly ConcurrentDictionary<string, BitmapSource> _cache = new(StringComparer.OrdinalIgnoreCase);

    public AcHudAssets(string root) => _root = root;

    public static string? FindRoot()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var candidates = new[]
        {
            // Preferred: next to the exe
            Path.Combine(baseDir, "Assets", "AcHud"),
            Path.Combine(baseDir, "AcHud"),
            // Dev tree
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "Assets", "AcHud")),
            // Common local installs of the AC mod
            Path.Combine(desktop, "Air Gestures", "nfsu2-hud-assets", "NFSU2HUD 3.0", "apps", "python", "NFSU2HUD", "img"),
            Path.Combine(desktop, "nfsu2-hud-assets", "NFSU2HUD 3.0", "apps", "python", "NFSU2HUD", "img"),
            Path.Combine(desktop, "NFSU2HUD 3.0", "apps", "python", "NFSU2HUD", "img"),
        };
        foreach (var c in candidates)
        {
            if (File.Exists(Path.Combine(c, "background", "background.png")))
                return c;
        }
        return null;
    }

    public BitmapSource Get(string relativePath)
    {
        return _cache.GetOrAdd(relativePath.Replace('/', Path.DirectorySeparatorChar), key =>
        {
            var full = Path.Combine(_root, key);
            if (!File.Exists(full))
                throw new FileNotFoundException("AC HUD texture missing", full);
            return LoadAcTransparent(full);
        });
    }

    public BitmapSource? TryGet(string relativePath)
    {
        try { return Get(relativePath); }
        catch { return null; }
    }

    public BitmapSource Rev(int frame) =>
        Get($"rev/rev_{Math.Clamp(frame, 0, 269):000}.png");

    public BitmapSource BoostNeedle(int frame) =>
        Get($"boost_needle/boost_needle_{Math.Clamp(frame, 0, 99):000}.png");

    public BitmapSource Speed(int value) =>
        Get($"speed/speed_{Math.Clamp(value, 0, 500):000}.png");

    public BitmapSource Gear(string label, bool flashOrange)
    {
        var folder = flashOrange ? "gears_orange" : "gears_white";
        var name = label.ToUpperInvariant() switch
        {
            "R" => "gear_R",
            "N" => "gear_N",
            _ => $"gear_{label}"
        };
        return Get($"{folder}/{name}_{(flashOrange ? "orange" : "white")}.png");
    }

    public BitmapSource Uom(bool mph) =>
        Get(mph ? "uom/uom_mph.png" : "uom/uom_kmh.png");

    public BitmapSource RpmFace(string face) =>
        Get($"background/rpm_{face}.png");

    public BitmapSource Background => Get("background/background.png");
    public BitmapSource BoostNosOverlay => Get("boost_nos_overlay/boost_nos_overlay.png");
    public BitmapSource GearFlash => Get("gear_flash/gear_flash.png");
    public BitmapSource GearFlashOff => Get("gear_flash/gear_flash_off.png");

    /// <summary>Port of NFSU2HUD.py face + spin_rate table (supports bikes up to ~20k).</summary>
    public static (string Face, double SpinRate) FaceForMaxRpm(float maxRpm)
    {
        if (maxRpm > 16999) return ("16500", 0.013475);
        if (maxRpm > 14999) return ("14200", 0.013485);
        if (maxRpm > 11999) return ("10700", 0.01809);
        if (maxRpm > 11099) return ("10300", 0.02235);
        if (maxRpm > 9999) return ("9200", 0.02441);
        if (maxRpm > 9399) return ("9200", 0.024435);
        if (maxRpm > 8999) return ("8800", 0.02682);
        if (maxRpm > 7999) return ("8000", 0.026695);
        if (maxRpm > 6999) return ("6400", 0.0339);
        if (maxRpm > 6099) return ("6200", 0.03875);
        if (maxRpm > 4999) return ("4600", 0.0445);
        if (maxRpm > 3499) return ("3300", 0.054);
        return ("8800", 0.04005);
    }

    public static int RevFrame(float rpm, double spinRate) =>
        (int)Math.Clamp(Math.Round(rpm * spinRate), 0, 269);

    /// <summary>Map Forza boost PSI (−30..+30) onto AC needle frames 0..99.</summary>
    public static int BoostFrame(float boostPsi)
    {
        var t = (Math.Clamp(boostPsi, -30f, 30f) + 30f) / 60f;
        return (int)Math.Clamp(Math.Round(t * 99.0), 0, 99);
    }

    private static BitmapSource LoadAcTransparent(string path)
    {
        var src = ImageUtil.Load(path);
        var converted = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
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
            int mx = Math.Max(r, Math.Max(g, b));

            // AC PNGs store empty texels as RGB white/black with alpha 0.
            // Never promote those to opaque (that created the giant white square).
            if (a < 8)
            {
                pixels[i] = pixels[i + 1] = pixels[i + 2] = pixels[i + 3] = 0;
                continue;
            }

            // Black RGB + alpha = smoked glass / filled gauge body.
            if (mx < 8)
            {
                pixels[i] = pixels[i + 1] = pixels[i + 2] = 16;
                pixels[i + 3] = a;
                continue;
            }

            // Colored pixel — keep authored RGBA as-is.
        }

        var result = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        result.Freeze();
        return result;
    }
}
