using System.Diagnostics;
using System.Text;

namespace VideoZaYT;

public static class ProcessRunner
{
    /// <summary>
    /// Pokreće spoljni program bez prozora, čita izlaz red po red i vraća exit kod.
    /// Na otkazivanje gasi proces (i njegove podprocese) i baca OperationCanceledException.
    /// </summary>
    public static async Task<int> RunAsync(
        string exe,
        IEnumerable<string> args,
        Action<string>? onOutput,
        Action<string>? onError,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(exe) is { Length: > 0 } d ? d : Environment.CurrentDirectory,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        psi.Environment["PYTHONUTF8"] = "1";

        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.OutputDataReceived += (_, e) => { if (e.Data != null) onOutput?.Invoke(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) onError?.Invoke(e.Data); };

        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        try
        {
            await p.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { }
            throw;
        }

        // sačekaj da se isprazne baferi izlaza
        p.WaitForExit();
        return p.ExitCode;
    }
}
