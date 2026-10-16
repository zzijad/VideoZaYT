using System.Globalization;
using System.Text.RegularExpressions;

namespace VideoZaYT;

/// <summary>
/// Pretvara bilo koji video u 1920×1080 (16:9): originalni snimak u sredini,
/// pozadina zamućena (ili crna/bela). Takav video YouTube ne svrstava u Shorts.
/// </summary>
public static partial class Converter
{
    public static async Task<double> GetDurationAsync(string file, CancellationToken ct)
    {
        string? text = null;
        await ProcessRunner.RunAsync(Tools.Ffprobe,
            ["-v", "error", "-show_entries", "format=duration", "-of", "default=nw=1:nk=1", file],
            line => text ??= line.Trim(), null, ct);
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
    }

    private static string BuildFilter(BackgroundMode bg)
    {
        const string fit = "scale=1920:1080:force_original_aspect_ratio=decrease:force_divisible_by=2:flags=lanczos,setsar=1";
        return bg switch
        {
            BackgroundMode.Black =>
                $"[0:v]{fit},pad=1920:1080:(ow-iw)/2:(oh-ih)/2:color=black,format=yuv420p[v]",
            BackgroundMode.White =>
                $"[0:v]{fit},pad=1920:1080:(ow-iw)/2:(oh-ih)/2:color=white,format=yuv420p[v]",
            _ =>
                "[0:v]split=2[a][b];" +
                "[a]scale=480:270:force_original_aspect_ratio=increase,crop=480:270,boxblur=20:3," +
                "scale=1920:1080,eq=brightness=-0.08,setsar=1[bg];" +
                $"[b]{fit}[fg];" +
                "[bg][fg]overlay=(W-w)/2:(H-h)/2,format=yuv420p[v]",
        };
    }

    private static List<string> BuildArgs(string input, string output, Settings s, bool nvidia)
    {
        var a = new List<string>
        {
            "-hide_banner", "-y", "-nostdin",
            "-i", input,
            "-filter_complex", BuildFilter(s.Background),
            "-map", "[v]", "-map", "0:a:0?",
        };

        if (nvidia)
            a.AddRange(["-c:v", "h264_nvenc", "-preset", "p5", "-rc", "vbr", "-cq", (s.Crf + 1).ToString(), "-b:v", "0"]);
        else
            a.AddRange(["-c:v", "libx264", "-preset", "medium", "-crf", s.Crf.ToString()]);

        a.AddRange([
            "-profile:v", "high",
            "-c:a", "aac", "-b:a", "192k", "-ar", "48000",
            "-movflags", "+faststart",
            "-progress", "pipe:1", "-nostats",
            output,
        ]);
        return a;
    }

    /// <summary>Konvertuje jedan fajl. Ako NVIDIA ne uspe, automatski pokušava ponovo na procesoru.</summary>
    public static async Task ConvertAsync(string input, string output, Settings s,
        Action<double> progress, Action<string> status, CancellationToken ct)
    {
        var duration = await GetDurationAsync(input, ct);
        if (duration <= 0)
            throw new InvalidOperationException("Fajl nije video ili je oštećen (ffprobe ne može da ga pročita).");

        var useNvidia = s.UseNvidia;
        while (true)
        {
            status(useNvidia ? "Obrađujem (NVIDIA)…" : "Obrađujem…");
            var errors = new List<string>();
            var code = await ProcessRunner.RunAsync(Tools.Ffmpeg, BuildArgs(input, output, s, useNvidia),
                line =>
                {
                    // out_time_us=12345678 (u mikrosekundama)
                    if ((line.StartsWith("out_time_us=") || line.StartsWith("out_time_ms=")) &&
                        long.TryParse(line[(line.IndexOf('=') + 1)..], out var us))
                        progress(Math.Clamp(us / 1_000_000.0 / duration * 100.0, 0, 100));
                },
                line => { lock (errors) { errors.Add(line); if (errors.Count > 30) errors.RemoveAt(0); } },
                ct);

            if (code == 0) { progress(100); return; }

            TryDelete(output);
            if (useNvidia) { useNvidia = false; continue; } // nema NVIDIA kartice/drajvera → procesor

            string msg;
            lock (errors) msg = string.Join(Environment.NewLine, errors.TakeLast(6));
            throw new InvalidOperationException("ffmpeg greška:" + Environment.NewLine + msg);
        }
    }

