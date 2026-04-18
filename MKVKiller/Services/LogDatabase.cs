using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using MKVKiller.Models;

namespace MKVKiller.Services;

public static class LogDatabase
{
    private static string DbPath => Path.Combine(App.AppDataPath, "mkvkiller.db");
    private static readonly object Lock = new();

    public static void Initialize()
    {
        using var cn = Connect();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS jobs (
  id TEXT PRIMARY KEY,
  input TEXT NOT NULL,
  output TEXT NOT NULL,
  input_size INTEGER,
  output_size INTEGER,
  status TEXT NOT NULL,
  error TEXT,
  encoder TEXT,
  resumable INTEGER DEFAULT 0,
  duration REAL,
  segments_total INTEGER DEFAULT 0,
  segments_done INTEGER DEFAULT 0,
  options TEXT NOT NULL,
  selected_streams TEXT,
  replace_original INTEGER DEFAULT 0,
  replaced_original INTEGER DEFAULT 0,
  final_path TEXT,
  created_at INTEGER NOT NULL,
  started_at INTEGER,
  finished_at INTEGER,
  log TEXT
);
CREATE INDEX IF NOT EXISTS idx_status ON jobs(status);
CREATE INDEX IF NOT EXISTS idx_created ON jobs(created_at DESC);";
        cmd.ExecuteNonQuery();
    }

    private static SqliteConnection Connect()
    {
        var cn = new SqliteConnection($"Data Source={DbPath}");
        cn.Open();
        return cn;
    }

