using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Channels;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using FaceMosaicSharp.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FaceMosaicSharp.Services;

/// <summary>
/// 视频处理服务接口
/// </summary>
public interface IVideoProcessingService : IDisposable
{
    /// <summary>设置人脸检测服务</summary>
    void SetFaceDetectionService(FaceDetectionMethod method);

    /// <summary>获取视频信息</summary>
    VideoInfo? GetVideoInfo(string videoPath);

    /// <summary>
    /// 异步处理视频
    /// 处理流程：提取帧 -> 检测人脸 -> 应用马赛克 -> 合成视频
    /// </summary>
    Task<ProcessingResult> ProcessVideoAsync(string inputPath, ProcessingOptions options,
        IProgress<(int current, int total, string status)>? progress = null,
        Action<Mat>? onFrameProcessed = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 继续处理剩余帧（用于处理手动标记后的帧）
    /// </summary>
    Task<bool> ProcessRemainingAsync(string inputPath, ProcessingOptions options,
        IProgress<(int current, int total, string status)>? progress = null,
        Action<Mat>? onFrameProcessed = null,
        CancellationToken cancellationToken = default);

    /// <summary>获取预览帧</summary>
    Task<Mat?> GetPreviewFrameAsync(string videoPath, int frameNumber = 0);

    /// <summary>设置预览时的面部解析开关</summary>
    void SetFaceParsingEnabled(bool enabled);

    /// <summary>设置预览马赛克（blockSize=0 禁用）</summary>
    void SetPreviewMosaic(int blockSize);

    /// <summary>设置预览时是否绘制眼睛线条马赛克</summary>
    void SetEyeLineMosaic(bool enabled);

    /// <summary>设置预览时是否绘制嘴巴线条马赛克</summary>
    void SetMouthLineMosaic(bool enabled);
}

/// <summary>
/// 视频处理服务实现
/// 负责视频帧提取、人脸检测、马赛克应用和视频合成
/// </summary>
/// <summary>马赛克应用优先级：凸包多边形（面部关键点）→ 面部解析掩码 → 矩形兜底</summary>
public class VideoProcessingService : IVideoProcessingService
{
    /// <summary>当前人脸检测服务</summary>
    private IFaceDetectionService _currentFaceDetectionService = null!;
    /// <summary>面部解析服务（BiSeNet），首次使用前延迟初始化</summary>
    private IFaceParsingService? _faceParsingService;
    /// <summary>预览时是否启用面部解析</summary>
    private bool _enableFaceParsing;
    /// <summary>是否已尝试初始化面部解析服务（避免重复尝试）</summary>
    private bool _faceParsingInitAttempted;
    /// <summary>预览模式的马赛克块大小（0=禁用预览马赛克）</summary>
    private int _previewMosaicBlockSize;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<VideoProcessingService> _logger;
    /// <summary>面部解析掩码有效覆盖率阈值，低于此值回退到矩形马赛克</summary>
    private double _maskCoverageThreshold = 0.05;
    /// <summary>预览时是否绘制眼睛线条马赛克</summary>
    private bool _eyeLineMosaic = true;
    /// <summary>预览时是否绘制嘴巴线条马赛克</summary>
    private bool _mouthLineMosaic = true;
    private readonly IVideoHistoryService _historyService;

    public void SetFaceParsingEnabled(bool enabled)
    {
        _enableFaceParsing = enabled;
    }

    public void SetPreviewMosaic(int blockSize)
    {
        _previewMosaicBlockSize = Math.Max(0, blockSize);
    }

    public void SetEyeLineMosaic(bool enabled)
    {
        _eyeLineMosaic = enabled;
    }

    public void SetMouthLineMosaic(bool enabled)
    {
        _mouthLineMosaic = enabled;
    }

    public VideoProcessingService(
        IServiceProvider serviceProvider,
        ILogger<VideoProcessingService> logger,
        IVideoHistoryService historyService)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _historyService = historyService;
        _currentFaceDetectionService = serviceProvider.GetRequiredKeyedService<IFaceDetectionService>(FaceDetectionMethod.Yolo);
    }

    public void SetFaceDetectionService(FaceDetectionMethod method)
    {
        _currentFaceDetectionService = _serviceProvider.GetRequiredKeyedService<IFaceDetectionService>(method);
    }

