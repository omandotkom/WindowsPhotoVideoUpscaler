param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path "$PSScriptRoot\.."
$project = Join-Path $repoRoot "Upscaler.App\Upscaler.App.csproj"

dotnet run --project $project -c $Configuration

Write-Host ""
Write-Host "Press any key to close..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
