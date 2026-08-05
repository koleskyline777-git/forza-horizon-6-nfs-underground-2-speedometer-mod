using System.IO;
using System.Text.Json;

namespace Nfsu2ForzaHud.Hud;

public sealed class HudSettings
{
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    public double Scale { get; set; } = 1.45;
    public bool UseMph { get; set; } = true;
    /// <summary>Optional absolute path to NFSU2HUD img folder (saved after folder picker).</summary>
    public string? AssetsPath { get; set; }

    private static string Path =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Nfsu2ForzaHud",
            "settings.json");

    public static HudSettings Load()
    {
        try
        {
            var path = Path;
            if (File.Exists(path))
            {
                var s = JsonSerializer.Deserialize<HudSettings>(File.ReadAllText(path));
                if (s != null) return s;
            }
        }
        catch { /* defaults */ }
        return new HudSettings();
    }

    public void Save()
    {
        try
        {
            var path = Path;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* ignore */ }
    }
}
