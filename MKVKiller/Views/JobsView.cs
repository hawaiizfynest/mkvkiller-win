using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MKVKiller.Models;
using MKVKiller.Services;

namespace MKVKiller.Views;

internal static class JobsView
{
    public static UIElement Build(Action refresh)
    {
        var root = new StackPanel();
        var jobs = JobManager.Instance.Jobs.ToList();
        var active = jobs.Where(j => j.Status == JobStatus.Running || j.Status == JobStatus.Queued || j.Status == JobStatus.Interrupted).ToList();
        var recent = jobs.Where(j => j.Status == JobStatus.Done || j.Status == JobStatus.Error || j.Status == JobStatus.Cancelled).Take(20).ToList();

        if (active.Count == 0 && recent.Count == 0)
        {
            return Ui.Panel(Ui.Txt("No jobs yet. Select a file from the sidebar and start a conversion.", 13, Ui.Res("MutedBrush")));
        }

        foreach (var j in active) root.Children.Add(JobCard(j, refresh));
        if (recent.Count > 0)
        {
            root.Children.Add(new TextBlock
            {
                Text = "RECENT",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = Ui.Res("MutedBrush"),
                Margin = new Thickness(0, 14, 0, 6)
            });
            foreach (var j in recent) root.Children.Add(JobCard(j, refresh));
        }

        return Ui.Panel(root);
    }

