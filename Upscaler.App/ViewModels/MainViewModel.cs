using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Upscaler.App.Infrastructure;
using Upscaler.App.Models;
using Upscaler.App.Processing;

namespace Upscaler.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private static readonly string[] SupportedImageExtensions = new[]
    {
        ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".tiff"
    };

    private static readonly string[] SupportedVideoExtensions = new[]
    {
        ".mp4", ".mov", ".mkv", ".avi", ".webm", ".3gp"
    };

    private readonly ModelCatalogService _catalogService = new();
    private readonly ModelDownloader _downloader = new();
    private CancellationTokenSource? _downloadCts;
    private CancellationTokenSource? _jobCts;

    private ModelDefinition? _selectedModel;
    private bool _isSelectedModelMissing;
    private string _modelStatus = "No model selected.";
    private ImageSource? _previewImage;
    private string _previewInfo = "No image loaded.";
    private string _suggestedScale = "Suggested: x2";
    private bool _isDownloading;
    private bool _isProcessing;
    private bool _isScanning;
    private double _downloadProgressPercent;
    private double _upscaleProgressPercent;
    private string _downloadProgressText = string.Empty;
    private string _jobProgressText = "Idle.";
    private string _deviceStatus = "GPU: detecting...";
    private string _outputFolder = AppPaths.OutputPath;
    private bool _skipDuplicates = true;
    private string _selectedOutputFormat = "Original";
    private int _jpegQuality = 100;
    private string _tileSizeText = "Auto";
    private string _tileOverlapText = "32";
    private bool _enableDenoise;
    private double _denoiseStrength = 0.12;
    private string _selectedQualityPreset = "Custom";
    private bool _enableTemporalBlend;
    private double _temporalBlendStrength = 0.15;
    private bool _useHardwareDecode;
    private double _videoDecodePercent;
    private double _videoUpscalePercent;
    private double _videoEncodePercent;
    private int _selectedScale = 2;

    public ObservableCollection<ModelDefinition> Models { get; } = new();
    public ObservableCollection<string> SelectedFiles { get; } = new();
    public ObservableCollection<string> RecentOutputs { get; } = new();

    public ObservableCollection<int> Scales { get; } = new() { 2, 4 };
    public ObservableCollection<string> Modes { get; } = new() { "Fast", "Quality" };
    public ObservableCollection<string> OutputFormats { get; } = new() { "Original", "Png", "Jpeg", "Bmp", "Tiff" };
    public ObservableCollection<string> VideoEncoders { get; } = new()
    {
        "CPU (libx264)",
        "NVIDIA NVENC (h264)",
        "AMD AMF (h264)",
        "Intel QSV (h264)"
    };
    public ObservableCollection<string> QualityPresets { get; } = new()
    {
        "Custom",
        "Clean + Sharp"
    };

    public int SelectedScale
    {
        get => _selectedScale;
        set
        {
            int resolved = value;
            if (SelectedModel?.Scale > 0)
            {
                resolved = SelectedModel.Scale;
            }

            if (_selectedScale != resolved)
            {
                _selectedScale = resolved;
                OnPropertyChanged();
            }
        }
    }
    public string SelectedMode { get; set; } = "Quality";
    public string SelectedVideoEncoder { get; set; } = "CPU (libx264)";

    public ModelDefinition? SelectedModel
    {
        get => _selectedModel;
        set
        {
            if (_selectedModel != value)
            {
                _selectedModel = value;
                OnPropertyChanged();
                UpdateModelStatus();
                SyncSelectedScaleToModel();
                UpdateActionState();
            }
        }
    }

    public bool IsSelectedModelMissing
    {
        get => _isSelectedModelMissing;
        private set
        {
            if (_isSelectedModelMissing != value)
            {
                _isSelectedModelMissing = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsSelectedModelAvailable));
            }
        }
    }

    public bool IsSelectedModelAvailable => !_isSelectedModelMissing;

    public string ModelStatus
    {
        get => _modelStatus;
        private set
        {
            if (_modelStatus != value)
            {
                _modelStatus = value;
                OnPropertyChanged();
            }
        }
    }

    public ImageSource? PreviewImage
    {
        get => _previewImage;
        private set
        {
            if (_previewImage != value)
            {
                _previewImage = value;
                OnPropertyChanged();
            }
        }
    }

    public string PreviewInfo
    {
        get => _previewInfo;
        private set
        {
            if (_previewInfo != value)
            {
                _previewInfo = value;
                OnPropertyChanged();
            }
        }
    }

    public string SuggestedScale
    {
        get => _suggestedScale;
        private set
        {
            if (_suggestedScale != value)
            {
                _suggestedScale = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsDownloading
    {
        get => _isDownloading;
        private set
        {
            if (_isDownloading != value)
            {
                _isDownloading = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsUiEnabled));
                UpdateActionState();
            }
        }
    }

    public bool IsProcessing
    {
        get => _isProcessing;
        private set
        {
            if (_isProcessing != value)
            {
                _isProcessing = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsUiEnabled));
                UpdateActionState();
            }
        }
    }

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (_isScanning != value)
            {
                _isScanning = value;
                OnPropertyChanged();
                UpdateActionState();
            }
        }
    }

    public bool IsUiEnabled => !IsProcessing && !IsDownloading;

    public string DeviceStatus
    {
        get => _deviceStatus;
        private set
        {
            if (_deviceStatus != value)
            {
                _deviceStatus = value;
                OnPropertyChanged();
            }
        }
    }

    public string OutputFolder
    {
        get => _outputFolder;
        set
        {
            if (_outputFolder != value)
            {
                _outputFolder = value;
                OnPropertyChanged();
            }
        }
    }

    public bool SkipDuplicates
    {
        get => _skipDuplicates;
        set
        {
            if (_skipDuplicates != value)
            {
                _skipDuplicates = value;
                OnPropertyChanged();
            }
        }
    }

    public string SelectedOutputFormat
    {
        get => _selectedOutputFormat;
        set
        {
            if (_selectedOutputFormat != value)
            {
                _selectedOutputFormat = value;
                OnPropertyChanged();
            }
        }
    }

    public int JpegQuality
    {
        get => _jpegQuality;
        set
        {
            int clamped = Math.Clamp(value, 1, 100);
            if (_jpegQuality != clamped)
            {
                _jpegQuality = clamped;
                OnPropertyChanged();
            }
        }
    }

    public string SelectedQualityPreset
    {
        get => _selectedQualityPreset;
        set
        {
            if (_selectedQualityPreset != value)
            {
                _selectedQualityPreset = value;
                OnPropertyChanged();
                ApplyQualityPreset(value);
            }
        }
    }

    public bool EnableDenoise
    {
        get => _enableDenoise;
        set
        {
            if (_enableDenoise != value)
            {
                _enableDenoise = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsDenoiseEnabled));
            }
        }
    }

    public bool IsDenoiseEnabled => EnableDenoise;

    public double DenoiseStrength
    {
        get => _denoiseStrength;
        set
        {
            double clamped = Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(_denoiseStrength - clamped) > 0.0001)
            {
                _denoiseStrength = clamped;
                OnPropertyChanged();
            }
        }
    }

    public bool EnableTemporalBlend
    {
        get => _enableTemporalBlend;
        set
        {
            if (_enableTemporalBlend != value)
            {
                _enableTemporalBlend = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsTemporalBlendEnabled));
            }
        }
    }

    public bool IsTemporalBlendEnabled => EnableTemporalBlend;

    public double TemporalBlendStrength
    {
        get => _temporalBlendStrength;
        set
        {
            double clamped = Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(_temporalBlendStrength - clamped) > 0.0001)
            {
                _temporalBlendStrength = clamped;
                OnPropertyChanged();
            }
        }
    }

    private void ApplyQualityPreset(string preset)
    {
        if (string.Equals(preset, "Clean + Sharp", StringComparison.OrdinalIgnoreCase))
        {
            EnableDenoise = true;
            DenoiseStrength = 0.12;
            EnableTemporalBlend = true;
            TemporalBlendStrength = 0.08;
        }
    }

    public bool UseHardwareDecode
    {
        get => _useHardwareDecode;
        set
        {
            if (_useHardwareDecode != value)
            {
                _useHardwareDecode = value;
                OnPropertyChanged();
            }
        }
    }

    public string TileSizeText
    {
        get => _tileSizeText;
        set
        {
            if (_tileSizeText != value)
            {
                _tileSizeText = value;
                OnPropertyChanged();
            }
        }
    }

    public string TileOverlapText
    {
        get => _tileOverlapText;
        set
        {
            if (_tileOverlapText != value)
            {
                _tileOverlapText = value;
                OnPropertyChanged();
            }
        }
    }

    public double DownloadProgressPercent
    {
        get => _downloadProgressPercent;
        private set
        {
            if (Math.Abs(_downloadProgressPercent - value) > 0.1)
            {
                _downloadProgressPercent = value;
                OnPropertyChanged();
            }
        }
    }

    public double UpscaleProgressPercent
    {
        get => _upscaleProgressPercent;
        private set
        {
            if (Math.Abs(_upscaleProgressPercent - value) > 0.1)
            {
                _upscaleProgressPercent = value;
                OnPropertyChanged();
            }
        }
    }

    public string DownloadProgressText
    {
        get => _downloadProgressText;
        private set
        {
            if (_downloadProgressText != value)
            {
                _downloadProgressText = value;
                OnPropertyChanged();
            }
        }
    }

    public string JobProgressText
    {
        get => _jobProgressText;
        private set
        {
            if (_jobProgressText != value)
            {
                _jobProgressText = value;
                OnPropertyChanged();
            }
        }
    }

    public AsyncRelayCommand OpenFilesCommand { get; }
    public AsyncRelayCommand OpenFolderCommand { get; }
    public RelayCommand BrowseOutputFolderCommand { get; }
    public AsyncRelayCommand DownloadModelCommand { get; }
    public AsyncRelayCommand RedownloadModelCommand { get; }
    public AsyncRelayCommand PreviewCommand { get; }
    public AsyncRelayCommand UpscaleCommand { get; }
    public RelayCommand CancelCommand { get; }

    public MainViewModel()
    {
        DeviceStatus = $"GPU: {DeviceInfoService.GetPrimaryGpuName()}";

        OpenFilesCommand = new AsyncRelayCommand(OpenFilesAsync, () => !IsProcessing && !IsScanning);
        OpenFolderCommand = new AsyncRelayCommand(OpenFolderAsync, () => !IsProcessing && !IsScanning);
        BrowseOutputFolderCommand = new RelayCommand(BrowseOutputFolder, () => !IsProcessing);
        DownloadModelCommand = new AsyncRelayCommand(DownloadModelAsync, CanDownloadModel);
        RedownloadModelCommand = new AsyncRelayCommand(RedownloadModelAsync, CanRedownloadModel);
        PreviewCommand = new AsyncRelayCommand(PreviewAsync, CanPreview);
        UpscaleCommand = new AsyncRelayCommand(UpscaleAsync, CanUpscale);
        CancelCommand = new RelayCommand(CancelAll, () => IsDownloading || IsProcessing);

        SelectedFiles.CollectionChanged += (_, _) => UpdateActionState();
    }

    public async Task LoadAsync()
    {
        ModelCatalog catalog = await _catalogService.LoadAsync();
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            Models.Clear();
            foreach (ModelDefinition model in catalog.Models)
            {
                Models.Add(model);
            }
        });

        if (Models.Count > 0)
        {
            SelectedModel = Models[0];
        }
    }

    public void AddFiles(IEnumerable<string> paths)
    {
        _ = AddFilesAsync(paths);
    }

    public async Task AddFilesAsync(IEnumerable<string> paths)
    {
        if (IsScanning)
        {
            return;
        }

        IsScanning = true;
        HashSet<string> existing = new(SelectedFiles, StringComparer.OrdinalIgnoreCase);
        try
        {
            (List<string> files, int skipped) = await Task.Run(() =>
            {
                List<string> found = new();
                int skippedCount = 0;
                foreach (string path in paths)
                {
                    try
                    {
                        if (File.Exists(path))
                        {
                            if (IsSupportedImage(path) || IsSupportedVideo(path))
                            {
                                found.Add(path);
                            }
                            else
                            {
                                skippedCount++;
                            }
                        }
                        else if (Directory.Exists(path))
                        {
                            found.AddRange(EnumerateImages(path));
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warn($"Failed to scan '{path}': {ex.Message}");
                    }
                }

                return (found, skippedCount);
            });

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (files.Count == 0)
                {
                    if (SelectedFiles.Count == 0)
                    {
                        UpscaleProgressPercent = 0;
                    }
                    if (skipped > 0)
                    {
                        JobProgressText = $"Skipped {skipped} unsupported file(s).";
                    }
                    return;
                }

                UpscaleProgressPercent = 0;
                files.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (string file in files)
                {
                    if (!existing.Contains(file))
                    {
                        SelectedFiles.Add(file);
                    }
                }

                if (IsSupportedVideo(files[0]))
                {
                    PreviewImage = null;
                    PreviewInfo = "Video selected. Preview unavailable.";
                    SuggestedScale = string.Empty;
                }
                else
                {
                    LoadPreview(files[0]);
                }
                string? baseDir = Path.GetDirectoryName(files[0]);
                if (!string.IsNullOrWhiteSpace(baseDir))
                {
                    OutputFolder = baseDir;
                }
                JobProgressText = skipped > 0
                    ? $"Loaded {SelectedFiles.Count} file(s), skipped {skipped} unsupported."
                    : $"Loaded {SelectedFiles.Count} file(s).";
            });
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to add files.", ex);
            JobProgressText = $"Failed to load files: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    private async Task OpenFilesAsync()
    {
        Microsoft.Win32.OpenFileDialog dialog = new()
        {
            Multiselect = true,
            Filter = "Images and videos|*.jpg;*.jpeg;*.png;*.webp;*.bmp;*.tiff;*.mp4;*.mov;*.mkv;*.avi;*.webm;*.3gp"
        };

        if (dialog.ShowDialog() == true)
        {
            await AddFilesAsync(dialog.FileNames);
        }
    }

    private async Task OpenFolderAsync()
    {
        using System.Windows.Forms.FolderBrowserDialog dialog = new();
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            await AddFilesAsync(new[] { dialog.SelectedPath });
        }
    }

    private void BrowseOutputFolder()
    {
        using System.Windows.Forms.FolderBrowserDialog dialog = new();
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            OutputFolder = dialog.SelectedPath;
        }
    }

    private async Task DownloadModelAsync()
    {
        if (SelectedModel == null)
        {
            return;
        }

        try
        {
            IsDownloading = true;
            DownloadProgressText = "Starting download...";
            DownloadProgressPercent = 0;
            _downloadCts = new CancellationTokenSource();

            Progress<DownloadProgress> progress = new(p =>
            {
                if (p.TotalBytes.HasValue && p.TotalBytes.Value > 0)
                {
                    DownloadProgressPercent = (double)p.BytesReceived / p.TotalBytes.Value * 100;
                    DownloadProgressText = $"{FormatSize(p.BytesReceived)} / {FormatSize(p.TotalBytes.Value)}";
                }
                else
                {
                    DownloadProgressText = $"{FormatSize(p.BytesReceived)} downloaded";
                }
            });

            await Task.Run(
                () => _downloader.DownloadAsync(SelectedModel, progress, _downloadCts.Token),
                _downloadCts.Token);
            UpdateModelStatus();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Model download failed.", ex);
            DownloadProgressText = $"Download failed: {ex.Message}";
        }
        finally
        {
            IsDownloading = false;
            _downloadCts = null;
        }
    }

    private async Task RedownloadModelAsync()
    {
        if (SelectedModel == null)
        {
            return;
        }

        try
        {
            DeleteModelFiles(SelectedModel);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Failed to delete existing model files: {ex.Message}");
        }

        UpdateModelStatus();
        await DownloadModelAsync();
    }

    private async Task PreviewAsync()
    {
        if (!CanPreview())
        {
            return;
        }

        string input = SelectedFiles[0];
        ImageCrop? crop = GetPreviewCrop(input);

        OnnxInferenceEngine? engine = null;
        try
        {
            IsProcessing = true;
            UpscaleProgressPercent = 0;
            JobProgressText = "Generating preview...";
            PreviewInfo = "Loading preview...";
            _jobCts = new CancellationTokenSource();

            string previewFolder = AppPaths.CachePath;
            Directory.CreateDirectory(previewFolder);

            ImagePipeline pipeline = CreatePipeline(out engine);
            UpscaleRequest request = new()
            {
                InputFiles = new List<string> { input },
                OutputFolder = previewFolder,
                Scale = ResolveEffectiveScale(),
                Mode = SelectedMode,
                Model = SelectedModel,
                TileSize = 0,
                TileOverlap = 0,
                OutputFormat = "Png",
                JpegQuality = JpegQuality,
                DenoiseStrength = EnableDenoise ? DenoiseStrength : 0,
                PreviewCrop = crop,
                EnableTemporalBlend = false
            };

            UpscaleResult result = await Task.Run(
                () => pipeline.UpscaleAsync(request, null, _jobCts.Token),
                _jobCts.Token);
            if (result.OutputFiles.Count > 0)
            {
                LoadPreview(result.OutputFiles[0]);
                PreviewInfo = crop == null
                    ? PreviewInfo
                    : $"Preview crop {crop.Width} x {crop.Height}";
            }

            if (engine != null)
            {
                UpdateDeviceStatus(engine);
            }
            JobProgressText = "Preview completed.";
        }
        catch (OperationCanceledException)
        {
            JobProgressText = "Preview canceled.";
        }
        catch (Exception ex)
        {
            AppLogger.Error("Preview failed.", ex);
            JobProgressText = $"Preview failed: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
            _jobCts = null;
            engine?.Dispose();
        }
    }

    private async Task UpscaleAsync()
    {
        if (!CanUpscale())
        {
            return;
        }

        List<string> inputs = SelectedFiles.ToList();
        inputs.Sort(StringComparer.OrdinalIgnoreCase);

        UpscaleProgressPercent = 0;

        List<string> videoInputs = inputs.Where(IsSupportedVideo).ToList();
        List<string> imageInputs = inputs.Where(IsSupportedImage).ToList();
        if (videoInputs.Count > 0 && imageInputs.Count > 0)
        {
            JobProgressText = "Mixed images and videos are not supported in one run.";
            return;
        }

        if (videoInputs.Count > 0)
        {
            await UpscaleVideosAsync(videoInputs);
            return;
        }

        if (SkipDuplicates)
        {
            imageInputs = FilterDuplicates(imageInputs, out int skipped);
            if (skipped > 0)
            {
                JobProgressText = $"Skipped {skipped} duplicate file(s).";
            }
        }

        if (imageInputs.Count == 0)
        {
            JobProgressText = "No files to upscale.";
            return;
        }

        string outputFolder = ResolveOutputFolder(imageInputs.Count);
        Directory.CreateDirectory(outputFolder);

        OnnxInferenceEngine? engine = null;
        try
        {
            IsProcessing = true;
            UpscaleProgressPercent = 0;
            _jobCts = new CancellationTokenSource();
            Stopwatch stopwatch = Stopwatch.StartNew();

            ImagePipeline pipeline = CreatePipeline(out engine);
            Progress<UpscaleProgress> progress = new(p =>
            {
                double avgSeconds = p.CurrentIndex > 0 ? stopwatch.Elapsed.TotalSeconds / p.CurrentIndex : 0;
                double remaining = Math.Max(0, p.Total - p.CurrentIndex);
                TimeSpan eta = TimeSpan.FromSeconds(avgSeconds * remaining);
                string tileText = p.TileTotal > 0 ? $" (Tile {p.TileIndex}/{p.TileTotal})" : string.Empty;
                JobProgressText = $"Image {p.CurrentIndex}/{p.Total}{tileText} - {p.Message} - ETA {eta:mm\\:ss}";
                if (p.OverallPercent > 0)
                {
                    UpscaleProgressPercent = p.OverallPercent;
                }
            });

            int tileSize = ResolveTileSize(imageInputs);
            int tileOverlap = ResolveTileOverlap();
            UpscaleRequest request = new()
            {
                InputFiles = imageInputs,
                OutputFolder = outputFolder,
                Scale = ResolveEffectiveScale(),
                Mode = SelectedMode,
                Model = SelectedModel,
                TileSize = tileSize,
                TileOverlap = tileOverlap,
                SkipDuplicates = SkipDuplicates,
                OutputFormat = SelectedOutputFormat,
                JpegQuality = JpegQuality,
                DenoiseStrength = EnableDenoise ? DenoiseStrength : 0,
                EnableTemporalBlend = EnableTemporalBlend,
                TemporalBlendStrength = TemporalBlendStrength
            };

            UpscaleResult result = await Task.Run(
                () => pipeline.UpscaleAsync(request, progress, _jobCts.Token),
                _jobCts.Token);
            if (engine != null)
            {
                UpdateDeviceStatus(engine);
            }
            UpdateRecentOutputs(result.OutputFiles);
            JobProgressText = $"Upscale completed. Saved {result.OutputFiles.Count} file(s).";
            UpscaleProgressPercent = 100;
        }
        catch (OperationCanceledException)
        {
            JobProgressText = "Upscale canceled.";
            UpscaleProgressPercent = 0;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Upscale failed.", ex);
            JobProgressText = $"Upscale failed: {ex.Message}";
            UpscaleProgressPercent = 0;
        }
        finally
        {
            IsProcessing = false;
            _jobCts = null;
            engine?.Dispose();
        }
    }

    private async Task UpscaleVideosAsync(IReadOnlyList<string> videos)
    {
        if (videos.Count == 0)
        {
            JobProgressText = "No videos to upscale.";
            return;
        }

        string outputFolder = ResolveOutputFolder(videos.Count);
        Directory.CreateDirectory(outputFolder);

        OnnxInferenceEngine? engine = null;
        try
        {
            IsProcessing = true;
            UpscaleProgressPercent = 0;
            _jobCts = new CancellationTokenSource();

            ImagePipeline pipeline = CreatePipeline(out engine);
            VideoPipeline videoPipeline = new(pipeline, new FfmpegRunner());
            List<string> outputs = new();

            for (int i = 0; i < videos.Count; i++)
            {
                int currentVideo = i + 1;
                string input = videos[i];
                ResetVideoPhaseProgress();
                Progress<UpscaleProgress> progress = new(p =>
                {
                    double videoFraction = p.OverallPercent / 100.0;
                    double overall = (currentVideo - 1 + videoFraction) / videos.Count;
                    double overallPercent = overall * 100;
                    UpscaleProgressPercent = overallPercent;
                    UpdateVideoPhaseProgress(p.Message);
                    JobProgressText = $"Video {currentVideo}/{videos.Count} - Decode {_videoDecodePercent:0}% | Upscale {_videoUpscalePercent:0}% | Encode {_videoEncodePercent:0}% (Overall {overallPercent:0}%)";
                });

                VideoUpscaleRequest request = new()
                {
                    InputPath = input,
                    OutputFolder = outputFolder,
                    Scale = ResolveEffectiveScale(),
                    Mode = SelectedMode,
                    Model = SelectedModel,
                    TileSize = ResolveVideoTileSize(),
                    TileOverlap = ResolveTileOverlap(),
                    JpegQuality = JpegQuality,
                    DenoiseStrength = EnableDenoise ? DenoiseStrength : 0,
                    EnableTemporalBlend = EnableTemporalBlend,
                    TemporalBlendStrength = TemporalBlendStrength,
                    UseHardwareDecode = UseHardwareDecode,
                    VideoEncoder = SelectedVideoEncoder
                };

                string output = await Task.Run(
                    () => videoPipeline.UpscaleAsync(request, progress, _jobCts.Token),
                    _jobCts.Token);
                outputs.Add(output);
                UpdateRecentOutputs(new[] { output });
            }

            if (engine != null)
            {
                UpdateDeviceStatus(engine);
            }

            JobProgressText = $"Video upscale completed. Saved {outputs.Count} file(s).";
            UpscaleProgressPercent = 100;
        }
        catch (OperationCanceledException)
        {
            JobProgressText = "Video upscale canceled.";
            UpscaleProgressPercent = 0;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Video upscale failed.", ex);
            JobProgressText = $"Video upscale failed: {ex.Message}";
            UpscaleProgressPercent = 0;
        }
        finally
        {
            IsProcessing = false;
            _jobCts = null;
            engine?.Dispose();
        }
    }

    private void CancelAll()
    {
        if (IsDownloading)
        {
            _downloadCts?.Cancel();
            DownloadProgressText = "Download canceled.";
        }

        if (IsProcessing)
        {
            _jobCts?.Cancel();
            UpscaleProgressPercent = 0;
        }
    }

    private bool CanDownloadModel() => SelectedModel != null && IsSelectedModelMissing && !IsDownloading && !IsProcessing;

    private bool CanRedownloadModel() => SelectedModel != null && IsSelectedModelAvailable && !IsDownloading && !IsProcessing;

    private bool CanPreview()
        => SelectedFiles.Count > 0
        && SelectedFiles.All(IsSupportedImage)
        && SelectedModel != null
        && !IsSelectedModelMissing
        && !IsProcessing
        && !IsDownloading;

    private bool CanUpscale()
        => SelectedFiles.Count > 0
        && SelectedModel != null
        && !IsSelectedModelMissing
        && !IsProcessing
        && !IsDownloading;

    private void UpdateModelStatus()
    {
        if (SelectedModel == null)
        {
            ModelStatus = "No model selected.";
            IsSelectedModelMissing = true;
            return;
        }

        bool available = ModelFileStore.IsModelAvailable(SelectedModel);
        IsSelectedModelMissing = !available;
        ModelStatus = available ? "Ready (local model found)." : "Missing model. Download required.";
        UpdateActionState();
    }

    private void SyncSelectedScaleToModel()
    {
        if (SelectedModel?.Scale > 0 && SelectedScale != SelectedModel.Scale)
        {
            SelectedScale = SelectedModel.Scale;
            OnPropertyChanged(nameof(SelectedScale));
        }
    }

    private int ResolveEffectiveScale()
    {
        if (SelectedModel?.Scale > 0)
        {
            return SelectedModel.Scale;
        }

        return SelectedScale;
    }

    private static void DeleteModelFiles(ModelDefinition model)
    {
        string modelPath = ModelFileStore.GetModelFilePath(model);
        string metadataPath = ModelFileStore.GetMetadataPath(modelPath);
        string? dir = Path.GetDirectoryName(modelPath);

        if (File.Exists(modelPath))
        {
            File.Delete(modelPath);
        }

        if (File.Exists(metadataPath))
        {
            File.Delete(metadataPath);
        }

        string zipPath = modelPath + ".zip";
        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }

        if (!string.IsNullOrWhiteSpace(dir))
        {
            string dataPath = Path.Combine(dir, "model.data");
            if (File.Exists(dataPath))
            {
                File.Delete(dataPath);
            }
        }

        Processing.OnnxInferenceEngine.ClearCachedSession(modelPath);
    }

    private void UpdateActionState()
    {
        OpenFilesCommand.RaiseCanExecuteChanged();
        OpenFolderCommand.RaiseCanExecuteChanged();
        BrowseOutputFolderCommand.RaiseCanExecuteChanged();
        DownloadModelCommand.RaiseCanExecuteChanged();
        RedownloadModelCommand.RaiseCanExecuteChanged();
        PreviewCommand.RaiseCanExecuteChanged();
        UpscaleCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
    }

    private void LoadPreview(string filePath)
    {
        _ = LoadPreviewAsync(filePath);
    }

    private async Task LoadPreviewAsync(string filePath)
    {
        try
        {
            (BitmapImage image, string info, string suggested) = await Task.Run(() =>
            {
                BitmapImage bmp = new();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(filePath);
                bmp.EndInit();
                bmp.Freeze();
                string infoText = $"{bmp.PixelWidth} x {bmp.PixelHeight}";
                string suggestion = bmp.PixelWidth < 1600 || bmp.PixelHeight < 1600 ? "Suggested: x2" : "Suggested: x4";
                return (bmp, infoText, suggestion);
            });

            PreviewImage = image;
            PreviewInfo = info;
            SuggestedScale = suggested;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Failed to load preview: {ex.Message}");
            PreviewInfo = "Preview unavailable.";
        }
    }

    private ImagePipeline CreatePipeline(out OnnxInferenceEngine engine)
    {
        if (SelectedModel == null)
        {
            throw new InvalidOperationException("No model selected.");
        }

        WicImagePreprocessor preprocessor = new();
        SimpleTileSplitter splitter = new();
        engine = new OnnxInferenceEngine(SelectedModel);
        WeightedTileMerger merger = new();
        WicImagePostprocessor postprocessor = new();
        return new ImagePipeline(preprocessor, splitter, engine, merger, postprocessor);
    }

    private void UpdateDeviceStatus(OnnxInferenceEngine engine)
    {
        DeviceStatus = engine.UsingCpuFallback
            ? "CPU fallback (no DirectML device)"
            : $"GPU: {DeviceInfoService.GetPrimaryGpuName()}";
    }

    private static IEnumerable<string> EnumerateImages(string folder)
    {
        Stack<string> pending = new();
        pending.Push(folder);
        while (pending.Count > 0)
        {
            string current = pending.Pop();
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(current, "*.*", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to scan folder '{current}': {ex.Message}");
                continue;
            }

            foreach (string file in files)
            {
                if (IsSupportedImage(file) || IsSupportedVideo(file))
                {
                    yield return file;
                }
            }

            IEnumerable<string> directories;
            try
            {
                directories = Directory.EnumerateDirectories(current);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to enumerate subfolders in '{current}': {ex.Message}");
                continue;
            }

            foreach (string directory in directories)
            {
                pending.Push(directory);
            }
        }
    }

    private static bool IsSupportedImage(string path)
        => SupportedImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static bool IsSupportedVideo(string path)
        => SupportedVideoExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static ImageCrop? GetPreviewCrop(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            BitmapDecoder decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            BitmapFrame frame = decoder.Frames[0];
            int width = frame.PixelWidth;
            int height = frame.PixelHeight;
            int size = Math.Min(256, Math.Min(width, height));
            if (size <= 0)
            {
                return null;
            }

            int x = Math.Max(0, (width - size) / 2);
            int y = Math.Max(0, (height - size) / 2);
            return new ImageCrop { X = x, Y = y, Width = size, Height = size };
        }
        catch
        {
            return null;
        }
    }

    private string ResolveOutputFolder(int count)
    {
        string baseFolder = string.IsNullOrWhiteSpace(OutputFolder) ? AppPaths.OutputPath : OutputFolder;
        if (count > 1)
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            return Path.Combine(baseFolder, timestamp);
        }

        return baseFolder;
    }

    private static List<string> FilterDuplicates(List<string> files, out int skipped)
    {
        Dictionary<string, string> seen = new(StringComparer.OrdinalIgnoreCase);
        List<string> unique = new();
        skipped = 0;

        foreach (string file in files)
        {
            string hash = HashUtils.ComputeSha256(file);
            if (seen.ContainsKey(hash))
            {
                skipped++;
                continue;
            }

            seen[hash] = file;
            unique.Add(file);
        }

        return unique;
    }

    private void UpdateRecentOutputs(IReadOnlyList<string> outputs)
    {
        foreach (string output in outputs)
        {
            if (!RecentOutputs.Contains(output))
            {
                RecentOutputs.Insert(0, output);
            }
        }
    }

    private void ResetVideoPhaseProgress()
    {
        _videoDecodePercent = 0;
        _videoUpscalePercent = 0;
        _videoEncodePercent = 0;
    }

    private void UpdateVideoPhaseProgress(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        if (TryParsePhasePercent(message, "Decoding frames", out double decode))
        {
            _videoDecodePercent = decode;
            return;
        }

        if (TryParsePhasePercent(message, "Upscaling frames", out double upscale))
        {
            _videoUpscalePercent = upscale;
            return;
        }

        if (TryParsePhasePercent(message, "Encoding video", out double encode))
        {
            _videoEncodePercent = encode;
        }
    }

    private static bool TryParsePhasePercent(string message, string prefix, out double percent)
    {
        percent = 0;
        if (!message.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int open = message.IndexOf('(');
        int close = message.IndexOf(')');
        if (open < 0 || close <= open)
        {
            return false;
        }

        string raw = message.Substring(open + 1, close - open - 1).TrimEnd('%').Trim();
        return double.TryParse(raw, out percent);
    }

    private int GetAutoTileSize(int count)
    {
        if (SelectedMode.Equals("Fast", StringComparison.OrdinalIgnoreCase))
        {
            return 256;
        }

        return count > 1 ? 512 : 768;
    }

    private int ResolveTileSize(IReadOnlyList<string> inputs)
    {
        if (int.TryParse(TileSizeText, out int size) && size > 0)
        {
            return size;
        }

        int maxPixels = 0;
        foreach (string path in inputs)
        {
            try
            {
                using FileStream stream = File.OpenRead(path);
                BitmapDecoder decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                BitmapFrame frame = decoder.Frames[0];
                int pixels = frame.PixelWidth * frame.PixelHeight;
                if (pixels > maxPixels)
                {
                    maxPixels = pixels;
                }
            }
            catch
            {
                // Ignore files that fail to probe; fallback to default size.
            }
        }

        if (maxPixels >= 8000 * 8000)
        {
            return 256;
        }

        if (maxPixels >= 4000 * 4000)
        {
            return 512;
        }

        return GetAutoTileSize(inputs.Count);
    }

    private int ResolveVideoTileSize()
    {
        if (int.TryParse(TileSizeText, out int size) && size > 0)
        {
            return size;
        }

        return 0;
    }

    private int ResolveTileOverlap()
    {
        if (int.TryParse(TileOverlapText, out int overlap) && overlap >= 0)
        {
            return overlap;
        }

        return 32;
    }

    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:0.##} {units[unit]}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
