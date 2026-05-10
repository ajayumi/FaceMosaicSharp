using System.Collections.ObjectModel;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Emgu.CV;
using Emgu.CV.CvEnum;
using FaceMosaicSharp.Models;
using FaceMosaicSharp.Services;

namespace FaceMosaicSharp.ViewModels;

/// <summary>
/// 人脸编辑 ViewModel，提供帧图像加载、手动标注人脸区域、拖动绘制、重检测等交互逻辑
/// </summary>
public partial class FaceEditorViewModel : ObservableObject
{
    private readonly ReviewFrameItem _frameItem;
    private readonly string _historyDir;
    private readonly IDialogService _dialogService;
    private readonly IFaceDetectionService _faceDetectionService;

    [ObservableProperty]
    private BitmapSource? _frameImage;

    [ObservableProperty]
    private ObservableCollection<RectangleViewModel> _faceRegions = new();

    [ObservableProperty]
    private bool _isDrawing;

    [ObservableProperty]
    private System.Windows.Point _drawStartPoint;

    [ObservableProperty]
    private System.Windows.Point _drawEndPoint;

    [ObservableProperty]
    private float _confidenceThreshold;

    public FaceEditorViewModel(ReviewFrameItem frameItem, string historyDir, IDialogService dialogService, IFaceDetectionService faceDetectionService)
    {
        _frameItem = frameItem;
        _historyDir = historyDir;
        _dialogService = dialogService;
        _faceDetectionService = faceDetectionService;
        _confidenceThreshold = (frameItem.MaxConfidence > 0 && frameItem.MaxConfidence < frameItem.ConfidenceThreshold)
            ? frameItem.MaxConfidence : frameItem.ConfidenceThreshold;
        LoadImage();
        LoadExistingRegions();
    }

    /// <summary>从磁盘加载帧图像到 BitmapSource</summary>
    private void LoadImage()
    {
        if (!File.Exists(_frameItem.FrameImagePath)) return;
        using var stream = new MemoryStream(File.ReadAllBytes(_frameItem.FrameImagePath));
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.StreamSource = stream;
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();
        FrameImage = bitmap;
    }

    /// <summary>从 ReviewFrame 加载已有的自定义人脸区域到 UI</summary>
    private void LoadExistingRegions()
    {
        foreach (var rect in _frameItem.CustomFaceRegions)
        {
            FaceRegions.Add(new RectangleViewModel(rect));
        }
    }

    /// <summary>鼠标按下：开始绘制矩形，记录起始点</summary>
    [RelayCommand]
    private void MouseDown(System.Windows.Point point)
    {
        IsDrawing = true;
        DrawStartPoint = point;
        DrawEndPoint = point;
    }

    /// <summary>鼠标移动：更新矩形终点（仅绘制模式下生效）</summary>
    [RelayCommand]
    private void MouseMove(System.Windows.Point point)
    {
        if (!IsDrawing) return;
        DrawEndPoint = point;
    }

    /// <summary>鼠标释放：根据起终点生成有效矩形并添加到人脸区域列表</summary>
    [RelayCommand]
    private void MouseUp()
    {
        if (!IsDrawing) return;
        IsDrawing = false;

        var x = (int)Math.Min(DrawStartPoint.X, DrawEndPoint.X);
        var y = (int)Math.Min(DrawStartPoint.Y, DrawEndPoint.Y);
        var width = (int)Math.Abs(DrawEndPoint.X - DrawStartPoint.X);
        var height = (int)Math.Abs(DrawEndPoint.Y - DrawStartPoint.Y);

        if (width > 10 && height > 10)
        {
            var rect = new Rectangle(x, y, width, height);
            FaceRegions.Add(new RectangleViewModel(rect));
        }
    }

    /// <summary>删除指定的人脸区域标注</summary>
    [RelayCommand]
    private void DeleteRegion(RectangleViewModel? region)
    {
        if (region != null)
        {
            FaceRegions.Remove(region);
        }
    }

    /// <summary>保存后回调（由视图关闭窗口使用）</summary>
    public Action? OnSaved { get; set; }

    /// <summary>保存当前标注的人脸区域到 review_frames.json</summary>
    [RelayCommand]
    private void Save()
    {
        _frameItem.CustomFaceRegions.Clear();
        foreach (var region in FaceRegions)
        {
            _frameItem.AddCustomRegion(region.ToRectangle());
        }
        _frameItem.SaveCustomRegions(_historyDir);
        OnSaved?.Invoke();
    }

    /// <summary>使用调整后的置信度阈值重新检测当前帧的人脸</summary>
    [RelayCommand]
    private async Task ReDetect()
    {
        try
        {
            FaceRegions.Clear();

            if (!File.Exists(_frameItem.FrameImagePath))
            {
                await _dialogService.ShowErrorAsync("错误", "帧图像文件不存在");
                return;
            }

            _faceDetectionService.SetConfidenceThreshold(ConfidenceThreshold);

            using var bitmap = new Bitmap(_frameItem.FrameImagePath);
            using var mat = BitmapToMat(bitmap);
            var result = _faceDetectionService.DetectFaces(mat, _frameItem.FrameIndex);

            foreach (var rect in result.Faces)
            {
                FaceRegions.Add(new RectangleViewModel(rect));
            }

            await _dialogService.ShowInfoAsync("提示", $"重新检测完成，找到 {result.Faces.Length} 个人脸");
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("错误", $"重新检测失败: {ex.Message}");
        }
    }

    private static Mat BitmapToMat(Bitmap bitmap)
    {
        var mat = new Mat();
        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Bmp);
        stream.Position = 0;
        CvInvoke.Imdecode(stream.ToArray(), Emgu.CV.CvEnum.ImreadModes.AnyColor, mat);
        return mat;
    }
}

/// <summary>
/// 矩形区域 ViewModel，用于绑定到 UI 上显示和编辑人脸标注框
/// </summary>
public partial class RectangleViewModel : ObservableObject
{
    [ObservableProperty]
    private double _x;

    [ObservableProperty]
    private double _y;

    [ObservableProperty]
    private double _width;

    [ObservableProperty]
    private double _height;

    public RectangleViewModel(Rectangle rect)
    {
        X = rect.X;
        Y = rect.Y;
        Width = rect.Width;
        Height = rect.Height;
    }

    public Rectangle ToRectangle()
    {
        return new Rectangle((int)X, (int)Y, (int)Width, (int)Height);
    }
}