    public VideoInfo? GetVideoInfo(string videoPath)
    {
        try
        {
            using var capture = new VideoCapture(videoPath);
            if (!capture.IsOpened) return null;

            return new VideoInfo
            {
                FilePath = videoPath,
                FileName = Path.GetFileName(videoPath),
                Width = (int)capture.Get(CapProp.FrameWidth),
                Height = (int)capture.Get(CapProp.FrameHeight),
                Fps = capture.Get(CapProp.Fps),
                TotalFrames = (int)capture.Get(CapProp.FrameCount),
                Duration = TimeSpan.FromSeconds(capture.Get(CapProp.FrameCount) / capture.Get(CapProp.Fps))
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 完整视频处理管线：提取音频 → 提取帧 → 检测人脸 → 应用马赛克 → 合成视频
    /// </summary>
    public async Task<ProcessingResult> ProcessVideoAsync(
        string inputPath,
        ProcessingOptions options,
        IProgress<(int current, int total, string status)>? progress = null,
        Action<Mat>? onFrameProcessed = null,
        CancellationToken cancellationToken = default)
    {
        // 配置检测服务参数
        _currentFaceDetectionService.SetConfidenceThreshold(options.ConfidenceThreshold);
        _currentFaceDetectionService.SetFacialFeaturesEnabled(options.EnableFacialFeatures);
        _maskCoverageThreshold = options.MinMaskCoverageRatio;

        // 初始化面部解析服务（按需）
        if (options.EnableFaceParsing)
        {
            var faceParsing = _serviceProvider.GetRequiredService<IFaceParsingService>();
            if (!faceParsing.IsInitialized)
            {
                var inited = await faceParsing.InitializeAsync();
                if (inited)
                {
                    _faceParsingService = faceParsing;
                    _logger.LogInformation("面部解析服务已初始化");
                }
                else
                {
                    _logger.LogWarning("面部解析服务初始化失败，将使用矩形马赛克");
                }
            }
            else
            {
                _faceParsingService = faceParsing;
            }
        }
        else
        {
            _faceParsingService = null;
        }

        // 创建历史缓存目录结构（按视频文件哈希隔离，支持断点续传）
        var historyDir = _historyService.GetHistoryDirectory(inputPath);
        var stepFolder1 = _historyService.StepFolder1(historyDir);
        var stepFolder2 = _historyService.StepFolder2(historyDir);
        var stepFolder3 = _historyService.StepFolder3(historyDir);
        var audioFile = _historyService.AudioFile(historyDir);

        _historyService.EnsureHistoryDirectories(historyDir);

        var capture = new VideoCapture(inputPath);
        if (!capture.IsOpened) return new ProcessingResult { Success = false };

        // 优先使用 ffprobe 获取准确帧数（CapProp.FrameCount 对某些编码不可靠）
        int totalFrames = await FFmpegService.GetVideoFrameCountAsync(inputPath);
        if (totalFrames <= 0)
            totalFrames = (int)capture.Get(CapProp.FrameCount);

        int start = Math.Clamp(options.StartFrame, 0, totalFrames - 1);
        int end = options.EndFrame <= 0 ? totalFrames : Math.Clamp(options.EndFrame + 1, start, totalFrames);
        int framesToProcess = options.ProcessAllFrames ? totalFrames : end - start;

        await FFmpegService.ExtractAudioAsync(inputPath, audioFile, progress, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Run(() => ExtractFramesToStepFolder(inputPath, stepFolder1, framesToProcess, start, options, progress, cancellationToken, onFrameProcessed), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Run(() => DetectFacesToStepFolder(stepFolder1, stepFolder2, options, progress, cancellationToken, onFrameProcessed), cancellationToken);

        capture.Dispose();
        bool needsManualReview = _historyService.ReviewFramesFileExists(stepFolder2);
        return new ProcessingResult { Success = true, NeedsManualReview = needsManualReview, HistoryDir = historyDir, WasInterrupted = false };
    }

    /// <summary>Step 1: 使用 FFmpeg 从视频中提取帧序列保存为 JPG（比 Emgu.CV VideoCapture 更准确，支持 VFR）</summary>
    private void ExtractFramesToStepFolder(string inputPath, string outputFolder, int frameCount, int startFrame, ProcessingOptions options, IProgress<(int current, int total, string status)>? progress, CancellationToken cancellationToken, Action<Mat>? onFrameProcessed = null)
    {
        var existingFiles = _historyService.CountFrameFiles(outputFolder, "frame_*.jpg");
        if (existingFiles >= frameCount) return;

        // 清除不完整的残留文件，确保从头提取
        if (existingFiles > 0)
        {
            foreach (var f in Directory.GetFiles(outputFolder, "frame_*.jpg"))
                File.Delete(f);
        }

        var args = $"-i \"{inputPath}\" -qscale:v 2 -vsync 0 -start_number {startFrame} ";

        if (startFrame > 0)
            args += $"-vf \"select='gte(n,{startFrame})'\" ";

        // 仅对范围处理（非全帧）限制提取帧数，全帧处理时让 FFmpeg 提取到结尾
        // （CapProp.FrameCount 可能少于实际帧数，用 -frames:v 会丢失尾部帧导致不同步）
        if (!options.ProcessAllFrames && frameCount > 0)
            args += $"-frames:v {frameCount} ";

        args += $"\"{outputFolder}\\frame_%06d.jpg\"";

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = args,
            UseShellExecute = true,
            CreateNoWindow = true,
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
        };

        using var process = System.Diagnostics.Process.Start(psi);
        if (process == null) return;

        int timeout = 300;
        while (!process.HasExited && timeout > 0)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                if (!process.HasExited) process.Kill();
                return;
            }
            var currentFiles = Directory.GetFiles(outputFolder, "frame_*.jpg").Length;
            progress?.Report((currentFiles, frameCount, $"正在提取帧 {currentFiles}/{frameCount}..."));
            Thread.Sleep(1000);
            timeout--;
        }

        if (!process.HasExited)
        {
            process.Kill();
            throw new TimeoutException("FFmpeg 帧提取超时");
        }
    }

    /// <summary>Step 2: 检测每帧的人脸，保存人脸矩形、关键点凸包、面部解析掩码</summary>
    private void DetectFacesToStepFolder(string inputFolder, string outputFolder, ProcessingOptions options, IProgress<(int current, int total, string status)>? progress, CancellationToken cancellationToken, Action<Mat>? onFrameProcessed = null)
    {
        var files = _historyService.GetOrderedFrameFiles(inputFolder);
        int total = files.Length;

        if (total == 0) return;

        string jsonPath = _historyService.FaceDataPath(outputFolder);
        string reviewFramesPath = _historyService.ReviewFramesPath(outputFolder);

        // 检查是否需要重新检测（置信度阈值变化）
        bool needRedetect = false;
        var oldReviewFrames = _historyService.ReadReviewFrames(outputFolder);
        if (oldReviewFrames != null && oldReviewFrames.Count > 0)
        {
            // 如果之前保存的置信度阈值与当前不同，需要重新检测
            var firstFrame = oldReviewFrames.FirstOrDefault();
            if (firstFrame != null && Math.Abs(firstFrame.ConfidenceThreshold - options.ConfidenceThreshold) > 0.001f)
            {
                needRedetect = true;
            }
        }

        if (System.IO.File.Exists(jsonPath) && !needRedetect)
        {
            var step2Files = _historyService.CountFrameFiles(outputFolder);
            if (step2Files >= total)
            {
                progress?.Report((total, total, "人脸标注已存在，跳过..."));
                return;
            }
        }

        // 开始新的检测，清除上次残留的 review_frames.json
        if (System.IO.File.Exists(reviewFramesPath))
        {
            System.IO.File.Delete(reviewFramesPath);
        }

        Rectangle[]? lastFaces = null;
        FaceDetectionResult? lastResult = null;
        FaceDetail[]? lastFaceDetails = null;
        var reviewFrames = new List<ReviewFrame>();

        for (int i = 0; i < total; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string inputPath = files[i];
            string outputPath = System.IO.Path.Combine(outputFolder, System.IO.Path.GetFileName(inputPath));

            using var image = CvInvoke.Imread(inputPath);

            Rectangle[] faces;
            FaceDetail[] faceDetails;
            FaceDetectionResult? result = null;
            bool needDetect = i % options.FrameInterval == 0;

            if (needDetect)
            {
                result = _currentFaceDetectionService.DetectFaces(image, i);
                faces = result.Faces;
                faceDetails = result.FaceDetails;
                lastFaces = faces;
                lastResult = result;
                lastFaceDetails = faceDetails;
            }
            else if (lastFaces != null && options.FrameInterval > 1)
            {
                faces = lastFaces;
                faceDetails = lastFaceDetails ?? Array.Empty<FaceDetail>();
            }
            else
            {
                result = _currentFaceDetectionService.DetectFaces(image, i);
                faces = result.Faces;
                faceDetails = result.FaceDetails;
                lastFaces = faces;
                lastResult = result;
                lastFaceDetails = faceDetails;
            }

            if (faces.Length != 1)
            {
                var rf = new ReviewFrame
                {
                    FrameIndex = i,
                    FrameImagePath = inputPath,
                    MaxConfidence = result?.MaxConfidence ?? 0,
                    ConfidenceThreshold = options.ConfidenceThreshold
                };
                if (faces.Length > 1)
                {
                    rf.CustomFaceRegions.AddRange(faces);
                }
                reviewFrames.Add(rf);
            }

            var annotatedImage = image.Clone();
            foreach (var face in faces)
            {
                CvInvoke.Rectangle(annotatedImage, face, new MCvScalar(0, 255, 0), 2);
            }

            if (options.EnableFacialFeatures && faceDetails.Length > 0)
            {
                _logger.LogInformation("批量处理: 第 {Frame} 帧绘制 {Count} 组关键点", i, faceDetails.Length);
                DrawFaceKeypoints(annotatedImage, faceDetails);
            }

            if (options.EnableFaceParsing && faces.Length > 0 && _faceParsingService != null && _faceParsingService.IsInitialized)
            {
                _logger.LogInformation("批量处理: 第 {Frame} 帧绘制 {Count} 个面部解析轮廓", i, faces.Length);
                var masksDir = System.IO.Path.Combine(outputFolder, "masks");
                System.IO.Directory.CreateDirectory(masksDir);
                VisualizeParsingMask(annotatedImage, faces, masksDir, i);
            }

            CvInvoke.Imwrite(outputPath, annotatedImage);

            if (onFrameProcessed != null && i % options.FrameInterval == 0)
            {
                using var frameCopy = annotatedImage.Clone();
                onFrameProcessed(frameCopy);
            }

            annotatedImage.Dispose();

            var faceData = _historyService.ReadFaceData(outputFolder);
            var faceDataList = faces.Select(f => new FaceData { X = f.X, Y = f.Y, Width = f.Width, Height = f.Height }).ToList();
            if (options.EnableFacialFeatures && needDetect)
            {
                for (int fi = 0; fi < faceDetails.Length && fi < faceDataList.Count; fi++)
                {
                    var pts = GetLandmarkPoints(faceDetails[fi], image);
                    if (pts != null)
                    {
                        faceDataList[fi].Polygon = new List<int> { pts[0].X, pts[0].Y, pts[1].X, pts[1].Y, pts[2].X, pts[2].Y, pts[3].X, pts[3].Y };
                    }
                }
            }
            faceData.Add(new FrameFaceData { FrameIndex = i, Faces = faceDataList });
            _historyService.WriteFaceData(outputFolder, faceData);

            progress?.Report((i + 1, total, $"正在检测第 {i + 1}/{total} 帧人脸..."));
        }

        if (reviewFrames.Count > 0)
        {
            _historyService.WriteReviewFrames(outputFolder, reviewFrames);
        }
    }

    /// <summary>在图像上绘制面部关键点（圆圈）及其凸包连线</summary>
    private void DrawFaceKeypoints(Mat image, FaceDetail[] faceDetails)
    {
        _logger.LogInformation("绘制关键点: {Count} 个人脸", faceDetails.Length);

        var colors = new MCvScalar[]
        {
            new(255, 0, 0),   // 左眼 - 蓝色
            new(0, 0, 255),   // 右眼 - 红色
            new(0, 255, 255), // 鼻尖 - 黄色
            new(0, 255, 0),   // 左嘴角 - 绿色
            new(255, 0, 255), // 右嘴角 - 紫色
        };

        var label = new[] { "左眼", "右眼", "鼻尖", "左嘴角", "右嘴角" };

        foreach (var detail in faceDetails)
        {
            if (detail.Landmarks == null)
            {
                _logger.LogWarning("关键点为空, 跳过人脸 {Face}", detail.Face);
                continue;
            }

            var rawPoints = new (PointF pt, MCvScalar color, string name)[]
            {
                (detail.Landmarks.LeftEye, colors[0], label[0]),
                (detail.Landmarks.RightEye, colors[1], label[1]),
                (detail.Landmarks.Nose, colors[2], label[2]),
                (detail.Landmarks.LeftMouth, colors[3], label[3]),
                (detail.Landmarks.RightMouth, colors[4], label[4]),
            };

            var validPoints = new List<Point>();
            foreach (var (pt, color, name) in rawPoints)
            {
                var center = new Point((int)pt.X, (int)pt.Y);
                _logger.LogInformation("  关键点 {Name}: ({X}, {Y})", name, center.X, center.Y);

                if (center.X < 0 || center.Y < 0 || center.X >= image.Width || center.Y >= image.Height)
                {
                    _logger.LogWarning("  关键点 {Name} 超出图像范围! 图像尺寸: {W}x{H}", name, image.Width, image.Height);
                    continue;
                }

                validPoints.Add(center);
            }

            if (validPoints.Count > 1)
            {
                var connColor = new MCvScalar(255, 255, 255);
                if (validPoints.Count <= 2)
                {
                    CvInvoke.Line(image, validPoints[0], validPoints[1], connColor, 2);
                }
                else
                {
                    using var pts = new Emgu.CV.Util.VectorOfPoint(validPoints.ToArray());
                    using var hull = new Emgu.CV.Util.VectorOfPoint();
                    CvInvoke.ConvexHull(pts, hull, false);
                    CvInvoke.Polylines(image, hull.ToArray(), true, connColor, 2);
                }
            }

            foreach (var (pt, color, _) in rawPoints)
            {
                var center = new Point((int)pt.X, (int)pt.Y);
                if (center.X < 0 || center.Y < 0 || center.X >= image.Width || center.Y >= image.Height)
                    continue;

                CvInvoke.Circle(image, center, 8, color, -1);
                CvInvoke.Circle(image, center, 8, new MCvScalar(255, 255, 255), 3);
            }
        }
    }

    /// <summary>检查面部解析掩码中非零像素占比是否达到覆盖率阈值</summary>
    private bool IsMaskUsable(Mat mask, Rectangle region)
    {
        int nonZero = CvInvoke.CountNonZero(mask);
        double roiArea = region.Width * region.Height;
        double ratio = roiArea > 0 ? (double)nonZero / roiArea : 0;
        return ratio >= _maskCoverageThreshold;
    }

    /// <summary>在指定区域内按掩码位置应用像素化马赛克</summary>
    private static void ApplyMosaicWithMask(Mat image, Rectangle region, Mat faceMask, int blockSize)
    {
        int x = Math.Max(0, region.X);
        int y = Math.Max(0, region.Y);
        int width = Math.Min(region.Width, image.Width - x);
        int height = Math.Min(region.Height, image.Height - y);

        if (width <= 0 || height <= 0) return;

        using var rgbImage = new Mat();
        if (image.NumberOfChannels == 1)
            CvInvoke.CvtColor(image, rgbImage, ColorConversion.Gray2Bgr);
        else
            image.CopyTo(rgbImage);

        using var small = new Mat();
        var smallSize = new Size(Math.Max(1, width / blockSize), Math.Max(1, height / blockSize));
        using var roi = new Mat(rgbImage, new Rectangle(x, y, width, height));
        CvInvoke.Resize(roi, small, smallSize, 0, 0, Inter.Nearest);

        using var pixelated = new Mat();
        CvInvoke.Resize(small, pixelated, new Size(width, height), 0, 0, Inter.Nearest);

        pixelated.CopyTo(new Mat(rgbImage, new Rectangle(x, y, width, height)), faceMask);

        if (image.NumberOfChannels == 1)
        {
            using var grayResult = new Mat();
            CvInvoke.CvtColor(rgbImage, grayResult, ColorConversion.Bgr2Gray);
            grayResult.CopyTo(image);
        }
        else
        {
            rgbImage.CopyTo(image);
        }
    }

    /// <summary>Step 3: 对每帧图像应用马赛克（凸包 → 面部解析掩码 → 矩形兜底）</summary>
    private void ApplyMosaicToStepFolder(string stepFolder1, string stepFolder2, string stepFolder3, ProcessingOptions options, IProgress<(int current, int total, string status)>? progress, CancellationToken cancellationToken, Action<Mat>? onFrameProcessed = null)
    {
        _eyeLineMosaic = options.EyeLineMosaic;
        _mouthLineMosaic = options.MouthLineMosaic;
        var files = _historyService.GetOrderedFrameFiles(stepFolder1);
        int total = files.Length;

        if (total == 0) return;

        // 检查是否有手动标记的帧，如果有则需要重新应用马赛克
        bool hasManualMarks = _historyService.HasManualMarks(stepFolder2);

        var step3Files = _historyService.CountFrameFiles(stepFolder3);
        if (step3Files >= total && !hasManualMarks)
        {
            progress?.Report((total, total, $"马赛克已存在，跳过..."));
            return;
        }

        // 如果有手动标记，删除step3中的文件重新生成
        if (hasManualMarks && step3Files > 0)
        {
            _historyService.DeleteFrameFiles(stepFolder3);
            progress?.Report((0, total, $"检测到手动标记，重新应用马赛克..."));
        }

        var faceData = _historyService.ReadFaceData(stepFolder2);
        var reviewFrames = _historyService.ReadReviewFrames(stepFolder2);

        int blockSize = options.MosaicBlockSize;

        for (int i = 0; i < total; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string inputPath = files[i];
            string outputPath = System.IO.Path.Combine(stepFolder3, System.IO.Path.GetFileName(inputPath));

            if (System.IO.File.Exists(outputPath)) continue;

            using var image = CvInvoke.Imread(inputPath);

            Rectangle[] regionsToMosaic = GetRegionsForFrame(i, faceData, reviewFrames);
            List<List<int>>? facePolygons = options.EnableFacialFeatures ? GetPolygonsForFrame(i, faceData) : null;

            if (regionsToMosaic.Length > 0)
            {
                var masksDir = _faceParsingService != null && _faceParsingService.IsInitialized
                    ? System.IO.Path.Combine(stepFolder2, "masks") : null;
                bool hasCachedMasks = masksDir != null && System.IO.Directory.Exists(masksDir);

                for (int faceIdx = 0; faceIdx < regionsToMosaic.Length; faceIdx++)
                {
                    var rect = regionsToMosaic[faceIdx];

                    Point[]? hull = null;
                    if (facePolygons != null && faceIdx < facePolygons.Count && facePolygons[faceIdx].Count == 8)
                    {
                        var polygon = facePolygons[faceIdx];
                        hull = new Point[polygon.Count / 2];
                        for (int pi = 0; pi < hull.Length; pi++)
                            hull[pi] = new Point(polygon[pi * 2], polygon[pi * 2 + 1]);
                    }

                    Mat? parsingMask = null;
                    if (masksDir != null)
                    {
                        if (hasCachedMasks)
                        {
                            var maskPath = System.IO.Path.Combine(masksDir, $"frame_{i:D6}_face_{faceIdx:D2}.png");
                            if (System.IO.File.Exists(maskPath))
                                parsingMask = CvInvoke.Imread(maskPath, ImreadModes.Grayscale);
                        }

                        if (parsingMask == null)
                        {
                            using var faceRoi = new Mat(image, rect);
                            parsingMask = _faceParsingService!.ParseFaceMask(faceRoi);
                        }
                    }

                    ApplyMosaicToFace(image, rect, hull, parsingMask, blockSize);
                    parsingMask?.Dispose();
                }
            }

            CvInvoke.Imwrite(outputPath, image);

            if (onFrameProcessed != null && i % options.FrameInterval == 0)
            {
                using var frameCopy = image.Clone();
                onFrameProcessed(frameCopy);
            }

            progress?.Report((i + 1, total, $"正在应用马赛克第 {i + 1}/{total} 帧..."));
        }
    }

    /// <summary>获取指定帧中需要打马赛克的人脸矩形区域（优先使用手动标记区域）</summary>
    private static Rectangle[] GetRegionsForFrame(int frameIndex, List<FrameFaceData> faceData, List<ReviewFrame>? reviewFrames)
    {
        var faceRegions = faceData.FirstOrDefault(f => f.FrameIndex == frameIndex);
        var reviewFrame = reviewFrames?.FirstOrDefault(f => f.FrameIndex == frameIndex);

        if (reviewFrame?.HasCustomRegions == true)
        {
            return reviewFrame.CustomFaceRegions.ToArray();
        }

        if (faceRegions?.Faces != null && faceRegions.Faces.Count > 0)
        {
            return faceRegions.Faces.Select(f => new Rectangle(f.X, f.Y, f.Width, f.Height)).ToArray();
        }

        return Array.Empty<Rectangle>();
    }

    /// <summary>获取指定帧的面部关键点数据 [leX,leY, reX,reY, lmX,lmY, rmX,rmY]，无数据时返回 null</summary>
    private static List<List<int>>? GetPolygonsForFrame(int frameIndex, List<FrameFaceData> faceData)
    {
        var frame = faceData.FirstOrDefault(f => f.FrameIndex == frameIndex);
        if (frame?.Faces == null || frame.Faces.Count == 0)
            return null;

        var polygons = new List<List<int>>();
        bool anyPolygon = false;
        foreach (var face in frame.Faces)
        {
            polygons.Add(face.Polygon ?? new List<int>());
            if (face.Polygon != null && face.Polygon.Count == 8)
                anyPolygon = true;
        }

        return anyPolygon ? polygons : null;
    }



    /// <summary>矩形马赛克应用（降采样→升采样实现像素化）</summary>
    private class MosaicService
    {
        public void ApplyMosaicToRegion(Mat image, Rectangle region, int blockSize)
        {
            if (image.IsEmpty || region.Width <= 0 || region.Height <= 0) return;

            int x = Math.Max(0, region.X);
            int y = Math.Max(0, region.Y);
            int width = Math.Min(region.Width, image.Width - x);
            int height = Math.Min(region.Height, image.Height - y);

            if (width <= 0 || height <= 0) return;

            using var rgbImage = new Mat();
            if (image.NumberOfChannels == 1)
                CvInvoke.CvtColor(image, rgbImage, ColorConversion.Gray2Bgr);
            else
                image.CopyTo(rgbImage);

            using var small = new Mat();
            var smallRect = new Size(Math.Max(1, width / blockSize), Math.Max(1, height / blockSize));
            using var roi = new Mat(rgbImage, new Rectangle(x, y, width, height));
            CvInvoke.Resize(roi, small, smallRect, 0, 0, Inter.Nearest);

            using var large = new Mat();
            CvInvoke.Resize(small, large, new Size(width, height), 0, 0, Inter.Nearest);

            large.CopyTo(new Mat(rgbImage, new Rectangle(x, y, width, height)));

            if (image.NumberOfChannels == 1)
            {
                using var grayResult = new Mat();
                CvInvoke.CvtColor(rgbImage, grayResult, ColorConversion.Bgr2Gray);
                grayResult.CopyTo(image);
            }
            else
            {
                rgbImage.CopyTo(image);
            }
        }
    }



    /// <summary>获取指定帧的预览图像（含人脸框、关键点、马赛克效果）</summary>
    public async Task<Mat?> GetPreviewFrameAsync(string videoPath, int frameNumber = 0)
    {
        return await Task.Run(() =>
        {
            try
            {
                EnsureFaceParsingService();

                using var capture = new VideoCapture(videoPath);
                if (!capture.IsOpened) return null;

                capture.Set(CapProp.PosFrames, frameNumber);
                using var frame = new Mat();
                capture.Read(frame);

                if (frame.IsEmpty) return null;

                var result = _currentFaceDetectionService.DetectFaces(frame, frameNumber);
                _logger.LogInformation("预览: 检测到 {Count} 个人脸, 关键点组={KptGroups}, 五官解析={FacialFeatures}, 面部解析={FaceParsing}, 帧号: {FrameNumber}",
                    result.Faces.Length, result.FaceDetails.Length,
                    _currentFaceDetectionService.IsFacialFeaturesEnabled, _enableFaceParsing, frameNumber);
                if (result.Faces.Length > 0)
                {
                    if (_previewMosaicBlockSize > 0)
                    {
                        _logger.LogInformation("预览: 应用马赛克 blockSize={BlockSize}", _previewMosaicBlockSize);
                        ApplyPreviewMosaic(frame, result.Faces, result.FaceDetails);
                    }

                    foreach (var face in result.Faces)
                    {
                        _logger.LogDebug("预览: 绘制矩形 face={Face}", face);
                        CvInvoke.Rectangle(frame, face, new Emgu.CV.Structure.MCvScalar(0, 255, 0), 3);
                    }

                    if (_currentFaceDetectionService.IsFacialFeaturesEnabled && result.FaceDetails.Length > 0)
                    {
                        _logger.LogInformation("预览: 绘制 {Count} 组面部关键点", result.FaceDetails.Length);
                        DrawFaceKeypoints(frame, result.FaceDetails);
                    }

                    if (_enableFaceParsing && _faceParsingService != null && _faceParsingService.IsInitialized)
                    {
                        _logger.LogInformation("预览: 绘制 {Count} 个面部解析 mask", result.Faces.Length);
                        VisualizeParsingMask(frame, result.Faces);
                    }
                }

                var preview = new Mat();
                frame.CopyTo(preview);
                return preview;
            }
            catch { return null; }
        });
    }

    /// <summary>预览模式：对人脸区域应用马赛克，实时计算凸包和面部解析掩码</summary>
    private void ApplyPreviewMosaic(Mat image, Rectangle[] faces, FaceDetail[] faceDetails)
    {
        for (int i = 0; i < faces.Length; i++)
        {
            var face = faces[i];

            Point[]? hull = null;
            if (_currentFaceDetectionService.IsFacialFeaturesEnabled && i < faceDetails.Length)
                hull = GetLandmarkPoints(faceDetails[i], image);

            Mat? parsingMask = null;
            if (_enableFaceParsing && _faceParsingService != null && _faceParsingService.IsInitialized)
            {
                using var faceRoi = new Mat(image, face);
                parsingMask = _faceParsingService.ParseFaceMask(faceRoi);
            }

            ApplyMosaicToFace(image, face, hull, parsingMask, _previewMosaicBlockSize);
            parsingMask?.Dispose();
        }
    }

    /// <summary>统一的马赛克应用入口：面部关键点粗线条 → 面部解析掩码 → 矩形兜底</summary>
    private void ApplyMosaicToFace(Mat image, Rectangle faceRect, Point[]? landmarkPts, Mat? parsingMask, int blockSize)
    {
        if (landmarkPts != null && landmarkPts.Length == 4)
        {
            using var lineMask = CreateMaskFromLandmarkLines(landmarkPts, faceRect, _eyeLineMosaic, _mouthLineMosaic);
            if (lineMask != null)
            {
                ApplyMosaicWithMask(image, faceRect, lineMask, blockSize);
                return;
            }
        }

        if (parsingMask != null && !parsingMask.IsEmpty && IsMaskUsable(parsingMask, faceRect))
        {
            ApplyMosaicWithMask(image, faceRect, parsingMask, blockSize);
            return;
        }

        if (parsingMask != null && !parsingMask.IsEmpty)
            _logger.LogWarning("面部解析 mask 覆盖率不足, 回退到矩形马赛克");

        new MosaicService().ApplyMosaicToRegion(image, faceRect, blockSize);
    }

    /// <summary>从双眼和嘴角关键点生成粗线条二值掩码</summary>
    private static Mat? CreateMaskFromLandmarkLines(Point[] landmarks, Rectangle faceRect, bool drawEyeLine, bool drawMouthLine)
    {
        // landmarks: [leftEye, rightEye, leftMouth, rightMouth]
        var le = new Point(landmarks[0].X - faceRect.X, landmarks[0].Y - faceRect.Y);
        var re = new Point(landmarks[1].X - faceRect.X, landmarks[1].Y - faceRect.Y);
        var lm = new Point(landmarks[2].X - faceRect.X, landmarks[2].Y - faceRect.Y);
        var rm = new Point(landmarks[3].X - faceRect.X, landmarks[3].Y - faceRect.Y);

        bool hasEyes = drawEyeLine && landmarks[0].X >= 0 && landmarks[1].X >= 0;
        bool hasMouth = drawMouthLine && landmarks[2].X >= 0 && landmarks[3].X >= 0;
        // 双眼和嘴角都不可用时返回 null，上层回退到面部解析 mask 或矩形
        if (!hasEyes && !hasMouth) return null;

        var mask = new Mat(faceRect.Height, faceRect.Width, DepthType.Cv8U, 1);
        mask.SetTo(new MCvScalar(0));

        const int thickness = 50;
        if (hasEyes)
        {
            double eyeDx = re.X - le.X, eyeDy = re.Y - le.Y;
            int extend = (int)(Math.Sqrt(eyeDx * eyeDx + eyeDy * eyeDy) * 0.25);
            var (eyeStart, eyeEnd) = ExtendLine(le, re, Math.Max(extend, 10));
            CvInvoke.Line(mask, eyeStart, eyeEnd, new MCvScalar(255), thickness);
        }
        if (hasMouth)
        {
            CvInvoke.Line(mask, lm, rm, new MCvScalar(255), thickness);
        }

        return mask;
    }

    private static (Point start, Point end) ExtendLine(Point a, Point b, int extendPx)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1) return (a, b);
        double ux = dx / len;
        double uy = dy / len;
        return (
            new Point((int)(a.X - ux * extendPx), (int)(a.Y - uy * extendPx)),
            new Point((int)(b.X + ux * extendPx), (int)(b.Y + uy * extendPx))
        );
    }

