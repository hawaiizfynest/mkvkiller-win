# MKVKiller (Windows)

[![Build](https://github.com/HawaiizFynest/mkvkiller-win/actions/workflows/build.yml/badge.svg)](https://github.com/HawaiizFynest/mkvkiller-win/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-3ecf8e)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%20x64-0078D4?logo=windows&logoColor=white)](https://github.com/HawaiizFynest/mkvkiller-win/releases)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![ffmpeg](https://img.shields.io/badge/ffmpeg-bundled-007808?logo=ffmpeg&logoColor=white)](https://ffmpeg.org)
[![Intel QSV](https://img.shields.io/badge/Intel-QuickSync-0071C5?logo=intel&logoColor=white)](https://www.intel.com/content/www/us/en/architecture-and-technology/quick-sync-video/quick-sync-video-general.html)
[![NVIDIA NVENC](https://img.shields.io/badge/NVIDIA-NVENC-76B900?logo=nvidia&logoColor=white)](https://developer.nvidia.com/nvidia-video-codec-sdk)

Native Windows desktop app for converting MKV/MP4 files with per-track control, batch queue, resumable encoding, persistent log, and optional hardware acceleration. Companion app to [MKVForge/MKVKiller (web)](https://github.com/HawaiizFynest/mkvforge). Works with **local drives and network shares** (SMB/UNC paths like `\\10.0.0.19\Multimedia`) for both input and output.

## Features

- **Browse local drives and network shares** — including UNC paths (`\\server\share`) and mapped drives.
- **Per-track selection** — video, audio, subtitles with codec, language, channel, and bitrate shown.
- **Content-aware presets** — Light / Balanced / Shrink hard / 1080p downscale based on source codec.
- **Live size estimation** — predicted output size updates as you tweak settings.
- **Batch queue** — check files in sidebar, configure shared settings, submit all at once with per-file estimates.
- **Resumable encoding** — segment-based encoding survives crashes, reboots, and app closures.
- **Persistent log** — SQLite history survives restarts. Stats, searchable ffmpeg logs, space-saved totals.
- **Hardware encoding** — auto-detects Intel QSV and NVIDIA NVENC. Falls back to CPU x264 on unsupported hardware.
- **Replace original** — optional deletion of source after successful conversion.
- **Color themes** — Blue, Red, Green accent colors. Preference persists across sessions.
- **Sort by name or size** — find biggest files to compress first.
- **Bundled ffmpeg** — zero setup, works offline after install.

## Installation

Download the latest `MKVKiller-win-x64.zip` from [Releases](https://github.com/HawaiizFynest/mkvkiller-win/releases), unzip anywhere, and run `MKVKiller.exe`. No installer, no Admin rights needed.

App data (preferences, conversion log, resume scratch) is stored in `%APPDATA%\MKVKiller`.

## Usage

1. **Set source folder** — type/paste a path or use the 📁 button. Works with local paths (`C:\Videos`), mapped drives (`Z:\`), and UNC paths (`\\10.0.0.19\Multimedia`).
2. **Set output folder** — defaults to `Videos\MKVKiller`. Change via 📁 to write to a network share.
3. **Pick a file** — click a filename in the list to inspect its tracks.
4. **Choose settings** — pick a preset (Balanced is applied by default) or manually tweak quality/preset/audio/subtitles.
5. **Start** — click **Start Conversion**. Watch progress in the **Jobs** tab.

### Batch Convert

Check the green boxes next to multiple files to queue them, click the **Batch** button that appears, configure shared settings, and submit them all at once. Jobs run sequentially by default.

### Network Drives

MKVKiller works with any path Windows can see:

| Type | Example |
|---|---|
| Local | `C:\Videos` |
| Mapped drive | `Z:\Multimedia` |
| UNC path | `\\10.0.0.19\Multimedia` |
| NAS via IP | `\\qnap-nas\share` |

For your QNAP TS-h973AX, use `\\10.0.0.19\Multimedia` as the source folder, and set output to another share or a local drive.

**Tip:** Converting TO a network drive is slower than converting to local disk, because ffmpeg writes continuously during encode. If you're CPU-bound (typical on older hardware), the network is usually not the bottleneck. If you're using NVENC and doing 30x realtime, it can saturate Gigabit.

## Hardware Encoders

On startup, the app runs a 1-frame test encode with each supported encoder. The results show as pills in the header — green means available.

| Encoder | Requirement | Speed (1080p HEVC→H.264) | File Size |
|---|---|---|---|
| CPU (x264) | Always works | 0.5–2× realtime | ✅ Smallest |
| Intel QSV | Intel iGPU Gen 8+ | 8–20× realtime | ~20% larger |
| NVIDIA NVENC | Turing+ GPU | 20–50× realtime | ~15% larger |

QSV and NVENC are auto-detected; no driver install needed beyond your normal GPU drivers.

## Resumable Encoding

Enable the **Resumable encoding** checkbox for long files or unreliable environments:

1. File is encoded in 10-minute segments into `%APPDATA%\MKVKiller\scratch\{jobId}\`.
2. If the app closes mid-encode, those jobs are marked `interrupted` in the log.
3. On next launch, they automatically resume at the first incomplete segment.
4. Segments are concatenated losslessly into the final MP4 when done.

**Note:** Subtitles are dropped in resumable mode (segment concat with subs is fragile). Use non-resumable mode if you need soft subs.

## Log Tab

Every conversion (success, failure, or cancellation) is logged to a SQLite database in `%APPDATA%\MKVKiller\mkvkiller.db`. The Log tab shows:

- Stat cards: total, successful, failed, interrupted
- Total space saved running tally
- Scrollable history (last 200 jobs)
- Click any row to see full ffmpeg output, source/output paths, timestamps

## Building from Source

Requires .NET 8 SDK and PowerShell.

```powershell
git clone https://github.com/HawaiizFynest/mkvkiller-win.git
cd mkvkiller-win
.\build.ps1
```

`build.ps1` downloads the ffmpeg essentials build from gyan.dev, then runs `dotnet publish` with `--self-contained true`. Output lands in `publish\`.

## Settings Reference

**CPU mode (CRF):**
- 18–20: visually lossless, archival
- 23: streaming quality
- 26+: small files

**HW mode (CQ):** Similar scale but values 2–3 lower than CRF for equivalent quality.

**Presets:** `slow` + CRF 20 is the best size/quality tradeoff. Use `medium` for 2× encode speed at small size cost. `veryfast` if you just need fast.

## License

MIT
