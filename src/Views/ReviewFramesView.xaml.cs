using System.Windows;
using System.Windows.Controls;
using FaceMosaicSharp.ViewModels;

namespace FaceMosaicSharp.Views;

/// <summary>
/// 审核帧列表视图，以缩略图网格展示待审核帧，支持单击打开编辑器、继续处理/关闭操作
/// </summary>
public partial class ReviewFramesView : UserControl
{
    public ReviewFramesView(ReviewFramesViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    /// <summary>"继续处理"按钮：标记继续并关闭窗口</summary>
    private void ContinueButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is ReviewFramesViewModel vm)
            vm.ContinueProcessing = true;
        var window = Window.GetWindow(this);
        window?.Close();
    }

    /// <summary>"关闭"按钮：标记不继续并关闭窗口</summary>
    private void CloseButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is ReviewFramesViewModel vm)
            vm.ContinueProcessing = false;
        var window = Window.GetWindow(this);
        window?.Close();
    }
}
