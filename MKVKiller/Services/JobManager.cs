using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using MKVKiller.Models;

namespace MKVKiller.Services;

public class JobManager
{
    public static JobManager Instance { get; } = new();

    public ObservableCollection<ConversionJob> Jobs { get; } = new();

    private readonly SemaphoreSlim _slot;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CancellationTokenSource> _cts = new();

    public int SegmentLengthSec { get; set; } = 600;

    public JobManager()
    {
        _slot = new SemaphoreSlim(Preferences.Current.MaxConcurrent, Math.Max(4, Preferences.Current.MaxConcurrent));
    }

    public void LoadFromDatabase()
    {
        foreach (var j in LogDatabase.LoadAll())
        {
            // Anything that was "running" when the app last closed is now "interrupted" if resumable, else "error"
            if (j.Status == JobStatus.Running)
            {
                j.Status = j.Resumable ? JobStatus.Interrupted : JobStatus.Error;
                if (j.Status == JobStatus.Error) j.Error = "Application closed during encode (not resumable)";
                LogDatabase.Upsert(j);
            }
            Jobs.Add(j);
        }
    }

    public void ResumeInterrupted()
    {
        var interrupted = Jobs.Where(j => j.Status == JobStatus.Interrupted).ToList();
        foreach (var j in interrupted)
        {
            j.Status = JobStatus.Queued;
            LogDatabase.Upsert(j);
            _ = RunAsync(j);
        }
    }

    public async Task SubmitAsync(ConversionJob j)
    {
        j.Status = JobStatus.Queued;
        Application.Current.Dispatcher.Invoke(() => Jobs.Insert(0, j));
        LogDatabase.Upsert(j);
        _ = RunAsync(j);
        await Task.CompletedTask;
    }

    private async Task RunAsync(ConversionJob j)
    {
        await _slot.WaitAsync();
        var cts = new CancellationTokenSource();
        _cts[j.Id] = cts;
        try
        {
            j.Status = JobStatus.Running;
            j.StartedAt ??= DateTime.Now;
            LogDatabase.Upsert(j);

            if (j.Resumable && j.Duration > 0)
                await RunSegmentedAsync(j, cts.Token);
            else
                await RunSingleAsync(j, cts.Token);

            if (j.Status == JobStatus.Done && j.ReplaceOriginal)
                await MaybeReplaceOriginalAsync(j);
        }
        catch (OperationCanceledException)
        {
            j.Status = JobStatus.Cancelled;
            try { File.Delete(j.OutputPath); } catch { }
        }
        catch (Exception ex)
        {
            j.Status = JobStatus.Error;
            j.Error = ex.Message;
            j.AppendLog("[runner error] " + ex.Message + "\n");
        }
        finally
        {
            j.FinishedAt = DateTime.Now;
            LogDatabase.Upsert(j);
            _cts.TryRemove(j.Id, out _);
            _slot.Release();
        }
    }

    private async Task RunSingleAsync(ConversionJob j, CancellationToken ct)
    {
        j.SegmentsTotal = 100;  // progress %
        var args = FFmpegService.BuildEncodeArgs(j);
        j.AppendLog("ffmpeg " + string.Join(" ", args) + "\n");

        int code = await FFmpegService.RunAsync(args, line =>
        {
            j.AppendLog(line);
            FFmpegService.ParseProgress(line, j.Duration, out var sec, out var fps, out var spd);
            if (sec.HasValue && j.Duration > 0)
            {
                var pct = Math.Min(100, sec.Value / j.Duration * 100);
                j.Progress = pct;
                j.SegmentsDone = (int)pct;
            }
            if (fps.HasValue) j.Fps = fps.Value;
            if (spd.HasValue)
            {
                j.Speed = spd.Value;
                if (j.Duration > 0 && j.Speed > 0)
                    j.EtaSeconds = (int)(j.Duration * (1 - j.Progress / 100) / j.Speed);
            }
        }, null, ct);

        if (ct.IsCancellationRequested) throw new OperationCanceledException();
        if (code != 0)
        {
            j.Status = JobStatus.Error;
            j.Error = $"ffmpeg exited {code}";
            return;
        }
        j.Status = JobStatus.Done;
        j.Progress = 100;
        if (File.Exists(j.OutputPath)) j.OutputSize = new FileInfo(j.OutputPath).Length;
    }

