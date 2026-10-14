using System.Text;
using System.Text.Json;

namespace TraeCheckin;

/// <summary>
/// 本地配置：存储 token、设备号、自动签到设置。
/// 保存到数据根目录 TraeTools/config.json（Windows 为 %APPDATA%，Android 为 App 私有目录）。
/// 敏感字段（Token/Session/GitHubToken/DeviceId）以平台机制加密落盘
/// （Windows：DPAPI；Android：Keystore），加载时解密回内存；旧版明文在首次加载时自动迁移加密。
/// </summary>
public class AppConfig
{
    /// <summary>敏感字段加密版本标记："dpapi-v1"=已用 DPAPI 加密；null=旧版明文（等待迁移）。</summary>
    public string? SecretsVaultVersion { get; set; }
    public string? Token { get; set; }
    /// <summary>X-Cloudide-Session 会话 Cookie 值（约 14 天有效），用于 token 失效时静默换新。</summary>
    public string? Session { get; set; }
    public DateTime? TokenUpdatedAt { get; set; }
    /// <summary>GitHub OAuth 设备码授权得到的 access_token（用于云端自动签到部署）。</summary>
    public string? GitHubToken { get; set; }
    /// <summary>GitHub 授权后的登录用户名（fork 目标 owner）。</summary>
    public string? GitHubLogin { get; set; }
    /// <summary>飞书机器人 webhook（签到结果推送；为空则关闭推送）。</summary>
    public string? FeishuWebhook { get; set; }
    public string DeviceId { get; set; } = GenerateDeviceId();
    public bool AutoCheckinEnabled { get; set; } = true;
    public string AutoCheckinTime { get; set; } = "08:00";
    /// <summary>多账号签到间隔（秒），每个账号签到完成后等待该时长再签到下一个；默认 5 秒。</summary>
    public int CheckinIntervalSeconds { get; set; } = 5;
    public DateTime? LastCheckinDate { get; set; }
    public double LastRemaining { get; set; } = -1;
    /// <summary>全部 Trae 账号（多账号模型主存储）。</summary>
    public List<TraeAccount> Accounts { get; set; } = new();
    /// <summary>仪表盘当前展示的账号 Id；为空时取 Accounts[0]。</summary>
    public string? ActiveAccountId { get; set; }
    /// <summary>主窗口上次关闭时的位置与尺寸（正常状态的边界），null/无效时用默认尺寸居中。</summary>
    public int? WindowLeft { get; set; }
    public int? WindowTop { get; set; }
    public int? WindowWidth { get; set; }
    public int? WindowHeight { get; set; }
    /// <summary>是否已同意首次启动的用户协议（EULA）。同意后不再弹出。</summary>
    public bool EulaAccepted { get; set; }
    /// <summary>云端部署成功后是否已询问过用户「愿不愿意给源仓库点 star」。置 true 后不再打扰。</summary>
    public bool StarAskedAfterDeploy { get; set; }
    /// <summary>关闭主窗口时是否最小化到系统托盘（托盘驻留）。</summary>
    public bool MinimizeToTray { get; set; } = true;
    /// <summary>深色模式：true 时启动即应用暗色主题（设置页可实时切换）。</summary>
    public bool DarkMode { get; set; }

    /// <summary>
    /// 主题模式三态："system"=跟随系统 / "light"=浅色 / "dark"=深色。
    /// 空值代表旧版配置，由 <see cref="DarkMode"/> 迁移得到（见 NormalizeTheme）。
    /// </summary>
    public string ThemeMode { get; set; } = "";

    /// <summary>日志页「自动滚动」偏好（最近日志与全部日志共用）。</summary>
    public bool LogAutoScroll { get; set; } = true;

    // ==================== 推送通知配置 ====================

    /// <summary>签到完成后是否触发推送（总开关，关闭后各渠道一律不发送）。</summary>
    public bool NotifyOnCheckin { get; set; } = true;

    /// <summary>系统本地通知：Windows 通知中心 / Android 通知栏。</summary>
    public bool NotifySystemEnabled { get; set; }

