using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace TraeTools.Platform;

/// <summary>
/// 全局崩溃 / 启动轨迹日志。
/// Android：写入外部应用目录 /storage/emulated/0/Android/data/cn.akexin.traetools/files/crash/
///（无需 root，手机文件管理器即可查看），并同步输出 logcat（tag: TraeTools）。
/// Windows：写入 %APPDATA%\TraeTools\logs\crash\。
/// 闪退排查：把 log_日期.txt 发回即可。
/// </summary>
public static class CrashLog
{
    private static readonly object LockObj = new();
    private static string? _dir;

    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Write("AppDomain.UnhandledException", e.ExceptionObject as Exception, e.IsTerminating);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };
#if ANDROID
        Android.Runtime.AndroidEnvironment.UnhandledExceptionRaiser += (_, e) =>
            Write("AndroidEnvironment.UnhandledExceptionRaiser", e.Exception);
#endif
    }

    /// <summary>记录启动轨迹（无异常也记，用于定位闪退卡在哪个环节）。</summary>
    public static void Boot(string message)
    {
        try
        {
#if ANDROID
            global::Android.Util.Log.Info("TraeTools", message);
#endif
            Append($"[BOOT {DateTime.Now:HH:mm:ss.fff}] {message}");
        }
        catch { /* 日志失败不影响主流程 */ }
    }

    public static void Write(string source, Exception? ex, bool terminating = false)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"===== {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{source}] terminating={terminating} =====");
            sb.AppendLine(ex?.ToString() ?? "(non-exception object)");
            sb.AppendLine();
#if ANDROID
            global::Android.Util.Log.Error("TraeTools", ex?.ToString() ?? source);
#endif
            Append(sb.ToString());
        }
        catch { /* 忽略 */ }
    }

    private static void Append(string text)
    {
        lock (LockObj)
        {
            var dir = EnsureDir();
            if (dir == null) return;
            File.AppendAllText(Path.Combine(dir, $"log_{DateTime.Now:yyyyMMdd}.txt"),
                text + Environment.NewLine, Encoding.UTF8);
        }
    }

    private static string? EnsureDir()
    {
        if (_dir != null) return _dir;
        try
        {
#if ANDROID
            // 外部应用专属目录：无需 root，文件管理器/USB 直接可见
            var ext = Android.App.Application.Context.GetExternalFilesDir(null);
            var baseDir = ext?.AbsolutePath;
            if (string.IsNullOrEmpty(baseDir)) return null;
            _dir = Path.Combine(baseDir, "crash");
#else
            _dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TraeTools", "logs", "crash");
#endif
            Directory.CreateDirectory(_dir);
            return _dir;
        }
        catch { return null; }
    }
}
