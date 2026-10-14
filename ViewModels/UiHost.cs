using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace TraeTools.ViewModels;

/// <summary>解析当前主窗口 / 剪贴板，供页面 VM 弹模态窗口与复制文本（ShowDialog 需要 owner）。</summary>
internal static class UiHost
{
    public static Window? MainWindow
    {
        get
        {
            // Android 上 Application 需写全称：.NET Android SDK 的隐式全局 using 含 Android.App，
            // 裸写 Application 会与 Android.App.Application 歧义（CS0104）
            var lifetime = Avalonia.Application.Current?.ApplicationLifetime;
            if (lifetime is IClassicDesktopStyleApplicationLifetime desktop)
                return desktop.MainWindow;
#if ANDROID
            if (lifetime is ISingleViewApplicationLifetime singleView && singleView.MainView is Window w)
                return w;
#endif
            return null;
        }
    }

    /// <summary>
    /// 当前根 TopLevel（桌面端为主窗口，Android 为承载单视图的根），
    /// 文件选择器（IStorageProvider）等能力必须经它获取。
    /// 刻意不叫 TopLevel，避免属性名与类型名同名带来的解析歧义。
    /// </summary>
    public static TopLevel? Root
    {
        get
        {
            try
            {
                var lifetime = Avalonia.Application.Current?.ApplicationLifetime;
                if (lifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    return desktop.MainWindow;
#if ANDROID
                if (lifetime is ISingleViewApplicationLifetime singleView && singleView.MainView is Control root)
                    return TopLevel.GetTopLevel(root);
#endif
            }
            catch { /* 获取失败时返回 null，调用方回退到剪贴板方案 */ }
            return null;
        }
    }

    /// <summary>
    /// 剪贴板：桌面端取自主窗口；单视图平台（Android）没有 Window，
    /// 需从根视图反查 TopLevel 才能拿到剪贴板——这是「复制 Token / 复制日志」在手机端可用的前提。
    /// </summary>
    public static Avalonia.Input.Platform.IClipboard? Clipboard
    {
        get
        {
            try
            {
                var lifetime = Avalonia.Application.Current?.ApplicationLifetime;
                if (lifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    return desktop.MainWindow?.Clipboard;
#if ANDROID
                if (lifetime is ISingleViewApplicationLifetime singleView && singleView.MainView is Control root)
                    return TopLevel.GetTopLevel(root)?.Clipboard;
#endif
            }
            catch { /* 剪贴板不可用时返回 null，调用方给出提示 */ }
            return null;
        }
    }
}
