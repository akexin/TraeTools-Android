using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Presenters;

namespace TraeTools.Views;

/// <summary>
/// 模态对话框宿主（Android 单视图）。
///
/// ⚠️ 关键背景：Android 是 Avalonia 单视图平台，`new Window()` 会在
/// Avalonia.Android.WindowingPlatformStub.CreateWindow() 抛 NotSupportedException，
/// 因此**所有对话框都必须是 UserControl**：把 UserControl 直接挂到 MainView 的覆盖层
/// （ZIndex 999）呈现，用 TaskCompletionSource 模拟模态返回。
/// （原桌面端「用宿主 Window 包装后 ShowDialog」的分支已随 Windows 目标一并移除。）
/// </summary>
public static class OverlayDialog
{
    private static Panel? _root;
    private static ContentPresenter? _host;
    private static UserControl? _current;
    private static TaskCompletionSource<object?>? _tcs;

    /// <summary>MainView 启动时挂接覆盖层。</summary>
    public static void Attach(Panel root, ContentPresenter host)
    {
        _root = root;
        _host = host;
    }

    /// <summary>
    /// 显示对话框（覆盖层模式）。返回对话框通过 CloseDialog 关闭时携带的结果。
    /// owner 参数保留以维持各调用点的签名不变；单视图平台不需要 owner。
    /// </summary>
    public static async Task<T> ShowAsync<T>(UserControl dialog, Window? owner)
    {
        _ = owner;
        try
        {
            var tcs = ShowCore(dialog);
            var result = await tcs.Task;
            return result is T typed ? typed : default!;
        }
        catch (Exception ex)
        {
            // 覆盖层呈现失败时降级返回默认值：绝不让对话框问题带崩应用（Android 上即闪退）
            TraeTools.Platform.CrashLog.Write("OverlayDialog.ShowAsync(overlay)", ex);
            return default!;
        }
    }

    /// <summary>对话框按钮统一关闭入口：还原覆盖层并把结果交回 ShowAsync。</summary>
    public static void CloseDialog(UserControl dialog, object? result)
    {
        if (ReferenceEquals(_current, dialog))
        {
            Finish(dialog, result);
        }
    }

    private static TaskCompletionSource<object?> ShowCore(UserControl dialog)
    {
        if (_root == null || _host == null)
            throw new InvalidOperationException("OverlayDialog 尚未 Attach（主视图未就绪）");
        if (_tcs != null)
            throw new InvalidOperationException("已有对话框在显示中，不允许叠加");

        // 手机屏幕较窄：清掉 XAML 中为桌面端写死的 Width，改为填满覆盖层卡片
        try
        {
            dialog.Width = double.NaN;
            dialog.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        }
        catch
        {
            // 尺寸调整失败不影响显示
        }

        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _tcs = tcs;
        _current = dialog;
        _host.Content = dialog;
        _root.IsVisible = true;
        return tcs;
    }

    private static void Finish(UserControl dialog, object? result)
    {
        if (!ReferenceEquals(_current, dialog) || _tcs == null || _host == null || _root == null)
            return;

        _host.Content = null;
        _root.IsVisible = false;

        var tcs = _tcs;
        _tcs = null;
        _current = null;
        tcs.TrySetResult(result);
    }
}
