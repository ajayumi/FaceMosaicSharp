using System.Drawing;

namespace FaceMosaicSharp.Models;

public class FaceDetectionResult
{
    /// <summary>检测到的人脸区域数组</summary>
    public Rectangle[] Faces { get; set; } = Array.Empty<Rectangle>();

    /// <summary>人脸详细信息（含关键点）</summary>
    public FaceDetail[] FaceDetails { get; set; } = Array.Empty<FaceDetail>();

    /// <summary>帧索引</summary>
    public int FrameIndex { get; set; }

    /// <summary>当前使用的置信度阈值</summary>
    public float ConfidenceThreshold { get; set; }

    /// <summary>最高置信度（用于显示）</summary>
    public float MaxConfidence { get; set; }
}

/// <summary>人脸详细信息（含边界框和关键点）</summary>
public class FaceDetail
{
    /// <summary>人脸边界框</summary>
    public Rectangle Face { get; set; }

    /// <summary>面部关键点（左右眼、鼻尖、左右嘴角）</summary>
    public FaceLandmark? Landmarks { get; set; }
}

/// <summary>面部关键点</summary>
public class FaceLandmark
{
    /// <summary>左眼</summary>
    public PointF LeftEye { get; set; }

    /// <summary>右眼</summary>
    public PointF RightEye { get; set; }

    /// <summary>鼻尖</summary>
    public PointF Nose { get; set; }

    /// <summary>左嘴角</summary>
    public PointF LeftMouth { get; set; }

    /// <summary>右嘴角</summary>
    public PointF RightMouth { get; set; }
}