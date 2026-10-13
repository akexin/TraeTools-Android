#if ANDROID
namespace TraeSwitch.Services;

/// <summary>
/// Android 空实现：账号切换依赖本机 TRAE 客户端的数据目录与进程，
/// 该能力仅 Windows 桌面端提供（见 CarrierDefaults.cs 的 !ANDROID 分支）。
/// </summary>
public static class CarrierDefaults
{
    public static string SettingsDir => TraeTools.Services.DataPaths.SwitchDir;

    /// <summary>Android 无本地客户端，返回空串（界面按空值禁用相关操作）。</summary>
    public static string DefaultUserDataDir => "";

    public static string ResolveUserDataDir() => "";

    public static string DefaultProcessName => "";

    public static string DefaultClientExe => "";

    public static void ResetDetection() { }

    public static string? AutoDetectClientExe() => null;

    public static List<string> DetectFingerprint() => new();
}
#endif
