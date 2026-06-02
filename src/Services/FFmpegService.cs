using System.Globalization;
using System.IO;
using System.Threading;

namespace FaceMosaicSharp.Services;

/// <summary>
/// FFmpeg 音视频处理静态工具类
/// 提供音频提取、视频合成、音视频合并等命令行封装
/// </summary>
public class FFmpegService
{
    private static bool? _isAvailable;

    /// <summary>
    /// 创建FFmpeg进程启动信息
    /// </summary>
    /// <param name="arguments">FFmpeg命令行参数</param>
    /// <returns>进程启动信息</returns>
    private static System.Diagnostics.ProcessStartInfo CreateStartInfo(string arguments) => new()
    {
        FileName = "ffmpeg",
        Arguments = arguments,
        UseShellExecute = true,
        CreateNoWindow = true,
        WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
    };

    /// <summary>
    /// 检查系统是否安装了FFmpeg
    /// </summary>
    /// <returns>FFmpeg是否可用</returns>
    public static bool IsAvailable()
    {
        if (_isAvailable.HasValue) return _isAvailable.Value;

        try
        {
            using var process = System.Diagnostics.Process.Start(CreateStartInfo("-version"));
            process?.WaitForExit(5000);
            _isAvailable = process?.ExitCode == 0;
        }
        catch
        {
            _isAvailable = false;
        }

        return _isAvailable.Value;
    }

