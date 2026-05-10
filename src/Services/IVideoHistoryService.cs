using FaceMosaicSharp.Models;

namespace FaceMosaicSharp.Services;

/// <summary>
/// 本地历史数据处理服务接口
/// 负责视频处理任务的缓存目录管理、帧数据文件读写、中间产物清理等
/// </summary>
public interface IVideoHistoryService
{
    /// <summary>计算视频文件的 MD5 哈希，用作任务唯一标识</summary>
    string ComputeFileHash(string filePath);

    /// <summary>获取历史缓存根目录路径</summary>
    string GetHistoryDirectory(string videoPath);

    /// <summary>确保历史缓存目录结构（historyDir 及 step1/step2/step3 子目录）存在</summary>
    void EnsureHistoryDirectories(string historyDir);

    /// <summary>获取 step1（原始帧）路径</summary>
    string StepFolder1(string historyDir);

    /// <summary>获取 step2（人脸标注帧）路径</summary>
    string StepFolder2(string historyDir);

    /// <summary>获取 step3（马赛克帧）路径</summary>
    string StepFolder3(string historyDir);

    /// <summary>获取音频文件路径</summary>
    string AudioFile(string historyDir);

    /// <summary>获取 review_frames.json 路径</summary>
    string ReviewFramesPath(string stepFolder2);

    /// <summary>获取 faces.json 路径</summary>
    string FaceDataPath(string stepFolder2);

    /// <summary>获取面部解析掩码缓存目录路径</summary>
    string MaskDirectory(string stepFolder2);

    /// <summary>统计指定目录中的帧文件数量</summary>
    int CountFrameFiles(string folder, string searchPattern = "*.jpg");

    /// <summary>获取指定目录中排序后的帧文件列表</summary>
    string[] GetOrderedFrameFiles(string folder, string searchPattern = "*.jpg");

    /// <summary>删除指定目录中的所有匹配文件</summary>
    void DeleteFrameFiles(string folder, string searchPattern = "*.jpg");

    /// <summary>从 step2 目录读取人脸标注数据</summary>
    List<FrameFaceData> ReadFaceData(string stepFolder2);

    /// <summary>写入人脸标注数据到 step2 目录</summary>
    void WriteFaceData(string stepFolder2, List<FrameFaceData> data);

    /// <summary>从 step2 目录读取需要人工审核的帧记录</summary>
    List<ReviewFrame>? ReadReviewFrames(string stepFolder2);

    /// <summary>写入需要人工审核的帧记录到 step2 目录</summary>
    void WriteReviewFrames(string stepFolder2, List<ReviewFrame> frames);

    /// <summary>检查 step2 中是否存在 review_frames.json</summary>
    bool ReviewFramesFileExists(string stepFolder2);

    /// <summary>检查 step2 中是否存在手动标记的帧</summary>
    bool HasManualMarks(string stepFolder2);

    /// <summary>清理 step2 和 step3 目录</summary>
    void CleanupProcessingFolders(string stepFolder2, string stepFolder3);
}