    public static void Upsert(ConversionJob j)
    {
        lock (Lock)
        {
            using var cn = Connect();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO jobs (id, input, output, input_size, output_size, status, error, encoder, resumable, duration,
  segments_total, segments_done, options, selected_streams, replace_original, replaced_original, final_path,
  created_at, started_at, finished_at, log)
VALUES (@id, @input, @output, @isize, @osize, @status, @error, @encoder, @resumable, @duration,
  @st, @sd, @options, @sel, @ro, @replaced, @final, @created, @started, @finished, @log)
ON CONFLICT(id) DO UPDATE SET
  output=excluded.output, input_size=excluded.input_size, output_size=excluded.output_size,
  status=excluded.status, error=excluded.error, encoder=excluded.encoder,
  segments_total=excluded.segments_total, segments_done=excluded.segments_done,
  replaced_original=excluded.replaced_original, final_path=excluded.final_path,
  started_at=excluded.started_at, finished_at=excluded.finished_at, log=excluded.log";

            cmd.Parameters.AddWithValue("@id", j.Id);
            cmd.Parameters.AddWithValue("@input", j.InputPath);
            cmd.Parameters.AddWithValue("@output", j.OutputPath);
            cmd.Parameters.AddWithValue("@isize", j.InputSize);
            cmd.Parameters.AddWithValue("@osize", j.OutputSize);
            cmd.Parameters.AddWithValue("@status", j.StatusText);
            cmd.Parameters.AddWithValue("@error", (object?)j.Error ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@encoder", j.Options.Encoder);
            cmd.Parameters.AddWithValue("@resumable", j.Resumable ? 1 : 0);
            cmd.Parameters.AddWithValue("@duration", j.Duration);
            cmd.Parameters.AddWithValue("@st", j.SegmentsTotal);
            cmd.Parameters.AddWithValue("@sd", j.SegmentsDone);
            cmd.Parameters.AddWithValue("@options", JsonSerializer.Serialize(j.Options));
            cmd.Parameters.AddWithValue("@sel", JsonSerializer.Serialize(j.SelectedStreams));
            cmd.Parameters.AddWithValue("@ro", j.ReplaceOriginal ? 1 : 0);
            cmd.Parameters.AddWithValue("@replaced", j.ReplacedOriginal ? 1 : 0);
            cmd.Parameters.AddWithValue("@final", (object?)j.FinalPath ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@created", new DateTimeOffset(j.CreatedAt).ToUnixTimeMilliseconds());
            cmd.Parameters.AddWithValue("@started", j.StartedAt.HasValue ? new DateTimeOffset(j.StartedAt.Value).ToUnixTimeMilliseconds() : DBNull.Value);
            cmd.Parameters.AddWithValue("@finished", j.FinishedAt.HasValue ? new DateTimeOffset(j.FinishedAt.Value).ToUnixTimeMilliseconds() : DBNull.Value);
            cmd.Parameters.AddWithValue("@log", j.Log);
            cmd.ExecuteNonQuery();
        }
    }

    public static List<ConversionJob> LoadAll(int limit = 500)
    {
        var list = new List<ConversionJob>();
        lock (Lock)
        {
            using var cn = Connect();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = $"SELECT * FROM jobs ORDER BY created_at DESC LIMIT {limit}";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                try
                {
                    var j = new ConversionJob
                    {
                        Id = r.GetString(r.GetOrdinal("id")),
                        InputPath = r.GetString(r.GetOrdinal("input")),
                        OutputPath = r.GetString(r.GetOrdinal("output")),
                        InputSize = r.GetInt64(r.GetOrdinal("input_size")),
                        OutputSize = r.IsDBNull(r.GetOrdinal("output_size")) ? 0 : r.GetInt64(r.GetOrdinal("output_size")),
                        Status = ParseStatus(r.GetString(r.GetOrdinal("status"))),
                        Error = r.IsDBNull(r.GetOrdinal("error")) ? null : r.GetString(r.GetOrdinal("error")),
                        Resumable = r.GetInt32(r.GetOrdinal("resumable")) == 1,
                        Duration = r.GetDouble(r.GetOrdinal("duration")),
                        SegmentsTotal = r.GetInt32(r.GetOrdinal("segments_total")),
                        SegmentsDone = r.GetInt32(r.GetOrdinal("segments_done")),
                        ReplaceOriginal = r.GetInt32(r.GetOrdinal("replace_original")) == 1,
                        ReplacedOriginal = r.GetInt32(r.GetOrdinal("replaced_original")) == 1,
                        FinalPath = r.IsDBNull(r.GetOrdinal("final_path")) ? null : r.GetString(r.GetOrdinal("final_path")),
                        CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(r.GetOrdinal("created_at"))).LocalDateTime,
                        StartedAt = r.IsDBNull(r.GetOrdinal("started_at")) ? null : DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(r.GetOrdinal("started_at"))).LocalDateTime,
                        FinishedAt = r.IsDBNull(r.GetOrdinal("finished_at")) ? null : DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(r.GetOrdinal("finished_at"))).LocalDateTime,
                        Log = r.IsDBNull(r.GetOrdinal("log")) ? "" : r.GetString(r.GetOrdinal("log"))
                    };
                    var optsJson = r.GetString(r.GetOrdinal("options"));
                    j.Options = JsonSerializer.Deserialize<EncodeOptions>(optsJson) ?? new EncodeOptions();
                    var selJson = r.GetString(r.GetOrdinal("selected_streams"));
                    j.SelectedStreams = JsonSerializer.Deserialize<List<int>>(selJson) ?? new List<int>();
                    if (j.SegmentsTotal > 0 && j.SegmentsDone > 0)
                        j.Progress = Math.Min(100, (double)j.SegmentsDone / j.SegmentsTotal * 100);
                    list.Add(j);
                }
                catch { /* skip malformed rows */ }
            }
        }
        return list;
    }

    private static JobStatus ParseStatus(string s) => s switch
    {
        "queued" => JobStatus.Queued,
        "running" => JobStatus.Running,
        "done" => JobStatus.Done,
        "error" => JobStatus.Error,
        "cancelled" => JobStatus.Cancelled,
        "interrupted" => JobStatus.Interrupted,
        _ => JobStatus.Error
    };

    public static void Delete(string id)
    {
        lock (Lock)
        {
            using var cn = Connect();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "DELETE FROM jobs WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }
    }
}