    internal static Border JobCard(ConversionJob j, Action refresh)
    {
        var card = new Border
        {
            Background = Ui.Res("PanelBrush"),
            BorderBrush = Ui.Res("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 10)
        };
        var sp = new StackPanel();
        card.Child = sp;

        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var nameTb = new TextBlock { Text = j.FileName, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(nameTb, 0);
        var statusBadge = StatusBadge(j.Status);
        Grid.SetColumn(statusBadge, 1);
        head.Children.Add(nameTb); head.Children.Add(statusBadge);
        sp.Children.Add(head);

        // Progress bar
        var bar = new ProgressBar
        {
            Height = 6,
            Margin = new Thickness(0, 8, 0, 6),
            Minimum = 0,
            Maximum = 100,
            Value = j.Progress,
            Background = Ui.Res("BorderBrush"),
            Foreground = Ui.Res("AccentBrush"),
            BorderThickness = new Thickness(0)
        };
        sp.Children.Add(bar);

        // Live updates
        j.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ConversionJob.Progress))
                Application.Current?.Dispatcher.BeginInvoke(new Action(() => bar.Value = j.Progress));
            if (args.PropertyName == nameof(ConversionJob.Status))
            {
                Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    head.Children.Remove(statusBadge);
                    statusBadge = StatusBadge(j.Status);
                    Grid.SetColumn(statusBadge, 1);
                    head.Children.Add(statusBadge);
                }));
            }
        };

        // Meta row
        var meta = new WrapPanel();
        void AddMeta(string text, Brush? color = null)
        {
            var t = new TextBlock { Text = text, FontSize = 11, Foreground = color ?? Ui.Res("MutedBrush"), Margin = new Thickness(0, 0, 12, 0) };
            meta.Children.Add(t);
        }
        AddMeta($"{j.Progress:F1}%");
        if (j.SegmentsTotal > 1 && j.Resumable) AddMeta($"seg {j.SegmentsDone}/{j.SegmentsTotal}");
        AddMeta(j.Options.Encoder.ToUpper(), Ui.Res("Accent2Brush"));
        if (j.Resumable) AddMeta("⟲ resumable", Ui.Res("Accent2Brush"));
        if (j.Speed > 0) AddMeta($"{j.Speed:F2}x");
        if (j.Fps > 0) AddMeta($"{j.Fps:F1} fps");
        if (j.EtaSeconds > 0) AddMeta($"ETA {Fmt.Duration(j.EtaSeconds)}");
        if (j.OutputSize > 0)
        {
            int pct = j.InputSize > 0 ? (int)((1 - (double)j.OutputSize / j.InputSize) * 100) : 0;
            AddMeta($"{Fmt.Bytes(j.InputSize)} → {Fmt.Bytes(j.OutputSize)}  {(pct > 0 ? "-" : "+")}{Math.Abs(pct)}%", pct > 0 ? Ui.Res("GoodBrush") : Ui.Res("WarnBrush"));
        }
        else
        {
            AddMeta(Fmt.Bytes(j.InputSize));
        }
        if (j.ReplacedOriginal) AddMeta("✓ replaced", Ui.Res("GoodBrush"));
        if (!string.IsNullOrEmpty(j.Error)) AddMeta(j.Error!, Ui.Res("ErrBrush"));
        sp.Children.Add(meta);

        // Buttons
        var btns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        if (j.Status == JobStatus.Running || j.Status == JobStatus.Queued)
        {
            btns.Children.Add(Ui.Btn("Cancel", (_, _) => { JobManager.Instance.Cancel(j.Id); refresh(); }, small: true, ghost: true));
        }
        if (j.Status == JobStatus.Error || j.Status == JobStatus.Cancelled || j.Status == JobStatus.Interrupted)
        {
            btns.Children.Add(Ui.Btn(j.Resumable ? "Restart (resume)" : "Restart", async (_, _) => { await JobManager.Instance.RestartAsync(j.Id); refresh(); }, small: true, ghost: true));
        }
        if (j.Status != JobStatus.Running)
        {
            btns.Children.Add(Ui.Btn("Remove", (_, _) => { JobManager.Instance.Remove(j.Id); refresh(); }, small: true, ghost: true));
        }
        btns.Children.Add(Ui.Btn("View log", (_, _) =>
        {
            var win = new Window
            {
                Title = "Log: " + j.FileName,
                Width = 900, Height = 700,
                Background = Ui.Res("BgBrush"),
                Owner = Application.Current.MainWindow
            };
            var tb = new TextBox
            {
                Text = j.Log,
                IsReadOnly = true,
                FontFamily = new FontFamily("Consolas, Menlo"),
                FontSize = 11,
                Background = new SolidColorBrush(Color.FromRgb(10, 12, 16)),
                Foreground = new SolidColorBrush(Color.FromRgb(205, 209, 218)),
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                AcceptsReturn = true,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(14)
            };
            win.Content = tb;
            win.Show();
        }, small: true, ghost: true));
        sp.Children.Add(btns);

        return card;
    }

    internal static Border StatusBadge(JobStatus s)
    {
        var (bg, fg, text) = s switch
        {
            JobStatus.Running => (Color.FromRgb(43, 61, 110), Color.FromRgb(156, 181, 255), "RUNNING"),
            JobStatus.Done => (Color.FromRgb(30, 74, 58), Color.FromRgb(156, 229, 193), "DONE"),
            JobStatus.Error => (Color.FromRgb(94, 43, 43), Color.FromRgb(255, 161, 161), "ERROR"),
            JobStatus.Interrupted => (Color.FromRgb(94, 74, 43), Color.FromRgb(255, 217, 161), "INTERRUPTED"),
            JobStatus.Cancelled => (Color.FromRgb(58, 58, 58), Color.FromRgb(153, 153, 153), "CANCELLED"),
            _ => (Color.FromRgb(58, 58, 58), Color.FromRgb(204, 204, 204), "QUEUED")
        };
        return new Border
        {
            Background = new SolidColorBrush(bg),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 2, 8, 2),
            Child = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(fg) }
        };
    }
}

internal static class LogView
{
    public static UIElement Build(Action refresh)
    {
        var root = new StackPanel();
        var all = JobManager.Instance.Jobs.ToList();
        int total = all.Count;
        int done = all.Count(j => j.Status == JobStatus.Done);
        int failed = all.Count(j => j.Status == JobStatus.Error);
        int interrupted = all.Count(j => j.Status == JobStatus.Interrupted);
        long totalSaved = all.Where(j => j.Status == JobStatus.Done && j.OutputSize > 0 && j.InputSize > 0).Sum(j => j.InputSize - j.OutputSize);

        // Stat cards
        var statsGrid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        for (int i = 0; i < 4; i++)
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        void AddStat(int col, string label, string value, Brush? valueColor = null)
        {
            var card = new Border
            {
                Background = Ui.Res("PanelBrush"),
                BorderBrush = Ui.Res("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(col > 0 ? 6 : 0, 0, col < 3 ? 6 : 0, 0)
            };
            var inner = new StackPanel();
            inner.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = Ui.Res("MutedBrush"), FontWeight = FontWeights.SemiBold });
            inner.Children.Add(new TextBlock { Text = value, FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = valueColor ?? Ui.Res("TextBrush"), Margin = new Thickness(0, 4, 0, 0) });
            card.Child = inner;
            Grid.SetColumn(card, col);
            statsGrid.Children.Add(card);
        }
        AddStat(0, "Total", total.ToString());
        AddStat(1, "Successful", done.ToString(), Ui.Res("GoodBrush"));
        AddStat(2, "Failed", failed.ToString(), Ui.Res("ErrBrush"));
        AddStat(3, "Interrupted", interrupted.ToString(), Ui.Res("WarnBrush"));
        root.Children.Add(statsGrid);

