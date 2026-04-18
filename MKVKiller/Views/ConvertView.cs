using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MKVKiller.Models;
using MKVKiller.Services;

namespace MKVKiller.Views;

internal static class ConvertView
{
    public static UIElement Build(FileListItem? file, ProbeResult? probe, string outputFolder)
    {
        if (file == null || probe == null)
        {
            return Ui.Panel(new TextBlock
            {
                Text = "Click a file in the list to inspect tracks. Use the green checkboxes to queue multiple files.",
                Foreground = Ui.Res("MutedBrush"),
                TextAlignment = TextAlignment.Center,
                Padding = new Thickness(20),
                FontSize = 13
            });
        }

        var root = new StackPanel();
        var v = probe.Streams.FirstOrDefault(s => s.CodecType == "video");
        string srcCodec = v?.CodecName ?? "?";

        root.Children.Add(Ui.Txt(file.Name, 16, weight: FontWeights.SemiBold));
        var info = $"{Fmt.Bytes(probe.Format.Size)} · {Fmt.Duration(probe.Format.Duration)} · codec: {srcCodec}";
        if (v?.Width > 0) info += $" · {v.Width}×{v.Height}";
        root.Children.Add(Ui.Txt(info, 11, Ui.Res("MutedBrush")));

        var audStreams = probe.Streams.Where(s => s.CodecType == "audio").ToList();
        if (audStreams.Count > 0)
        {
            var audSummary = "Source audio: " + string.Join(", ", audStreams.Select(s => $"{s.CodecName}/{s.Channels ?? 0}ch ({(s.BitRate > 0 ? $"{s.BitRate / 1000} kbps" : "?")})"));
            root.Children.Add(Ui.Txt(audSummary, 11, Ui.Res("MutedBrush")));
        }

        // Encoder selector
        root.Children.Add(Ui.H2("Encoder"));
        var caps = EncoderDetection.Current;
        var encCombo = new ComboBox { Margin = new Thickness(0, 0, 0, 8) };
        encCombo.Items.Add("CPU (x264) — best quality");
        var qsvItem = new ComboBoxItem { Content = "Intel QSV — fast, iGPU" };
        qsvItem.IsEnabled = caps.Qsv; encCombo.Items.Add(qsvItem);
        var nvItem = new ComboBoxItem { Content = "NVIDIA NVENC — fast, dGPU" };
        nvItem.IsEnabled = caps.Nvenc; encCombo.Items.Add(nvItem);
        encCombo.SelectedIndex = 0;
        root.Children.Add(encCombo);

        // Recommended presets
        root.Children.Add(Ui.H2("Recommended settings"));
        var presetPanel = new WrapPanel();
        bool isHevc = srcCodec == "hevc" || srcCodec == "h265";
        var recs = isHevc
            ? new[] {
                ("Archival", 20, "slow", 0),
                ("Balanced", 23, "slow", 0),
                ("Shrink hard", 25, "slow", 0)
            }
            : new[] {
                ("Light", 22, "slow", 0),
                ("Balanced", 24, "slow", 0),
                ("Shrink hard", 26, "slow", 0)
            };

        // Settings fields (declared first so preset buttons can update them)
        var crfBox = new TextBox { Text = isHevc ? "23" : "24", Width = 80 };
        var hwQBox = new TextBox { Text = "23", Width = 80 };
        var presetCombo = Ui.Combo(new[] { "ultrafast", "superfast", "veryfast", "faster", "fast", "medium", "slow", "slower", "veryslow" }, "slow");
        var audioMode = Ui.Combo(new[] { "auto", "copy", "aac" }, "auto");
        var maxCh = audStreams.Count > 0 ? audStreams.Max(s => s.Channels ?? 0) : 0;
        var recAudio = Fmt.SnapAudioBitrate(Fmt.RecommendAudioKbps(maxCh));
        var audioBr = Ui.Combo(new[] { "128", "192", "256", "384" }, recAudio.ToString());
        var subMode = Ui.Combo(new[] { "soft", "burn", "none" }, "soft");
        var maxH = Ui.Combo(new[] { "Keep original", "2160", "1080", "720", "480" }, "Keep original");
        var outName = new TextBox { Text = Path.ChangeExtension(file.Name, ".mp4") };

        foreach (var (name, crf, preset, _) in recs)
        {
            var btn = new Button
            {
                Content = $"{name} · CRF {crf}",
                Style = (Style)Application.Current.FindResource("GhostButton"),
                Padding = new Thickness(10, 6, 10, 6),
                FontSize = 12,
                Margin = new Thickness(0, 0, 6, 6)
            };
            btn.Click += (_, _) =>
            {
                crfBox.Text = crf.ToString();
                hwQBox.Text = crf.ToString();
                presetCombo.SelectedItem = preset;
            };
            presetPanel.Children.Add(btn);
        }
        root.Children.Add(presetPanel);

        // Tracks
        root.Children.Add(Ui.H2("Tracks (check to keep)"));
        var trackPanel = new StackPanel();
        var trackCheckboxes = new List<(CheckBox cb, MediaStream stream)>();
        foreach (var s in probe.Streams)
        {
            string info2 = $"{s.CodecName}";
            if (s.CodecType == "video" && s.Width > 0) info2 += $" · {s.Width}×{s.Height}";
            if (s.CodecType == "audio")
            {
                info2 += $" · {s.Channels ?? 0}ch";
                if (!string.IsNullOrEmpty(s.ChannelLayout)) info2 += $" {s.ChannelLayout}";
                if (s.BitRate > 0) info2 += $" · {s.BitRate / 1000} kbps";
            }
            info2 += $" · lang: {s.Language}";
            if (!string.IsNullOrEmpty(s.Title)) info2 += $" · \"{s.Title}\"";
            if (s.IsDefault) info2 += " · default";

            bool def = s.CodecType == "video" || (s.CodecType == "audio" && (s.IsDefault || s.Language == "eng"))
                       || (s.CodecType == "subtitle" && s.IsDefault);

            var row = new Border
            {
                Background = Ui.Res("PanelBrush"),
                BorderBrush = Ui.Res("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 6)
            };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var cb = new CheckBox { IsChecked = def, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            Grid.SetColumn(cb, 0);
            g.Children.Add(cb);

            var badge = new Border
            {
                Padding = new Thickness(7, 2, 7, 2),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Background = s.CodecType switch
                {
                    "video" => new SolidColorBrush(Color.FromRgb(58, 43, 94)),
                    "audio" => new SolidColorBrush(Color.FromRgb(30, 74, 58)),
                    "subtitle" => new SolidColorBrush(Color.FromRgb(94, 58, 43)),
                    _ => new SolidColorBrush(Color.FromRgb(58, 58, 58))
                },
                Child = new TextBlock
                {
                    Text = s.CodecType ?? "?",
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = s.CodecType switch
                    {
                        "video" => new SolidColorBrush(Color.FromRgb(201, 177, 255)),
                        "audio" => new SolidColorBrush(Color.FromRgb(156, 229, 193)),
                        "subtitle" => new SolidColorBrush(Color.FromRgb(255, 201, 161)),
                        _ => new SolidColorBrush(Color.FromRgb(204, 204, 204))
                    }
                }
            };
            Grid.SetColumn(badge, 1); g.Children.Add(badge);

            var metaTb = new TextBlock
            {
                Text = $"#{s.Index} {info2}",
                Foreground = Ui.Res("MutedBrush"),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(metaTb, 2); g.Children.Add(metaTb);

            row.Child = g;
            trackPanel.Children.Add(row);
            trackCheckboxes.Add((cb, s));
        }
        root.Children.Add(trackPanel);

        // Settings grid
        var settings = Ui.Grid2(
            ("Quality (CRF)", crfBox),
            ("Preset", presetCombo),
            ("Audio mode", audioMode),
            ($"Audio bitrate (rec {recAudio}k for {maxCh}ch)", audioBr),
            ("Subtitles", subMode),
            ("Max height", maxH),
            ("Output filename", outName),
            ("HW quality (CQ)", hwQBox)
        );
        settings.Margin = new Thickness(0, 14, 0, 0);
        root.Children.Add(settings);

        // Options
        var resumableCb = new CheckBox
        {
            Content = new TextBlock { Inlines = { new System.Windows.Documents.Run("Resumable encoding ") { FontWeight = FontWeights.SemiBold, Foreground = Ui.Res("Accent2Brush") }, new System.Windows.Documents.Run("— segment-based, survives restarts; drops subtitles") { Foreground = Ui.Res("MutedBrush"), FontSize = 11 } } },
            Margin = new Thickness(0, 14, 0, 0)
        };
        var replaceCb = new CheckBox
        {
            Content = new TextBlock { Inlines = { new System.Windows.Documents.Run("Replace original file ") { FontWeight = FontWeights.SemiBold, Foreground = Ui.Res("ErrBrush") }, new System.Windows.Documents.Run("— deletes the source after successful conversion") { Foreground = Ui.Res("MutedBrush"), FontSize = 11 } } },
            Margin = new Thickness(0, 8, 0, 0)
        };
        root.Children.Add(resumableCb);
        root.Children.Add(replaceCb);

        // Estimate box
        var estBox = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(26, 37, 56)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(45, 64, 102)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(0, 14, 0, 0)
        };
        var estContent = new StackPanel();
        estBox.Child = estContent;
        root.Children.Add(estBox);

        void UpdateEstimate()
        {
            var keptVideo = trackCheckboxes.FirstOrDefault(t => t.stream.CodecType == "video" && t.cb.IsChecked == true);
            if (keptVideo.stream == null || probe.Format.Duration <= 0)
            {
                estContent.Children.Clear();
                estContent.Children.Add(Ui.Txt("Select a video track to see estimate.", 12, Ui.Res("MutedBrush")));
                return;
            }
            int encIdx = encCombo.SelectedIndex;
            string encoder = encIdx == 1 ? "qsv" : encIdx == 2 ? "nvenc" : "cpu";
            int crf = int.TryParse(encoder == "cpu" ? crfBox.Text : hwQBox.Text, out var c) ? c : 23;
            int w = keptVideo.stream.Width ?? 1920;
            int h = keptVideo.stream.Height ?? 1080;
            string mh = maxH.SelectedItem?.ToString() ?? "Keep original";
            if (mh != "Keep original" && int.TryParse(mh, out var mhv) && h > mhv)
            {
                w = (int)(w * ((double)mhv / h)); h = mhv;
            }
            double vkbps = Fmt.EstimateVideoKbps(crf, w, h, keptVideo.stream.CodecName ?? "", encoder);
            int abKbps = int.Parse(audioBr.SelectedItem?.ToString() ?? "192");
            var keptAudio = trackCheckboxes.Where(t => t.stream.CodecType == "audio" && t.cb.IsChecked == true).ToList();
            double akbps = keptAudio.Count * abKbps;
            if (audioMode.SelectedItem?.ToString() == "copy")
            {
                akbps = keptAudio.Sum(t => t.stream.BitRate > 0 ? t.stream.BitRate / 1000.0 : abKbps);
            }
            double totalBytes = (vkbps + akbps) * 1000 / 8 * probe.Format.Duration;
            double delta = probe.Format.Size > 0 ? (1 - totalBytes / probe.Format.Size) * 100 : 0;

            estContent.Children.Clear();
            var headerRow = new Grid();
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var lbl = Ui.Txt("Estimated output size", 12, Ui.Res("MutedBrush"));
            var val = new TextBlock
            {
                FontSize = 14, FontWeight = FontWeights.SemiBold,
                Inlines = {
                    new System.Windows.Documents.Run(Fmt.Bytes((long)totalBytes)),
                    new System.Windows.Documents.Run($"  {(delta >= 0 ? "-" : "+")}{Math.Abs(delta):F0}%")
                        { Foreground = delta >= 5 ? Ui.Res("GoodBrush") : (delta <= -5 ? Ui.Res("WarnBrush") : Ui.Res("MutedBrush")), FontSize = 12 }
                }
            };
            Grid.SetColumn(lbl, 0); Grid.SetColumn(val, 1);
            headerRow.Children.Add(lbl); headerRow.Children.Add(val);
            estContent.Children.Add(headerRow);

            var srcRow = new Grid();
            srcRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            srcRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            srcRow.Margin = new Thickness(0, 4, 0, 0);
            var srcL = Ui.Txt("Source size", 12, Ui.Res("MutedBrush"));
            var srcV = Ui.Txt(Fmt.Bytes(probe.Format.Size), 13, Ui.Res("MutedBrush"));
            Grid.SetColumn(srcL, 0); Grid.SetColumn(srcV, 1);
            srcRow.Children.Add(srcL); srcRow.Children.Add(srcV);
            estContent.Children.Add(srcRow);

            var brRow = new Grid();
            brRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            brRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            brRow.Margin = new Thickness(0, 4, 0, 0);
            var brL = Ui.Txt("Target bitrate", 12, Ui.Res("MutedBrush"));
            var brV = Ui.Txt($"{(vkbps / 1000):F1} Mbps + {(int)akbps} kbps audio", 12);
            Grid.SetColumn(brL, 0); Grid.SetColumn(brV, 1);
            brRow.Children.Add(brL); brRow.Children.Add(brV);
            estContent.Children.Add(brRow);

            if (delta < 0)
                estContent.Children.Add(Ui.Txt("⚠ Output will be larger than source.", 11, Ui.Res("WarnBrush")));
            else if (delta < 10)
                estContent.Children.Add(Ui.Txt("⚠ Minor savings only.", 11, Ui.Res("WarnBrush")));
        }

        // Wire up change events
        crfBox.TextChanged += (_, _) => UpdateEstimate();
        hwQBox.TextChanged += (_, _) => UpdateEstimate();
        encCombo.SelectionChanged += (_, _) =>
        {
            bool cpu = encCombo.SelectedIndex == 0;
            // Could swap CRF/CQ label, but simple: just recalc
            UpdateEstimate();
        };
        audioMode.SelectionChanged += (_, _) => UpdateEstimate();
        audioBr.SelectionChanged += (_, _) => UpdateEstimate();
        maxH.SelectionChanged += (_, _) => UpdateEstimate();
        foreach (var (cb, _) in trackCheckboxes) cb.Click += (_, _) => UpdateEstimate();
        UpdateEstimate();

        // Start button
        var startBtn = Ui.Btn("Start Conversion", async (_, _) =>
        {
            if (replaceCb.IsChecked == true &&
                MessageBox.Show($"This will DELETE the original file after conversion:\n{file.Name}\n\nContinue?",
                    "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            if (string.IsNullOrWhiteSpace(outputFolder) || !Directory.Exists(outputFolder))
            {
                try { Directory.CreateDirectory(outputFolder); }
                catch { MessageBox.Show("Output folder invalid or not writable: " + outputFolder); return; }
            }

            int encIdx = encCombo.SelectedIndex;
            string encoder = encIdx == 1 ? "qsv" : encIdx == 2 ? "nvenc" : "cpu";
            var selected = trackCheckboxes.Where(t => t.cb.IsChecked == true).Select(t => t.stream.Index).ToList();
            if (!selected.Any(i => probe.Streams.Any(s => s.Index == i && s.CodecType == "video")))
            {
                MessageBox.Show("At least one video track required."); return;
            }

            var job = new ConversionJob
            {
                InputPath = file.FullPath,
                OutputPath = Path.Combine(outputFolder, outName.Text),
                InputSize = probe.Format.Size,
                Duration = probe.Format.Duration,
                SelectedStreams = selected,
                ReplaceOriginal = replaceCb.IsChecked == true,
                Resumable = resumableCb.IsChecked == true,
                Options = new EncodeOptions
                {
                    Encoder = encoder,
                    Crf = int.TryParse(crfBox.Text, out var ci) ? ci : 23,
                    HwQuality = int.TryParse(hwQBox.Text, out var hqi) ? hqi : 23,
                    Preset = presetCombo.SelectedItem?.ToString() ?? "slow",
                    AudioMode = audioMode.SelectedItem?.ToString() ?? "auto",
                    AudioBitrate = (audioBr.SelectedItem?.ToString() ?? "192") + "k",
                    SubtitleMode = subMode.SelectedItem?.ToString() ?? "soft",
                    MaxHeight = (maxH.SelectedItem?.ToString() == "Keep original") ? null : (int?)int.Parse(maxH.SelectedItem!.ToString()!)
                }
            };
            await JobManager.Instance.SubmitAsync(job);
        });
        startBtn.Margin = new Thickness(0, 16, 0, 0);
        var startWrap = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
        startWrap.Children.Add(startBtn);
        root.Children.Add(startWrap);

        return Ui.Panel(root);
    }
}