    /// <summary>获取面部关键点坐标 [leftEye, rightEye, leftMouth, rightMouth]，任一对无效返回 null</summary>
    private Point[]? GetLandmarkPoints(FaceDetail detail, Mat image)
    {
        if (detail.Landmarks == null) return null;

        var le = new Point((int)detail.Landmarks.LeftEye.X, (int)detail.Landmarks.LeftEye.Y);
        var re = new Point((int)detail.Landmarks.RightEye.X, (int)detail.Landmarks.RightEye.Y);
        var lm = new Point((int)detail.Landmarks.LeftMouth.X, (int)detail.Landmarks.LeftMouth.Y);
        var rm = new Point((int)detail.Landmarks.RightMouth.X, (int)detail.Landmarks.RightMouth.Y);

        bool eyesValid = le.X >= 0 && le.Y >= 0 && le.X < image.Width && le.Y < image.Height
                      && re.X >= 0 && re.Y >= 0 && re.X < image.Width && re.Y < image.Height;
        bool mouthValid = lm.X >= 0 && lm.Y >= 0 && lm.X < image.Width && lm.Y < image.Height
                       && rm.X >= 0 && rm.Y >= 0 && rm.X < image.Width && rm.Y < image.Height;

        if (!eyesValid && !mouthValid) return null;

        return new[] { le, re, lm, rm };
    }