    /// <summary>
    /// 通过 ffprobe 获取视频时长（秒），失败时返回 0
    /// </summary>
    public static async Task<double> GetVideoDurationAsync(string inputPath, CancellationToken cancellationToken = default)
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ffprobe",
                Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{inputPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };
            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process != null)
            {
                var output = await process.StandardOutput.ReadToEndAsync();
                process.WaitForExit(3000);
                var trimmed = output.Trim();
                if (double.TryParse(trimmed, NumberStyles.Any, CultureInfo.InvariantCulture, out var duration))
                    return duration;
            }
        }
        catch { }
        return 0;
    }

    /// <summary>
    /// 通过 ffprobe 获取视频帧数（来自容器元数据），失败时返回 0
    /// </summary>
    public static async Task<int> GetVideoFrameCountAsync(string inputPath)
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ffprobe",
                Arguments = $"-v error -select_streams v:0 -show_entries stream=nb_frames -of default=noprint_wrappers=1:nokey=1 \"{inputPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };
            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process != null)
            {
                var output = await process.StandardOutput.ReadToEndAsync();
                process.WaitForExit(3000);
                var trimmed = output.Trim();
                if (int.TryParse(trimmed, NumberStyles.Any, CultureInfo.InvariantCulture, out var count))
                    return count;
            }
        }
        catch { }
        return 0;
    }

    /// <summary>
    /// 从视频文件中提取音频并保存为AAC格式
    /// </summary>
    /// <param name="inputPath">输入视频路径</param>
    /// <param name="audioFile">输出音频文件路径</param>
    /// <param name="progress">进度报告回调</param>
    public static async Task ExtractAudioAsync(string inputPath, string audioFile, IProgress<(int current, int total, string status)>? progress = null, CancellationToken cancellationToken = default)
    {
        if (System.IO.File.Exists(audioFile)) return;
        if (!IsAvailable()) return;

        progress?.Report((0, 1, "正在提取音频..."));

        try
        {
            using var process = System.Diagnostics.Process.Start(CreateStartInfo($"-i \"{inputPath}\" -vn -c:a aac -y \"{audioFile}\""));
            if (process != null)
            {
                await WaitForProcessOrCancellation(process, 60000, cancellationToken);
                if (cancellationToken.IsCancellationRequested)
                {
                    if (!process.HasExited) process.Kill();
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { }

        if (!cancellationToken.IsCancellationRequested)
            progress?.Report((1, 1, "音频提取完成"));
    }

    /// <summary>
    /// 合并视频文件和音频文件
    /// </summary>
    /// <param name="videoFile">输入视频文件路径</param>
    /// <param name="audioFile">输入音频文件路径</param>
    /// <param name="outputPath">输出文件路径</param>
    /// <param name="originalCodec">原始视频编码</param>
    /// <param name="progress">进度报告回调</param>
    public static async Task MergeVideoWithAudioAsync(string videoFile, string audioFile, string outputPath, string originalCodec, IProgress<(int current, int total, string status)>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable())
        {
            if (System.IO.File.Exists(videoFile) && !System.IO.File.Exists(outputPath))
                System.IO.File.Move(videoFile, outputPath);
            return;
        }

        progress?.Report((0, 1, "正在合并音视频..."));

        try
        {
            if (System.IO.File.Exists(outputPath))
                System.IO.File.Delete(outputPath);

            bool convertToH264 = originalCodec.Equals("H264", StringComparison.OrdinalIgnoreCase) ||
                                  originalCodec.Equals("avc1", StringComparison.OrdinalIgnoreCase);

            string args;
            if (!string.IsNullOrEmpty(audioFile) && System.IO.File.Exists(audioFile))
            {
                if (convertToH264)
                    args = $"-i \"{videoFile}\" -i \"{audioFile}\" -c:v libx264 -preset fast -c:a copy -shortest -y \"{outputPath}\"";
                else
                    args = $"-i \"{videoFile}\" -i \"{audioFile}\" -c:v copy -c:a copy -shortest -y \"{outputPath}\"";
            }
            else
            {
                if (convertToH264)
                    args = $"-i \"{videoFile}\" -c:v libx264 -preset fast -y \"{outputPath}\"";
                else
                    args = $"-i \"{videoFile}\" -c copy -y \"{outputPath}\"";
            }

            using var process = System.Diagnostics.Process.Start(CreateStartInfo(args));
            if (process != null)
            {
                await WaitForProcessOrCancellation(process, 60000, cancellationToken);
                if (cancellationToken.IsCancellationRequested)
                {
                    if (!process.HasExited) process.Kill();
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MergeVideoWithAudio error: {ex.Message}");
            if (System.IO.File.Exists(videoFile) && !System.IO.File.Exists(outputPath))
                System.IO.File.Move(videoFile, outputPath);
        }

        if (!cancellationToken.IsCancellationRequested)
            progress?.Report((1, 1, "音视频合并完成"));
    }

    /// <summary>
    /// 将图片序列合成视频并添加音频
    /// </summary>
    /// <param name="imageFolder">图片文件夹路径</param>
    /// <param name="audioFile">音频文件路径</param>
    /// <param name="outputPath">输出视频路径</param>
    /// <param name="fps">输出帧率</param>
    /// <param name="codec">视频编码</param>
    /// <param name="progress">进度报告回调</param>
    public static async Task CombineImagesWithAudioAsync(string imageFolder, string audioFile, string outputPath, double fps, string codec, IProgress<(int current, int total, string status)>? progress = null, CancellationToken cancellationToken = default)
    {
        var imageFiles = System.IO.Directory.GetFiles(imageFolder, "*.jpg").OrderBy(f => f).ToArray();
        if (imageFiles.Length == 0) return;

        progress?.Report((0, imageFiles.Length, $"正在合成视频 ({imageFiles.Length} 帧)..."));

        string videoFile = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(imageFolder)!, "output_video.mp4");

        // 使用 image2 demuxer（比 concat 更可靠，帧率行为一致）
        var process = System.Diagnostics.Process.Start(CreateStartInfo($"-framerate {fps} -i \"{imageFolder}\\frame_%06d.jpg\" -c:v libx264 -preset ultrafast -pix_fmt yuv420p -y \"{videoFile}\""));
        int elapsedSeconds = 0;
        int timeoutSeconds = 120;
        while (!process.HasExited && elapsedSeconds < timeoutSeconds)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                process.Kill();
                cancellationToken.ThrowIfCancellationRequested();
            }
            await Task.Delay(500, cancellationToken);
            elapsedSeconds += 1;
            progress?.Report((0, imageFiles.Length, $"正在合成视频... ({elapsedSeconds}/{timeoutSeconds}秒)"));
        }

        if (!process.HasExited)
        {
            process.Kill();
            throw new TimeoutException("视频合成超时");
        }

        progress?.Report((imageFiles.Length, imageFiles.Length, "正在合并音频..."));

        if (System.IO.File.Exists(videoFile))
        {
            await MergeVideoWithAudioAsync(videoFile, audioFile, outputPath, codec, progress, cancellationToken);
        }

        try
        {
            if (System.IO.File.Exists(videoFile))
                System.IO.File.Delete(videoFile);
        }
        catch { }

        if (!cancellationToken.IsCancellationRequested)
            progress?.Report((imageFiles.Length, imageFiles.Length, "视频合成完成"));
    }

    private static async Task WaitForProcessOrCancellation(System.Diagnostics.Process process, int timeoutMs, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<bool>();
        cancellationToken.Register(() => tcs.TrySetCanceled());
        _ = Task.Run(() =>
        {
            process.WaitForExit(timeoutMs);
            tcs.TrySetResult(true);
        });
        await tcs.Task;
    }
}