namespace FaceMosaicSharp.Models;

/// <summary>
/// 人脸区域数据（用于JSON序列化）
/// </summary>
public class FaceData
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    /// <summary>面部关键点坐标 [leftEyeX,leftEyeY, rightEyeX,rightEyeY, leftMouthX,leftMouthY, rightMouthX,rightMouthY]，图像坐标系</summary>
    public List<int>? Polygon { get; set; }
}

/// <summary>
/// 帧人脸数据（用于JSON序列化）
/// </summary>
public class FrameFaceData
{
    public int FrameIndex { get; set; }
    public List<FaceData> Faces { get; set; } = new();
}
