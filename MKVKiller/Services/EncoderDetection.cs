using System.Diagnostics;

namespace MKVKiller.Services;

public class EncoderCapabilities
{
    public bool Cpu { get; set; } = true;
    public bool Qsv { get; set; }
    public bool Nvenc { get; set; }
}

public static class EncoderDetection
{
    public static EncoderCapabilities Current { get; private set; } = new();

    public static async Task<EncoderCapabilities> DetectAsync()
    {
        var caps = new EncoderCapabilities();
        if (!FFmpegService.IsAvailable()) { Current = caps; return caps; }

        // List available encoders
        string encList = await RunFFmpegCaptureAsync("-hide_banner -encoders");
        bool hasQsvEncoder = encList.Contains("h264_qsv");
        bool hasNvencEncoder = encList.Contains("h264_nvenc");

        if (hasQsvEncoder) caps.Qsv = await TestEncoderAsync("h264_qsv");
        if (hasNvencEncoder) caps.Nvenc = await TestEncoderAsync("h264_nvenc");

        Current = caps;
        return caps;
    }

    private static async Task<string> RunFFmpegCaptureAsync(string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = FFmpegService.FFmpegExe,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in args.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            psi.ArgumentList.Add(a);
        using var proc = Process.Start(psi);
        if (proc == null) return "";
        var output = await proc.StandardOutput.ReadToEndAsync();
        var err = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();
        return output + err;
    }

    private static async Task<bool> TestEncoderAsync(string codec)
    {
        var psi = new ProcessStartInfo
        {
            FileName = FFmpegService.FFmpegExe,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in new[]
        {
            "-hide_banner", "-loglevel", "error",
            "-f", "lavfi", "-i", "color=black:s=320x240:d=0.1",
            "-c:v", codec, "-frames:v", "1", "-f", "null", "-"
        }) psi.ArgumentList.Add(a);

        try
        {
            using var proc = Process.Start(psi);
            if (proc == null) return false;
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            try { await proc.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException) { try { proc.Kill(); } catch { } return false; }
            return proc.ExitCode == 0;
        }
        catch { return false; }
    }
}
