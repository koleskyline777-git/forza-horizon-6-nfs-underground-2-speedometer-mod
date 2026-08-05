using System.IO;
using System.Text.Json;
using System.Windows.Media;

namespace Nfsu2ForzaHud.Hud;

public sealed class RaceLayout
{
    public double CanvasWidth { get; set; } = 640;
    public double CanvasHeight { get; set; } = 420;
    public string Anchor { get; set; } = "bottom-right";
    public double MarginX { get; set; } = 24;
    public double MarginY { get; set; } = 18;
    public double Scale { get; set; } = 1.0;
    public string TintBlue { get; set; } = "#2A6FDB";
    public string TintCyan { get; set; } = "#3EC4FF";
    public string TintRed { get; set; } = "#D62828";
    public string GearColor { get; set; } = "#FF7A18";
    public string SpeedColor { get; set; } = "#FFFFFF";
    public string UnitColor { get; set; } = "#E8EEF7";
    public GaugeLayout Tach { get; set; } = new();
    public GaugeLayout Turbo { get; set; } = new();
    public RectLayout Nos { get; set; } = new();
    public TextLayout Gear { get; set; } = new();
    public TextLayout Speed { get; set; } = new();
    public TextLayout Unit { get; set; } = new();

    public Color Blue => (Color)ColorConverter.ConvertFromString(TintBlue)!;
    public Color Cyan => (Color)ColorConverter.ConvertFromString(TintCyan)!;
    public Color Red => (Color)ColorConverter.ConvertFromString(TintRed)!;
    public Color GearBrush => (Color)ColorConverter.ConvertFromString(GearColor)!;
    public Color SpeedBrush => (Color)ColorConverter.ConvertFromString(SpeedColor)!;
    public Color UnitBrush => (Color)ColorConverter.ConvertFromString(UnitColor)!;

    public static RaceLayout Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<RaceLayout>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new RaceLayout();
    }
}

public sealed class GaugeLayout
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double NeedlePivotX { get; set; } = 0.5;
    public double NeedlePivotY { get; set; } = 0.5;
    public double AngleStartDeg { get; set; } = 0;
    public double AngleEndDeg { get; set; } = 240;
    public double NeedleWidth { get; set; } = 24;
    public double NeedleHeight { get; set; } = 160;
    public float FaceMaxRpm { get; set; } = 8000;

    public double AngleFor(double t01)
    {
        t01 = Math.Clamp(t01, 0, 1.05);
        // Clockwise sweep from start → end (WPF: 0° = up, positive = CW).
        double cw = (AngleEndDeg - AngleStartDeg + 360.0) % 360.0;
        if (cw < 1.0) cw = 360.0;
        return AngleStartDeg + cw * t01;
    }
}

public sealed class RectLayout
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}

public sealed class TextLayout
{
    public double X { get; set; }
    public double Y { get; set; }
    public double FontSize { get; set; } = 48;
}
