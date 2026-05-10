using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace FaceMosaicSharp.Services;

/// <summary>
/// 面部解析服务：加载 ONNX 模型，对人脸区域进行像素级语义分割，
/// 生成二值化人脸掩码（255=人脸, 0=背景）。
/// </summary>
public class FaceParsingService : IFaceParsingService
{
    private InferenceSession? _session;                   // ONNX Runtime 推理会话
    private bool _isInitialized;                          // 模型是否已成功加载
    private readonly object _lock = new();                 // 线程安全锁
    private string _inputName = "input";                  // 模型输入节点名称
    private string _outputName = "output";                // 模型输出节点名称
    private readonly ILogger<FaceParsingService> _logger; // 日志记录器
    private const int InputSize = 512;                    // 模型输入图片尺寸 (512x512)
    private const int NumClasses = 19;                    // 模型输出的语义类别数

    public bool IsInitialized => _isInitialized;

    public FaceParsingService(ILogger<FaceParsingService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 异步初始化：加载 ONNX 模型到推理会话中。
    /// </summary>
    public async Task<bool> InitializeAsync()
    {
        return await Task.Run(() =>
        {
            lock (_lock)
            {
                // 已初始化则直接返回
                if (_isInitialized) return true;

                try
                {
                    // 检查模型文件是否存在
                    var modelPath = GetModelPath();
                    if (!File.Exists(modelPath))
                    {
                        _logger.LogWarning("面部解析模型文件未找到: {ModelPath}", modelPath);
                        return false;
                    }

                    _logger.LogInformation("正在加载面部解析模型: {ModelPath}", modelPath);
                    var sessionOptions = new SessionOptions();
                    sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
                    try { sessionOptions.AppendExecutionProvider_DML(); }
                    catch (Exception ex) { _logger.LogWarning(ex, "DirectML 不可用，回退到 CPU 执行"); }

                    // 创建 ONNX Runtime 推理会话
                    _session = new InferenceSession(modelPath, sessionOptions);
                    _isInitialized = _session != null;

                    // 读取模型输入/输出节点名称
                    if (_session != null)
                    {
                        _inputName = _session.InputNames.FirstOrDefault() ?? "input";
                        _outputName = _session.OutputNames.FirstOrDefault() ?? "output";
                        _logger.LogInformation("面部解析模型加载成功. 输入: {Input}, 输出: {Output}",
                            _inputName, _outputName);
                    }

                    return _isInitialized;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "面部解析模型初始化失败");
                    return false;
                }
            }
        });
    }

    /// <summary>
    /// 获取 ONNX 模型文件的完整路径。
    /// </summary>
    private static string GetModelPath()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        return Path.Combine(baseDir, "Assets", "resnet18.onnx");
    }

    /// <summary>
    /// 对一张人脸 ROI 图像进行语义分割，返回二值化掩码（255=人脸区域, 0=背景）。
    /// </summary>
    public Mat ParseFaceMask(Mat faceRoi)
    {
        lock (_lock)
        {
            // 未初始化或输入为空时返回空 Mat
            if (!_isInitialized || _session == null || faceRoi.IsEmpty)
                return new Mat();

            try
            {
                // 缩放到模型输入尺寸
                using var resized = new Mat();
                CvInvoke.Resize(faceRoi, resized, new Size(InputSize, InputSize));

                // 预处理：BGR→RGB + 归一化
                var inputTensor = PreprocessImage(resized);
                var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor(_inputName, inputTensor)
                };

                // 执行 ONNX 推理
                using var results = _session.Run(inputs);
                var outputTensor = results.First().AsTensor<float>();

                // 后处理：argmax → 二值化 → 缩回原尺寸
                var binaryMask = PostprocessMask(outputTensor, faceRoi.Width, faceRoi.Height);
                return binaryMask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "面部解析失败");
                return new Mat();
            }
        }
    }

    /// <summary>
    /// 图像预处理：BGR → RGB 转换，归一化（ImageNet 均值/标准差），
    /// 输出 shape 为 [1, 3, 512, 512] 的张量。
    /// </summary>
    private static Tensor<float> PreprocessImage(Mat image)
    {
        var tensor = new DenseTensor<float>(new[] { 1, 3, InputSize, InputSize });

        using var rgb = new Mat();
        CvInvoke.CvtColor(image, rgb, ColorConversion.Bgr2Rgb);

        using var imageBytes = rgb.ToImage<Rgb, byte>();
        var bytes = imageBytes.Bytes;

        // ImageNet 标准化参数
        float[] mean = { 0.485f, 0.456f, 0.406f };
        float[] std = { 0.229f, 0.224f, 0.225f };

        // 逐像素归一化：pixel = (pixel/255 - mean) / std
        for (int c = 0; c < 3; c++)
        {
            for (int y = 0; y < InputSize; y++)
            {
                for (int x = 0; x < InputSize; x++)
                {
                    int idx = (y * InputSize + x) * 3 + c;
                    tensor[0, c, y, x] = (bytes[idx] / 255.0f - mean[c]) / std[c];
                }
            }
        }

        return tensor;
    }

    /// <summary>
    /// 模型输出后处理：argmax 获取每个像素的预测类别 → 缩放到原图尺寸 → 二值化（类别 > 0 视为人脸）。
    /// </summary>
    private Mat PostprocessMask(Tensor<float> output, int targetWidth, int targetHeight)
    {
        // 解析输出维度
        var dims = output.Dimensions;
        int height = dims.Length >= 4 ? dims[2] : InputSize;
        int width = dims.Length >= 4 ? dims[3] : InputSize;

        // argmax：取每个像素上概率最高的类别索引
        var maskData = new byte[height * width];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float maxVal = float.MinValue;
                int maxIdx = 0;
                for (int c = 0; c < NumClasses; c++)
                {
                    float val = output[0, c, y, x];
                    if (val > maxVal)
                    {
                        maxVal = val;
                        maxIdx = c;
                    }
                }
                maskData[y * width + x] = (byte)(maxIdx switch
                {
                    0 or 14 or 15 or 16 or 17 or 18 => 0,
                    _ => 255
                });
            }
        }

        // 将 mask 数据复制到 Mat 中，缩放回原始 ROI 尺寸
        var handle = GCHandle.Alloc(maskData, GCHandleType.Pinned);
        try
        {
            using var srcMat = new Mat(height, width, DepthType.Cv8U, 1,
                handle.AddrOfPinnedObject(), width);

            using var resized = new Mat();
            CvInvoke.Resize(srcMat, resized, new Size(targetWidth, targetHeight), 0, 0, Inter.Nearest);

            // 二值化：类别索引 > 0 置 255（人脸），=0 置 0（背景）
            var binaryMask = new Mat();
            CvInvoke.Threshold(resized, binaryMask, 0, 255, ThresholdType.Binary);

            return binaryMask;
        }
        finally
        {
            handle.Free();
        }
    }

    /// <summary>
    /// 释放推理会话资源。
    /// </summary>
    public void Dispose()
    {
        lock (_lock)
        {
            _session?.Dispose();
            _session = null;
            _isInitialized = false;
        }
    }
}
