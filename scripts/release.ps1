param(
    [ValidateSet("patch","minor","major")]
    [string]$Bump = "patch",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path "$PSScriptRoot\.."

& "$PSScriptRoot\build-release.ps1" -Runtime $Runtime -Bump $Bump
