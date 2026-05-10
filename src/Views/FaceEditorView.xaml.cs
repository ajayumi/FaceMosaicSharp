using System.Windows.Controls;
using System.Windows.Input;
using System.Windows;
using System.Windows.Media;
using System.Collections.Specialized;
using FaceMosaicSharp.ViewModels;

namespace FaceMosaicSharp.Views
{
        /// <summary>
    /// 帧人脸编辑器视图，支持在图像上拖动绘制矩形标注人脸区域、空格键拖拽平移
    /// </summary>
    public partial class FaceEditorView : UserControl
    {
        private readonly Dictionary<RectangleViewModel, UIElement> _regionElements = new(); // 区域 ViewModel → 可视元素映射
        private FaceEditorViewModel? _viewModel;
        private bool _spaceDown;  // 空格键是否按下（控制平移模式）
        private bool _panning;    // 是否正在拖拽平移
        private Point _panStart; // 平移拖拽起始点

        public FaceEditorView(FaceEditorViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            _viewModel = viewModel;
            _viewModel.OnSaved = () => Window.GetWindow(this)?.Close();
            viewModel.FaceRegions.CollectionChanged += FaceRegions_CollectionChanged;

            foreach (var region in viewModel.FaceRegions)
            {
                AddRegionVisual(region);
            }
        }

        /// <summary>空格键按下时切换为拖拽平移模式</summary>
        private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space)
            {
                _spaceDown = true;
                ImageCanvas.Cursor = Cursors.Hand;
                e.Handled = true;
            }
        }

        /// <summary>空格键释放时退出拖拽平移模式</summary>
        private void UserControl_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space)
            {
                _spaceDown = false;
                _panning = false;
                ImageCanvas.Cursor = null;
                e.Handled = true;
            }
        }

        /// <summary>人脸区域集合变化时同步更新画布上的可视元素</summary>
        private void FaceRegions_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                foreach (var element in _regionElements.Values)
                    ImageCanvas.Children.Remove(element);
                _regionElements.Clear();
            }
            else
            {
                if (e.NewItems != null)
                {
                    foreach (RectangleViewModel region in e.NewItems)
                        AddRegionVisual(region);
                }
                if (e.OldItems != null)
                {
                    foreach (RectangleViewModel region in e.OldItems)
                        RemoveRegionVisual(region);
                }
            }
        }

        /// <summary>在画布上为指定区域添加可视化矩形框（红色）和删除按钮</summary>
        private void AddRegionVisual(RectangleViewModel region)
        {
            var grid = new Grid
            {
                Width = region.Width,
                Height = region.Height
            };
            Canvas.SetLeft(grid, region.X);
            Canvas.SetTop(grid, region.Y);

            var border = new Border
            {
                BorderBrush = Brushes.Red,
                BorderThickness = new Thickness(2),
                Background = new SolidColorBrush(Color.FromArgb(64, 255, 0, 0)),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            grid.Children.Add(border);

            var button = new Button
            {
                Content = "X",
                Width = 20,
                Height = 20,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Background = Brushes.Red,
                Foreground = Brushes.White
            };
            button.Click += (s, e) =>
            {
                if (DataContext is FaceEditorViewModel vm)
                {
                    vm.DeleteRegionCommand.Execute(region);
                }
            };
            grid.Children.Add(button);

            ImageCanvas.Children.Add(grid);
            _regionElements[region] = grid;
        }

        /// <summary>从画布移除指定区域的可视元素</summary>
        private void RemoveRegionVisual(RectangleViewModel region)
        {
            if (_regionElements.TryGetValue(region, out var element))
            {
                ImageCanvas.Children.Remove(element);
                _regionElements.Remove(region);
            }
        }

        /// <summary>鼠标按下：空格+左键=开始平移；普通模式=开始绘制矩形</summary>
        private void ImageCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (Keyboard.IsKeyDown(Key.Space) && e.LeftButton == MouseButtonState.Pressed)
            {
                _panning = true;
                _panStart = e.GetPosition(ImageScrollViewer);
                ImageCanvas.Cursor = Cursors.ScrollAll;
                return;
            }
            if (DataContext is FaceEditorViewModel vm)
            {
                var point = e.GetPosition(FrameImageControl);
                vm.MouseDownCommand.Execute(point);
                UpdateDrawingRect();
            }
        }

        /// <summary>鼠标移动：平移时滚动视口；绘制时更新矩形预览</summary>
        private void ImageCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_panning)
            {
                var pos = e.GetPosition(ImageScrollViewer);
                var dx = _panStart.X - pos.X;
                var dy = _panStart.Y - pos.Y;
                ImageScrollViewer.ScrollToHorizontalOffset(ImageScrollViewer.HorizontalOffset + dx);
                ImageScrollViewer.ScrollToVerticalOffset(ImageScrollViewer.VerticalOffset + dy);
                _panStart = pos;
                return;
            }
            if (DataContext is FaceEditorViewModel vm && vm.IsDrawing)
            {
                var point = e.GetPosition(FrameImageControl);
                vm.MouseMoveCommand.Execute(point);
                UpdateDrawingRect();
            }
        }

        /// <summary>鼠标释放：结束平移或结束绘制矩形</summary>
        private void ImageCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_panning)
            {
                _panning = false;
                ImageCanvas.Cursor = Cursors.Hand;
                return;
            }
            if (DataContext is FaceEditorViewModel vm)
            {
                vm.MouseUpCommand.Execute(null);
                DrawingRect.Visibility = Visibility.Collapsed;
            }
        }

        private void ImageCanvas_MouseEnter(object sender, MouseEventArgs e)
        {
            if (Keyboard.IsKeyDown(Key.Space))
                ImageCanvas.Cursor = Cursors.Hand;
        }

        /// <summary>更新正在绘制的矩形预览框位置和尺寸</summary>
        private void UpdateDrawingRect()
        {
            if (DataContext is FaceEditorViewModel vm)
            {
                var x = Math.Min(vm.DrawStartPoint.X, vm.DrawEndPoint.X);
                var y = Math.Min(vm.DrawStartPoint.Y, vm.DrawEndPoint.Y);
                var width = Math.Abs(vm.DrawEndPoint.X - vm.DrawStartPoint.X);
                var height = Math.Abs(vm.DrawEndPoint.Y - vm.DrawStartPoint.Y);

                Canvas.SetLeft(DrawingRect, x);
                Canvas.SetTop(DrawingRect, y);
                DrawingRect.Width = width;
                DrawingRect.Height = height;
                DrawingRect.Visibility = Visibility.Visible;
            }
        }

        /// <summary>关闭编辑器窗口</summary>
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            var window = Window.GetWindow(this);
            window?.Close();
        }

        /// <summary>图像加载完成后更新 Canvas 尺寸以匹配图像实际像素</summary>
        private void FrameImage_Loaded(object sender, RoutedEventArgs e)
        {
            if (FrameImageControl.Source is System.Windows.Media.Imaging.BitmapSource bitmap)
            {
                ImageCanvas.Width = bitmap.PixelWidth;
                ImageCanvas.Height = bitmap.PixelHeight;
            }
        }
    }
}
