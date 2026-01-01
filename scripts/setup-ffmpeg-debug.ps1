param(
    [string]$Configuration = "Debug",
    [string]$FfmpegDir = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path "$PSScriptRoot\\.."
$projectDir = Join-Path $repoRoot "Upscaler.App"
$outputDir = Join-Path $projectDir "bin\\$Configuration\\net10.0-windows"

function Resolve-FfmpegDir([string]$dir) {
    if ($dir) {
        if (-not (Test-Path $dir)) {
            throw "FFmpeg directory not found: $dir"
        }
        return (Resolve-Path $dir).Path
    }

    $candidate = Join-Path $repoRoot "ffmpeg"
    if (Test-Path $candidate) {
        return (Resolve-Path $candidate).Path
    }

    return $null
}

function Ensure-Ffmpeg([string]$targetDir) {
    if (Test-Path $targetDir) {
        return
    }

    $api = "https://api.github.com/repos/BtbN/FFmpeg-Builds/releases/latest"
    $resp = Invoke-RestMethod -Uri $api -Headers @{ "User-Agent" = "Upscaler" }
    $asset = $resp.assets | Where-Object {
        $_.name -match "win64" -and $_.name -match "lgpl" -and $_.name -match "\\.zip$"
    } | Select-Object -First 1
    if (-not $asset) {
        throw "FFmpeg asset not found in latest release."
    }
    $url = $asset.browser_download_url
    $tempRoot = Join-Path $env:TEMP ("ffmpeg_" + [Guid]::NewGuid().ToString("N"))
    $zipPath = Join-Path $tempRoot "ffmpeg.zip"
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
    Write-Host "Downloading FFmpeg (LGPL) from $url"
    Invoke-WebRequest -Uri $url -OutFile $zipPath -UseBasicParsing
    Expand-Archive -Path $zipPath -DestinationPath $tempRoot -Force

    $exe = Get-ChildItem -Path $tempRoot -Recurse -Filter "ffmpeg.exe" | Select-Object -First 1
    if (-not $exe) {
        throw "ffmpeg.exe not found after download."
    }

    $binDir = $exe.Directory.FullName
    New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
    Copy-Item (Join-Path $binDir "*") $targetDir -Force
    Remove-Item $tempRoot -Recurse -Force
}

$ffmpegRoot = Resolve-FfmpegDir $FfmpegDir
if (-not $ffmpegRoot) {
    $autoDir = Join-Path $repoRoot "ffmpeg"
    Ensure-Ffmpeg $autoDir
    $ffmpegRoot = $autoDir
}

$ffmpegExe = Join-Path $ffmpegRoot "ffmpeg.exe"
$ffprobeExe = Join-Path $ffmpegRoot "ffprobe.exe"
if (-not (Test-Path $ffmpegExe)) {
    throw "ffmpeg.exe not found in $ffmpegRoot"
}
if (-not (Test-Path $ffprobeExe)) {
    throw "ffprobe.exe not found in $ffmpegRoot"
}

New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
Copy-Item (Join-Path $ffmpegRoot "*") $outputDir -Force
Write-Host "FFmpeg copied to $outputDir"
