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
        try
        {
            AvaloniaXamlLoader.Load(this);
            TraeTools.Platform.CrashLog.Boot("App.Initialize completed");
        }
        catch (System.Exception ex)
        {
            TraeTools.Platform.CrashLog.Write("App.Initialize", ex, true);
            throw;
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var log = TraeTools.Platform.CrashLog;
        log.Install();
        log.Boot(
            $"App.OnFrameworkInitializationCompleted lifetime={ApplicationLifetime?.GetType().FullName}");

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(),
            };
            log.Boot("App: desktop MainWindow created");
        }
#if ANDROID
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            // 上次运行崩溃过：本次先进错误页（可复制堆栈 / 清除后重试），避免反复闪退
            var flag = log.ReadFlag();
            if (!string.IsNullOrEmpty(flag))
            {
                log.Boot("App: previous crash detected -> startup error view");
                singleView.MainView = BuildErrorView(singleView, "上次启动崩溃了", flag);
                base.OnFrameworkInitializationCompleted();
                return;
            }

            try
            {
                // Android：单视图生命周期，MainWindow 直接作为主视图全屏展示
                singleView.MainView = new MainWindow
                {
                    DataContext = new MainViewModel(),
                };
                log.Boot("App: single-view MainWindow created");
            }
            catch (System.Exception ex)
            {
                // 主界面构建失败也不能闪退：显示可读的错误页
                log.Write("App: create MainWindow failed", ex, true);
                singleView.MainView = BuildErrorView(singleView, "界面初始化失败", ex.ToString());
            }
        }
#endif

        base.OnFrameworkInitializationCompleted();
    }

#if ANDROID
    /// <summary>构建启动错误页：异常堆栈 + 启动日志 + 「清除并重新加载界面」。</summary>
    private static Control BuildErrorView(
        Avalonia.Controls.ApplicationLifetimes.ISingleViewApplicationLifetime lifetime,
        string title,
        string detail)
    {
        var body = detail + System.Environment.NewLine + System.Environment.NewLine
                 + "---- 启动日志（尾部）----" + System.Environment.NewLine
                 + TraeTools.Platform.CrashLog.ReadRecent();

        return new StartupErrorView(title, body, TraeTools.Platform.CrashLog.LogDirectory, () =>
        {
            TraeTools.Platform.CrashLog.ClearFlag();
            try
            {
                lifetime.MainView = new MainWindow { DataContext = new MainViewModel() };
                TraeTools.Platform.CrashLog.Boot("App: retry create MainWindow ok");
            }
            catch (System.Exception ex)
            {
                TraeTools.Platform.CrashLog.Write("App: retry create MainWindow failed", ex, true);
            }
        });
    }
#endif
}
