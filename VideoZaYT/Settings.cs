using System.Text.Json;

namespace VideoZaYT;

public enum BackgroundMode { Blur = 0, Black = 1, White = 2 }

public enum CookieSource { None = 0, Firefox = 1, Chrome = 2, Edge = 3, Brave = 4, CookiesFile = 5 }

public sealed class Settings
{
    public BackgroundMode Background { get; set; } = BackgroundMode.Blur;

    /// <summary>0 = visok (CRF 18), 1 = srednji (CRF 21), 2 = manji fajl (CRF 24)</summary>
    public int Quality { get; set; } = 0;

    public bool UseNvidia { get; set; } = false;

    /// <summary>Lokalni fajlovi se čuvaju pored originala; inače u OutputFolder.</summary>
    public bool SaveNextToOriginal { get; set; } = true;

    public string OutputFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "VideoZaYT");

    public CookieSource Cookies { get; set; } = CookieSource.None;
    public string CookiesFile { get; set; } = "";

    public int Crf => Quality switch { 1 => 21, 2 => 24, _ => 18 };

    // ---------------------------------------------------------------

    public static string AppDataDir
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VideoZaYT");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private static string FilePath => Path.Combine(AppDataDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch { /* oštećen fajl — kreće se od podrazumevanih */ }
        return new Settings();
    }

    public void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOpts)); }
        catch { /* podešavanja nisu kritična */ }
    }
}
