using System.Drawing;

namespace FaceMosaicSharp.Models;

/// <summary>
/// 需要人工审核的帧记录（未检测到人脸/检测到多个人脸），用于后续手动标记处理
/// </summary>
public class ReviewFrame
{
    /// <summary>帧索引</summary>
    public int FrameIndex { get; set; }

    /// <summary>帧图像文件路径</summary>
    public string FrameImagePath { get; set; } = string.Empty;

    /// <summary>用户手动标记的人脸区域列表</summary>
    public List<Rectangle> CustomFaceRegions { get; set; } = new();

    /// <summary>检测时的最高置信度</summary>
    public float MaxConfidence { get; set; }

    /// <summary>检测时使用的置信度阈值</summary>
    public float ConfidenceThreshold { get; set; }

    /// <summary>是否包含手动标记的区域</summary>
    public bool HasCustomRegions => CustomFaceRegions.Count > 0;
}
