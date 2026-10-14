using System;

namespace TraeTools.Services.Notify;

/// <summary>
/// 系统本地通知：Windows 用系统托盘气泡（进通知中心），Android 用通知栏。
///
/// 定位说明：这是「本机在跑的时候」的即时反馈，属补充手段——
/// 云端签到由 GitHub Actions 执行，那一刻本机很可能并未开机，
/// 因此重要结果仍应依赖邮箱 / Webhook 等推送渠道。
/// </summary>
public static class SystemNotifier
{
#if ANDROID
    private const string ChannelId = "traetools_checkin";
    private const string ChannelName = "签到通知";
#endif

    /// <summary>本机通知所需权限是否已具备（Android 13+ 需运行时授权；其余平台恒为 true）。</summary>
    public static bool HasPermission()
    {
#if ANDROID
        try
        {
            // Android 13（API 33）起发通知需要 POST_NOTIFICATIONS 运行时权限
            if (OperatingSystem.IsAndroidVersionAtLeast(33))
            {
                var ctx = Android.App.Application.Context;
                return ctx.CheckSelfPermission("android.permission.POST_NOTIFICATIONS")
                       == Android.Content.PM.Permission.Granted;
            }
            return true;
        }
        catch
        {
            return false;
        }
#else
        return true;
#endif
    }

    /// <summary>
    /// 申请通知权限（Android 13+）。用户从设置页打开「系统本地通知」开关时调用；
    /// 其它平台为无操作。返回是否已经/仍然具备权限（授权是异步弹窗，此处只反映发起前的状态）。
    /// </summary>
    public static bool RequestPermission()
    {
#if ANDROID
        try
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(33)) return true;
            if (HasPermission()) return true;
            TraeTools.MainActivity.Current?.RequestPermissions(
                new[] { "android.permission.POST_NOTIFICATIONS" }, 1001);
            return false;
        }
        catch
        {
            return false;
        }
#else
        return true;
#endif
    }

    /// <summary>发送一条系统本地通知。</summary>
    public static NotifyResult Send(string title, string body)
    {
#if ANDROID
        return SendAndroid(title, body);
#else
        return SendDesktop(title, body);
#endif
    }

#if !ANDROID
    /// <summary>
    /// 桌面端：走主窗口的 WindowNotificationManager（右下角通知）。
    /// 该管理器必须在 UI 线程上创建与调用，签到流程可能跑在后台线程，故此处做线程切换。
    /// 未持有窗口实例时明确报错，而不是静默失败 —— 避免用户以为「已开启但没收到」。
    /// </summary>
    private static NotifyResult SendDesktop(string title, string body)
    {
        try
        {
            var show = TraeTools.App.DesktopNotifier;
            if (show == null)
                return NotifyResult.Fail("系统通知不可用：主窗口尚未就绪（请先打开主界面再试）");

            if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
                show(title, body);
            else
                Avalonia.Threading.Dispatcher.UIThread.Post(() => show(title, body));

            return NotifyResult.Ok0("已发送系统通知");
        }
        catch (Exception ex)
        {
            return NotifyResult.Fail("系统通知失败：" + ex.Message);
        }
    }
#endif

#if ANDROID
    /// <summary>Android：创建通知渠道后投递；未授权时给出可执行的提示。</summary>
    private static NotifyResult SendAndroid(string title, string body)
    {
        try
        {
            var ctx = Android.App.Application.Context;
            var mgr = ctx.GetSystemService(Android.Content.Context.NotificationService)
                      as Android.App.NotificationManager;
            if (mgr == null) return NotifyResult.Fail("系统通知服务不可用");

            if (OperatingSystem.IsAndroidVersionAtLeast(26))
            {
                // 渠道重复创建是幂等的，Android 会忽略同名渠道的重复定义
                mgr.CreateNotificationChannel(new Android.App.NotificationChannel(
                    ChannelId, ChannelName, Android.App.NotificationImportance.Default)
                {
                    Description = "签到结果与云端状态提醒",
                });
            }

            if (!HasPermission())
                return NotifyResult.Fail("未授予通知权限，请到「系统设置 → 应用 → TraeTools → 通知」中开启");

            var builder = new Android.App.Notification.Builder(ctx, ChannelId)
                .SetContentTitle(title)
                .SetContentText(body)
                .SetStyle(new Android.App.Notification.BigTextStyle().BigText(body))
                // 使用系统内置图标，无需额外打包 drawable 资源
                .SetSmallIcon(Android.Resource.Drawable.IcDialogInfo)
                .SetAutoCancel(true);

            mgr.Notify(NextId(), builder.Build());
            return NotifyResult.Ok0("已发送系统通知");
        }
        catch (Exception ex)
        {
            return NotifyResult.Fail("系统通知失败：" + ex.Message);
        }
    }

    private static int _id = 1000;

    /// <summary>每条通知用递增 ID，避免后一条覆盖前一条（便于查看多次签到结果）。</summary>
    private static int NextId() => System.Threading.Interlocked.Increment(ref _id);
#endif
}
