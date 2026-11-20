using System.Text.Json;

namespace XRPlayer;

public class SubStyle
{
    public bool Enabled { get; set; } = true;
    public string Font { get; set; } = "Segoe UI";
    public int Size { get; set; } = 52;              // px at 1080p
    public bool Bold { get; set; } = false;
    public bool Italic { get; set; } = false;
    public bool Underline { get; set; } = false;
    public string Color { get; set; } = "#FFFFFF";
    public int Opacity { get; set; } = 100;           // text opacity %
    public bool UseBox { get; set; } = false;         // background box instead of outline
    public string OutlineColor { get; set; } = "#000000";
    public double OutlineWidth { get; set; } = 2.5;
    public int BoxPadding { get; set; } = 8;
    public int BoxOpacity { get; set; } = 60;
    public string ShadowColor { get; set; } = "#000000";
    public double ShadowDepth { get; set; } = 1.5;
    public double Blur { get; set; } = 0.5;
    public double LetterSpacing { get; set; } = 0;
    public int Align { get; set; } = 1;               // 0 left,1 center,2 right
    public double PosY { get; set; } = 92;            // % from top (custom layout)
    public double PosX { get; set; } = 50;            // % from left (custom layout)
    public double DelaySec { get; set; } = 0;
    public string PreferredLang { get; set; } = "";   // e.g. "eng", "ben"
}

public class AppSettings
{
    // "Stacked" = both in one block, "TopBottom" = sub1 bottom + sub2 top, "Custom" = free positions
    public string Layout { get; set; } = "Stacked";
    public bool Sub2OnTop { get; set; } = true;       // stacked order
    public int StackGap { get; set; } = 6;            // px between the two blocks
    public bool NativeSubRendering { get; set; } = false;
    public SubStyle Sub1 { get; set; } = new SubStyle { Color = "#FFFFFF", PosY = 92 };
    public SubStyle Sub2 { get; set; } = new SubStyle { Color = "#FFE066", Size = 44, PosY = 6 };

    public int Volume { get; set; } = 80;
    public string Hwdec { get; set; } = "auto-safe";
    public bool ResumePlayback { get; set; } = true;
    public bool AutoPlayNext { get; set; } = true;
    public bool AlwaysOnTop { get; set; } = false;
    public int SeekSmall { get; set; } = 5;
    public int SeekBig { get; set; } = 30;
    public string ScreenshotDir { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
    public string AudioLang { get; set; } = "";
    public string Deinterlace { get; set; } = "no";
    public string Scale { get; set; } = "ewa_lanczossharp";
    public bool Loop { get; set; } = false;
    public Dictionary<string, double> ResumePositions { get; set; } = new();

    // Portable: settings live next to the exe (data\settings.json). Falls back to AppData if that folder is read-only.
    static string FilePath
    {
        get
        {
            try
            {
                var dir = System.IO.Path.Combine(AppContext.BaseDirectory, "data");
                Directory.CreateDirectory(dir);
                var test = System.IO.Path.Combine(dir, ".w"); File.WriteAllText(test, "x"); File.Delete(test);
                return System.IO.Path.Combine(dir, "settings.json");
            }
            catch
            {
                return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinPlayer", "settings.json");
            }
        }
    }

    public static AppSettings Load()
    {
        try { if (File.Exists(FilePath)) return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings(); }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
