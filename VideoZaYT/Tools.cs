using System.IO.Compression;
using System.Net.Http;

namespace VideoZaYT;

/// <summary>
/// Pronalazi ili preuzima ffmpeg/ffprobe i yt-dlp u %LOCALAPPDATA%\VideoZaYT\tools.
/// </summary>
public static class Tools
{
    private const string FfmpegZipUrl =
        "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";
    private const string YtDlpUrl =
        "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";

    public static string ToolsDir
    {
        get
        {
            var dir = Path.Combine(Settings.AppDataDir, "tools");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string Ffmpeg => Path.Combine(ToolsDir, "ffmpeg.exe");
    public static string Ffprobe => Path.Combine(ToolsDir, "ffprobe.exe");
    public static string YtDlp => Path.Combine(ToolsDir, "yt-dlp.exe");

    private static readonly HttpClient Http = CreateHttp();

    private static HttpClient CreateHttp()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("VideoZaYT/1.0");
        return h;
    }

    private static readonly SemaphoreSlim FfmpegLock = new(1, 1);
    private static readonly SemaphoreSlim YtLock = new(1, 1);

    public static bool HasFfmpeg => File.Exists(Ffmpeg) && File.Exists(Ffprobe);
    public static bool HasYtDlp => File.Exists(YtDlp);

    /// <summary>Ako ffmpeg nije tu, kopira ga pored programa (ako postoji) ili ga preuzima.</summary>
    public static async Task EnsureFfmpegAsync(Action<string> status, Action<double> progress, CancellationToken ct)
    {
        await FfmpegLock.WaitAsync(ct);
        try
        {
            if (HasFfmpeg) return;

            // 1) ffmpeg.exe i ffprobe.exe stavljeni pored VideoZaYT.exe
            var local = AppContext.BaseDirectory;
            if (File.Exists(Path.Combine(local, "ffmpeg.exe")) && File.Exists(Path.Combine(local, "ffprobe.exe")))
            {
                File.Copy(Path.Combine(local, "ffmpeg.exe"), Ffmpeg, true);
                File.Copy(Path.Combine(local, "ffprobe.exe"), Ffprobe, true);
                return;
            }

            // 2) preuzimanje (jednom, ~150 MB)
            status("Preuzimam ffmpeg (samo prvi put, ~150 MB)…");
            var zip = Path.Combine(ToolsDir, "ffmpeg-download.zip");
            await DownloadAsync(FfmpegZipUrl, zip, progress, ct);

            status("Raspakujem ffmpeg…");
            using (var archive = ZipFile.OpenRead(zip))
            {
                foreach (var entry in archive.Entries)
                {
                    var name = entry.Name.ToLowerInvariant();
                    if (name is "ffmpeg.exe" or "ffprobe.exe" && entry.FullName.Replace('\\', '/').Contains("/bin/"))
                        entry.ExtractToFile(Path.Combine(ToolsDir, name), overwrite: true);
                }
            }
            File.Delete(zip);

            if (!HasFfmpeg)
                throw new InvalidOperationException("ffmpeg nije pronađen u preuzetoj arhivi.");
        }
        finally { FfmpegLock.Release(); }
    }

    /// <summary>Preuzima yt-dlp ako ga nema, a jednom dnevno proverava ažuriranje (Instagram često menja sajt).</summary>
    public static async Task EnsureYtDlpAsync(Action<string> status, Action<double> progress, CancellationToken ct)
    {
        await YtLock.WaitAsync(ct);
        try
        {
            var stamp = Path.Combine(ToolsDir, "yt-dlp.lastcheck");

            if (!HasYtDlp)
            {
                status("Preuzimam yt-dlp (samo prvi put)…");
                await DownloadAsync(YtDlpUrl, YtDlp, progress, ct);
                File.WriteAllText(stamp, DateTime.Now.ToString("O"));
                return;
            }

            var last = File.Exists(stamp) ? File.GetLastWriteTime(stamp) : DateTime.MinValue;
            if (DateTime.Now - last > TimeSpan.FromDays(1))
            {
                status("Proveravam da li ima nova verzija yt-dlp…");
                try
                {
                    await ProcessRunner.RunAsync(YtDlp, ["-U"], null, null, ct);
                }
                catch (OperationCanceledException) { throw; }
                catch { /* bez interneta za ažuriranje — radi se sa postojećom verzijom */ }
                File.WriteAllText(stamp, DateTime.Now.ToString("O"));
            }
        }
        finally { YtLock.Release(); }
    }

    private static async Task DownloadAsync(string url, string dest, Action<double> progress, CancellationToken ct)
    {
        var tmp = dest + ".part";
        using (var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? -1;
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(tmp);
            var buf = new byte[1 << 16];
            long done = 0;
            int n;
            var lastReport = DateTime.MinValue;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                done += n;
                if (total > 0 && (DateTime.Now - lastReport).TotalMilliseconds > 150)
                {
                    progress(done * 100.0 / total);
                    lastReport = DateTime.Now;
                }
            }
        }
        File.Move(tmp, dest, overwrite: true);
        progress(100);
    }
}
