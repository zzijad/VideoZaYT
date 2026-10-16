using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VideoZaYT;

public enum JobState { Waiting, Running, Done, Error, Canceled }

public sealed class VideoJob : INotifyPropertyChanged
{
    public VideoJob(string source, bool isUrl)
    {
        Source = source;
        IsUrl = isUrl;
        Display = isUrl ? source : Path.GetFileName(source);
    }

    public string Source { get; }
    public bool IsUrl { get; }
    public string Display { get; }
    public string Kind => IsUrl ? "Link" : "Fajl";

    private JobState _state = JobState.Waiting;
    public JobState State { get => _state; set { _state = value; OnChanged(); OnChanged(nameof(IsIndeterminate)); } }

    private string _status = "Čeka";
    public string Status { get => _status; set { _status = value; OnChanged(); } }

    private double _progress;
    public double Progress { get => _progress; set { _progress = value; OnChanged(); } }

    private bool _indeterminate;
    public bool IsIndeterminate { get => _indeterminate && State == JobState.Running; set { _indeterminate = value; OnChanged(); } }

    /// <summary>Gotovi fajlovi (Instagram objava može imati više videa).</summary>
    public List<string> Outputs { get; } = [];

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
