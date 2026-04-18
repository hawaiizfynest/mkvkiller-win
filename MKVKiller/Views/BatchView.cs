using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MKVKiller.Models;
using MKVKiller.Services;

namespace MKVKiller.Views;

internal static class BatchView
{
    public static UIElement Build(List<FileListItem> queue, string outputFolder, Action onSubmitted, Dictionary<string, ProbeResult> probeCache)
    {
        if (queue.Count == 0)
        {
            return Ui.Panel(Ui.Txt("No files queued. Check boxes in the file list to add files.", 13, Ui.Res("MutedBrush")));
        }

        var root = new StackPanel();

        long totalSize = queue.Sum(f => f.Size);

        // Summary
        var sum = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(26, 37, 56)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(45, 64, 102)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(0, 0, 0, 14)
        };
        var sumPanel = new StackPanel();
        sum.Child = sumPanel;
        sumPanel.Children.Add(KV("Files queued", queue.Count.ToString()));
        sumPanel.Children.Add(KV("Total source size", Fmt.Bytes(totalSize)));
        var estValue = Ui.Txt("probing...", 13, Ui.Res("MutedBrush"));
        var estRow = new Grid();
        estRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        estRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        estRow.Margin = new Thickness(0, 4, 0, 0);
        var estL = Ui.Txt("Estimated output size", 12, Ui.Res("MutedBrush"));
        Grid.SetColumn(estL, 0); Grid.SetColumn(estValue, 1);
        estRow.Children.Add(estL); estRow.Children.Add(estValue);
        sumPanel.Children.Add(estRow);
        root.Children.Add(sum);

        // Encoder
        root.Children.Add(Ui.H2("Encoder"));
        var caps = EncoderDetection.Current;
        var encCombo = new ComboBox();
        encCombo.Items.Add("CPU (x264)");
        var qsvI = new ComboBoxItem { Content = "Intel QSV", IsEnabled = caps.Qsv };
        var nvI = new ComboBoxItem { Content = "NVIDIA NVENC", IsEnabled = caps.Nvenc };
        encCombo.Items.Add(qsvI); encCombo.Items.Add(nvI);
        encCombo.SelectedIndex = 0;
        root.Children.Add(encCombo);