    // ------------------------------------------------------------------ Instagram / linkovi

    [GeneratedRegex(@"\[download\]\s+(\d+(?:\.\d+)?)%")]
    private static partial Regex DlProgress();

    /// <summary>Preuzima video(e) sa linka preko yt-dlp. Vraća putanje preuzetih fajlova.</summary>
    public static async Task<List<string>> DownloadAsync(string url, string tempDir, Settings s,
        Action<double> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(tempDir);
        var args = new List<string>
        {
            "--encoding", "utf-8",
            "--newline", "--progress", "--no-mtime", "--no-part",
            "-f", "bv*+ba/b",
            "--merge-output-format", "mp4",
            "--ffmpeg-location", Tools.ToolsDir,
            "-P", tempDir,
            "-o", "%(uploader,channel,id)s_%(id)s.%(ext)s",
            "--windows-filenames", "--trim-filenames", "100",
            "--print", "after_move:filepath",
        };

        switch (s.Cookies)
        {
            case CookieSource.Firefox: args.AddRange(["--cookies-from-browser", "firefox"]); break;
            case CookieSource.Chrome: args.AddRange(["--cookies-from-browser", "chrome"]); break;
            case CookieSource.Edge: args.AddRange(["--cookies-from-browser", "edge"]); break;
            case CookieSource.Brave: args.AddRange(["--cookies-from-browser", "brave"]); break;
            case CookieSource.CookiesFile when File.Exists(s.CookiesFile):
                args.AddRange(["--cookies", s.CookiesFile]); break;
        }
        args.Add("--");
        args.Add(url);

        var files = new List<string>();
        var errors = new List<string>();
        var code = await ProcessRunner.RunAsync(Tools.YtDlp, args,
            line =>
            {
                var m = DlProgress().Match(line);
                if (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var p))
                    progress(p);
                else
                {
                    var t = line.Trim();
                    if (t.Length > 3 && !t.StartsWith('[') && File.Exists(t))
                        lock (files) files.Add(t);
                }
            },
            line => { if (line.Contains("ERROR", StringComparison.OrdinalIgnoreCase)) lock (errors) errors.Add(line); },
            ct);

        // rezervno: ako --print nije vratio putanje, uzmi sve video fajlove iz privremenog foldera
        if (files.Count == 0)
            files.AddRange(Directory.GetFiles(tempDir)
                .Where(f => f.EndsWith(".mp4", true, null) || f.EndsWith(".mkv", true, null) ||
                            f.EndsWith(".webm", true, null) || f.EndsWith(".mov", true, null)));

        if (files.Count == 0)
        {
            string err;
            lock (errors) err = string.Join(Environment.NewLine, errors.TakeLast(3));
            if (string.IsNullOrWhiteSpace(err)) err = $"yt-dlp nije preuzeo nijedan video (kod {code}).";

            if (err.Contains("login", StringComparison.OrdinalIgnoreCase) ||
                err.Contains("cookies", StringComparison.OrdinalIgnoreCase) ||
                err.Contains("rate-limit", StringComparison.OrdinalIgnoreCase) ||
                err.Contains("empty media response", StringComparison.OrdinalIgnoreCase))
                err += Environment.NewLine + Environment.NewLine +
                       "Savet: Instagram traži prijavu. U podešavanjima izaberi „Instagram prijava“ → Firefox " +
                       "(u kom si ulogovan na Instagram) ili cookies.txt fajl, pa pokušaj ponovo.";
            if (err.Contains("cookie", StringComparison.OrdinalIgnoreCase) &&
                (s.Cookies is CookieSource.Chrome or CookieSource.Edge or CookieSource.Brave))
                err += Environment.NewLine + "Chrome/Edge/Brave često ne daju kolačiće dok su otvoreni — zatvori browser ili koristi Firefox.";

            throw new InvalidOperationException(err);
        }
        return files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    // ------------------------------------------------------------------ pomoćno

    public static string UniquePath(string folder, string baseName, string ext = ".mp4")
    {
        foreach (var c in Path.GetInvalidFileNameChars()) baseName = baseName.Replace(c, '_');
        var p = Path.Combine(folder, baseName + ext);
        for (var i = 2; File.Exists(p); i++)
            p = Path.Combine(folder, $"{baseName} ({i}){ext}");
        return p;
    }

    public static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