        if (totalSaved > 0)
        {
            root.Children.Add(new TextBlock
            {
                Text = $"💾 Total space saved: {Fmt.Bytes(totalSaved)}",
                FontSize = 13,
                Foreground = Ui.Res("GoodBrush"),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 14)
            });
        }

        if (all.Count == 0)
        {
            root.Children.Add(Ui.Txt("No conversions logged yet.", 13, Ui.Res("MutedBrush")));
            return Ui.Panel(root);
        }

        // Job list
        foreach (var j in all.Take(200))
        {
            var row = new Border
            {
                Background = Ui.Res("PanelBrush"),
                BorderBrush = Ui.Res("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 0, 6),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var badge = JobsView.StatusBadge(j.Status);
            badge.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetColumn(badge, 0);

            var nameTb = new TextBlock { Text = j.FileName, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(10, 0, 0, 0) };
            Grid.SetColumn(nameTb, 1);

            var trailTb = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 10, 0) };
            if (j.Status == JobStatus.Done && j.OutputSize > 0 && j.InputSize > 0)
            {
                int pct = (int)((1 - (double)j.OutputSize / j.InputSize) * 100);
                trailTb.Text = $"{(pct > 0 ? "-" : "+")}{Math.Abs(pct)}%";
                trailTb.Foreground = pct > 0 ? Ui.Res("GoodBrush") : Ui.Res("WarnBrush");
                trailTb.FontWeight = FontWeights.SemiBold;
            }
            Grid.SetColumn(trailTb, 2);

            var whenTb = new TextBlock
            {
                Text = Fmt.Ago(j.FinishedAt ?? j.StartedAt ?? j.CreatedAt),
                FontSize = 11, Foreground = Ui.Res("MutedBrush"), VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(whenTb, 3);

            g.Children.Add(badge); g.Children.Add(nameTb); g.Children.Add(trailTb); g.Children.Add(whenTb);
            row.Child = g;
            row.MouseLeftButtonUp += (_, _) =>
            {
                var win = new Window
                {
                    Title = "Job details: " + j.FileName,
                    Width = 900, Height = 700,
                    Background = Ui.Res("BgBrush"),
                    Owner = Application.Current.MainWindow
                };
                var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(20) };
                var inner = new StackPanel();
                sv.Content = inner;
                inner.Children.Add(JobsView.JobCard(j, () => { }));
                inner.Children.Add(new TextBlock { Text = "Full ffmpeg log", FontSize = 12, Foreground = Ui.Res("MutedBrush"), Margin = new Thickness(0, 14, 0, 8) });
                var logTb = new TextBox
                {
                    Text = j.Log,
                    IsReadOnly = true,
                    FontFamily = new FontFamily("Consolas, Menlo"),
                    FontSize = 11,
                    Background = new SolidColorBrush(Color.FromRgb(10, 12, 16)),
                    Foreground = new SolidColorBrush(Color.FromRgb(205, 209, 218)),
                    TextWrapping = TextWrapping.Wrap,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    AcceptsReturn = true,
                    BorderThickness = new Thickness(0),
                    MinHeight = 400,
                    Padding = new Thickness(14)
                };
                inner.Children.Add(logTb);
                win.Content = sv;
                win.Show();
            };
            root.Children.Add(row);
        }

        return Ui.Panel(root);
    }
}
