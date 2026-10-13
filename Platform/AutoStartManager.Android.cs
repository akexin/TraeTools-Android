#if ANDROID
namespace TraeCheckin;

/// <summary>
/// Android 空实现：开机自启依赖 Windows 注册表（HKCU\...\Run）。
/// Android 端自动签到仅在 App 运行期间有效；长期无人值守签到请使用「云端签到」（GitHub Actions）。
/// </summary>
internal static class AutoStartManager
{
    public static bool IsEnabled() => false;

    public static void SetEnabled(bool enabled) { /* Android 无注册表，空操作 */ }
}
#endif
