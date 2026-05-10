using System.Windows;
using FaceMosaicSharp.ViewModels;
using Wpf.Ui.Controls;

namespace FaceMosaicSharp;

/// <summary>
/// 主窗口（FluentWindow 风格），承载 MainView 用户控件
/// </summary>
public partial class MainWindow : FluentWindow
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        MainView.DataContext = viewModel;
        Closed += MainWindow_Closed;

        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        if (version != null)
        {
            Title = $"人脸打码工具 v{version}";
            MainTitleBar.Title = $"人脸打码工具 v{version}";
        }
    }

    /// <summary>窗口关闭时释放 ViewModel 资源</summary>
    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.Dispose();
        }
    }
}