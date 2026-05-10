using System.Windows;
using System.Windows.Controls;
using UiMessageBox = Wpf.Ui.Controls.MessageBox;
using UiMessageBoxResult = Wpf.Ui.Controls.MessageBoxResult;

namespace FaceMosaicSharp.Services;

/// <summary>
/// 对话框服务接口
/// </summary>
public interface IDialogService
{
    /// <summary>显示信息对话框</summary>
    Task<bool> ShowInfoAsync(string title, string content);

    /// <summary>显示警告对话框</summary>
    Task<bool> ShowWarningAsync(string title, string content);

    /// <summary>显示错误对话框</summary>
    Task<bool> ShowErrorAsync(string title, string content);

    /// <summary>显示是/否确认对话框</summary>
    Task<bool> ShowYesNoAsync(string title, string content);
}

/// <summary>
/// WPF UI对话框服务实现
/// </summary>
public class WpfUiDialogService : IDialogService
{
    public async Task<bool> ShowInfoAsync(string title, string content)
    {
        var msgBox = CreateMessageBox(title, content, "确定", null, Wpf.Ui.Controls.SymbolRegular.Info24);
        var result = await msgBox.ShowDialogAsync(true);
        return result == UiMessageBoxResult.Primary;
    }

    public async Task<bool> ShowWarningAsync(string title, string content)
    {
        var msgBox = CreateMessageBox(title, content, "确定", null, Wpf.Ui.Controls.SymbolRegular.Warning24);
        var result = await msgBox.ShowDialogAsync(true);
        return result == UiMessageBoxResult.Primary;
    }

    public async Task<bool> ShowErrorAsync(string title, string content)
    {
        var msgBox = CreateMessageBox(title, content, "确定", null, Wpf.Ui.Controls.SymbolRegular.ErrorCircle24);
        var result = await msgBox.ShowDialogAsync(true);
        return result == UiMessageBoxResult.Primary;
    }

    public async Task<bool> ShowYesNoAsync(string title, string content)
    {
        var msgBox = CreateMessageBox(title, content, "是", "否", Wpf.Ui.Controls.SymbolRegular.Question24);
        var result = await msgBox.ShowDialogAsync(true);
        return result == UiMessageBoxResult.Primary;
    }

    /// <summary>
    /// 创建消息框
    /// </summary>
    private UiMessageBox CreateMessageBox(string title, string content, string primaryText, string? closeText, Wpf.Ui.Controls.SymbolRegular icon)
    {
        var box = new UiMessageBox
        {
            Title = title,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 12),
                Children =
                {
                    new Wpf.Ui.Controls.SymbolIcon { Symbol = icon, FontSize = 24, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) },
                    new System.Windows.Controls.TextBlock { Text = content, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 350 }
                }
            },
            ShowTitle = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Application.Current.MainWindow
        };

        if (string.IsNullOrEmpty(closeText))
        {
            box.CloseButtonText = primaryText;
        }
        else
        {
            box.PrimaryButtonText = primaryText;
            box.CloseButtonText = closeText;
        }

        return box;
    }
}
