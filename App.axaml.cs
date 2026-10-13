using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using TraeTools.ViewModels;
using TraeTools.Views;

namespace TraeTools;

// 注意：.NET Android SDK 会生成含 Android.App 的全局 using，此处必须写全称
// Avalonia.Application，否则与 Android.App.Application 歧义（CS0104）
public partial class App : Avalonia.Application
{
    /// <summary>系统托盘图标全局引用（TrayIcon 无源头引用会被 GC 回收，故存于此；仅 Windows 桌面端使用）。</summary>
    public static TrayIcon? TrayRef;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        TraeTools.Platform.CrashLog.Install();
        TraeTools.Platform.CrashLog.Boot(
            $"App.OnFrameworkInitializationCompleted lifetime={ApplicationLifetime?.GetType().FullName}");

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(),
            };
            TraeTools.Platform.CrashLog.Boot("App: desktop MainWindow created");
        }
#if ANDROID
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            // Android：单视图生命周期，MainWindow 直接作为主视图全屏展示
            singleView.MainView = new MainWindow
            {
                DataContext = new MainViewModel(),
            };
            TraeTools.Platform.CrashLog.Boot("App: single-view MainWindow created");
        }
#endif

        base.OnFrameworkInitializationCompleted();
    }
}
