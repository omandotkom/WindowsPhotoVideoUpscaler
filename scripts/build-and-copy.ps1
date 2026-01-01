param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputRoot = "dist",
    [string]$ArchiveDrop = "Z:\\D\\Upscaler"
)

$ErrorActionPreference = "Stop"

function Require-Command([string]$name) {
    if (-not (Get-Command $name -ErrorAction SilentlyContinue)) {
        throw "Required command not found: $name"
    }
}

$repoRoot = Resolve-Path "$PSScriptRoot\\.."
$project = Join-Path $repoRoot "Upscaler.App\\Upscaler.App.csproj"
$publishDir = Join-Path $repoRoot "Upscaler.App\\bin\\$Configuration\\net10.0-windows\\$Runtime\\publish"
$staging = Join-Path $repoRoot "$OutputRoot\\Upscaler-$Runtime"
$zipPath = Join-Path $repoRoot "$OutputRoot\\Upscaler-$Runtime.zip"

try {
    Require-Command "dotnet"

    if (Test-Path $staging) {
        Remove-Item $staging -Recurse -Force
    }
    if (Test-Path $zipPath) {
        Remove-Item $zipPath -Force
    }

    dotnet publish $project `
        -c $Configuration `
        -r $Runtime `
        --self-contained false `
        /p:PublishSingleFile=true `
        /p:IncludeNativeLibrariesForSelfExtract=true `
        /p:PublishReadyToRun=false

    New-Item -ItemType Directory -Path $staging -Force | Out-Null
    Copy-Item "$publishDir\\*" $staging -Recurse -Force

    Compress-Archive -Path "$staging\\*" -DestinationPath $zipPath
    Write-Host "Release package created at $zipPath"

    New-Item -ItemType Directory -Path $ArchiveDrop -Force | Out-Null
    Copy-Item $zipPath -Destination $ArchiveDrop -Force
    Write-Host "Copied release package to $ArchiveDrop"
}
catch {
    Write-Host "Build failed: $($_.Exception.Message)"
}

Write-Host ""
Write-Host "Press any key to close..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