        // Queue list
        root.Children.Add(Ui.H2("Queue"));
        var queueList = new StackPanel();
        var estCells = new Dictionary<string, TextBlock>();
        foreach (var f in queue)
        {
            var row = new Border
            {
                Background = Ui.Res("PanelBrush"),
                BorderBrush = Ui.Res("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 6)
            };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = Ui.Txt(f.Name, 13);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            var size = Ui.Txt(Fmt.Bytes(f.Size), 11, Ui.Res("MutedBrush"));
            size.Margin = new Thickness(10, 0, 8, 0);
            size.VerticalAlignment = VerticalAlignment.Center;
            var arrow = Ui.Txt("→", 11, Ui.Res("MutedBrush"));
            arrow.Margin = new Thickness(0, 0, 8, 0);
            arrow.VerticalAlignment = VerticalAlignment.Center;
            var est = Ui.Txt("probing...", 12, Ui.Res("MutedBrush"));
            est.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(name, 0); Grid.SetColumn(size, 1); Grid.SetColumn(arrow, 2); Grid.SetColumn(est, 3);
            g.Children.Add(name); g.Children.Add(size); g.Children.Add(arrow); g.Children.Add(est);
            row.Child = g;
            queueList.Children.Add(row);
            estCells[f.FullPath] = est;
        }
        root.Children.Add(queueList);

        // Settings
        var crfBox = new TextBox { Text = "24" };
        var presetCombo = Ui.Combo(new[] { "fast", "medium", "slow", "slower" }, "slow");
        var audioBr = Ui.Combo(new[] { "128", "192", "256", "384" }, "192");
        var maxHeight = Ui.Combo(new[] { "Keep original", "2160", "1080", "720" }, "Keep original");
        var trackMode = Ui.Combo(new[] { "Default + English", "All tracks", "Video + default audio only" }, "Default + English");
        var subMode = Ui.Combo(new[] { "soft", "none" }, "soft");

        // Presets
        root.Children.Add(Ui.H2("Shared encode settings"));
        var presets = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        foreach (var (n, q) in new[] { ("Light", 22), ("Balanced", 24), ("Shrink hard", 26) })
        {
            var btn = new Button
            {
                Content = $"{n} · CRF {q}",
                Style = (Style)Application.Current.FindResource("GhostButton"),
                Padding = new Thickness(10, 6, 10, 6),
                FontSize = 12,
                Margin = new Thickness(0, 0, 6, 0)
            };
            btn.Click += (_, _) => { crfBox.Text = q.ToString(); };
            presets.Children.Add(btn);
        }
        root.Children.Add(presets);

        root.Children.Add(Ui.Grid2(
            ("Quality (CRF/CQ)", crfBox),
            ("Preset", presetCombo),
            ("Audio bitrate", audioBr),
            ("Max height", maxHeight),
            ("Track selection", trackMode),
            ("Subtitles", subMode)
        ));

        var resumable = new CheckBox
        {
            Content = new TextBlock { Inlines = { new System.Windows.Documents.Run("Resumable encoding ") { FontWeight = FontWeights.SemiBold, Foreground = Ui.Res("Accent2Brush") }, new System.Windows.Documents.Run("— picks up after restart; drops subtitles") { Foreground = Ui.Res("MutedBrush"), FontSize = 11 } } },
            Margin = new Thickness(0, 14, 0, 0)
        };
        var replace = new CheckBox
        {
            Content = new TextBlock { Inlines = { new System.Windows.Documents.Run("Replace originals ") { FontWeight = FontWeights.SemiBold, Foreground = Ui.Res("ErrBrush") }, new System.Windows.Documents.Run("— delete source files after each successful conversion") { Foreground = Ui.Res("MutedBrush"), FontSize = 11 } } },
            Margin = new Thickness(0, 8, 0, 0)
        };
        root.Children.Add(resumable);
        root.Children.Add(replace);

        // Submit button
        var submitBtn = new Button
        {
            Content = "Start Batch Conversion",
            Style = (Style)Application.Current.FindResource("SuccessButton"),
            Margin = new Thickness(0, 16, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        submitBtn.Click += async (_, _) =>
        {
            if (replace.IsChecked == true &&
                MessageBox.Show($"This will DELETE the original file for every successful conversion ({queue.Count} files).\n\nContinue?",
                    "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            if (!Directory.Exists(outputFolder))
            {
                try { Directory.CreateDirectory(outputFolder); }
                catch { MessageBox.Show("Output folder invalid"); return; }
            }

            submitBtn.IsEnabled = false;
            int encIdx = encCombo.SelectedIndex;
            string encoder = encIdx == 1 ? "qsv" : encIdx == 2 ? "nvenc" : "cpu";
            int q = int.TryParse(crfBox.Text, out var qv) ? qv : 24;
            string preset = presetCombo.SelectedItem?.ToString() ?? "slow";
            int abBase = int.Parse(audioBr.SelectedItem?.ToString() ?? "192");
            string mh = maxHeight.SelectedItem?.ToString() ?? "Keep original";
            int? mhv = mh == "Keep original" ? null : int.Parse(mh);
            string tm = trackMode.SelectedItem?.ToString() ?? "Default + English";
            string sm = subMode.SelectedItem?.ToString() ?? "soft";
            bool res = resumable.IsChecked == true;
            bool rep = replace.IsChecked == true;

            int submitted = 0, failed = 0;
            foreach (var f in queue)
            {
                try
                {
                    submitBtn.Content = $"Submitting {submitted + 1}/{queue.Count}...";
                    ProbeResult? probe;
                    if (!probeCache.TryGetValue(f.FullPath, out probe))
                    {
                        probe = await FFmpegService.ProbeAsync(f.FullPath);
                        if (probe != null) probeCache[f.FullPath] = probe;
                    }
                    if (probe == null) { failed++; continue; }

                    var sel = new List<int>();
                    foreach (var st in probe.Streams)
                    {
                        if (st.CodecType == "video") sel.Add(st.Index);
                        else if (st.CodecType == "audio")
                        {
                            if (tm == "All tracks") sel.Add(st.Index);
                            else if (tm == "Default + English" && (st.IsDefault || st.Language == "eng" || st.Language == "und")) sel.Add(st.Index);
                            else if (tm == "Video + default audio only" && st.IsDefault) sel.Add(st.Index);
                        }
                        else if (st.CodecType == "subtitle" && sm == "soft" && !res)
                        {
                            if (tm == "All tracks") sel.Add(st.Index);
                            else if (tm == "Default + English" && (st.IsDefault || st.Language == "eng")) sel.Add(st.Index);
                        }
                    }
                    if (!sel.Any(i => probe.Streams.Any(s => s.Index == i && s.CodecType == "audio")))
                    {
                        var firstAudio = probe.Streams.FirstOrDefault(s => s.CodecType == "audio");
                        if (firstAudio != null) sel.Add(firstAudio.Index);
                    }
                    var keptAudio = probe.Streams.Where(s => sel.Contains(s.Index) && s.CodecType == "audio").ToList();
                    int maxCh = keptAudio.Count > 0 ? (keptAudio.Max(s => (int?)s.Channels) ?? 0) : 0;
                    int abKbps = Math.Max(abBase, Fmt.SnapAudioBitrate(Fmt.RecommendAudioKbps(maxCh)));

                    var job = new ConversionJob
                    {
                        InputPath = f.FullPath,
                        OutputPath = Path.Combine(outputFolder, Path.ChangeExtension(f.Name, ".mp4")),
                        InputSize = f.Size,
                        Duration = probe.Format.Duration,
                        SelectedStreams = sel,
                        ReplaceOriginal = rep,
                        Resumable = res,
                        Options = new EncodeOptions
                        {
                            Encoder = encoder,
                            Crf = encoder == "cpu" ? q : 20,
                            HwQuality = encoder == "cpu" ? 23 : q,
                            Preset = preset,
                            AudioMode = "auto",
                            AudioBitrate = abKbps + "k",
                            SubtitleMode = res ? "none" : sm,
                            MaxHeight = mhv
                        }
                    };
                    await JobManager.Instance.SubmitAsync(job);
                    submitted++;
                }
                catch { failed++; }
            }
            MessageBox.Show($"Queued {submitted} job(s){(failed > 0 ? $", {failed} failed" : "")}.");
            onSubmitted();
        };
        root.Children.Add(submitBtn);

        // Background: probe files and update estimates
        _ = Task.Run(async () =>
        {
            long totalIn = totalSize, totalOut = 0;
            int done = 0;
            foreach (var f in queue)
            {
                try
                {
                    ProbeResult? probe;
                    if (!probeCache.TryGetValue(f.FullPath, out probe))
                    {
                        probe = await FFmpegService.ProbeAsync(f.FullPath);
                        if (probe != null) probeCache[f.FullPath] = probe;
                    }
                    if (probe == null)
                    {
                        Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (estCells.TryGetValue(f.FullPath, out var tb)) tb.Text = "?";
                        }));
                        continue;
                    }
                    var v = probe.Streams.FirstOrDefault(s => s.CodecType == "video");
                    if (v == null || probe.Format.Duration <= 0)
                    {
                        Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (estCells.TryGetValue(f.FullPath, out var tb)) tb.Text = "?";
                        }));
                        continue;
                    }

                    Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        int encIdx = encCombo.SelectedIndex;
                        string encoder = encIdx == 1 ? "qsv" : encIdx == 2 ? "nvenc" : "cpu";
                        int q = int.TryParse(crfBox.Text, out var qv) ? qv : 24;
                        int w = v.Width ?? 1920, h = v.Height ?? 1080;
                        string mh = maxHeight.SelectedItem?.ToString() ?? "Keep original";
                        if (mh != "Keep original" && int.TryParse(mh, out var mhv) && h > mhv)
                        { w = (int)(w * ((double)mhv / h)); h = mhv; }
                        double vkbps = Fmt.EstimateVideoKbps(q, w, h, v.CodecName ?? "", encoder);
                        int audCount = probe.Streams.Count(s => s.CodecType == "audio");
                        double akbps = audCount * int.Parse(audioBr.SelectedItem?.ToString() ?? "192");
                        double bytes = (vkbps + akbps) * 1000 / 8 * probe.Format.Duration;
                        int pct = f.Size > 0 ? (int)((1 - bytes / f.Size) * 100) : 0;
                        if (estCells.TryGetValue(f.FullPath, out var tb))
                        {
                            tb.Inlines.Clear();
                            tb.Inlines.Add(new System.Windows.Documents.Run(Fmt.Bytes((long)bytes)) { FontWeight = FontWeights.SemiBold });
                            tb.Inlines.Add(new System.Windows.Documents.Run($"  {(pct >= 0 ? "-" : "+")}{Math.Abs(pct)}%")
                            {
                                Foreground = pct >= 5 ? Ui.Res("GoodBrush") : (pct <= -5 ? Ui.Res("WarnBrush") : Ui.Res("MutedBrush"))
                            });
                        }
                        totalOut += (long)bytes;
                        done++;
                        if (done == queue.Count)
                        {
                            int spct = totalIn > 0 ? (int)((1 - (double)totalOut / totalIn) * 100) : 0;
                            estValue.Inlines.Clear();
                            estValue.Inlines.Add(new System.Windows.Documents.Run(Fmt.Bytes(totalOut)) { FontWeight = FontWeights.SemiBold });
                            estValue.Inlines.Add(new System.Windows.Documents.Run($"  {(spct >= 0 ? "-" : "+")}{Math.Abs(spct)}%")
                            {
                                Foreground = spct >= 5 ? Ui.Res("GoodBrush") : (spct <= -5 ? Ui.Res("WarnBrush") : Ui.Res("MutedBrush"))
                            });
                        }
                    }));
                }
                catch { }
            }
        });

        return Ui.Panel(root);
    }

    private static Grid KV(string k, string v)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Margin = new Thickness(0, 0, 0, 4);
        var lbl = Ui.Txt(k, 12, Ui.Res("MutedBrush"));
        var val = Ui.Txt(v, 13, weight: FontWeights.SemiBold);
        Grid.SetColumn(lbl, 0); Grid.SetColumn(val, 1);
        g.Children.Add(lbl); g.Children.Add(val);
        return g;
    }
}
