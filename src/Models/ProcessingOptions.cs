using FaceMosaicSharp.Services;

namespace FaceMosaicSharp.Models;

/// <summary>
/// 视频处理选项配置类
/// </summary>
public class ProcessingOptions
{
    /// <summary>马赛克块大小（像素），值越大马赛克效果越明显</summary>
    public int MosaicBlockSize { get; set; } = 15;

    /// <summary>输出视频格式（mp4/avi/mov/wmv）</summary>
    public string OutputFormat { get; set; } = "mp4";

    /// <summary>视频编码器（mp4v/H264/XVID/avc1）</summary>
    public string VideoCodec { get; set; } = "mp4v";

    /// <summary>输出帧率（0表示与原视频相同）</summary>
    public int OutputFps { get; set; } = 30;

    /// <summary>是否处理所有帧</summary>
    public bool ProcessAllFrames { get; set; } = true;

    /// <summary>起始帧索引（从0开始，仅在ProcessAllFrames为false时生效）</summary>
    public int StartFrame { get; set; } = 0;

    /// <summary>结束帧索引（0表示到最后一帧，仅在ProcessAllFrames为false时生效）</summary>
    public int EndFrame { get; set; } = 0;

    /// <summary>帧检测间隔（每间隔N帧检测一次，中间帧复用检测结果）</summary>
    public int FrameInterval { get; set; } = 1;

    /// <summary>输出文件路径</summary>
    public string OutputPath { get; set; } = string.Empty;

    /// <summary>人脸检测方法</summary>
    public FaceDetectionMethod FaceDetectionMethod { get; set; } = FaceDetectionMethod.Yolo;

    /// <summary>人脸检测置信度阈值</summary>
    public float ConfidenceThreshold { get; set; } = 0.6f;

    /// <summary>是否启用五官解析（解析面部关键点：左右眼、鼻尖、左右嘴角）</summary>
    public bool EnableFacialFeatures { get; set; } = false;

    /// <summary>启用五官解析时，是否绘制眼睛线条打马赛克</summary>
    public bool EyeLineMosaic { get; set; } = true;

    /// <summary>启用五官解析时，是否绘制嘴巴线条打马赛克</summary>
    public bool MouthLineMosaic { get; set; } = true;

    /// <summary>是否启用面部解析（BiSeNet 像素级分割，按人脸轮廓打马赛克）</summary>
    public bool EnableFaceParsing { get; set; } = false;

    /// <summary>面部解析掩码覆盖率阈值（0~1），低于此值时回退到矩形马赛克</summary>
    public float MinMaskCoverageRatio { get; set; } = 0.05f;
}