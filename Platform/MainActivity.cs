#if ANDROID
using Android.App;
using Android.Content.PM;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;

namespace TraeTools;

/// <summary>Android 唯一 Activity：承载 Avalonia 单视图生命周期。</summary>
[Activity(
    Label = "TraeTools",
    Theme = "@style/MyTheme.NoActionBar",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    protected override void OnCreate(Android.OS.Bundle? savedInstanceState)
    {
        try
        {
            base.OnCreate(savedInstanceState);
            TraeTools.Platform.CrashLog.Boot("MainActivity.OnCreate completed");
        }
        catch (System.Exception ex)
        {
            // Activity 创建即失败时，用原生页面把异常显示出来（而不是静默闪退）
            TraeTools.Platform.CrashLog.Write("MainActivity.OnCreate", ex, true);
            TraeTools.Platform.CrashReportActivity.Launch(ex.ToString());
        }
    }
}

/// <summary>Android Application：提供 Avalonia AppBuilder（Inter 字体）。</summary>
[Application]
public class TraeAndroidApplication : AvaloniaAndroidApplication<App>
{
    protected TraeAndroidApplication(nint javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
        // 进程内最早的可执行点（早于 Avalonia 初始化）：先装好全局异常捕获，
        // 之后任何阶段崩溃都会写日志并拉起原生崩溃报告页，而不再是"一闪而过"。
        try
        {
            TraeTools.Platform.CrashLog.Install(text =>
                TraeTools.Platform.CrashReportActivity.Launch(text));
            TraeTools.Platform.CrashLog.Boot("Application ctor: crash handler installed");
        }
        catch
        {
            // 安装失败不能影响启动
        }
    }

    protected override Avalonia.AppBuilder CustomizeAppBuilder(Avalonia.AppBuilder builder)
    {
        var b = base.CustomizeAppBuilder(builder);
        try
        {
            // Avalonia 12 的 Android 渲染后端按 RenderingMode 顺序尝试（默认 [Egl, Software]）。
            // 问题：EglPlatformGraphics.TryCreate() 在部分机型上不是返回 null 而是直接抛异常
            // （Android 16 / 澎湃 OS 实测启动即闪退），此时 Software 兜底根本不会被执行，
            // 平台层继续抛 InvalidOperationException / native 崩溃 → 进程直接死。
            // 因此这里显式只用软件渲染：本应用是轻量 IO 型工具，先保证能起来。
            b = b.With(new Avalonia.AndroidPlatformOptions
            {
                RenderingMode = new[] { Avalonia.AndroidRenderingMode.Software }
            });
            TraeTools.Platform.CrashLog.Boot("CustomizeAppBuilder: RenderingMode=Software");
        }
        catch (System.Exception ex)
        {
            TraeTools.Platform.CrashLog.Write("CustomizeAppBuilder", ex);
        }
        return b.WithInterFont();
    }
}
#endif