    /// <summary>延迟初始化面部解析服务，仅在预览使用且尚未初始化时触发</summary>
    private void EnsureFaceParsingService()
    {
        if (_enableFaceParsing && _faceParsingService == null && !_faceParsingInitAttempted)
        {
            _faceParsingInitAttempted = true;
            try
            {
                var service = _serviceProvider.GetService<IFaceParsingService>();
                if (service != null)
                {
                    service.InitializeAsync().GetAwaiter().GetResult();
                    if (service.IsInitialized)
                    {
                        _faceParsingService = service;
                        _logger.LogInformation("面部解析服务已就绪（预览模式）");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "面部解析服务初始化失败（预览模式）");
            }
        }
    }

    /// <summary>在图像上绘制面部解析轮廓线，并将掩码缓存到磁盘供 Step 3 复用</summary>
    private void VisualizeParsingMask(Mat image, Rectangle[] faces, string? masksDir = null, int frameIndex = -1)
    {
        for (int faceIdx = 0; faceIdx < faces.Length; faceIdx++)
        {
            var face = faces[faceIdx];
            try
            {
                using var faceRoi = new Mat(image, face);
                using var mask = _faceParsingService!.ParseFaceMask(faceRoi);
                if (mask.IsEmpty)
                {
                    _logger.LogWarning("  mask 为空, 跳过人脸 {Face}", face);
                    continue;
                }

                // 缓存掩码到磁盘，供 Step 3 复用
                if (masksDir != null && frameIndex >= 0)
                {
                    var maskPath = System.IO.Path.Combine(masksDir, $"frame_{frameIndex:D6}_face_{faceIdx:D2}.png");
                    CvInvoke.Imwrite(maskPath, mask);
                }

                _logger.LogInformation("  mask 尺寸: {W}x{H}, 人脸矩形: {Face}", mask.Width, mask.Height, face);

                // mask 坐标是相对于 faceRoi 的，需要放到全图对应位置
                using var fullMask = new Mat(image.Height, image.Width, DepthType.Cv8U, 1);
                fullMask.SetTo(new MCvScalar(0));
                using var targetRoi = new Mat(fullMask, face);
                mask.CopyTo(targetRoi);

                using var contours = new Emgu.CV.Util.VectorOfVectorOfPoint();
                using var hierarchy = new Mat();
                CvInvoke.FindContours(fullMask, contours, hierarchy, RetrType.External, ChainApproxMethod.ChainApproxSimple);

                const double minContourArea = 5.0;
                var validContours = new Emgu.CV.Util.VectorOfVectorOfPoint();
                int filteredCount = 0;
                double totalContourArea = 0;

                for (int i = 0; i < contours.Size; i++)
                {
                    var contour = contours[i];
                    if (contour.Length < 3) continue;
                    var area = CvInvoke.ContourArea(contour);
                    if (area < minContourArea)
                    {
                        filteredCount++;
                        continue;
                    }
                    validContours.Push(contour);
                    totalContourArea += area;
                    var bbox = CvInvoke.BoundingRectangle(contour);
                    _logger.LogInformation(
                        "  轮廓[{Idx}]: 点数={Points}, 面积={Area:F1}, 中心=({CX},{CY}), 包围盒=({BX},{BY},{BW},{BH})",
                        i, contour.Length, area,
                        bbox.X + bbox.Width / 2, bbox.Y + bbox.Height / 2,
                        bbox.X, bbox.Y, bbox.Width, bbox.Height);
                }

                double faceRoiArea = face.Width * face.Height;
                double coverageRatio = faceRoiArea > 0 ? totalContourArea / faceRoiArea : 0;

                if (filteredCount > 0)
                    _logger.LogInformation("  过滤了 {Count} 个面积小于 {MinArea} 的小轮廓", filteredCount, minContourArea);

                _logger.LogInformation(
                    "  面部解析轮廓汇总: {Count} 条有效轮廓, 总轮廓面积={TotalArea:F1}, 人脸ROI面积={RoiArea:F1}, 覆盖率={Coverage:P1}",
                    validContours.Size, totalContourArea, faceRoiArea, coverageRatio);

                if (validContours.Size > 0)
                {
                    CvInvoke.DrawContours(image, validContours, -1, new MCvScalar(0, 255, 255), 2);
                    _logger.LogInformation("  已绘制 {Count} 条轮廓到预览帧", validContours.Size);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "绘制面部解析轮廓失败");
            }
        }
    }

    public async Task<bool> ProcessRemainingAsync(string inputPath, ProcessingOptions options,
        IProgress<(int current, int total, string status)>? progress = null,
        Action<Mat>? onFrameProcessed = null,
        CancellationToken cancellationToken = default)
    {
        var historyDir = _historyService.GetHistoryDirectory(inputPath);
        var stepFolder1 = _historyService.StepFolder1(historyDir);
        var stepFolder2 = _historyService.StepFolder2(historyDir);
        var stepFolder3 = _historyService.StepFolder3(historyDir);
        var audioFile = _historyService.AudioFile(historyDir);

        var capture = new VideoCapture(inputPath);
        if (!capture.IsOpened) return false;

        int totalFrames = (int)capture.Get(CapProp.FrameCount);
        int start = Math.Clamp(options.StartFrame, 0, totalFrames - 1);
        int end = options.EndFrame <= 0 ? totalFrames : Math.Clamp(options.EndFrame + 1, start, totalFrames);
        int framesToProcess = options.ProcessAllFrames ? totalFrames : end - start;

        cancellationToken.ThrowIfCancellationRequested();
        await Task.Run(() => ApplyMosaicToStepFolder(stepFolder1, stepFolder2, stepFolder3, options, progress, cancellationToken, onFrameProcessed), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        // 统计实际帧数并计算正确的输出帧率，确保视频时长与音频匹配
        var actualFrameFiles = Directory.GetFiles(stepFolder3, "*.jpg");
        int actualFrameCount = actualFrameFiles.Length;

        double fps;
        if (options.OutputFps > 0)
        {
            fps = options.OutputFps;
        }
        else
        {
            fps = capture.Get(CapProp.Fps);
            if (options.ProcessAllFrames && actualFrameCount > 0)
            {
                double duration = await FFmpegService.GetVideoDurationAsync(inputPath, cancellationToken);
                if (duration > 0)
                {
                    fps = actualFrameCount / duration;
                    fps = Math.Clamp(fps, 1, 120);
                }
            }
        }

        await FFmpegService.CombineImagesWithAudioAsync(stepFolder3, audioFile, options.OutputPath, fps, options.VideoCodec, progress, cancellationToken);

        _historyService.CleanupProcessingFolders(stepFolder2, stepFolder3);
        capture.Dispose();
        return !cancellationToken.IsCancellationRequested;
    }

    public void Dispose() { }
}