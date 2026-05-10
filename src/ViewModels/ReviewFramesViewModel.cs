using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FaceMosaicSharp.Models;

namespace FaceMosaicSharp.ViewModels;

/// <summary>
/// 人工审核帧列表 ViewModel，加载 review_frames.json 中未检测到/检测到多个人脸的帧供用户手动标注
/// </summary>
public partial class ReviewFramesViewModel : ObservableObject
{
    private readonly string _historyDir;

    public ObservableCollection<ReviewFrameItem> ReviewFrames { get; } = new();

    [ObservableProperty]
    private ReviewFrameItem? _selectedFrame;

    [ObservableProperty]
    private string _title = "需要人工审核的帧";

    public bool ContinueProcessing { get; set; }

    public ReviewFramesViewModel(string historyDir)
    {
        _historyDir = historyDir;
        LoadReviewFrames();
    }

    /// <summary>从 review_frames.json 加载待审核帧列表</summary>
    private void LoadReviewFrames()
    {
        ReviewFrames.Clear();
        var reviewFramesPath = Path.Combine(_historyDir, "step2", "review_frames.json");
        if (!File.Exists(reviewFramesPath)) return;

        var json = File.ReadAllText(reviewFramesPath);
        var frames = System.Text.Json.JsonSerializer.Deserialize<List<ReviewFrame>>(json);
        if (frames == null) return;

        foreach (var frame in frames)
        {
            ReviewFrames.Add(new ReviewFrameItem(frame));
        }
        Title = $"需要人工审核的帧 - 共{ReviewFrames.Count}帧";
    }

    /// <summary>重新加载审核帧列表</summary>
    [RelayCommand]
    public void Refresh()
    {
        LoadReviewFrames();
    }

    /// <summary>打开指定帧的人脸编辑器（触发 SelectedFrame 变化，由 UI 层捕获打开编辑器窗口）</summary>
    [RelayCommand]
    private void OpenFrameEditor(ReviewFrameItem? item)
    {
        if (item == null) return;
        SelectedFrame = item;
    }
}

/// <summary>
/// 审核帧条目包装类，封装缩略图生成、区域标记持久化等操作
/// </summary>
public partial class ReviewFrameItem : ObservableObject
{
    private readonly ReviewFrame _frame;
    private const int ThumbSize = 260;

    public int FrameIndex => _frame.FrameIndex;
    public string FrameImagePath => _frame.FrameImagePath;
    public List<System.Drawing.Rectangle> CustomFaceRegions => _frame.CustomFaceRegions;
    public float MaxConfidence => _frame.MaxConfidence;
    public float ConfidenceThreshold => _frame.ConfidenceThreshold;

    public ReviewFrameItem(ReviewFrame frame)
    {
        _frame = frame;
    }

    public BitmapSource? PreviewImage
    {
        get
        {
            if (!File.Exists(FrameImagePath)) return null;

            using var src = new System.Drawing.Bitmap(FrameImagePath);
            float scale = Math.Min((float)ThumbSize / src.Width, (float)ThumbSize / src.Height);
            int w = Math.Max(1, (int)(src.Width * scale));
            int h = Math.Max(1, (int)(src.Height * scale));

            using var bitmap = new System.Drawing.Bitmap(w, h);
            using var g = System.Drawing.Graphics.FromImage(bitmap);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(src, 0, 0, w, h);

            if (CustomFaceRegions.Count > 0)
            {
                using var pen = new System.Drawing.Pen(System.Drawing.Color.Lime, 2);
                foreach (var rect in CustomFaceRegions)
                {
                    g.DrawRectangle(pen,
                        rect.X * scale, rect.Y * scale,
                        rect.Width * scale, rect.Height * scale);
                }
            }

            var hBitmap = bitmap.GetHbitmap();
            try
            {
                var result = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap, IntPtr.Zero, System.Windows.Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                result.Freeze();
                return result;
            }
            finally
            {
                DeleteObject(hBitmap);
            }
        }
    }

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    public void AddCustomRegion(System.Drawing.Rectangle rect)
    {
        _frame.CustomFaceRegions.Add(rect);
    }

    public void SaveCustomRegions(string historyDir)
    {
        var reviewFramesPath = Path.Combine(historyDir, "step2", "review_frames.json");
        if (!File.Exists(reviewFramesPath)) return;

        var json = File.ReadAllText(reviewFramesPath);
        var frames = System.Text.Json.JsonSerializer.Deserialize<List<ReviewFrame>>(json);
        if (frames == null) return;

        var target = frames.FirstOrDefault(f => f.FrameIndex == FrameIndex);
        if (target != null)
        {
            target.CustomFaceRegions = _frame.CustomFaceRegions;
        }

        File.WriteAllText(reviewFramesPath, System.Text.Json.JsonSerializer.Serialize(frames));
    }
}
