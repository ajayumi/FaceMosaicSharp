using System.Drawing;
using System.IO;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using FaceMosaicSharp.Models;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace FaceMosaicSharp.Services;

/// <summary>
/// YOLO ONNX 模型人脸检测服务
/// 使用预训练的YOLO模型进行人脸检测
/// </summary>
[FaceDetectionMethod(FaceDetectionMethod.Yolo)]
public class YoloFaceDetectionService : IFaceDetectionService
{
    private InferenceSession? _session;       // ONNX Runtime 推理会话
    private bool _isInitialized;              // 模型是否已成功加载
    private readonly object _lock = new();    // 线程安全锁
    private float _confidenceThreshold = 0.3f; // 检测置信度阈值
    private float _nmsThreshold = 0.45f;       // NMS 交并比阈值
    private string _inputName = "images";      // 模型输入节点名称
    private string _outputName = "output0";    // 模型输出节点名称
    private bool _facialFeaturesEnabled;       // 是否启用五官关键点解析
    private readonly ILogger<YoloFaceDetectionService> _logger;

    public bool IsInitialized => _isInitialized;
    public bool IsFacialFeaturesEnabled => _facialFeaturesEnabled;

    public YoloFaceDetectionService(ILogger<YoloFaceDetectionService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 异步初始化YOLO模型
    /// </summary>
    public async Task<bool> InitializeAsync()
    {
        return await Task.Run(() =>
        {
            lock (_lock)
            {
                if (_isInitialized) return true;

                try
                {
                    var modelPath = GetModelPath();
                    if (!File.Exists(modelPath))
                    {
                        _logger.LogWarning("YOLO模型文件未找到: {ModelPath}", modelPath);
                        return false;
                    }

                    _logger.LogInformation("正在加载YOLO模型: {ModelPath}", modelPath);

                    var sessionOptions = new SessionOptions();
                    sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
                    try { sessionOptions.AppendExecutionProvider_DML(); }
                    catch (Exception ex) { _logger.LogWarning(ex, "DirectML 不可用，回退到 CPU 执行"); }
                    _logger.LogInformation("加载YOLO模型");

                    _session = new InferenceSession(modelPath, sessionOptions);
                    _isInitialized = _session != null;

                    if (_session != null)
                    {
                        _inputName = _session.InputNames.FirstOrDefault() ?? "images";
                        _outputName = _session.OutputNames.FirstOrDefault() ?? "output0";
                        _logger.LogInformation("YOLO模型加载成功. 输入: {@InputMetadata}, 输出: {@OutputMetadata}", _session.InputMetadata, _session.OutputMetadata);
                    }

                    return _isInitialized;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "YOLO模型初始化失败");
                    return false;
                }
            }
        });
    }