    /// <summary>邮箱（SMTP）推送总开关。</summary>
    public bool EmailEnabled { get; set; }
    public string SmtpHost { get; set; } = "";
    /// <summary>SMTP 端口：465=隐式 SSL，587=STARTTLS，25=明文。</summary>
    public int SmtpPort { get; set; } = 465;
    /// <summary>连接安全方式："ssl"=隐式 TLS / "starttls"=STARTTLS / "none"=明文。</summary>
    public string SmtpSecurity { get; set; } = "ssl";
    public string SmtpUser { get; set; } = "";
    /// <summary>SMTP 密码 / 授权码（加密落盘）。</summary>
    public string? SmtpPassword { get; set; }
    /// <summary>发件人地址；为空时回退 <see cref="SmtpUser"/>。</summary>
    public string SmtpFrom { get; set; } = "";
    /// <summary>收件人，多个以逗号/分号分隔。</summary>
    public string SmtpTo { get; set; } = "";

    /// <summary>通用 Webhook 推送总开关（兼容 Server酱 / PushPlus / Bark 等）。</summary>
    public bool WebhookEnabled { get; set; }
    /// <summary>目标地址（含令牌，加密落盘）。</summary>
    public string? WebhookUrl { get; set; }
    /// <summary>请求体模板，支持 {title} / {content} 占位符。</summary>
    public string WebhookBodyTemplate { get; set; } = "{\"title\":\"{title}\",\"content\":\"{content}\"}";
    public string WebhookContentType { get; set; } = "application/json";

    /// <summary>钉钉群机器人。</summary>
    public bool DingTalkEnabled { get; set; }
    public string? DingTalkWebhook { get; set; }
    /// <summary>钉钉「加签」密钥（可选；填写后启用 HMAC-SHA256 签名）。</summary>
    public string? DingTalkSecret { get; set; }

    /// <summary>企业微信群机器人。</summary>
    public bool WeComEnabled { get; set; }
    public string? WeComWebhook { get; set; }

    /// <summary>Telegram Bot 推送。</summary>
    public bool TelegramEnabled { get; set; }
    /// <summary>Bot Token（加密落盘）。</summary>
    public string? TelegramBotToken { get; set; }
    /// <summary>目标会话 ID（私聊为数字 ID，群为负数）。</summary>
    public string TelegramChatId { get; set; } = "";

    private static string ConfigDir => TraeTools.Services.DataPaths.Root;

