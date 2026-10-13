using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace TraeTools.ViewModels;

/// <summary>解析当前主窗口，供页面 VM 弹模态窗口（ShowDialog 需要 owner）。</summary>
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
}
