using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MKVKiller.Models;

namespace MKVKiller.Services;

public static class FFmpegService
{
    // Locate ffmpeg.exe bundled next to our executable
    private static string BaseDir => AppContext.BaseDirectory;
    public static string FFmpegExe => Path.Combine(BaseDir, "ffmpeg", "ffmpeg.exe");
    public static string FFprobeExe => Path.Combine(BaseDir, "ffmpeg", "ffprobe.exe");

    public static bool IsAvailable() => File.Exists(FFmpegExe) && File.Exists(FFprobeExe);

    public static async Task<ProbeResult?> ProbeAsync(string path, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = FFprobeExe,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in new[] { "-v", "error", "-print_format", "json", "-show_format", "-show_streams", path })
            psi.ArgumentList.Add(a);
        using var proc = Process.Start(psi);
        if (proc == null) return null;
        var output = await proc.StandardOutput.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        if (proc.ExitCode != 0) return null;
        try
        {
            return JsonSerializer.Deserialize<ProbeResult>(output);
        }
        catch { return null; }
    }

    public static List<string> BuildEncodeArgs(ConversionJob j, double? startTime = null, double? duration = null, string? overrideOutput = null)
    {
        var o = j.Options;
        var args = new List<string> { "-hide_banner", "-y" };

        if (o.Encoder == "nvenc") args.AddRange(new[] { "-hwaccel", "cuda" });
        else if (o.Encoder == "qsv") args.AddRange(new[] { "-hwaccel", "qsv", "-hwaccel_output_format", "qsv" });

        if (startTime.HasValue) args.AddRange(new[] { "-ss", startTime.Value.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) });
        args.AddRange(new[] { "-i", j.InputPath });
        if (duration.HasValue) args.AddRange(new[] { "-t", duration.Value.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) });

        foreach (var idx in j.SelectedStreams)
            args.AddRange(new[] { "-map", $"0:{idx}?" });

        var vf = new List<string>();
        if (o.MaxHeight.HasValue)
        {
            var h = o.MaxHeight.Value;
            if (o.Encoder == "qsv") vf.Add($"scale_qsv=-1:'min({h},ih)'");
            else if (o.Encoder == "nvenc") vf.Add($"scale_cuda=-2:'min({h},ih)'");
            else vf.Add($"scale=-2:'min({h},ih)'");
        }
        if (o.SubtitleMode == "burn" && o.BurnSubIndex.HasValue)
        {
            if (o.Encoder != "cpu") vf.Clear();
            if (o.MaxHeight.HasValue && vf.Count == 0)
                vf.Add($"scale=-2:'min({o.MaxHeight.Value},ih)'");
            var escaped = j.InputPath.Replace("\\", "/").Replace(":", "\\:").Replace("'", "\\'");
            vf.Add($"subtitles='{escaped}':si={o.BurnSubIndex.Value}");
        }
        if (vf.Count > 0) args.AddRange(new[] { "-vf", string.Join(",", vf) });

        // Video codec
        if (o.Encoder == "qsv")
        {
            args.AddRange(new[] { "-c:v", "h264_qsv", "-preset", MapQsvPreset(o.Preset),
                "-global_quality", o.HwQuality.ToString(), "-look_ahead", "1", "-profile:v", "high" });
        }
        else if (o.Encoder == "nvenc")
        {
            args.AddRange(new[] { "-c:v", "h264_nvenc", "-preset", MapNvencPreset(o.Preset),
                "-rc", "vbr", "-cq", o.HwQuality.ToString(), "-b:v", "0",
                "-profile:v", "high", "-rc-lookahead", "20", "-spatial_aq", "1" });
        }
        else
        {
            args.AddRange(new[] { "-c:v", "libx264", "-preset", o.Preset,
                "-crf", o.Crf.ToString(), "-profile:v", "high", "-level", "4.1" });
        }
        args.AddRange(new[] { "-pix_fmt", "yuv420p" });

        // Audio
        if (o.AudioMode == "copy") args.AddRange(new[] { "-c:a", "copy" });
        else args.AddRange(new[] { "-c:a", "aac", "-b:a", o.AudioBitrate });

        // Subs
        if (o.SubtitleMode == "soft" && !startTime.HasValue) args.AddRange(new[] { "-c:s", "mov_text" });
        else args.Add("-sn");

        args.AddRange(new[] { "-movflags", "+faststart", "-max_muxing_queue_size", "9999" });
        if (startTime.HasValue) args.AddRange(new[] { "-reset_timestamps", "1" });
        args.Add(overrideOutput ?? j.OutputPath);
        return args;
    }

    public static string MapQsvPreset(string p) => new[] {"veryfast","faster","fast","medium","slow","slower","veryslow"}.Contains(p) ? p : "slow";
    public static string MapNvencPreset(string p) => p switch
    {
        "ultrafast" or "superfast" => "p1",
        "veryfast" => "p2", "faster" => "p3", "fast" => "p4",
        "medium" => "p5", "slow" => "p6", "slower" or "veryslow" => "p7",
        _ => "p6"
    };

    // Regex patterns for parsing ffmpeg stderr progress
    private static readonly Regex TimeRegex = new(@"time=(\d+):(\d+):(\d+\.\d+)", RegexOptions.Compiled);
    private static readonly Regex FpsRegex = new(@"fps=\s*([\d.]+)", RegexOptions.Compiled);
    private static readonly Regex SpeedRegex = new(@"speed=\s*([\d.]+)x", RegexOptions.Compiled);

    public static void ParseProgress(string line, double totalDuration, out double? sec, out double? fps, out double? speed)
    {
        sec = null; fps = null; speed = null;
        var tm = TimeRegex.Match(line);
        if (tm.Success)
        {
            sec = int.Parse(tm.Groups[1].Value) * 3600 + int.Parse(tm.Groups[2].Value) * 60
                  + double.Parse(tm.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
        }
        var fm = FpsRegex.Match(line);
        if (fm.Success) fps = double.Parse(fm.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        var sm = SpeedRegex.Match(line);
        if (sm.Success) speed = double.Parse(sm.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    // Track active ffmpeg processes for cleanup on app exit
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, Process> ActiveProcesses = new();

    public static void KillAll()
    {
        foreach (var kvp in ActiveProcesses)
        {
            try { if (!kvp.Value.HasExited) kvp.Value.Kill(entireProcessTree: true); } catch { }
        }
        ActiveProcesses.Clear();
    }

    public static async Task<int> RunAsync(IEnumerable<string> args, Action<string> onStderrLine,
        Process? captureProc = null, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = FFmpegExe,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = new Process { StartInfo = psi };
        var sb = new StringBuilder();
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                onStderrLine(e.Data + "\n");
            }
        };
        proc.Start();
        ActiveProcesses[proc.Id] = proc;
        proc.BeginErrorReadLine();

        using var reg = ct.Register(() => { try { if (!proc.HasExited) proc.Kill(); } catch { } });
        try
        {
            await proc.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException) { }
        ActiveProcesses.TryRemove(proc.Id, out _);
        return proc.HasExited ? proc.ExitCode : -1;
    }
}
