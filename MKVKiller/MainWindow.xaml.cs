using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using MKVKiller.Models;
using MKVKiller.Services;

namespace MKVKiller;

public partial class MainWindow : Window
{
    // Win32: force dark title bar on Windows 10 1809+ / Windows 11
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    private string _currentPath = "";
    private string _sortMode = "name";
    private readonly ObservableCollection<FileListItem> _files = new();
    private readonly Dictionary<string, FileListItem> _queue = new(); // path -> item
    private string _currentTab = "convert";
    private FileListItem? _selectedFile;
    private ProbeResult? _currentProbe;
    private readonly Dictionary<string, ProbeResult> _probeCache = new();

    public MainWindow()
    {
        InitializeComponent();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Force dark title bar
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int darkMode = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
        }
        catch { /* pre-1809 Windows, ignore */ }

        _sortMode = Preferences.Current.SortMode;
        SourceFolderText.Text = Preferences.Current.LastInputFolder ?? "";
        OutputFolderText.Text = Preferences.Current.LastOutputFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "MKVKiller");

        // Initialize sort buttons
        SortNameBtn.IsChecked = _sortMode == "name";
        SortSizeDescBtn.IsChecked = _sortMode == "size-desc";
        SortSizeAscBtn.IsChecked = _sortMode == "size-asc";

        FileListBox.ItemsSource = _files;

        // Database + jobs
        LogDatabase.Initialize();
        JobManager.Instance.LoadFromDatabase();

        // FFmpeg presence check
        if (!FFmpegService.IsAvailable())
        {
            FFmpegStatusText.Text = "⚠ ffmpeg not found!";
            FFmpegStatusText.Foreground = new SolidColorBrush(Color.FromRgb(255, 92, 92));
            MessageBox.Show(
                $"ffmpeg.exe not found in:\n{Path.Combine(AppContext.BaseDirectory, "ffmpeg")}\n\nPlease run build.ps1 or reinstall.",
                "MKVKiller", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        else
        {
            StatusText.Text = "Detecting encoders...";
            var caps = await EncoderDetection.DetectAsync();
            UpdateHwPills(caps);
            StatusText.Text = "Ready.";
            FFmpegStatusText.Text = $"ffmpeg: {Path.Combine(AppContext.BaseDirectory, "ffmpeg")}";
        }

        // Render initial tab
        RenderCurrentTab();

        // Browse initial folder if set
        if (!string.IsNullOrEmpty(SourceFolderText.Text) && Directory.Exists(SourceFolderText.Text))
            Browse(SourceFolderText.Text);

        JobManager.Instance.Jobs.CollectionChanged += (_, _) => { if (_currentTab == "jobs" || _currentTab == "log") RenderCurrentTab(); };

        // Auto-resume interrupted
        _ = Dispatcher.BeginInvoke(new Action(() => JobManager.Instance.ResumeInterrupted()), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private void UpdateHwPills(EncoderCapabilities caps)
    {
        void SetPill(Border pill, bool on)
        {
            pill.BorderBrush = on ? (System.Windows.Media.Brush)FindResource("GoodBrush") : (System.Windows.Media.Brush)FindResource("BorderBrush");
            if (pill.Child is TextBlock tb)
                tb.Foreground = on ? (System.Windows.Media.Brush)FindResource("GoodBrush") : (System.Windows.Media.Brush)FindResource("MutedBrush");
        }
        SetPill(PillCpu, caps.Cpu);
        SetPill(PillQsv, caps.Qsv);
        SetPill(PillNvenc, caps.Nvenc);
    }

    // ---- Theme ----
    private void OnThemeClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Shapes.Ellipse el && el.Tag is string t)
        {
            App.ApplyTheme(t);
        }
    }

    // ---- Source / output folders ----
    private void BrowseSource_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Select source folder" };
        if (!string.IsNullOrEmpty(SourceFolderText.Text) && Directory.Exists(SourceFolderText.Text))
            dlg.InitialDirectory = SourceFolderText.Text;
        if (dlg.ShowDialog() == true)
        {
            SourceFolderText.Text = dlg.FolderName;
            Browse(dlg.FolderName);
        }
    }

    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Select output folder" };
        if (!string.IsNullOrEmpty(OutputFolderText.Text) && Directory.Exists(OutputFolderText.Text))
            dlg.InitialDirectory = OutputFolderText.Text;
        if (dlg.ShowDialog() == true)
        {
            OutputFolderText.Text = dlg.FolderName;
            Preferences.Current.LastOutputFolder = dlg.FolderName;
            Preferences.Save();
        }
    }

    private void GoSource_Click(object sender, RoutedEventArgs e) => Browse(SourceFolderText.Text);

    private void SourceFolderText_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Browse(SourceFolderText.Text);
    }

    private void Browse(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            StatusText.Text = "Folder not found: " + path;
            return;
        }
        _currentPath = path;
        CurrentPathText.Text = path;
        Preferences.Current.LastInputFolder = path;
        Preferences.Save();

        try
        {
            _files.Clear();
            // Parent link
            var parent = Directory.GetParent(path);
            if (parent != null)
            {
                _files.Add(new FileListItem { FullPath = parent.FullName, Name = ".. (up)", IsDirectory = true });
            }

            var items = new List<FileListItem>();
            foreach (var dir in Directory.EnumerateDirectories(path))
            {
                try
                {
                    var info = new DirectoryInfo(dir);
                    if ((info.Attributes & FileAttributes.Hidden) != 0) continue;
                    items.Add(new FileListItem { FullPath = dir, Name = info.Name, IsDirectory = true });
                }
                catch { }
            }
            foreach (var file in Directory.EnumerateFiles(path))
            {
                try
                {
                    var info = new FileInfo(file);
                    if ((info.Attributes & FileAttributes.Hidden) != 0) continue;
                    var item = new FileListItem { FullPath = file, Name = info.Name, IsDirectory = false, Size = info.Length };
                    item.IsQueued = _queue.ContainsKey(file);
                    items.Add(item);
                }
                catch { }
            }

            // Sort
            var dirs = items.Where(i => i.IsDirectory).OrderBy(i => i.Name).ToList();
            var fls = items.Where(i => !i.IsDirectory).ToList();
            fls = _sortMode switch
            {
                "size-desc" => fls.OrderByDescending(f => f.Size).ToList(),
                "size-asc" => fls.OrderBy(f => f.Size).ToList(),
                _ => fls.OrderBy(f => f.Name).ToList()
            };
            foreach (var d in dirs) _files.Add(d);
            foreach (var f in fls) _files.Add(f);

            StatusText.Text = $"{items.Count} items in {path}";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Error: " + ex.Message;
        }
    }

    private void Sort_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string s)
        {
            _sortMode = s;
            Preferences.Current.SortMode = s;
            Preferences.Save();
            if (!string.IsNullOrEmpty(_currentPath)) Browse(_currentPath);
        }
    }

    private bool _suppressSelectionChange = false;

    private void FileListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionChange) return;
        if (FileListBox.SelectedItem is FileListItem item)
        {
            if (item.IsDirectory)
            {
                _suppressSelectionChange = true;
                try
                {
                    Browse(item.FullPath);
                    FileListBox.SelectedItem = null;
                    SourceFolderText.Text = item.FullPath;
                }
                finally { _suppressSelectionChange = false; }
            }
            else if (item.IsMedia)
            {
                _selectedFile = item;
                _ = SelectFileAsync(item);
            }
        }
    }

    private async Task SelectFileAsync(FileListItem item)
    {
        _currentTab = "convert";
        ConvertTabBtn.IsChecked = true;
        StatusText.Text = "Probing " + item.Name + "...";
        var probe = await FFmpegService.ProbeAsync(item.FullPath);
        if (probe == null)
        {
            StatusText.Text = "Failed to probe " + item.Name;
            return;
        }
        _currentProbe = probe;
        _probeCache[item.FullPath] = probe;
        StatusText.Text = "Ready.";
        RenderCurrentTab();
    }

    // ---- Queue / checkboxes ----
    private void FileItem_CheckChanged(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.DataContext is FileListItem item)
        {
            if (cb.IsChecked == true) _queue[item.FullPath] = item;
            else _queue.Remove(item.FullPath);
            UpdateQueueBar();
        }
    }

    private void UpdateQueueBar()
    {
        int n = _queue.Count;
        BatchBtn.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
        BatchBtn.Content = $"Batch → {n}";
        QueueCountText.Text = n > 0 ? $"{n} queued" : "";
        BatchTabBtn.Content = n > 0 ? $"Batch ({n})" : "Batch";
        if (_currentTab == "batch") RenderCurrentTab();
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var f in _files.Where(f => f.IsMedia))
        {
            _queue[f.FullPath] = f;
            f.IsQueued = true;
        }
        UpdateQueueBar();
    }

    private void ClearQueue_Click(object sender, RoutedEventArgs e)
    {
        foreach (var f in _files) f.IsQueued = false;
        _queue.Clear();
        UpdateQueueBar();
    }

    private void BatchTab_Click(object sender, RoutedEventArgs e)
    {
        BatchTabBtn.IsChecked = true;
        _currentTab = "batch";
        RenderCurrentTab();
    }

    // ---- Tabs ----
    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string t)
        {
            _currentTab = t;
            RenderCurrentTab();
        }
    }

    private void RenderCurrentTab()
    {
        UIElement? content = _currentTab switch
        {
            "convert" => Views.ConvertView.Build(_selectedFile, _currentProbe, OutputFolderText.Text),
            "batch" => Views.BatchView.Build(_queue.Values.ToList(), OutputFolderText.Text, () =>
            {
                _queue.Clear();
                foreach (var f in _files) f.IsQueued = false;
                UpdateQueueBar();
                _currentTab = "jobs";
                JobsTabBtn.IsChecked = true;
                RenderCurrentTab();
            }, _probeCache),
            "jobs" => Views.JobsView.Build(() => RenderCurrentTab()),
            "log" => Views.LogView.Build(() => RenderCurrentTab()),
            _ => null
        };
        TabContent.Content = content;
    }

    // Build file list items with checkbox - need a custom DataTemplate since we need event handler
    // Use a simple ItemTemplate via code
}