    private static string ConfigPath => Path.Combine(ConfigDir, "config.json");

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var cfg = JsonSerializer.Deserialize<AppConfig>(json);
                if (cfg != null)
                {
                    // DPAPI 加密迁移：已是 dpapi-v1 则解密回内存使用；旧明文标记版本，稍后统一加密落盘
                    var needSeal = cfg.SecretsVaultVersion != SecretVaultVersionV1;
                    UnsealSecrets(cfg);
                    cfg.SecretsVaultVersion = SecretVaultVersionV1;

                    // 多账号迁移：旧单账号转成 Accounts[0]
                    if (TryMigrateLegacy(cfg))
                    {
                        // 迁移后清空旧字段，避免双份数据源（DeviceId 已复制进新账号）
                        cfg.Token = null;
                        cfg.Session = null;
                        cfg.TokenUpdatedAt = null;
                        cfg.LastCheckinDate = null;
                        if (string.IsNullOrEmpty(cfg.ActiveAccountId))
                            cfg.ActiveAccountId = cfg.Accounts[0].Id;
                        cfg.Save();
                    }
                    // 迁移：优先复用官方客户端的真实 Aha 设备 ID，否则签到接口风控会返回 9074
                    var aha = TryResolveAhaDeviceId();
                    if (aha != null && aha != cfg.DeviceId)
                    {
                        cfg.DeviceId = aha;
                        cfg.Save();
                    }
                    // 旧明文首次加载：立即加密落盘，避免明文长期留存
                    // 主题模式迁移：旧配置只有 DarkMode 布尔值，此处补齐三态 ThemeMode
                    bool themeMigrated = cfg.NormalizeTheme();
                    if (needSeal || themeMigrated) cfg.Save();
                    return cfg;
                }
            }
        }
        catch { /* 配置损坏时回退默认 */ }

        var fresh = new AppConfig();
        var resolved = TryResolveAhaDeviceId();
        if (resolved != null) fresh.DeviceId = resolved;
        return fresh;
    }

    /// <summary>
    /// 单账号 → 多账号迁移：当 Accounts 为空且存在旧版 Token/Session 时，
    /// 生成第一个账号并放入 Accounts。返回是否发生迁移。幂等（已有账号则不动）。
    /// </summary>
    public static bool TryMigrateLegacy(AppConfig cfg)
    {
        if (cfg.Accounts.Count > 0) return false;
        if (string.IsNullOrEmpty(cfg.Token) && string.IsNullOrEmpty(cfg.Session)) return false;

        cfg.Accounts.Add(new TraeAccount
        {
            Token = cfg.Token,
            Session = cfg.Session,
            DeviceId = string.IsNullOrEmpty(cfg.DeviceId) ? "" : cfg.DeviceId,
            TokenUpdatedAt = cfg.TokenUpdatedAt,
            LastCheckinDate = cfg.LastCheckinDate
        });
        return true;
    }

    /// <summary>
    /// 生成一个 16 位十进制设备 ID（与 TRAE 客户端 Aha SDK 的设备号格式一致）。
    /// 签到接口风控要求 x-device-id 为数字设备号，使用 GUID/UUID 会触发 9074。
    /// </summary>
    private static string GenerateDeviceId()
    {
        return Random.Shared.NextInt64(1_000_000_000_000_000L, 10_000_000_000_000_000L).ToString();
    }

    /// <summary>
    /// 从本机 TRAE 官方客户端的数据目录解析 Aha 设备 ID（16 位数字）。
    /// storage.json 中存在形如 "iCubeAuthInfo://icube-dc:3049374157909753" 的键。
    /// 2026-10 起 TraeWork 与 TraeCode 已合并为统一的 TRAE 客户端，
    /// 目录名覆盖新旧各版本（TRAE / TRAE CN / TRAE SOLO CN / Trae CN）。
    /// </summary>
    internal static string? TryResolveAhaDeviceId()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dirs = new[] { "TRAE", "TRAE CN", "TRAE SOLO CN", "Trae CN", "TRAE SOLO" };
        foreach (var dir in dirs)
        {
            var path = Path.Combine(appData, dir, "User", "globalStorage", "storage.json");
            if (!File.Exists(path)) continue;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    const string prefix = "iCubeAuthInfo://icube-dc:";
                    if (!prop.Name.StartsWith(prefix, StringComparison.Ordinal)) continue;
                    var id = prop.Name.Substring(prefix.Length);
                    if (id.Length >= 8 && id.All(char.IsDigit)) return id;
                }
            }
            catch { /* 忽略读取/解析失败，继续尝试其他目录 */ }
        }
        return null;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            // 写「加密副本」而非 this：敏感字段加密落盘，且不修改内存中的明文（各调用方无感）
            var json = JsonSerializer.Serialize(CloneForStorage(), new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
        catch { /* 保存失败（含 DPAPI 异常）时保留磁盘旧文件，下次启动仍可读取 */ }
    }

    /// <summary>敏感字段加密版本号。</summary>
    private const string SecretVaultVersionV1 = "dpapi-v1";

    /// <summary>敏感字段清单（落盘前加密、加载后解密；新增敏感字段只需加到这里）。</summary>
    private static readonly JsonSerializerOptions CloneOptions = new();

    /// <summary>
    /// 生成一份「敏感字段已加密」的落盘副本。
    ///
    /// 实现方式为 JSON 深拷贝（而非逐字段白名单拷贝）：历史上的白名单写法要求每新增一个
    /// 配置项都必须在拷贝清单里再写一遍，一旦遗漏该配置就永远无法落盘
    /// （DarkMode 曾因此丢失，深色偏好重启即失效）。JSON 深拷贝天然覆盖全部属性，
    /// 从根本上消除这类缺陷；随后只对敏感字段做加密覆盖。
    /// </summary>
    private AppConfig CloneForStorage()
    {
        // 先做 JSON 深拷贝：copy 与 this 完全独立，之后的加密只作用在 copy 上
        var copy = JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(this, CloneOptions), CloneOptions)
                   ?? new AppConfig();

        // ⚠️ 以下所有加密都必须读 copy 自己的值、写 copy 自己的字段。
        // 这里刻意全部写成 copy.xxx：一旦误写成裸的 Accounts / Token（即 this 的成员），
        // 就会**就地加密运行中实例**的敏感字段 —— 表现为「保存一次设置之后，
        // 内存里的 Token 变成 DPAPI 密文，后续所有接口调用发出密文、签到全部失败」。
        copy.SecretsVaultVersion = SecretVaultVersionV1;
        copy.Token = EncryptSecret(copy.Token);
        copy.Session = EncryptSecret(copy.Session);
        copy.GitHubToken = EncryptSecret(copy.GitHubToken);
        // DeviceId 非可空：加密失败（Protect 返回 null）时退回明文，而不是写入 null。
        // 明文 DeviceId 在下次加载时解密会失败并被原样保留，属于可自愈的降级，比写入 null 安全。
        copy.DeviceId = EncryptSecret(copy.DeviceId) ?? copy.DeviceId;
        // 推送渠道中内嵌令牌的字段同样按密文落盘
        copy.FeishuWebhook = EncryptSecret(copy.FeishuWebhook);
        copy.SmtpPassword = EncryptSecret(copy.SmtpPassword);
        copy.WebhookUrl = EncryptSecret(copy.WebhookUrl);
        copy.DingTalkWebhook = EncryptSecret(copy.DingTalkWebhook);
        copy.DingTalkSecret = EncryptSecret(copy.DingTalkSecret);
        copy.WeComWebhook = EncryptSecret(copy.WeComWebhook);
        copy.TelegramBotToken = EncryptSecret(copy.TelegramBotToken);

        // 账号里的敏感字段：逐项覆写 copy.Accounts 中「深拷贝出来的新对象」
        foreach (var a in copy.Accounts)
        {
            a.Token = EncryptSecret(a.Token);
            a.Session = EncryptSecret(a.Session);
            // 同 DeviceId：非可空，加密失败时保留明文而不是赋 null
            a.DeviceId = EncryptSecret(a.DeviceId) ?? a.DeviceId;
        }

        return copy;
    }

    /// <summary>
    /// 已加密配置加载后：解密敏感字段回内存（仅 dpapi-v1 生效；旧明文保持原样等迁移）。
    /// </summary>
    private static void UnsealSecrets(AppConfig cfg)
    {
        if (cfg.SecretsVaultVersion != SecretVaultVersionV1) return;
        cfg.Token = DecryptSecret(cfg.Token);
        cfg.Session = DecryptSecret(cfg.Session);
        cfg.GitHubToken = DecryptSecret(cfg.GitHubToken);
        // 飞书 webhook 在旧版本是以明文存放的（当时不在加密清单内），
        // 因此解密失败时必须保留原值，否则老配置的推送地址会被静默清空。
        cfg.FeishuWebhook = DecryptSecret(cfg.FeishuWebhook) ?? cfg.FeishuWebhook;
        cfg.SmtpPassword = DecryptSecret(cfg.SmtpPassword);
        cfg.WebhookUrl = DecryptSecret(cfg.WebhookUrl);
        cfg.DingTalkWebhook = DecryptSecret(cfg.DingTalkWebhook);
        cfg.DingTalkSecret = DecryptSecret(cfg.DingTalkSecret);
        cfg.WeComWebhook = DecryptSecret(cfg.WeComWebhook);
        cfg.TelegramBotToken = DecryptSecret(cfg.TelegramBotToken);
        var dev = DecryptSecret(cfg.DeviceId);
        if (!string.IsNullOrEmpty(dev)) cfg.DeviceId = dev; // 解密失败（换机/换用户）保留原值，等重新登录刷新
        foreach (var a in cfg.Accounts)
        {
            a.Token = DecryptSecret(a.Token);
            a.Session = DecryptSecret(a.Session);
            var aDev = DecryptSecret(a.DeviceId);
            if (!string.IsNullOrEmpty(aDev)) a.DeviceId = aDev;
        }
    }

    /// <summary>
    /// 主题模式规整：把旧版仅有的 DarkMode 布尔值迁移为三态字符串；
    /// 取值非法时回退 "system"。返回是否发生了修正（调用方据此决定是否落盘）。
    /// </summary>
    public bool NormalizeTheme()
    {
        var normalized = ThemeMode switch
        {
            "system" or "light" or "dark" => ThemeMode,
            _ => DarkMode ? "dark" : "system",
        };
        if (normalized == ThemeMode) return false;
        ThemeMode = normalized;
        return true;
    }

    /// <summary>DPAPI 加密（当前 Windows 用户作用域）。保护失败抛异常，由调用方兜底（保留旧文件）。</summary>
    private static string? EncryptSecret(string? plain) => TraeTools.Platform.Secrets.Protect(plain);

    /// <summary>DPAPI 解密；失败（如换 Windows 用户/重装系统）返回 null，触发重新登录。</summary>
    private static string? DecryptSecret(string? enc) => TraeTools.Platform.Secrets.Unprotect(enc);
}