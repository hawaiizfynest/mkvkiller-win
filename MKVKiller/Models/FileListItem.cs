using CommunityToolkit.Mvvm.ComponentModel;

namespace MKVKiller.Models;

public partial class FileListItem : ObservableObject
{
    public string FullPath { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsDirectory { get; set; }
    public long Size { get; set; }

    [ObservableProperty] private bool isQueued;

    public bool IsMedia => !IsDirectory && MediaExts.Any(ext => Name.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
    public string Icon => IsDirectory ? "📁" : (IsMedia ? "🎬" : "📄");
    public string SizeText => IsDirectory ? "" : Services.Fmt.Bytes(Size);

    private static readonly string[] MediaExts = { ".mkv", ".mp4", ".avi", ".mov", ".webm", ".ts", ".m4v" };
}
