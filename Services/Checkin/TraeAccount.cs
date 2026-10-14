namespace TraeCheckin;

/// <summary>
/// 一个 Trae 账号。每个账号独立 Token(JWT)/Session(Cookie)/DeviceId，
/// 本地自动签到遍历所有 Enabled 账号，云端部署按顺序写入独立 secret。
/// </summary>
public class TraeAccount
{
    /// <summary>账号唯一标识（也用于隔离 WebView2 用户数据目录）。</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    /// <summary>用户备注名（如「主号 / 小号」）；为空时 UI 显示「账号 n」。</summary>
    public string? Name { get; set; }
    /// <summary>JWT（约 8 小时，失效时用 Session 静默换新）。</summary>
    public string? Token { get; set; }
    /// <summary>X-Cloudide-Session 会话 Cookie 值（约 14 天）。</summary>
    public string? Session { get; set; }
    /// <summary>x-device-id（16 位数字，风控关键），每账号独立。</summary>
    public string DeviceId { get; set; } = "";
    /// <summary>
    /// 账号唯一标识（JWT payload data.id，16 位数字账号 ID，跨登录会话恒定）。
    /// 用于识别"同一手机号账号"重复添加；为 null 表示尚未解析（旧数据/未登录）。
    /// </summary>
    public string? AccountUid { get; set; }
    public DateTime? TokenUpdatedAt { get; set; }
    /// <summary>该账号最近一次本地签到日期。</summary>
    public DateTime? LastCheckinDate { get; set; }
    /// <summary>该账号最近一次刷新/签到的剩余积分；-1 表示尚未获取过（仪表盘回退用全局 LastRemaining）。</summary>
    public double RemainingCredits { get; set; } = -1;
    /// <summary>是否参与本地自动签到与云端部署。</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>
    /// 是否会员：非会员每日签到只获得基础 credits（实测 150），
    /// extra_credits（连签 +50）仅会员到账。默认 false。
    /// </summary>
    public bool IsMember { get; set; }

    // ---- 账号资料（GetUserInfo / student_verification 逆向接口回填）----
    /// <summary>平台昵称（ScreenName）。Name 为空时优先用它展示。</summary>
    public string? ScreenName { get; set; }
    /// <summary>脱敏手机号（如 186******12）。</summary>
    public string? MobileMasked { get; set; }
    /// <summary>头像 URL。</summary>
    public string? AvatarUrl { get; set; }
    /// <summary>是否学生认证（student_verification.status==1）。</summary>
    public bool IsStudent { get; set; }

    // ==================== 账号简介（用户自填，随配置持久化） ====================

    /// <summary>
    /// 用户自填的账号简介 / 备注（自由文本，如「主号 · 只用来跑代码」）。
    /// 与 <see cref="Name"/> 的区别：Name 是短展示名（顶栏切换器用），Bio 是可写多行的说明。
    /// </summary>
    public string? Bio { get; set; }

    // ==================== 保活 ====================

    /// <summary>
    /// 是否对该账号开启保活：按间隔用 X-Cloudide-Session 静默换新 JWT，
    /// 避免 Token（约 8 小时）过期后掉线，同时顺带刷新资料与积分数据。
    /// 默认关闭——保活会产生周期性网络请求，交由用户按需开启。
    /// </summary>
    public bool KeepAliveEnabled { get; set; }

    /// <summary>保活间隔（分钟）。合法区间 5–1440，越界时由 <see cref="ClampKeepAliveMinutes"/> 收敛。</summary>
    public int KeepAliveIntervalMinutes { get; set; } = DefaultKeepAliveMinutes;

    /// <summary>最近一次保活执行时间（含失败的一次，用于展示与到期判定）。</summary>
    public DateTime? LastKeepAliveAt { get; set; }

    /// <summary>最近一次保活结果描述（成功/失败原因），直接展示在设置页。</summary>
    public string? LastKeepAliveMessage { get; set; }

    /// <summary>保活间隔的取值范围与默认值。</summary>
    public const int MinKeepAliveMinutes = 5;
    public const int MaxKeepAliveMinutes = 1440;
    public const int DefaultKeepAliveMinutes = 60;

    /// <summary>把任意输入收敛到合法保活间隔（分钟）。</summary>
    public static int ClampKeepAliveMinutes(int minutes)
        => minutes < MinKeepAliveMinutes ? MinKeepAliveMinutes
         : minutes > MaxKeepAliveMinutes ? MaxKeepAliveMinutes
         : minutes;

    /// <summary>该账号本轮是否到了保活时间（未开启 → 恒为 false）。</summary>
    public bool IsKeepAliveDue(DateTime now)
    {
        if (!KeepAliveEnabled) return false;
        // 从未保活过：视为立即到期（开启后马上生效，不用先等满一个间隔）
        if (LastKeepAliveAt is not { } last) return true;
        var gap = TimeSpan.FromMinutes(ClampKeepAliveMinutes(KeepAliveIntervalMinutes));
        return now - last >= gap;
    }

    // ==================== 积分数据面板（额度统计） ====================

    /// <summary>额度总配额（user_current_entitlement_list 的 credits_limit 汇总）；-1 表示尚未获取。</summary>
    public double QuotaLimit { get; set; } = -1;

    /// <summary>已消耗积分（usage.credits_amount 汇总）；-1 表示尚未获取。</summary>
    public double QuotaUsed { get; set; } = -1;

    /// <summary>统计数据最近更新时间（界面据此标注「数据截至」）。</summary>
    public DateTime? StatsUpdatedAt { get; set; }
}
