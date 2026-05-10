using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using FaceMosaicSharp.Models;

namespace FaceMosaicSharp.Services;

/// <summary>
/// 本地历史数据处理服务
/// 管理视频处理任务的缓存目录、帧数据 JSON 文件读写、中间产物清理等
/// </summary>
public class VideoHistoryService : IVideoHistoryService
{
    private const string HistoryRoot = "history";

    public string ComputeFileHash(string filePath)
    {
        try
        {
            using var md5 = MD5.Create();
            using var stream = File.OpenRead(filePath);
            byte[] hash = md5.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLower();
        }
        catch
        {
            return Guid.NewGuid().ToString("N");
        }
    }

    public string GetHistoryDirectory(string videoPath)
    {
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, HistoryRoot, ComputeFileHash(videoPath));
    }

    public void EnsureHistoryDirectories(string historyDir)
    {
        Directory.CreateDirectory(historyDir);
        Directory.CreateDirectory(StepFolder1(historyDir));
        Directory.CreateDirectory(StepFolder2(historyDir));
        Directory.CreateDirectory(StepFolder3(historyDir));
    }

    public string StepFolder1(string historyDir) => Path.Combine(historyDir, "step1");

    public string StepFolder2(string historyDir) => Path.Combine(historyDir, "step2");

    public string StepFolder3(string historyDir) => Path.Combine(historyDir, "step3");

    public string AudioFile(string historyDir) => Path.Combine(historyDir, "audio.aac");

    public string ReviewFramesPath(string stepFolder2) => Path.Combine(stepFolder2, "review_frames.json");

    public string FaceDataPath(string stepFolder2) => Path.Combine(stepFolder2, "faces.json");

    public string MaskDirectory(string stepFolder2) => Path.Combine(stepFolder2, "masks");

    public int CountFrameFiles(string folder, string searchPattern = "*.jpg")
    {
        return Directory.Exists(folder) ? Directory.GetFiles(folder, searchPattern).Length : 0;
    }

    public string[] GetOrderedFrameFiles(string folder, string searchPattern = "*.jpg")
    {
        return Directory.Exists(folder)
            ? Directory.GetFiles(folder, searchPattern).OrderBy(f => f).ToArray()
            : Array.Empty<string>();
    }

    public void DeleteFrameFiles(string folder, string searchPattern = "*.jpg")
    {
        if (!Directory.Exists(folder)) return;
        foreach (var file in Directory.GetFiles(folder, searchPattern))
        {
            File.Delete(file);
        }
    }

    public List<FrameFaceData> ReadFaceData(string stepFolder2)
    {
        var path = FaceDataPath(stepFolder2);
        if (!File.Exists(path))
            return new List<FrameFaceData>();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<FrameFaceData>>(json) ?? new List<FrameFaceData>();
        }
        catch
        {
            return new List<FrameFaceData>();
        }
    }

    public void WriteFaceData(string stepFolder2, List<FrameFaceData> data)
    {
        var path = FaceDataPath(stepFolder2);
        var json = JsonSerializer.Serialize(data);
        File.WriteAllText(path, json);
    }

    public List<ReviewFrame>? ReadReviewFrames(string stepFolder2)
    {
        var path = ReviewFramesPath(stepFolder2);
        if (!File.Exists(path))
            return null;

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<ReviewFrame>>(json);
        }
        catch
        {
            return null;
        }
    }

    public void WriteReviewFrames(string stepFolder2, List<ReviewFrame> frames)
    {
        var path = ReviewFramesPath(stepFolder2);
        var json = JsonSerializer.Serialize(frames);
        File.WriteAllText(path, json);
    }

    public bool ReviewFramesFileExists(string stepFolder2)
    {
        return File.Exists(ReviewFramesPath(stepFolder2));
    }

    public bool HasManualMarks(string stepFolder2)
    {
        var frames = ReadReviewFrames(stepFolder2);
        return frames?.Any(f => f.HasCustomRegions) == true;
    }

    public void CleanupProcessingFolders(string stepFolder2, string stepFolder3)
    {
        try { if (Directory.Exists(stepFolder2)) Directory.Delete(stepFolder2, true); } catch { }
        try { if (Directory.Exists(stepFolder3)) Directory.Delete(stepFolder3, true); } catch { }
    }
}
