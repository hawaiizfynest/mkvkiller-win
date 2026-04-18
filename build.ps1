# MKVKiller build script
# Downloads ffmpeg and ffprobe, then builds the WPF app in Release mode.

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$ffmpegDir = Join-Path $root "MKVKiller\ffmpeg"

Write-Host "=== MKVKiller Build Script ===" -ForegroundColor Cyan

# 1. Download ffmpeg (gyan.dev essentials build - small, Windows x64)
if (-not (Test-Path (Join-Path $ffmpegDir "ffmpeg.exe"))) {
    Write-Host "Downloading ffmpeg..." -ForegroundColor Yellow
    $tmp = Join-Path $env:TEMP "mkvkiller-ffmpeg"
    New-Item -ItemType Directory -Force -Path $tmp | Out-Null
    $zipPath = Join-Path $tmp "ffmpeg.zip"

    # gyan.dev "essentials" build - about 35 MB compressed
    $url = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip"
    Write-Host "  From: $url" -ForegroundColor Gray
    Invoke-WebRequest -Uri $url -OutFile $zipPath -UseBasicParsing

    Write-Host "Extracting..." -ForegroundColor Yellow
    Expand-Archive -Path $zipPath -DestinationPath $tmp -Force

    # Find ffmpeg.exe inside extracted folder
    $exe = Get-ChildItem -Path $tmp -Filter "ffmpeg.exe" -Recurse | Select-Object -First 1
    $probe = Get-ChildItem -Path $tmp -Filter "ffprobe.exe" -Recurse | Select-Object -First 1
    if (-not $exe -or -not $probe) { throw "ffmpeg/ffprobe not found in download" }

    New-Item -ItemType Directory -Force -Path $ffmpegDir | Out-Null
    Copy-Item $exe.FullName (Join-Path $ffmpegDir "ffmpeg.exe") -Force
    Copy-Item $probe.FullName (Join-Path $ffmpegDir "ffprobe.exe") -Force

    Remove-Item $tmp -Recurse -Force
    Write-Host "ffmpeg installed to $ffmpegDir" -ForegroundColor Green
} else {
    Write-Host "ffmpeg already present, skipping download" -ForegroundColor Gray
}

# 2. Build in Release mode
Write-Host "Building MKVKiller (Release, win-x64)..." -ForegroundColor Yellow
Set-Location $root
dotnet publish MKVKiller\MKVKiller.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o publish

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Write-Host ""
Write-Host "=== Build complete ===" -ForegroundColor Green
Write-Host "Output: $(Join-Path $root 'publish')" -ForegroundColor Cyan
Write-Host "Run: $(Join-Path $root 'publish\MKVKiller.exe')" -ForegroundColor Cyan