    /// <summary>
    /// 获取模型文件路径
    /// </summary>
    private static string GetModelPath()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        return Path.Combine(baseDir, "Assets", "yoloface_8n.onnx");
    }

    /// <summary>
    /// 检测图像中的人脸
    /// </summary>
    public FaceDetectionResult DetectFaces(Mat image, int frameIndex = 0)
    {
        lock (_lock)
        {
            if (!_isInitialized || _session == null || image.IsEmpty)
            {
                return new FaceDetectionResult { FrameIndex = frameIndex };
            }

            try
            {
                _logger.LogInformation("正在检测第 {FrameIndex} 帧, 图像尺寸: {Width}x{Height}, 五官解析: {FacialFeatures}", frameIndex, image.Width, image.Height, _facialFeaturesEnabled);

                using var rgbImage = new Mat();
                if (image.NumberOfChannels == 1)
                {
                    CvInvoke.CvtColor(image, rgbImage, ColorConversion.Gray2Bgr);
                }
                else
                {
                    image.CopyTo(rgbImage);
                }

                var inputTensor = PreprocessImage(rgbImage);
                var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor(_inputName, inputTensor)
                };

                _logger.LogInformation("正在执行YOLO推理, 输入: {InputName}", _inputName);
                using var results = _session.Run(inputs);
                var output = results.FirstOrDefault();
                _logger.LogInformation("YOLO推理完成, 处理结果");

                var (detections, landmarks, maxConfidence) = PostprocessResults(output, rgbImage.Width, rgbImage.Height);
                _logger.LogInformation("检测到 {Count} 个人脸候选区域", detections.Length);

                var validFaces = FilterDetections(detections, image.Width, image.Height);
                _logger.LogInformation("过滤后剩余 {Count} 个人脸区域, 当前置信度阈值: {Threshold}, 最高置信度: {Max}", validFaces.Length, _confidenceThreshold, maxConfidence);

                var faceDetails = BuildFaceDetails(detections, landmarks);

                return new FaceDetectionResult
                {
                    Faces = validFaces,
                    FaceDetails = faceDetails,
                    FrameIndex = frameIndex,
                    ConfidenceThreshold = _confidenceThreshold,
                    MaxConfidence = maxConfidence
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检测人脸时发生错误");
                return new FaceDetectionResult { FrameIndex = frameIndex };
            }
        }
    }

    /// <summary>
    /// 图像预处理：将图像转换为ONNX模型输入张量
    /// </summary>
    /// <param name="image">输入图像</param>
    /// <returns>预处理后的张量</returns>
    private static Tensor<float> PreprocessImage(Mat image)
    {
        int width = 640;
        int height = 640;

        using var resized = new Mat();
        CvInvoke.Resize(image, resized, new Size(width, height));

        var tensor = new DenseTensor<float>(new[] { 1, 3, height, width });

        using var rgb = new Mat();
        CvInvoke.CvtColor(resized, rgb, ColorConversion.Bgr2Rgb);

        using var rgbMat = rgb.ToImage<Bgr, byte>();
        var bytes = rgbMat.Bytes;

        for (int c = 0; c < 3; c++)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = (y * width + x) * 3 + c;
                    tensor[0, c, y, x] = bytes[index] / 255.0f;
                }
            }
        }

        return tensor;
    }

    /// <summary>
    /// 后处理模型输出：解析检测结果并转换为矩形区域和关键点
    /// </summary>
    private (Rectangle[] rectangles, FaceLandmark[] landmarks, float maxConfidence) PostprocessResults(NamedOnnxValue output, int imageWidth, int imageHeight)
    {
        var tensor = output.AsTensor<float>();
        var dims = tensor.Dimensions;

        if (dims.Length < 3)
        {
            return (Array.Empty<Rectangle>(), Array.Empty<FaceLandmark>(), 0f);
        }

        int channels = dims[1];
        int numDetections = dims[2];
        bool hasLandmarks = channels >= 15 && _facialFeaturesEnabled;

        _logger.LogInformation("Postprocess: 输出通道数={Channels}, 检测数={NumDetections}, 五官解析={FacialFeatures}, 可解析关键点={HasLandmarks}",
            channels, numDetections, _facialFeaturesEnabled, hasLandmarks);

        var detections = new List<Rectangle>();
        var landmarks = new List<FaceLandmark>();
        float maxConfidence = 0f;

        float scaleX = (float)imageWidth / 640f;
        float scaleY = (float)imageHeight / 640f;

        for (int i = 0; i < numDetections; i++)
        {
            float confidence = tensor[0, 4, i];
            if (confidence > maxConfidence)
            {
                maxConfidence = confidence;
            }

            if (confidence < _confidenceThreshold)
            {
                continue;
            }

            float x = tensor[0, 0, i] * scaleX;
            float y = tensor[0, 1, i] * scaleY;
            float w = tensor[0, 2, i] * scaleX;
            float h = tensor[0, 3, i] * scaleY;

            int x1 = (int)(x - w / 2);
            int y1 = (int)(y - h / 2);
            int x2 = (int)(x + w / 2);
            int y2 = (int)(y + h / 2);

            x1 = Math.Max(0, Math.Min(x1, imageWidth - 1));
            y1 = Math.Max(0, Math.Min(y1, imageHeight - 1));
            x2 = Math.Max(0, Math.Min(x2, imageWidth));
            y2 = Math.Max(0, Math.Min(y2, imageHeight));

            if (x2 > x1 && y2 > y1)
            {
                detections.Add(new Rectangle(x1, y1, x2 - x1, y2 - y1));

                if (hasLandmarks)
                {
                    _logger.LogInformation("  原始通道 [5-19]: {V5:F2} {V6:F2} {V7:F2} {V8:F2} {V9:F2} {V10:F2} {V11:F2} {V12:F2} {V13:F2} {V14:F2} {V15:F2} {V16:F2} {V17:F2} {V18:F2} {V19:F2}",
                        tensor[0, 5, i], tensor[0, 6, i], tensor[0, 7, i],
                        tensor[0, 8, i], tensor[0, 9, i], tensor[0, 10, i],
                        tensor[0, 11, i], tensor[0, 12, i], tensor[0, 13, i],
                        tensor[0, 14, i], tensor[0, 15, i], tensor[0, 16, i],
                        tensor[0, 17, i], tensor[0, 18, i], tensor[0, 19, i]);

                    // 20通道布局: 4(bbox) + 1(conf) + 5*kpt(x, y, conf) = 20
                    // 每3个通道一组: (x, y, confidence)
                    landmarks.Add(new FaceLandmark
                    {
                        LeftEye = new PointF(tensor[0, 5, i] * scaleX, tensor[0, 6, i] * scaleY),
                        RightEye = new PointF(tensor[0, 8, i] * scaleX, tensor[0, 9, i] * scaleY),
                        Nose = new PointF(tensor[0, 11, i] * scaleX, tensor[0, 12, i] * scaleY),
                        LeftMouth = new PointF(tensor[0, 14, i] * scaleX, tensor[0, 15, i] * scaleY),
                        RightMouth = new PointF(tensor[0, 17, i] * scaleX, tensor[0, 18, i] * scaleY),
                    });
                }
            }
        }

        var (nmsRects, nmsLandmarks) = NmsWithLandmarks(detections, landmarks);

        if (_facialFeaturesEnabled)
        {
            _logger.LogInformation("Postprocess: NMS后矩形={RectCount}, 关键点={KptCount}, 最高置信度={MaxConf:F4}",
                nmsRects.Length, nmsLandmarks.Length, maxConfidence);
        }

        return (nmsRects, nmsLandmarks, maxConfidence);
    }

    /// <summary>
    /// 过滤检测结果：根据面积比例移除不合理的人脸区域
    /// </summary>
    private Rectangle[] FilterDetections(Rectangle[] detections, int frameWidth, int frameHeight)
    {
        return detections;
        return detections.Where(f =>
        {
            int faceArea = f.Width * f.Height;
            double faceRatio = (double)faceArea / (frameWidth * frameHeight);
            if (faceRatio > 0.3 || faceRatio < 0.001)
            {
                _logger.LogDebug("过滤: 面积比例 {FaceRatio:F6} 超出范围 [0.001, 0.3], 框: {Rect}, 面积: {Area}, 图像: {Width}x{Height}", faceRatio, f, faceArea, frameWidth, frameHeight);
                return false;
            }

            return true;
        }).ToArray();
    }

    /// <summary>
    /// 构建人脸详细信息（合并边界框和关键点）
    /// </summary>
    private static FaceDetail[] BuildFaceDetails(Rectangle[] faces, FaceLandmark[] landmarks)
    {
        if (faces.Length == 0)
        {
            return Array.Empty<FaceDetail>();
        }

        var details = new FaceDetail[faces.Length];
        bool hasLandmarks = landmarks.Length > 0 && landmarks.Length == faces.Length;

        for (int i = 0; i < faces.Length; i++)
        {
            details[i] = new FaceDetail
            {
                Face = faces[i],
                Landmarks = hasLandmarks ? landmarks[i] : null
            };
        }

        return details;
    }

    /// <summary>
    /// 非极大值抑制（NMS）：移除重叠的检测框，同步过滤关键点
    /// </summary>
    private (Rectangle[] rects, FaceLandmark[] landmarks) NmsWithLandmarks(List<Rectangle> detections, List<FaceLandmark> landmarks)
    {
        if (detections.Count == 0)
        {
            return (Array.Empty<Rectangle>(), Array.Empty<FaceLandmark>());
        }

        int landmarkCount = landmarks.Count;
        var hasLandmarks = landmarkCount > 0 && landmarkCount == detections.Count;

        var sorted = detections
            .Select((r, i) => new { Rect = r, Area = r.Width * r.Height, Index = i })
            .OrderByDescending(x => x.Area)
            .ToList();

        var keepRects = new List<Rectangle>();
        var keepLandmarks = new List<FaceLandmark>();
        var suppressed = new HashSet<int>();

        foreach (var current in sorted)
        {
            if (suppressed.Contains(current.Index))
            {
                continue;
            }

            keepRects.Add(current.Rect);
            if (hasLandmarks && current.Index < landmarkCount && landmarks[current.Index] != null)
            {
                keepLandmarks.Add(landmarks[current.Index]);
            }
            suppressed.Add(current.Index);

            foreach (var other in sorted)
            {
                if (suppressed.Contains(other.Index) || other.Index == current.Index)
                {
                    continue;
                }

                if (CalculateIoU(current.Rect, other.Rect) > _nmsThreshold)
                {
                    suppressed.Add(other.Index);
                }
            }
        }

        return (keepRects.ToArray(), keepLandmarks.ToArray());
    }

    /// <summary>
    /// 计算两个矩形的交并比（IoU）
    /// </summary>
    private static double CalculateIoU(Rectangle a, Rectangle b)
    {
        int x1 = Math.Max(a.X, b.X);
        int y1 = Math.Max(a.Y, b.Y);
        int x2 = Math.Min(a.X + a.Width, b.X + b.Width);
        int y2 = Math.Min(a.Y + a.Height, b.Y + b.Height);

        if (x2 < x1 || y2 < y1)
        {
            return 0;
        }

        int intersection = (x2 - x1) * (y2 - y1);
        int union = a.Width * a.Height + b.Width * b.Height - intersection;

        return (double)intersection / union;
    }

    /// <summary>
    /// 对图像中的人脸区域应用马赛克
    /// </summary>
    public void ApplyMosaic(Mat image, Rectangle[] faces, int blockSize)
    {
        if (image.IsEmpty || faces.Length == 0) return;

        foreach (var face in faces)
        {
            ApplyMosaicToRegion(image, face, blockSize);
        }
    }

    /// <summary>
    /// 对指定区域应用马赛克效果（使用缩放法）
    /// </summary>
    private static void ApplyMosaicToRegion(Mat image, Rectangle region, int blockSize)
    {
        if (image.IsEmpty || region.Width <= 0 || region.Height <= 0) return;

        int x = Math.Max(0, region.X);
        int y = Math.Max(0, region.Y);
        int width = Math.Min(region.Width, image.Width - x);
        int height = Math.Min(region.Height, image.Height - y);

        if (width <= 0 || height <= 0) return;

        using var rgbImage = new Mat();
        if (image.NumberOfChannels == 1)
        {
            CvInvoke.CvtColor(image, rgbImage, ColorConversion.Gray2Bgr);
        }
        else
        {
            image.CopyTo(rgbImage);
        }

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

    public void Dispose()
    {
        lock (_lock)
        {
            _session?.Dispose();
            _session = null;
            _isInitialized = false;
        }
    }

    public void SetConfidenceThreshold(float threshold)
    {
        _confidenceThreshold = threshold;
    }

    public void SetFacialFeaturesEnabled(bool enabled)
    {
        _facialFeaturesEnabled = enabled;
    }
}