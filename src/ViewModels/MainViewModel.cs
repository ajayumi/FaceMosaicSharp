using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Emgu.CV;
using FaceMosaicSharp.Models;
using FaceMosaicSharp.Services;
using FaceMosaicSharp.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;

namespace FaceMosaicSharp.ViewModels;

/// <summary>
/// 主界面 ViewModel，负责视频选择、参数配置、预览刷新、处理任务调度等核心业务逻辑
/// </summary>
public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IDialogService _dialogService;
    private IFaceDetectionService _currentFaceDetectionService = null!;
    private readonly IVideoProcessingService _videoProcessingService;
    private readonly IVideoHistoryService _historyService;
    private readonly ILogger<MainViewModel> _logger;
    private CancellationTokenSource? _processingCts;
    private readonly IServiceProvider _serviceProvider;

    [ObservableProperty]
    private string _videoPath = string.Empty;

    [ObservableProperty]
    private VideoInfo? _videoInfo;

    [ObservableProperty]
    private BitmapSource? _previewImage;

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private string _statusMessage = "就绪";

    [ObservableProperty]
    private int _progressValue;

    [ObservableProperty]
    private int _progressMax = 100;

    [ObservableProperty]
    private string _processingTime = string.Empty;

    [ObservableProperty]
    private int _mosaicBlockSize = 15;

    [ObservableProperty]
    private string _outputFormat = "mp4";

    [ObservableProperty]
    private string _videoCodec = "mp4v";

    [ObservableProperty]
    private int _outputFps = 0;

    [ObservableProperty]
    private bool _processAllFrames = true;

    [ObservableProperty]
    private int _startFrame;

    [ObservableProperty]
    private int _endFrame;

    [ObservableProperty]
    private int _frameInterval = 1;

    [ObservableProperty]
    private float _confidenceThreshold = 0.65f;

    [ObservableProperty]
    private bool _enableFacialFeatures;

    [ObservableProperty]
    private bool _eyeLineMosaic = true;

    [ObservableProperty]
    private bool _mouthLineMosaic = true;

    [ObservableProperty]
    private bool _enableFaceParsing;

    [ObservableProperty]
    private double _maskCoverageThreshold = 0.05;

    [ObservableProperty]
    private bool _previewMosaicEnabled;

    [ObservableProperty]
    private string _outputPath = string.Empty;

    [ObservableProperty]
    private bool _hasVideoSelected;

    [ObservableProperty]
    private FaceDetectionMethod _faceDetectionMethod = FaceDetectionMethod.Yolo;

    [ObservableProperty]
    private int _previewFrameNumber;

    public bool CanStartProcessing => HasVideoSelected && !IsProcessing;

    partial void OnHasVideoSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanStartProcessing));
    }

    partial void OnIsProcessingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanStartProcessing));
    }

    public ObservableCollection<string> OutputFormats { get; } = new() { "mp4", "avi", "mov", "wmv" };
    public ObservableCollection<string> VideoCodecs { get; } = new() { "mp4v", "H264", "XVID", "avc1" };
    public ObservableCollection<FaceDetectionMethod> FaceDetectionMethods { get; } = new();

    public MainViewModel(
        IVideoProcessingService videoProcessingService,
        IVideoHistoryService historyService,
        ILogger<MainViewModel> logger,
        IDialogService dialogService,
        IServiceProvider serviceProvider)
    {
        _videoProcessingService = videoProcessingService;
        _historyService = historyService;
        _logger = logger;
        _dialogService = dialogService;
        _serviceProvider = serviceProvider;
        LoadFaceDetectionMethods();
        _ = InitializeAsync();
    }

    private DateTime _processingStartTime;

    private void LoadFaceDetectionMethods()
    {
        var interfaceType = typeof(IFaceDetectionService);
        var implementations = interfaceType.Assembly
            .GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface && interfaceType.IsAssignableFrom(t))
            .Select(t => t.GetCustomAttribute<FaceDetectionMethodAttribute>())
            .Where(attr => attr != null)
            .Select(attr => attr!.Method);

        foreach (var method in implementations)
        {
            FaceDetectionMethods.Add(method);
        }

        if (FaceDetectionMethods.Count > 0)
        {
            var firstMethod = FaceDetectionMethods[0];
            _currentFaceDetectionService = GetFaceDetectionService(firstMethod);
            FaceDetectionMethod = firstMethod;
        }
    }

    private IFaceDetectionService GetFaceDetectionService(FaceDetectionMethod method)
    {
        return _serviceProvider.GetRequiredKeyedService<IFaceDetectionService>(method);
    }

    private async Task InitializeAsync()
    {
        var success = await _currentFaceDetectionService.InitializeAsync();

        bool ffmpegExists = CheckFfmpegAvailable();
        if (!ffmpegExists)
        {
            StatusMessage = "人脸检测已初始化。警告: 未检测到FFmpeg，声音将无法保留";
        }
        else
        {
            StatusMessage = success ? "人脸检测已初始化" : "人脸检测初始化失败";
        }
    }

    private static bool CheckFfmpegAvailable()
    {
        try
        {
            var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = "-version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            });
            process?.WaitForExit(5000);
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    partial void OnFaceDetectionMethodChanged(FaceDetectionMethod value)
    {
        SwitchFaceDetectionService(value);
    }

    [RelayCommand]
    private async Task RefreshPreview()
    {
        if (VideoInfo != null && PreviewFrameNumber >= 0 && PreviewFrameNumber < VideoInfo.TotalFrames)
        {
            await UpdatePreviewAsync();
        }
    }

    partial void OnPreviewFrameNumberChanged(int value)
    {
    }

    private async void SwitchFaceDetectionService(FaceDetectionMethod method)
    {
        _currentFaceDetectionService = GetFaceDetectionService(method);

        var success = await _currentFaceDetectionService.InitializeAsync();
        _videoProcessingService.SetFaceDetectionService(method);

        StatusMessage = success
            ? $"已切换到{method}检测方式"
            : $"{method}检测方式初始化失败";

        _ = UpdatePreviewAsync();
    }

    [RelayCommand]
    private async Task SelectVideo()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "视频文件|*.mp4;*.avi;*.mov;*.wmv;*.mkv;*.flv;*.webm|All Files|*.*",
            Title = "选择视频文件"
        };

        if (dialog.ShowDialog() == true)
        {
            VideoPath = dialog.FileName;
            await LoadVideoInfoAsync();
        }
    }

    private async Task LoadVideoInfoAsync()
    {
        if (string.IsNullOrEmpty(VideoPath) || !File.Exists(VideoPath)) return;

        VideoInfo = _videoProcessingService.GetVideoInfo(VideoPath);
        if (VideoInfo != null)
        {
            HasVideoSelected = true;
            StatusMessage = $"已加载: {VideoInfo.FileName} ({VideoInfo.Width}x{VideoInfo.Height}, {VideoInfo.Fps:F2} FPS, {VideoInfo.TotalFrames} 帧)";

            // 检查是否有历史记录并加载配置
            if (HasHistory())
            {
                var savedOptions = LoadOptionsFromHistory();
                if (savedOptions != null)
                {
                    BindOptionsToUI(savedOptions);
                    StatusMessage = "已加载历史配置";
                }
            }

            await UpdatePreviewAsync();
        }
    }

    [RelayCommand]
    private async Task UpdatePreview()
    {
        await UpdatePreviewAsync();
    }

    [RelayCommand]
    private void OpenPreviewImage()
    {
        if (PreviewImage == null) return;

        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FaceMosaicSharp");
        System.IO.Directory.CreateDirectory(tempDir);
        var tempFile = System.IO.Path.Combine(tempDir, $"preview_{PreviewFrameNumber}.png");

        using var stream = new System.IO.FileStream(tempFile, System.IO.FileMode.Create);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(PreviewImage));
        encoder.Save(stream);

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = tempFile,
            UseShellExecute = true
        });
    }

    private async Task UpdatePreviewAsync()
    {
        if (string.IsNullOrEmpty(VideoPath) || VideoInfo == null) return;

        _videoProcessingService.SetFaceDetectionService(FaceDetectionMethod);
        if (_currentFaceDetectionService is IFaceDetectionService service)
        {
            service.SetConfidenceThreshold(ConfidenceThreshold);
            service.SetFacialFeaturesEnabled(EnableFacialFeatures);
        }
        _videoProcessingService.SetFaceParsingEnabled(EnableFaceParsing);
        _videoProcessingService.SetPreviewMosaic(PreviewMosaicEnabled ? MosaicBlockSize : 0);
        _videoProcessingService.SetEyeLineMosaic(EyeLineMosaic);
        _videoProcessingService.SetMouthLineMosaic(MouthLineMosaic);

        _logger.LogInformation("预览刷新: 五官解析={EnableFacialFeatures}, 面部解析={EnableFaceParsing}, 预览打码={PreviewMosaic}, 置信度阈值={Threshold}",
            EnableFacialFeatures, EnableFaceParsing, PreviewMosaicEnabled, ConfidenceThreshold);

        PreviewImage = null;

        var frame = await _videoProcessingService.GetPreviewFrameAsync(VideoPath, PreviewFrameNumber);
        if (frame != null && !frame.IsEmpty)
        {
            PreviewImage = MatToBitmapSource(frame);
            frame.Dispose();
        }
    }

    [RelayCommand]
    private async Task StartProcessing()
    {
        if (string.IsNullOrEmpty(VideoPath) || !File.Exists(VideoPath))
        {
            await _dialogService.ShowWarningAsync("错误", "请先选择视频文件。");
            return;
        }

        // 保存当前界面配置到历史记录
        var historyOptions = new ProcessingOptions
        {
            MosaicBlockSize = MosaicBlockSize,
            OutputFormat = OutputFormat,
            VideoCodec = VideoCodec,
            OutputFps = OutputFps,
            ProcessAllFrames = ProcessAllFrames,
            StartFrame = StartFrame,
            EndFrame = EndFrame,
            FrameInterval = FrameInterval,
            FaceDetectionMethod = FaceDetectionMethod,
            ConfidenceThreshold = ConfidenceThreshold,
            EnableFacialFeatures = EnableFacialFeatures,
            EyeLineMosaic = EyeLineMosaic,
            MouthLineMosaic = MouthLineMosaic,
            EnableFaceParsing = EnableFaceParsing,
            MinMaskCoverageRatio = (float)MaskCoverageThreshold
        };
        SaveOptionsToHistory(historyOptions);
        StatusMessage = "配置已更新";

        // 统一弹出保存对话框
        var saveDialog = new SaveFileDialog
        {
            Filter = $"输出格式|*.{OutputFormat}",
            FileName = $"{Path.GetFileNameWithoutExtension(VideoInfo?.FileName ?? "output")}_mosaic_{DateTime.Now:yyyyMMddHHmmss}",
            DefaultExt = OutputFormat
        };

        if (saveDialog.ShowDialog() != true) return;
        OutputPath = saveDialog.FileName;

        _videoProcessingService.SetFaceDetectionService(FaceDetectionMethod);
        IsProcessing = true;
        ProgressValue = 0;
        ProcessingTime = string.Empty;
        _processingStartTime = DateTime.Now;
        StatusMessage = "正在处理...";
        _processingCts = new CancellationTokenSource();

        var options = new ProcessingOptions
        {
            MosaicBlockSize = MosaicBlockSize,
            OutputFormat = OutputFormat,
            VideoCodec = VideoCodec,
            OutputFps = OutputFps,
            ProcessAllFrames = ProcessAllFrames,
            StartFrame = StartFrame,
            EndFrame = EndFrame,
            FrameInterval = FrameInterval,
            OutputPath = OutputPath,
            FaceDetectionMethod = FaceDetectionMethod,
            ConfidenceThreshold = ConfidenceThreshold,
            EnableFacialFeatures = EnableFacialFeatures,
            EyeLineMosaic = EyeLineMosaic,
            MouthLineMosaic = MouthLineMosaic,
            EnableFaceParsing = EnableFaceParsing,
            MinMaskCoverageRatio = (float)MaskCoverageThreshold
        };

        var progress = new Progress<(int current, int total, string status)>(p =>
        {
            ProgressValue = p.current;
            ProgressMax = p.total;
            StatusMessage = p.status;
        });

        Action<Mat> onFrameProcessed = frame =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                PreviewImage = MatToBitmapSource(frame);
            });
        };

        try
        {
            // Step1+Step2: 提取帧并检测人脸
            var result = await _videoProcessingService.ProcessVideoAsync(
                VideoPath, options, progress, onFrameProcessed, _processingCts.Token);

            if (!result.Success) return;

            // 仅在存在未检测到人脸的帧时，询问用户是否手工介入
            if (result.NeedsManualReview)
            {
                bool needsManual = await _dialogService.ShowYesNoAsync(
                    "提示",
                    "自动检测不理想，是否人工介入？");

                if (needsManual)
                {
                    IsProcessing = false;
                    if (!ShowReviewFramesWindow(result.HistoryDir!))
                        return;
                }
            }

            // Step3: 应用马赛克 + 合成
            IsProcessing = true;
            StatusMessage = "正在应用马赛克...";
            var step3Success = await _videoProcessingService.ProcessRemainingAsync(
                VideoPath, options, progress, onFrameProcessed, _processingCts.Token);

            var elapsed = DateTime.Now - _processingStartTime;
            ProcessingTime = $"处理时长: {elapsed:hh\\:mm\\:ss}";
            StatusMessage = step3Success
                ? $"处理完成！已保存至: {OutputPath}"
                : "处理已取消或失败。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"错误: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
            _processingCts?.Dispose();
            _processingCts = null;
        }
    }

    [RelayCommand]
    private void CancelProcessing()
    {
        _processingCts?.Cancel();
        StatusMessage = "正在取消...";
    }

    private bool ShowReviewFramesWindow(string historyDir)
    {
        const int width = 1240;
        const int height = 700;

        var viewModel = ActivatorUtilities.CreateInstance<ReviewFramesViewModel>(_serviceProvider, historyDir);
        var view = ActivatorUtilities.CreateInstance<ReviewFramesView>(_serviceProvider, viewModel);

        var window = new Window
        {
            DataContext = viewModel,
            Content = view,
            Width = width,
            Height = height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Application.Current.MainWindow
        };

        viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ReviewFramesViewModel.SelectedFrame) && viewModel.SelectedFrame != null)
            {
                var editorViewModel = new FaceEditorViewModel(viewModel.SelectedFrame, historyDir, _dialogService, _currentFaceDetectionService);
                var editorView = ActivatorUtilities.CreateInstance<FaceEditorView>(_serviceProvider, editorViewModel);

                var editorWindow = new Window
                {
                    Title = $"编辑帧 {viewModel.SelectedFrame.FrameIndex} 的人脸区域",
                    Content = editorView,
                    Width = 800,
                    Height = 650,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = window
                };

                editorWindow.ShowDialog();
                viewModel.Refresh();
                viewModel.SelectedFrame = null;
            }
        };

        window.SetBinding(Window.TitleProperty, nameof(ReviewFramesViewModel.Title));
        window.ShowDialog();
        return viewModel.ContinueProcessing;
    }

    private static BitmapSource? MatToBitmapSource(Mat mat)
    {
        if (mat.IsEmpty) return null;

        using var bitmap = mat.ToBitmap();
        if (bitmap == null) return null;

        using var memory = new MemoryStream();
        bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Png);
        memory.Position = 0;

        var bitmapImage = new BitmapImage();
        bitmapImage.BeginInit();
        bitmapImage.StreamSource = memory;
        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
        bitmapImage.EndInit();
        bitmapImage.Freeze();

        return bitmapImage;
    }

    private string GetHistoryDir()
    {
        return _historyService.GetHistoryDirectory(VideoPath);
    }

    private string GetOptionsJsonPath()
    {
        return Path.Combine(GetHistoryDir(), "options.json");
    }

    private bool HasHistory()
    {
        if (string.IsNullOrEmpty(VideoPath) || !File.Exists(VideoPath))
            return false;

        return Directory.Exists(GetHistoryDir());
    }

    private void SaveOptionsToHistory(ProcessingOptions options)
    {
        var historyDir = GetHistoryDir();
        Directory.CreateDirectory(historyDir);

        var optionsJsonPath = GetOptionsJsonPath();
        var json = JsonSerializer.Serialize(options, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(optionsJsonPath, json);
    }

    private ProcessingOptions? LoadOptionsFromHistory()
    {
        var optionsJsonPath = GetOptionsJsonPath();
        if (!File.Exists(optionsJsonPath))
            return null;

        try
        {
            var json = File.ReadAllText(optionsJsonPath);
            return JsonSerializer.Deserialize<ProcessingOptions>(json);
        }
        catch
        {
            return null;
        }
    }

    private void BindOptionsToUI(ProcessingOptions options)
    {
        MosaicBlockSize = options.MosaicBlockSize;
        OutputFormat = options.OutputFormat;
        VideoCodec = options.VideoCodec;
        OutputFps = options.OutputFps;
        ProcessAllFrames = options.ProcessAllFrames;
        StartFrame = options.StartFrame;
        EndFrame = options.EndFrame;
        FrameInterval = options.FrameInterval;
        FaceDetectionMethod = options.FaceDetectionMethod;
        ConfidenceThreshold = options.ConfidenceThreshold;
        EnableFacialFeatures = options.EnableFacialFeatures;
        EyeLineMosaic = options.EyeLineMosaic;
        MouthLineMosaic = options.MouthLineMosaic;
        EnableFaceParsing = options.EnableFaceParsing;
        MaskCoverageThreshold = options.MinMaskCoverageRatio;
    }

    public void Dispose()
    {
        _processingCts?.Cancel();
        _processingCts?.Dispose();
        _currentFaceDetectionService?.Dispose();
        _videoProcessingService.Dispose();
    }
}