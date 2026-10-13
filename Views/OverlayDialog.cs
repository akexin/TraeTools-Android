using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;

namespace TraeTools.Views;

/// <summary>
/// 单视图平台（Android）的模态对话框宿主。
/// Avalonia 的 SingleViewApplicationLifetime 不支持再创建 Window（官方文档明确：
/// "If your app uses SingleViewApplicationLifetime, you cannot create a new Window
/// to host a dialog"），直接调用 ShowDialog 会失败并使 async void 崩溃。
/// Android 上把对话框窗口的内容临时"借用"到 MainWindow 顶部的覆盖层呈现，
/// 按钮关闭时还原内容并以 TCS 返回结果；桌面端仍走原生 ShowDialog。
/// </summary>
public static class OverlayDialog
{
    private static Panel? _root;
    private static ContentPresenter? _host;
    private static Window? _current;
    private static TaskCompletionSource<object?>? _tcs;

    /// <summary>当前是否处于覆盖层对话框模式（仅 Android）。</summary>
    public static bool IsOverlayMode => OperatingSystem.IsAndroid();

    /// <summary>MainWindow 启动时挂接覆盖层（仅 Android 需要；桌面端可安全跳过）。</summary>
    public static void Attach(Panel root, ContentPresenter host)
    {
        _root = root;
        _host = host;
    }

    /// <summary>
    /// 双平台对话框入口：桌面走原生 ShowDialog（保持原行为），Android 走覆盖层。
    /// 返回对话框通过 CloseDialog 关闭时携带的结果。
    /// </summary>
    public static async Task<T> ShowAsync<T>(Window dialog, Window owner)
    {
        if (!IsOverlayMode)
            return await dialog.ShowDialog<T>(owner);

        try
        {
            var tcs = ShowCore(dialog);
            var result = await tcs.Task;
            return result is T typed ? typed : default!;
        }
        catch (Exception ex)
        {
            // 覆盖层呈现失败时降级返回默认值：绝不让对话框问题带崩应用（Android 上即闪退）
            TraeTools.Platform.CrashLog.Write("OverlayDialog.ShowAsync", ex);
            return default!;
        }
    }

    /// <summary>对话框按钮统一关闭入口：Android 走覆盖层还原，桌面走 Window.Close(result)。</summary>
    public static void CloseDialog(Window dialog, object? result)
    {
        if (IsOverlayMode && ReferenceEquals(_current, dialog))
        {
            Finish(dialog, result);
            return;
        }
        dialog.Close(result);
    }

    private static TaskCompletionSource<object?> ShowCore(Window dialog)
    {
        if (_root == null || _host == null)
            throw new InvalidOperationException("OverlayDialog 尚未 Attach（Android 主窗口未就绪）");
        if (_tcs != null)
            throw new InvalidOperationException("已有对话框在显示中，不允许叠加");

        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _tcs = tcs;
        _current = dialog;

        // 把窗口内容临时移入覆盖层；显式设置 DataContext，阻断从视觉树继承到 MainViewModel
        var content = dialog.Content as Control;
        dialog.Content = null;
        if (content != null) content.DataContext = dialog.DataContext;
        _host.Content = content;
        _root.IsVisible = true;
        return tcs;
    }

    private static void Finish(Window dialog, object? result)
    {
        if (!ReferenceEquals(_current, dialog) || _tcs == null || _host == null || _root == null)
            return;

        var content = _host.Content as Control;
        _host.Content = null;
        _root.IsVisible = false;
        if (content != null && ReferenceEquals(_current.Content, null))
        {
            // 还原回窗口（仅当窗口内容仍是空位，避免重复移动）
            _current.Content = content;
        }

        var tcs = _tcs;
        _tcs = null;
        _current = null;
        tcs.TrySetResult(result);
    }
}
