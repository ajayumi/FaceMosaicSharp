using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FaceMosaicSharp.ViewModels;
using Wpf.Ui.Controls;

namespace FaceMosaicSharp.Views;

/// <summary>
/// 主视图控件，包含视频预览区、参数配置面板和进度条
/// </summary>
public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
    }

    /// <summary>帧号输入框按 Enter 时跳转到指定帧并刷新预览</summary>
    private async void FrameNumberBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is NumberBox nb)
        {
            if (DataContext is MainViewModel vm)
            {
                var text = nb.Text;
                if (int.TryParse(text, out int newValue))
                {
                    if (newValue != vm.PreviewFrameNumber)
                    {
                        vm.PreviewFrameNumber = newValue;
                    }
                }
                await vm.RefreshPreviewCommand.ExecuteAsync(null);
            }
        }
    }
}