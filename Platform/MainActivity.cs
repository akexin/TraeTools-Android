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
}

/// <summary>Android Application：提供 Avalonia AppBuilder（Inter 字体）。</summary>
[Application]
public class TraeAndroidApplication : AvaloniaAndroidApplication<App>
{
    protected TraeAndroidApplication(nint javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    protected override Avalonia.AppBuilder CustomizeAppBuilder(Avalonia.AppBuilder builder)
        => base.CustomizeAppBuilder(builder).WithInterFont();
}
#endif