    private async Task RunSegmentedAsync(ConversionJob j, CancellationToken ct)
    {
        string scratchDir = Path.Combine(App.AppDataPath, "scratch", j.Id);
        Directory.CreateDirectory(scratchDir);
        int segCount = (int)Math.Ceiling(j.Duration / SegmentLengthSec);
        j.SegmentsTotal = segCount;

        bool SegDone(int i)
        {
            var p = Path.Combine(scratchDir, $"seg_{i:D4}.mp4");
            return File.Exists(p) && new FileInfo(p).Length > 1024;
        }

        int completed = Enumerable.Range(0, segCount).Count(SegDone);
        j.SegmentsDone = completed;
        j.Progress = (double)completed / segCount * 100;
        j.AppendLog($"[resume] {completed}/{segCount} segments already complete\n");
        LogDatabase.Upsert(j);

        for (int i = 0; i < segCount; i++)
        {
            if (ct.IsCancellationRequested) throw new OperationCanceledException();
            if (SegDone(i)) continue;

            double startTime = i * SegmentLengthSec;
            double segDur = Math.Min(SegmentLengthSec, j.Duration - startTime);
            string segOut = Path.Combine(scratchDir, $"seg_{i:D4}.mp4");

            var segJob = new ConversionJob
            {
                InputPath = j.InputPath,
                OutputPath = segOut,
                Options = j.Options,
                SelectedStreams = j.SelectedStreams.Where(idx => true).ToList(),
                Duration = segDur
            };
            // Segmented mode must drop subs for clean concat
            if (segJob.Options.SubtitleMode == "soft") segJob.Options.SubtitleMode = "none";

            var segArgs = FFmpegService.BuildEncodeArgs(segJob, startTime, segDur, segOut);
            j.AppendLog($"\n[seg {i+1}/{segCount}] ffmpeg " + string.Join(" ", segArgs) + "\n");

            int code = await FFmpegService.RunAsync(segArgs, line =>
            {
                j.AppendLog(line);
                FFmpegService.ParseProgress(line, segDur, out var _, out var fps, out var spd);
                if (fps.HasValue) j.Fps = fps.Value;
                if (spd.HasValue) j.Speed = spd.Value;
                if (j.Speed > 0)
                {
                    double remaining = j.Duration - (j.SegmentsDone * SegmentLengthSec);
                    j.EtaSeconds = (int)(remaining / j.Speed);
                }
            }, null, ct);

            if (ct.IsCancellationRequested) throw new OperationCanceledException();
            if (code != 0)
            {
                j.Status = JobStatus.Interrupted;
                j.Error = $"segment {i} failed (exit {code}) - resume later";
                return;
            }
            completed++;
            j.SegmentsDone = completed;
            j.Progress = (double)completed / segCount * 100;
            LogDatabase.Upsert(j);
        }

        // Concat
        j.AppendLog($"\n[concat] joining {segCount} segments\n");
        string listFile = Path.Combine(scratchDir, "concat.txt");
        await File.WriteAllLinesAsync(listFile,
            Enumerable.Range(0, segCount).Select(i => $"file '{Path.Combine(scratchDir, $"seg_{i:D4}.mp4").Replace("\\", "/")}'"),
            ct);

        var concatArgs = new[] { "-hide_banner", "-y", "-f", "concat", "-safe", "0", "-i", listFile,
            "-c", "copy", "-movflags", "+faststart", j.OutputPath };
        j.AppendLog("ffmpeg " + string.Join(" ", concatArgs) + "\n");

        int concatCode = await FFmpegService.RunAsync(concatArgs, line => j.AppendLog(line), null, ct);
        if (concatCode != 0)
        {
            j.Status = JobStatus.Error;
            j.Error = "concat failed";
            return;
        }

        j.Status = JobStatus.Done;
        j.Progress = 100;
        if (File.Exists(j.OutputPath)) j.OutputSize = new FileInfo(j.OutputPath).Length;

        // Clean scratch
        try { Directory.Delete(scratchDir, true); } catch { }
    }

