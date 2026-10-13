using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using TraeTools.ViewModels;
using TraeTools.Views;
// CrashLog 是静态类，不能赋值给局部变量，故用别名直接调用
using CrashLog = TraeTools.Platform.CrashLog;

namespace TraeTools;

// 注意：.NET Android SDK 会生成含 Android.App 的全局 using，此处必须写全称
// Avalonia.Application，否则与 Android.App.Application 歧义（CS0104）
public partial class App : Avalonia.Application
{
    /// <summary>系统托盘图标全局引用（TrayIcon 无源头引用会被 GC 回收，故存于此；仅 Windows 桌面端使用）。</summary>
    public static TrayIcon? TrayRef;

    /// <summary>
    /// 应用全局主题（亮/暗）：切换 RequestedThemeVariant，AppTheme.axaml 中
    /// ThemeDictionaries 内的令牌随之整体切换；设置页深色模式开关实时调用。
    /// </summary>
    public static void ApplyTheme(bool dark)
    {
        var app = Current;
        if (app == null) return;
        app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    public override void Initialize()
    {
        try
        {
            AvaloniaXamlLoader.Load(this);
            CrashLog.Boot("App.Initialize completed");
        }
        catch (System.Exception ex)
        {
            CrashLog.Write("App.Initialize", ex, true);
            throw;
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        CrashLog.Install();
        CrashLog.Boot(
            $"App.OnFrameworkInitializationCompleted lifetime={ApplicationLifetime?.GetType().FullName}");

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(),
            };
            // 配置在 MainViewModel 构造时加载：视图显示前应用保存的亮/暗主题偏好
            ApplyTheme(MainViewModel.AppConfig?.DarkMode ?? false);
            CrashLog.Boot("App: desktop MainWindow created");
        }
#if ANDROID
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            // 上次运行崩溃过：本次先进错误页（可复制堆栈 / 清除后重试），避免反复闪退
            var flag = CrashLog.ReadFlag();
            if (!string.IsNullOrEmpty(flag))
            {
                CrashLog.Boot("App: previous crash detected -> startup error view");
                singleView.MainView = BuildErrorView(singleView, "上次启动崩溃了", flag);
                base.OnFrameworkInitializationCompleted();
                return;
            }

            try
            {
                // Android：单视图生命周期，MainView（UserControl）作为根视图全屏展示。
                // 不能用 MainWindow（Window）：单视图平台创建 Window 会抛 NotSupportedException。
                singleView.MainView = new MainView
                {
                    DataContext = new MainViewModel(),
                };
                // 配置在 MainViewModel 构造时加载：视图显示前应用保存的亮/暗主题偏好
                ApplyTheme(MainViewModel.AppConfig?.DarkMode ?? false);
                CrashLog.Boot("App: single-view MainView created");
            }
            catch (System.Exception ex)
            {
                // 主界面构建失败也不能闪退：显示可读的错误页
                CrashLog.Write("App: create MainWindow failed", ex, true);
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
                 + CrashLog.ReadRecent();

        return new StartupErrorView(title, body, CrashLog.LogDirectory, () =>
        {
            CrashLog.ClearFlag();
            try
            {
                lifetime.MainView = new MainView { DataContext = new MainViewModel() };
                ApplyTheme(MainViewModel.AppConfig?.DarkMode ?? false);
                CrashLog.Boot("App: retry create MainView ok");
            }
            catch (System.Exception ex)
            {
                CrashLog.Write("App: retry create MainView failed", ex, true);
            }
        });
    }
#endif
}
