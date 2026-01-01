# Windows Photo Upscaler

Prototype Windows photo upscaler built with WPF, ONNX Runtime, and DirectML.

## Features
- WPF desktop UI with drag-and-drop and file/folder picking.
- Model catalog + download with SHA256 verification and retry.
- DirectML GPU inference with CPU fallback.
- Preview on a small crop for quick quality checks.
- Tiling with overlap + per-tile progress.
- Output format and JPEG quality control.
- EXIF copy for JPEG/TIFF outputs.
- Daily rolling logs and AppData layout.

## Requirements
- Windows 10/11
- .NET 10 SDK
- Compatible GPU for DirectML (optional, CPU fallback supported)

## Repository layout
- `Upscaler.App/` WPF app code
- `Upscaler.App/models.json` model catalog
- `Upscaler.App/Assets/` UI assets (icon)
- `samples/` small test images (committed for smoke tests)

## AppData layout
Default base path is `<exe_dir>/AppData/`. If not writable, fallback is `%LOCALAPPDATA%/Upscaler/`.

Subfolders:
- `AppData/models/` downloaded models and metadata
- `AppData/cache/` preview outputs
- `AppData/logs/` application logs

## Model catalog
The app reads `Upscaler.App/models.json` to populate the model list.

Each model entry:
- `name`, `version`, `scale`, `url`, `sha256`, `size`, `description`, `license`

Current catalog includes the Qualcomm Real-ESRGAN x4plus ONNX zip. The download step:
- Verifies SHA256 of the zip.
- Extracts `.onnx` and any `.data` external weights into `AppData/models/`.
- Writes metadata JSON alongside the model file.

## Build
```powershell
dotnet build Upscaler.slnx
```

## Run
```powershell
dotnet run --project Upscaler.App/Upscaler.App.csproj
```

## Usage
1) Drag-drop images or click Open Files/Folder.
2) Select a model. If missing, click Download.
3) Choose scale, mode, output format, and JPEG quality.
4) Preview to test quality (uses a small center crop).
5) Upscale the full image set.

## Output behavior
- Default output folder: folder of the selected file or folder.
- Batch runs (more than 1 file) output into a timestamp subfolder.
- Default output naming: `<original>_upscaled.<ext>`.

## EXIF handling
- EXIF is copied for JPEG and TIFF outputs using ExifLibrary.
- PNG/BMP do not preserve EXIF by default.

## Logs
Logs are written to:
`AppData/logs/yyyy-MM-dd.log`

Events logged:
- App start/exit
- Model downloads
- Inference start/finish
- Errors

## Troubleshooting
- If DirectML is unavailable, the app falls back to CPU and logs a warning.
- If a model fails to load, use the Redownload button to refresh it.
- If build fails because the app is running, close the app and rebuild.

## Manual smoke checklist
- Load an image from `samples/`.
- Run Preview.
- Run Upscale and confirm output file saved.

## No telemetry
This app does not collect telemetry.
