using System.Diagnostics;

namespace TraeSwitch.Services;

public interface IClientController
{
    void KillAll();
    void Launch();
    bool IsRunning();
}

#if !ANDROID
/// <summary>真实进程控制：按进程名结束全部实例并重新拉起客户端。
/// 进程名支持逗号分隔的多个名字（2026-10 TraeWork/TraeCode 合并为 TRAE 后，
/// 过渡期内新旧客户端进程名并存，均参与匹配）。</summary>
public sealed class ClientController(string processName, string exePath) : IClientController
{
    /// <summary>拆分后的进程名列表（去空、去重）。</summary>
    private string[] ProcessNames => processName
        .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public void KillAll()
    {
        foreach (var name in ProcessNames)
            KillByName(name);
    }

    private void KillByName(string processName)
    {
        // 拿到的 Process 对象使用后必须 Dispose，否则常驻托盘下句柄持续累积（#33）
        Process[] procs;
        try { procs = Process.GetProcessesByName(processName); }
        catch { return; }
        foreach (var p in procs)
        {
            try { p.Kill(entireProcessTree: true); p.WaitForExit(10_000); } catch { /* 已退出则忽略 */ }
            finally { try { p.Dispose(); } catch { /* 忽略 */ }
            }
        }
    }

    public void Launch()
    {
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            throw new FileNotFoundException("找不到客户端：" + exePath);
        Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
    }

    public bool IsRunning() => ProcessNames.Sum(GetProcessCount) > 0;

    /// <summary>查询进程数并立即释放全部 Process 句柄（GetProcessesByName 返回的对象必须 Dispose）。</summary>
    private int GetProcessCount(string name)
    {
        try
        {
            var procs = Process.GetProcessesByName(name);
            var n = procs.Length;
            foreach (var p in procs) try { p.Dispose(); } catch { /* 忽略 */ }
            return n;
        }
        catch { return 0; }
    }
}
#endif // !ANDROID

#if ANDROID
/// <summary>Android 空实现：账号切换需要操作本地 Trae 客户端进程/文件，仅 Windows 桌面端支持。</summary>
/// 注意：文件顶部已有 file-scoped namespace（两平台共用），此处不可重复声明（CS8954）。</summary>
public sealed class ClientController(string processName, string exePath) : IClientController
{
    public void KillAll() { }
    public void Launch() => throw new PlatformNotSupportedException("账号切换仅支持 Windows 桌面端");
    public bool IsRunning() => false;
}
#endif