    public void Cancel(string id)
    {
        if (_cts.TryGetValue(id, out var cts)) cts.Cancel();
        else
        {
            var j = Jobs.FirstOrDefault(x => x.Id == id);
            if (j != null && (j.Status == JobStatus.Queued || j.Status == JobStatus.Interrupted))
            {
                j.Status = JobStatus.Cancelled;
                j.FinishedAt = DateTime.Now;
                LogDatabase.Upsert(j);
            }
        }
    }

    public void Shutdown()
    {
        // Cancel all running/queued jobs
        foreach (var cts in _cts.Values)
        {
            try { cts.Cancel(); } catch { }
        }

        // Mark running jobs as interrupted (resumable) or error
        foreach (var j in Jobs.Where(j => j.Status == JobStatus.Running || j.Status == JobStatus.Queued))
        {
            j.Status = j.Resumable ? JobStatus.Interrupted : JobStatus.Cancelled;
            j.FinishedAt = DateTime.Now;
            LogDatabase.Upsert(j);
        }

        // Kill any lingering ffmpeg processes
        FFmpegService.KillAll();
    }

    public void Remove(string id)
    {
        var j = Jobs.FirstOrDefault(x => x.Id == id);
        if (j != null && j.Status != JobStatus.Running)
        {
            Jobs.Remove(j);
            LogDatabase.Delete(id);
            try { Directory.Delete(Path.Combine(App.AppDataPath, "scratch", id), true); } catch { }
        }
    }

    public async Task RestartAsync(string id)
    {
        var j = Jobs.FirstOrDefault(x => x.Id == id);
        if (j == null || j.Status == JobStatus.Running || j.Status == JobStatus.Queued) return;
        j.Status = JobStatus.Queued;
        j.Error = null;
        j.StartedAt = null;
        j.FinishedAt = null;
        LogDatabase.Upsert(j);
        _ = RunAsync(j);
        await Task.CompletedTask;
    }

    private async Task MaybeReplaceOriginalAsync(ConversionJob j)
    {
        try
        {
            var srcDir = Path.GetDirectoryName(j.InputPath)!;
            var baseName = Path.GetFileNameWithoutExtension(j.InputPath);
            var target = Path.Combine(srcDir, baseName + ".mp4");
            if (!File.Exists(j.OutputPath)) throw new Exception("output missing");
            if (new FileInfo(j.OutputPath).Length < 1024) throw new Exception("output too small");

            bool sameFile = Path.GetFullPath(target) == Path.GetFullPath(j.InputPath);
            if (!sameFile && File.Exists(target)) throw new Exception($"{target} already exists");

            try
            {
                if (!sameFile) File.Move(j.OutputPath, target);
            }
            catch (IOException)
            {
                // Cross-volume move
                File.Copy(j.OutputPath, target, overwrite: sameFile);
                File.Delete(j.OutputPath);
            }

            if (sameFile)
            {
                File.Copy(j.OutputPath, target, overwrite: true);
                File.Delete(j.OutputPath);
            }
            else
            {
                try { File.Delete(j.InputPath); } catch { }
            }

            j.FinalPath = target;
            j.ReplacedOriginal = true;
        }
        catch (Exception ex)
        {
            j.Error = "Converted OK but replace failed: " + ex.Message;
            j.AppendLog("[replace error] " + ex.Message + "\n");
        }
        await Task.CompletedTask;
    }
}
