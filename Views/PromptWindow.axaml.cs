using Avalonia.Controls;
using Avalonia.Interactivity;

namespace TraeTools.Views;

/// <summary>
/// 通用确认/提示对话框。ShowDialog&lt;bool&gt; 返回：true=点了确认/知道了，false=取消。
/// </summary>
public partial class PromptWindow : UserControl
{
    /// <summary>桌面端承载窗口的标题（Android 单视图无窗口标题栏，不使用）。</summary>
    public string DialogTitle { get; set; } = "提示";

    public PromptWindow() { InitializeComponent(); }

    public PromptWindow(string title, string message, string confirmText = "确定",
        string cancelText = "取消", bool showCancel = true)
        : this()
    {
        DialogTitle = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        if (showCancel)
        {
            CancelButton.Content = cancelText;
            CancelButton.IsVisible = true;
        }
        else
        {
            CancelButton.IsVisible = false;
        }
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e) => OverlayDialog.CloseDialog(this, true);

    private void OnCancelClick(object? sender, RoutedEventArgs e) => OverlayDialog.CloseDialog(this, false);
}

