using TraeCheckin;
using TraeTools.ViewModels;

namespace TraeTools.Services.KeepAlive;

/// <summary>
/// 账号保活：按每个账号自己设定的间隔，用 X-Cloudide-Session 静默换新 JWT，
/// 顺带刷新账号资料与积分额度，避免 Token（约 8 小时）过期后掉线。
///
/// 设计要点：
/// <list type="bullet">
/// <item>**按账号独立**：开关与间隔都挂在 <see cref="TraeAccount"/> 上，互不影响。</item>
/// <item>**只换 Token 不签到**：保活走 status 探测 + GetUserToken，不调用 claim，
///       因此不会干扰「今日是否已签到」的判定，也不会产生额外积分。</item>
/// <item>**失败不重试风暴**：无论成败都写回 LastKeepAliveAt，下一个间隔才再试一次；
///       失败原因写进 LastKeepAliveMessage 与日志页，用户可见。</item>
/// <item>**进程内互斥**：与自动签到共用 UI 定时器节拍，用静态标志防止重叠执行。</item>
/// </list>
///
/// ⚠️ 与自动签到一样，保活只在**软件运行期间**有效（进程被杀即停止）；
/// 无人值守场景请使用「云端签到」。
/// </summary>
public static class KeepAliveService
{
    private static int _running;   // 0=空闲 1=执行中，用 Interlocked 做轻量互斥

    /// <summary>执行中（供界面显示「保活中…」）。</summary>
    public static bool IsRunning => Volatile.Read(ref _running) == 1;

    /// <summary>是否存在本轮到期的账号（界面可用于展示入口红点等）。</summary>
    public static bool AnyDue(DateTime now)
    {
        var cfg = MainViewModel.AppConfig;
        return cfg != null && cfg.Accounts.Any(a => a.IsKeepAliveDue(now));
    }

    /// <summary>
    /// 对所有到期账号执行一轮保活，返回成功保活的账号数。
    /// 由主视图定时器周期调用；内部自带重入保护，重复调用直接返回 0。
    /// </summary>
    public static async Task<int> RunDueAsync()
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return 0;
        try
        {
            var cfg = MainViewModel.AppConfig;
            if (cfg == null) return 0;

            var now = DateTime.Now;
            var due = cfg.Accounts.Where(a => a.IsKeepAliveDue(now)).ToList();
            if (due.Count == 0) return 0;

            AppLog.Info("keepalive", "", $"===== 保活开始，到期账号 {due.Count} 个 =====");
            int ok = 0;
            foreach (var acc in due)
            {
                var name = DisplayName(acc);
                var (success, message) = await KeepAliveOneAsync(acc);
                acc.LastKeepAliveAt = DateTime.Now;
                acc.LastKeepAliveMessage = message;
                if (success)
                {
                    ok++;
                    AppLog.Info("keepalive", name, "保活成功：" + message);
                }
                else
                {
                    AppLog.Warn("keepalive", name, "保活失败：" + message);
                }
                // 账号间隔：避免多账号同时在极短时间内打出请求（与签到同一套防风控思路）
                if (due.Count > 1 && !ReferenceEquals(acc, due[^1]))
                {
                    int baseSec = cfg.CheckinIntervalSeconds > 0 ? cfg.CheckinIntervalSeconds : 5;
                    await Task.Delay(Math.Max(800, baseSec * 500));
                }
            }
            try { cfg.Save(); } catch { /* 保存失败不影响本轮结果 */ }
            AppLog.Info("keepalive", "", $"===== 保活结束，成功 {ok} / {due.Count} =====");
            return ok;
        }
        catch (Exception ex)
        {
            AppLog.Error("keepalive", "", "保活异常：" + ex.Message);
            return 0;
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    /// <summary>
    /// 对单个账号执行一次保活：校验/换新 Token → 刷新额度 → 刷新资料。
    /// 不落盘、不改 LastKeepAliveAt（由调用方统一处理），便于「立即保活」按钮复用。
    /// </summary>
    public static async Task<(bool Ok, string Message)> KeepAliveOneAsync(TraeAccount acc)
    {
        var api = MainViewModel.CheckinApi;
        if (api == null) return (false, "服务未初始化");
        if (string.IsNullOrWhiteSpace(acc.Token) && string.IsNullOrWhiteSpace(acc.Session))
            return (false, "未登录：既无 Token 也无会话 Cookie");

        AccountHelpers.EnsureDeviceId(acc);

        // ① Token 校验/静默换新（内部会做一次 status 探测；失败且有 Session 时自动续期）
        bool valid = await AccountHelpers.EnsureValidTokenAsync(acc);
        if (!valid)
            return (false, string.IsNullOrEmpty(acc.Session)
                ? "会话失效且无 Cookie 可续期，请重新登录"
                : "Token 与 Cookie 均已失效，请重新登录");

        // ② 顺带刷新积分额度（保活的同时把数据面板刷新，不额外增加一次用户操作）
        string detail = "Token 已刷新";
        try
        {
            var ent = await api.GetEntitlementAsync(acc.Token ?? "", acc.DeviceId);
            if (ent != null)
            {
                acc.QuotaLimit = ent.Limit;
                acc.QuotaUsed = ent.Used;
                acc.RemainingCredits = ent.Remaining;
                acc.StatsUpdatedAt = DateTime.Now;
                try { MainViewModel.CheckinDb?.InsertSnapshot(acc.Id, ent.Remaining); } catch { /* 快照写失败不影响 */ }
                detail += $"，剩余 {ent.Remaining:0} 积分（已用 {ent.Used:0}/{ent.Limit:0}）";
            }
            else
            {
                detail += "，额度查询失败：" + (api.LastError ?? "未知原因");
            }
        }
        catch (Exception ex)
        {
            detail += "，额度查询异常：" + ex.Message;
        }

        // ③ 刷新账号资料（昵称/头像/学生认证，按天内存缓存，通常不产生请求）
        try { await AccountHelpers.RefreshProfileAsync(acc); } catch { /* 资料刷新失败不影响保活 */ }

        var expiry = TokenUtils.TryGetExpiry(acc.Token);
        if (expiry.HasValue) detail += $"，新 Token 有效期至 {expiry.Value:MM-dd HH:mm}";
        return (true, detail);
    }

    /// <summary>账号展示名（与其它模块同口径）。</summary>
    private static string DisplayName(TraeAccount acc)
    {
        if (!string.IsNullOrWhiteSpace(acc.Name)) return acc.Name!;
        if (!string.IsNullOrWhiteSpace(acc.ScreenName)) return acc.ScreenName!;
        if (!string.IsNullOrWhiteSpace(acc.MobileMasked)) return acc.MobileMasked!;
        return acc.Id.Length > 6 ? acc.Id[..6] : acc.Id;
    }
}
