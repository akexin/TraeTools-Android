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
    /// 桌面端「系统本地通知」入口，由 MainWindow 在构造时挂上、关闭时摘掉。
    ///
    /// 为什么不直接调 TrayIcon：Avalonia 12 的 TrayIcon **没有** ShowNotification 方法
    /// （该方法在 0.10 之后已移除），桌面通知统一走 WindowNotificationManager，
    /// 它绑定在具体窗口上，因此必须由 MainWindow 提供实例。
    /// 参数为 (标题, 正文)。Android 端不使用此委托（走原生通知栏）。
    /// </summary>
    public static System.Action<string, string>? DesktopNotifier;

    /// <summary>
    /// 应用全局主题（三态）：ThemeMode 取 "system"（跟随系统）/ "light" / "dark"，
    /// 切换 RequestedThemeVariant 后 AppTheme.axaml 的 ThemeDictionaries 令牌整体生效。
    /// 设置页的主题选择与启动时各调用一次。
    /// </summary>
    public static void ApplyThemeMode(string? mode)
    {
        var app = Current;
        if (app == null) return;
        try
        {
            app.RequestedThemeVariant = (mode ?? "system").ToLowerInvariant() switch
            {
                "light" => ThemeVariant.Light,
                "dark" => ThemeVariant.Dark,
                // ThemeVariant.Default 不是具体配色，而是「交给平台/系统决定」的占位值
                _ => ThemeVariant.Default,
            };
        }
        catch
        {
            // 主题切换失败（极端平台差异）不应影响启动
        }
    }

    /// <summary>兼容旧调用的布尔重载：true=深色，false=浅色。</summary>
    public static void ApplyTheme(bool dark) => ApplyThemeMode(dark ? "dark" : "light");

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
            // 配置在 MainViewModel 构造时加载：视图显示前应用保存的主题偏好（跟随系统/浅色/深色）
            ApplyThemeMode(MainViewModel.AppConfig?.ThemeMode);
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
                // 配置在 MainViewModel 构造时加载：视图显示前应用保存的主题偏好（跟随系统/浅色/深色）
                ApplyThemeMode(MainViewModel.AppConfig?.ThemeMode);
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
                ApplyThemeMode(MainViewModel.AppConfig?.ThemeMode);
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
