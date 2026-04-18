namespace MKVKiller.Services;

public static class Fmt
{
    public static string Bytes(long n)
    {
        if (n <= 0) return "-";
        double v = n;
        string[] u = { "B", "KB", "MB", "GB", "TB" };
        int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return $"{v:F2} {u[i]}";
    }

    public static string Duration(double s)
    {
        if (s <= 0) return "-";
        var total = TimeSpan.FromSeconds(s);
        return total.TotalHours >= 1 ? total.ToString(@"h\:mm\:ss") : total.ToString(@"m\:ss");
    }

    public static string Ago(DateTime? when)
    {
        if (!when.HasValue) return "-";
        var d = DateTime.Now - when.Value;
        if (d.TotalMinutes < 1) return "just now";
        if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes}m ago";
        if (d.TotalHours < 24) return $"{(int)d.TotalHours}h ago";
        return $"{(int)d.TotalDays}d ago";
    }

    public static int RecommendAudioKbps(int? channels) => channels switch
    {
        null or 0 => 192,
        <= 1 => 96,
        2 => 192,
        <= 6 => 384,
        _ => 512
    };

    public static int SnapAudioBitrate(int kbps)
    {
        int[] options = { 128, 192, 256, 384 };
        return options.OrderBy(o => Math.Abs(o - kbps)).First();
    }

    // Estimate video bitrate (kbps) for given CRF/CQ at target resolution
    public static double EstimateVideoKbps(int crf, int w, int h, string sourceCodec, string encoder)
    {
        var ref1080 = new Dictionary<int, double>
        {
            {14, 18000}, {16, 13000}, {18, 9000}, {19, 7500},
            {20, 6000}, {21, 5000}, {22, 4200}, {23, 3500},
            {24, 2900}, {25, 2400}, {26, 2000}, {27, 1700},
            {28, 1400}, {29, 1200}, {30, 1000}
        };
        int c = Math.Max(14, Math.Min(30, crf));
        int lo = c;
        while (!ref1080.ContainsKey(lo)) lo--;
        int hi = c;
        while (!ref1080.ContainsKey(hi)) hi++;
        double frac = lo == hi ? 0 : (c - lo) / (double)(hi - lo);
        double baseKbps = ref1080[lo] * (1 - frac) + ref1080[hi] * frac;
        double scaled = baseKbps * ((double)w * h / (1920 * 1080));
        if (sourceCodec == "hevc" || sourceCodec == "h265") scaled *= 1.1;
        if (encoder == "qsv") scaled *= 1.20;
        else if (encoder == "nvenc") scaled *= 1.15;
        return scaled;
    }
}
