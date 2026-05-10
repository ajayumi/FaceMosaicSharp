namespace FaceMosaicSharp.Models;

/// <summary>
/// 视频信息模型，包含视频文件的基本属性
/// </summary>
public class VideoInfo
{
    /// <summary>视频文件完整路径</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>视频文件名</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>视频宽度（像素）</summary>
    public int Width { get; set; }

    /// <summary>视频高度（像素）</summary>
    public int Height { get; set; }

    /// <summary>帧率（帧/秒）</summary>
    public double Fps { get; set; }

    /// <summary>总帧数</summary>
    public int TotalFrames { get; set; }

    /// <summary>视频时长</summary>
    public TimeSpan Duration { get; set; }
}