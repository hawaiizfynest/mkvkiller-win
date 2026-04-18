using CommunityToolkit.Mvvm.ComponentModel;

namespace MKVKiller.Models;

public enum JobStatus { Queued, Running, Done, Error, Cancelled, Interrupted }

public partial class ConversionJob : ObservableObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public string InputPath { get; set; } = "";
    public string OutputPath { get; set; } = "";
    public long InputSize { get; set; }

    [ObservableProperty] private long outputSize;
    [ObservableProperty] private JobStatus status = JobStatus.Queued;
    [ObservableProperty] private double progress;
    [ObservableProperty] private double fps;
    [ObservableProperty] private double speed;
    [ObservableProperty] private int etaSeconds;
    [ObservableProperty] private string? error;
    [ObservableProperty] private int segmentsTotal = 1;
    [ObservableProperty] private int segmentsDone;
    [ObservableProperty] private bool replacedOriginal;
    [ObservableProperty] private string? finalPath;

    public EncodeOptions Options { get; set; } = new();
    public List<int> SelectedStreams { get; set; } = new();
    public bool ReplaceOriginal { get; set; }
    public bool Resumable { get; set; }
    public double Duration { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string Log { get; set; } = "";

    public string FileName => System.IO.Path.GetFileName(InputPath);
    public string StatusText => Status.ToString().ToLowerInvariant();

    public void AppendLog(string text)
    {
        Log += text;
        if (Log.Length > 200_000) Log = Log[^200_000..];
    }
}

public class EncodeOptions
{
    public int Crf { get; set; } = 20;
    public int HwQuality { get; set; } = 23;
    public string Preset { get; set; } = "slow";
    public string Encoder { get; set; } = "cpu"; // cpu | qsv | nvenc
    public string AudioMode { get; set; } = "auto"; // auto | copy | aac
    public string AudioBitrate { get; set; } = "192k";
    public string SubtitleMode { get; set; } = "soft"; // soft | burn | none
    public int? BurnSubIndex { get; set; }
    public int? MaxHeight { get; set; }
}
