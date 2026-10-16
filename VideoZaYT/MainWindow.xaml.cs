using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;

namespace VideoZaYT;

public partial class MainWindow : Window
{
    private static readonly string[] VideoExtensions =
    [
        ".mp4", ".mov", ".m4v", ".avi", ".mkv", ".webm", ".3gp", ".wmv",
        ".mts", ".m2ts", ".ts", ".flv", ".mpg", ".mpeg", ".hevc",
    ];

    private readonly Settings _settings = Settings.Load();
    private readonly ObservableCollection<VideoJob> _jobs = [];
    private CancellationTokenSource? _cts;
    private bool _running;
    private bool _loading = true;

    public MainWindow()
    {
        InitializeComponent();
        LstJobs.ItemsSource = _jobs;

        CmbBackground.SelectedIndex = (int)_settings.Background;
        CmbQuality.SelectedIndex = Math.Clamp(_settings.Quality, 0, 2);
        CmbCookies.SelectedIndex = (int)_settings.Cookies;
        ChkNvidia.IsChecked = _settings.UseNvidia;
        ChkNextTo.IsChecked = _settings.SaveNextToOriginal;
        TxtOutput.Text = _settings.OutputFolder;
        _loading = false;
    }

    // ================================================================ dodavanje

    private void BtnFiles_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Izaberi video",
            Multiselect = true,
            Filter = "Video fajlovi|" + string.Join(";", VideoExtensions.Select(x => "*" + x)) + "|Svi fajlovi|*.*",
        };
        if (dlg.ShowDialog(this) == true)
            foreach (var f in dlg.FileNames) AddFile(f);
    }

    private void BtnUrl_Click(object sender, RoutedEventArgs e) => AddUrlFromBox();

    private void TxtUrl_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { AddUrlFromBox(); e.Handled = true; }
    }

    private void TxtUrl_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) =>
        UrlPlaceholder.Visibility = TxtUrl.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void AddUrlFromBox()
    {
        var added = false;
        foreach (var part in TxtUrl.Text.Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries))
            added |= AddUrl(part);

        if (added) TxtUrl.Clear();
        else if (TxtUrl.Text.Trim().Length > 0)
            MessageBox.Show(this, "To nije ispravan link. Nalepi ceo link koji počinje sa https://",
                "VideoZaYT", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private bool AddUrl(string text)
    {
        text = text.Trim();
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return false;

        _jobs.Add(new VideoJob(uri.ToString(), isUrl: true));
        StartQueue();
        return true;
    }

    private void AddFile(string path)
    {
        if (!File.Exists(path)) return;
        _jobs.Add(new VideoJob(path, isUrl: false));
        StartQueue();
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Text)
            ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            foreach (var p in paths)
            {
                if (Directory.Exists(p))
                {
                    foreach (var f in Directory.EnumerateFiles(p)
                                 .Where(f => VideoExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                                 .Order())
                        AddFile(f);
                }
                else AddFile(p);
            }
        }
        else if (e.Data.GetData(DataFormats.Text) is string text)
        {
            foreach (var part in text.Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries))
                AddUrl(part);
        }
    }

    // ================================================================ red obrade

    private async void StartQueue()
    {
        if (_running) return;
        _running = true;
        BtnCancel.IsEnabled = true;
        try
        {
            VideoJob? job;
            while ((job = _jobs.FirstOrDefault(j => j.State == JobState.Waiting)) != null)
            {
                _cts = new CancellationTokenSource();
                LstJobs.ScrollIntoView(job);
                await ProcessJobAsync(job, _cts.Token);
                _cts.Dispose();
                _cts = null;
            }
        }
        finally
        {
            _running = false;
            BtnCancel.IsEnabled = false;
            var done = _jobs.Count(j => j.State == JobState.Done);
            var failed = _jobs.Count(j => j.State == JobState.Error);
            TxtStatus.Text = failed > 0
                ? $"Završeno: {done} uspešno, {failed} sa greškom (pređi mišem preko statusa za detalje)."
                : $"Završeno: {done}. Dvoklik na video otvara folder sa njim.";
        }
    }

    private async Task ProcessJobAsync(VideoJob job, CancellationToken ct)
    {
        job.State = JobState.Running;
        job.Progress = 0;
        string? tempDir = null;
        var currentOutput = "";

        void Status(string s) => Dispatcher.Invoke(() => { job.Status = s; TxtStatus.Text = s; });
        void Progress(double p) => Dispatcher.InvokeAsync(() => job.Progress = p);

        try
        {
            // --- alati
            if (!Tools.HasFfmpeg)
            {
                job.IsIndeterminate = false;
                await Tools.EnsureFfmpegAsync(Status, Progress, ct);
                job.Progress = 0;
            }

            List<string> inputs;
            string outFolder;

            if (job.IsUrl)
            {
                job.IsIndeterminate = true;
                await Tools.EnsureYtDlpAsync(Status, Progress, ct);
                job.IsIndeterminate = false;

                Status("Preuzimam video…");
                job.Progress = 0;
                tempDir = Path.Combine(Path.GetTempPath(), "VideoZaYT", Guid.NewGuid().ToString("N"));
                inputs = await Converter.DownloadAsync(job.Source, tempDir, _settings, Progress, ct);
                outFolder = _settings.OutputFolder;
            }
            else
            {
                inputs = [job.Source];
                outFolder = _settings.SaveNextToOriginal
                    ? Path.GetDirectoryName(job.Source)!
                    : _settings.OutputFolder;
                if (!CanWrite(outFolder)) outFolder = _settings.OutputFolder;
            }

            Directory.CreateDirectory(outFolder);

            for (var i = 0; i < inputs.Count; i++)
            {
                var input = inputs[i];
                var baseName = Path.GetFileNameWithoutExtension(input) + "_YT";
                currentOutput = Converter.UniquePath(outFolder, baseName);
                var index = i;
                var count = inputs.Count;
                var prefix = count > 1 ? $"[{i + 1}/{count}] " : "";

                await Converter.ConvertAsync(input, currentOutput, _settings,
                    p => Progress((index + p / 100.0) / count * 100.0),
                    s => Status(prefix + s),
                    ct);

                job.Outputs.Add(currentOutput);
                currentOutput = "";
            }

            job.State = JobState.Done;
            job.Progress = 100;
            job.Status = job.Outputs.Count == 1
                ? "Gotovo → " + Path.GetFileName(job.Outputs[0])
                : $"Gotovo → {job.Outputs.Count} videa u {outFolder}";
        }
        catch (OperationCanceledException)
        {
            if (currentOutput.Length > 0) Converter.TryDelete(currentOutput);
            job.State = JobState.Canceled;
            job.Status = "Otkazano";
            job.Progress = 0;
        }
        catch (Exception ex)
        {
            if (currentOutput.Length > 0) Converter.TryDelete(currentOutput);
            job.State = JobState.Error;
            job.Status = "Greška: " + ex.Message;
        }
        finally
        {
            job.IsIndeterminate = false;
            if (tempDir != null)
                try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    private static bool CanWrite(string folder)
    {
        try
        {
            var test = Path.Combine(folder, $".vzyt_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(test, "");
            File.Delete(test);
            return true;
        }
        catch { return false; }
    }

    // ================================================================ dugmad

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        // otkaži tekući i sve koji čekaju
        foreach (var j in _jobs.Where(j => j.State == JobState.Waiting))
        {
            j.State = JobState.Canceled;
            j.Status = "Otkazano";
        }
        _cts?.Cancel();
    }

    private void BtnClear_Click(object sender, RoutedEventArgs e)
    {
        foreach (var j in _jobs.Where(j => j.State is JobState.Done or JobState.Error or JobState.Canceled).ToList())
            _jobs.Remove(j);
        TxtStatus.Text = "";
    }

    private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (LstJobs.SelectedItem is VideoJob { Outputs.Count: > 0 } job)
        {
            ShowInExplorer(job.Outputs[^1]);
            return;
        }
        Directory.CreateDirectory(_settings.OutputFolder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_settings.OutputFolder}\"") { UseShellExecute = true });
    }

    private void LstJobs_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (LstJobs.SelectedItem is VideoJob { Outputs.Count: > 0 } job)
            ShowInExplorer(job.Outputs[^1]);
        else if (LstJobs.SelectedItem is VideoJob { State: JobState.Error } bad)
            MessageBox.Show(this, bad.Status, "VideoZaYT", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static void ShowInExplorer(string file)
    {
        if (File.Exists(file))
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = true });
    }

    private void BtnOutput_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Folder za gotove videe",
            InitialDirectory = Directory.Exists(_settings.OutputFolder) ? _settings.OutputFolder : "",
        };
        if (dlg.ShowDialog(this) == true)
        {
            _settings.OutputFolder = dlg.FolderName;
            TxtOutput.Text = dlg.FolderName;
            _settings.Save();
        }
    }

    // ================================================================ podešavanja

    private void Combo_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => SaveSettings();

    private void Setting_Changed(object sender, RoutedEventArgs e) => SaveSettings();

    private void SaveSettings()
    {
        if (_loading) return;
        _settings.Background = (BackgroundMode)Math.Max(0, CmbBackground.SelectedIndex);
        _settings.Quality = Math.Max(0, CmbQuality.SelectedIndex);
        _settings.UseNvidia = ChkNvidia.IsChecked == true;
        _settings.SaveNextToOriginal = ChkNextTo.IsChecked == true;
        _settings.Save();
    }

    private void Cookies_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_loading) return;
        var sel = (CookieSource)Math.Max(0, CmbCookies.SelectedIndex);

        if (sel == CookieSource.CookiesFile)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Izaberi cookies.txt (Netscape format)",
                Filter = "cookies.txt|*.txt|Svi fajlovi|*.*",
            };
            if (dlg.ShowDialog(this) == true)
                _settings.CookiesFile = dlg.FileName;
            else
            {
                _loading = true;
                CmbCookies.SelectedIndex = (int)_settings.Cookies;
                _loading = false;
                return;
            }
        }

        _settings.Cookies = sel;
        _settings.Save();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_running &&
            MessageBox.Show(this, "Obrada je u toku. Da li sigurno želiš da zatvoriš program?",
                "VideoZaYT", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            e.Cancel = true;
            return;
        }
        foreach (var j in _jobs.Where(j => j.State == JobState.Waiting)) j.State = JobState.Canceled;
        _cts?.Cancel();
        _settings.Save();
    }
}
