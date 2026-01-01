# Windows Photo & Video Upscaler

Prototype Windows photo/video upscaler built with WPF, ONNX Runtime, and DirectML.

## Features
- WPF desktop UI with drag-and-drop, Open Files/Folder, recursive scan, and recent outputs list.
- Model ComboBox from `models.json` with download/redownload, resume support, SHA256 validation, and metadata JSON.
- DirectML GPU inference with device name shown and CPU fallback when unavailable.
- Preview on a small center crop with dimension readout and suggested scale (x2/x4).
- Configurable scale, mode, quality preset, denoise/temporal smoothing, tiling, output format, and JPEG quality.
- Batch processing with per-tile progress, ETA, cancel, and optional duplicate skipping by SHA256.

## Image upscaling details
- Supported input formats: jpg, jpeg, png, webp, bmp, tiff.
- Preview uses a center crop (up to 256x256) and writes preview output to `AppData/cache/`.
- Tiling auto-sizes based on image resolution; manual tile size/overlap available.
- Weighted seam blending reduces tile edges; overlap scales with output.
- Output formats: Original, PNG, JPEG, BMP, TIFF with optional EXIF copy for JPEG/TIFF.

## Video upscaling details
- Supported formats: mp4, mov, mkv, avi, webm, 3gp (requires FFmpeg).
- FFmpeg is auto-downloaded on first launch into `AppData/ffmpeg/` with a progress popup.
- Videos are decoded to frames in `AppData/cache/`, upscaled, then re-encoded with audio preserved when possible.
- Hardware decode option plus encoder choices (libx264, NVENC, AMF, QSV) with mpeg4 fallback.
- Progress is shown per phase: decode, upscale, encode.

## Model management
- Catalog source: `Upscaler.App/models.json` with name, version, scale, url, sha256, size, description, license.
- Downloads retry up to 3 times with exponential backoff and resume when supported.
- Zip models extract `.onnx` and optional `.data` weights into `AppData/models/`.
- A metadata JSON file is written alongside each downloaded model.

## Requirements
- Windows 10/11
- .NET 10 SDK
- Compatible GPU for DirectML (optional, CPU fallback supported)
- FFmpeg (optional, required for video upscaling)

## FFmpeg distribution
For public distribution, use an LGPL build of FFmpeg and bundle `ffmpeg.exe` and `ffprobe.exe` next to `Upscaler.App.exe`.
Avoid GPL builds unless you intend to release the entire app under GPL.

LGPL checklist:
- Include the FFmpeg license notice.
- Provide a link to the FFmpeg source used for the build.
- Allow users to replace the FFmpeg binaries (no locking/obfuscation).

See `THIRD_PARTY_NOTICES.txt` for the bundled FFmpeg notice.
On first launch, the app downloads an LGPL FFmpeg build into `AppData/ffmpeg/` if missing (shows a download popup).

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
- `AppData/ffmpeg/` bundled FFmpeg binaries

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

## Release packaging
To bundle FFmpeg, place `ffmpeg.exe` and `ffprobe.exe` in `ffmpeg/` at repo root
or pass `-FfmpegDir` to the build script:
```powershell
scripts/build-release.ps1 -FfmpegDir C:\path\to\ffmpeg
```
If no FFmpeg folder is present, the build script downloads an LGPL build from BtbN.

## Debug FFmpeg setup
To download and copy FFmpeg into the debug output folder:
```powershell
scripts/setup-ffmpeg-debug.ps1
```

## Usage
1) Drag-drop images or click Open Files/Folder.
   - Video files are supported (requires FFmpeg).
2) Select a model. If missing, click Download.
3) Choose scale, mode, output format, and JPEG quality.
4) Preview to test quality (uses a small center crop).
5) Upscale the full image set.

## Settings reference
- Scale: x2 or x4 (model scale overrides manual selection when fixed).
- Mode: Fast or Quality (affects default tile size in auto mode).
- Quality preset: "Clean + Sharp" enables denoise + temporal smoothing.
- Denoise: simple box blur pre-pass with strength slider (0 to 0.5 UI range).
- Temporal smoothing: blends with previous output frame/ image (useful for video).
- Tiling: Auto picks size by resolution; Advanced allows manual tile size/overlap.
- Output format: Original, PNG, JPEG, BMP, TIFF.
- Video options: hardware decode toggle and encoder selection.

## Output behavior
- Default output folder: folder of the selected file or folder.
- Batch runs (more than 1 file) output into a timestamp subfolder.
- Image output naming: `<original>_upscaled.{ext}` (Original preserves supported ext).
- Video output naming: `<original>_upscaled_x{scale}.{ext}` (3gp outputs as mp4).
- Mixed images and videos cannot be processed in a single run.

## Video acceleration
- Video decode defaults to CPU; you can enable hardware decode in Settings.
- Video encode defaults to CPU (libx264) with optional NVIDIA NVENC, AMD AMF, or Intel QSV.
- If a hardware encoder is unavailable, the app falls back to libx264 (or mpeg4 if libx264 is unavailable).

## EXIF handling
- EXIF is copied for JPEG and TIFF outputs using ExifLibrary.
- PNG/BMP do not preserve EXIF by default.

## Logs
Logs are written to:
`AppData/logs/app.log`

Events logged:
- App start/exit
- Model downloads
- Inference start/finish
- Errors

## Troubleshooting
- If DirectML is unavailable, the app falls back to CPU and logs a warning.
- If a model download fails due to SHA256 mismatch, use Redownload.
- If FFmpeg is missing, the app shows a download popup on launch and video upscaling is disabled until available.
- If FFmpeg is installed but not found, place `ffmpeg.exe`/`ffprobe.exe` next to the app or in `AppData/ffmpeg/`.
- If build fails because the app is running, close the app and rebuild.

## Manual smoke checklist
- Load an image from `samples/`.
- Run Preview.
- Run Upscale and confirm output file saved.

## No telemetry
This app does not collect telemetry.
