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
        => base.CustomizeAppBuilder(builder).WithInterFont();
}
#endif
