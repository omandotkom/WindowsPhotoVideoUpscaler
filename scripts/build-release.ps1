param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputRoot = "dist",
    [bool]$SelfContained = $true,
    [ValidateSet("patch","minor","major")]
    [string]$Bump = "patch"
)

$ErrorActionPreference = "Stop"

function Require-Command([string]$name) {
    if (-not (Get-Command $name -ErrorAction SilentlyContinue)) {
        throw "Required command not found: $name"
    }
}

function Get-Version([string]$csprojPath) {
    $content = Get-Content $csprojPath -Raw
    if ($content -match "<Version>([^<]+)</Version>") {
        return $Matches[1].Trim()
    }
    throw "Version tag not found in $csprojPath"
}

function Set-Version([string]$csprojPath, [string]$newVersion) {
    $content = Get-Content $csprojPath -Raw
    $updated = $content -replace "<Version>[^<]+</Version>", "<Version>$newVersion</Version>"
    if ($content -eq $updated) {
        throw "Failed to update Version in $csprojPath"
    }
    Set-Content -Path $csprojPath -Value $updated -Encoding UTF8
}

function Bump-Version([string]$version, [string]$kind) {
    $parts = $version.Split(".")
    if ($parts.Length -lt 3) {
        throw "Version must be in major.minor.patch format. Found: $version"
    }
    $major = [int]$parts[0]
    $minor = [int]$parts[1]
    $patch = [int]$parts[2]

    switch ($kind) {
        "major" { $major++; $minor = 0; $patch = 0 }
        "minor" { $minor++; $patch = 0 }
        "patch" { $patch++ }
    }

    return "$major.$minor.$patch"
}

$repoRoot = Resolve-Path "$PSScriptRoot\.."
$project = Join-Path $repoRoot "Upscaler.App\Upscaler.App.csproj"
$publishDir = Join-Path $repoRoot "Upscaler.App\bin\$Configuration\net10.0-windows\$Runtime\publish"
$staging = Join-Path $repoRoot "$OutputRoot\Upscaler-$Runtime"
$zipPath = Join-Path $repoRoot "$OutputRoot\Upscaler-$Runtime.zip"

$doRelease = $false
$confirm = Read-Host "Upload release to GitHub? (Y/N)"
if ($confirm -match "^[Yy]$") {
    $doRelease = $true
}

if ($doRelease) {
    Require-Command "git"
    Require-Command "gh"
    $current = Get-Version $project
    $next = Bump-Version $current $Bump
    Write-Host "Version: $current -> $next"
    Set-Version $project $next
    git add $project
    git commit -m "Bump version to $next"
}

if (Test-Path $staging) {
    Remove-Item $staging -Recurse -Force
}
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

dotnet publish $project `
    -c $Configuration `
    -r $Runtime `
    --self-contained $SelfContained `
    /p:PublishSingleFile=true `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    /p:PublishReadyToRun=false

New-Item -ItemType Directory -Path $staging -Force | Out-Null
Copy-Item "$publishDir\*" $staging -Recurse -Force

Compress-Archive -Path "$staging\*" -DestinationPath $zipPath
Write-Host "Release package created at $zipPath"

$archiveDrop = "Z:\D\Upscaler"
New-Item -ItemType Directory -Path $archiveDrop -Force | Out-Null
Copy-Item $zipPath -Destination $archiveDrop -Force
Write-Host "Copied release package to $archiveDrop"

if ($doRelease) {
    $tag = "v$next"
    git tag $tag
    git push
    git push --tags
    gh release create $tag $zipPath --title "Upscaler $tag" --notes "Release $tag"
    Write-Host "Release created: $tag"
}

Write-Host ""
Write-Host "Press any key to close..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
