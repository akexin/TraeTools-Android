using System.Diagnostics;

namespace TraeTools.Platform;

/// <summary>
/// 用系统默认浏览器打开 URL（跨平台封装）。
/// Windows：ShellExecute；Android：Intent.ACTION_VIEW（无浏览器时静默失败）。
/// </summary>
internal static class BrowserLauncher
{
    public static void Open(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
#if ANDROID
            var uri = Android.Net.Uri.Parse(url)!;
            var intent = new Android.Content.Intent(Android.Content.Intent.ActionView, uri);
            intent.AddFlags(Android.Content.ActivityFlags.NewTask);
            Android.App.Application.Context!.StartActivity(intent);
#else
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
#endif
        }
        catch { /* 无浏览器/被拦截等场景忽略 */ }
    }
}
