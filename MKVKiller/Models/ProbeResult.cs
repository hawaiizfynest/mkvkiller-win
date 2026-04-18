using System.Text.Json.Serialization;

namespace MKVKiller.Models;

public class MediaStream
{
    [JsonPropertyName("index")] public int Index { get; set; }
    [JsonPropertyName("codec_type")] public string? CodecType { get; set; }
    [JsonPropertyName("codec_name")] public string? CodecName { get; set; }
    [JsonPropertyName("width")] public int? Width { get; set; }
    [JsonPropertyName("height")] public int? Height { get; set; }
    [JsonPropertyName("channels")] public int? Channels { get; set; }
    [JsonPropertyName("channel_layout")] public string? ChannelLayout { get; set; }
    [JsonPropertyName("bit_rate")] public string? BitRateStr { get; set; }
    [JsonPropertyName("tags")] public Dictionary<string, string>? Tags { get; set; }
    [JsonPropertyName("disposition")] public Dictionary<string, int>? Disposition { get; set; }

    public string Language => Tags != null && (Tags.TryGetValue("language", out var l) || Tags.TryGetValue("LANGUAGE", out l)) ? l : "und";
    public string Title => Tags != null && (Tags.TryGetValue("title", out var t) || Tags.TryGetValue("TITLE", out t)) ? t : "";
    public bool IsDefault => Disposition != null && Disposition.TryGetValue("default", out var d) && d == 1;
    public bool IsForced => Disposition != null && Disposition.TryGetValue("forced", out var f) && f == 1;
    public long BitRate => long.TryParse(BitRateStr, out var v) ? v : 0;
}

public class ProbeFormat
{
    [JsonPropertyName("duration")] public string? DurationStr { get; set; }
    [JsonPropertyName("size")] public string? SizeStr { get; set; }
    [JsonPropertyName("bit_rate")] public string? BitRateStr { get; set; }
    [JsonPropertyName("format_name")] public string? FormatName { get; set; }

    public double Duration => double.TryParse(DurationStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0;
    public long Size => long.TryParse(SizeStr, out var s) ? s : 0;
    public long BitRate => long.TryParse(BitRateStr, out var b) ? b : 0;
}

public class ProbeResult
{
    [JsonPropertyName("format")] public ProbeFormat Format { get; set; } = new();
    [JsonPropertyName("streams")] public List<MediaStream> Streams { get; set; } = new();
}
