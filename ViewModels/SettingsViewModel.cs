using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Input.Platform;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TraeCheckin;
using TraeTools.Models;
using TraeTools.Services;
using TraeTools.Services.Avatar;
using TraeTools.Views;

namespace TraeTools.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _autoCheckinEnabled = true;

    [ObservableProperty]
    private string _autoCheckinTime = "08:00";

    [ObservableProperty]
    private int _checkinIntervalSeconds = 5;

    [ObservableProperty]
    private bool _autoStartEnabled;

    [ObservableProperty]
    private bool _minimizeToTray = true;

    /// <summary>
    /// 主题模式选择索引：0=跟随系统 / 1=浅色 / 2=深色。
    /// 用索引而非字符串绑定，避免 ComboBox 需要 ItemTemplate 与 SelectedItem 比对逻辑。
    /// </summary>
    [ObservableProperty]
    private int _themeModeIndex;

    partial void OnThemeModeIndexChanged(int value)
    {
        var mode = value switch
        {
            1 => "light",
            2 => "dark",
            _ => "system",
        };
        try
        {
            App.ApplyThemeMode(mode);
            var cfg = MainViewModel.AppConfig;
            if (cfg != null && cfg.ThemeMode != mode)
            {
                cfg.ThemeMode = mode;
                // 同步旧字段：若用户回退到不识别 ThemeMode 的旧版本，亮/暗仍能保持一致
                cfg.DarkMode = mode == "dark";
                cfg.Save();
            }
        }
        catch { /* 主题应用/保存失败不影响其它设置 */ }
    }

    [ObservableProperty]
    private string _tokenString = "（未登录）";

    /// <summary>Token 仅展示首尾各 8 位，中间打码；过短全打码（避免 UI 明文泄露）。</summary>
    private static string MaskToken(string? token)
    {
        if (string.IsNullOrEmpty(token)) return "（无 Token）";
        if (token.Length <= 20) return new string('*', 8);
        return token[..8] + "…" + token[^8..];
    }

    [ObservableProperty]
    private string _tokenUpdateTime = "—";

    /// <summary>选中账号的设备号（x-device-id，16 位数字；风控关键）。</summary>
    [ObservableProperty]
    private string _deviceIdText = "—";

    [ObservableProperty]
    private string _feishuWebhook = "";

    // ==================== 推送通知配置 ====================

    /// <summary>签到完成后是否推送（总开关，关闭后所有渠道静默）。</summary>
    [ObservableProperty]
    private bool _notifyOnCheckin = true;

    /// <summary>系统本地通知开关（Windows 通知中心 / Android 通知栏）。</summary>
    [ObservableProperty]
    private bool _notifySystemEnabled;

    /// <summary>打开系统通知时顺带申请 Android 13+ 的通知权限。</summary>
    partial void OnNotifySystemEnabledChanged(bool value)
    {
        if (!value) return;
        try
        {
            if (!TraeTools.Services.Notify.SystemNotifier.RequestPermission())
                PushStatus = "已尝试申请通知权限，请在弹出的系统对话框中允许";
        }
        catch { /* 权限申请失败不影响开关本身 */ }
    }

    // ---- 邮箱（SMTP） ----
    [ObservableProperty]
    private bool _emailEnabled;
    [ObservableProperty]
    private string _smtpHost = "";
    [ObservableProperty]
    private int _smtpPort = 465;
    /// <summary>连接安全方式索引：0=SSL(465) / 1=STARTTLS(587) / 2=无加密(25)。</summary>
    [ObservableProperty]
    private int _smtpSecurityIndex;
    [ObservableProperty]
    private string _smtpUser = "";
    [ObservableProperty]
    private string _smtpPassword = "";
    [ObservableProperty]
    private string _smtpFrom = "";
    [ObservableProperty]
    private string _smtpTo = "";

    // ---- 通用 Webhook ----
    [ObservableProperty]
    private bool _webhookEnabled;
    [ObservableProperty]
    private string _webhookUrl = "";
    [ObservableProperty]
    private string _webhookBodyTemplate = "{\"title\":\"{title}\",\"content\":\"{content}\"}";
    [ObservableProperty]
    private string _webhookContentType = "application/json";

    // ---- 钉钉 / 企业微信 ----
    [ObservableProperty]
    private bool _dingTalkEnabled;
    [ObservableProperty]
    private string _dingTalkWebhook = "";
    [ObservableProperty]
    private string _dingTalkSecret = "";
    [ObservableProperty]
    private bool _weComEnabled;
    [ObservableProperty]
    private string _weComWebhook = "";

    // ---- Telegram ----
    [ObservableProperty]
    private bool _telegramEnabled;
    [ObservableProperty]
    private string _telegramBotToken = "";
    [ObservableProperty]
    private string _telegramChatId = "";

    // ==================== 账号备份 ====================

    /// <summary>备份口令（同时用于导出加密与导入解密，不落盘、仅本次会话有效）。</summary>
    [ObservableProperty]
    private string _backupPassphrase = "";

    /// <summary>备份内容文本：导出时填入结果，导入时可手工粘贴。</summary>
    [ObservableProperty]
    private string _backupText = "";

    /// <summary>是否正在执行备份读写（防重复触发）。</summary>
    [ObservableProperty]
    private bool _backupBusy;

    [ObservableProperty]
    private string _pushStatus = "";

    /// <summary>底部状态条是否显示（非空状态消息时才显示，消息自动消失后隐藏）。</summary>
    [ObservableProperty]
    private bool _showStatusBar;

    private CancellationTokenSource? _statusClearCts;

    /// <summary>状态消息自动消失：非空显示，5 秒后若无新消息则清空隐藏；新消息会重置计时。</summary>
    partial void OnPushStatusChanged(string value)
    {
        ShowStatusBar = !string.IsNullOrEmpty(value);
        _statusClearCts?.Cancel();
        _statusClearCts = null;
        if (string.IsNullOrEmpty(value)) return;
        var cts = new CancellationTokenSource();
        _statusClearCts = cts;
        _ = AutoClearStatusAsync(cts.Token);
    }

    private async Task AutoClearStatusAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(5), ct); }
        catch (TaskCanceledException) { return; }
        if (!ct.IsCancellationRequested) PushStatus = "";
    }

    /// <summary>是否正在进行「检查更新」（防止并发下载/安装）。</summary>
    [ObservableProperty]
    private bool _updateChecking;

    private int _lastUpdatePct = -1;

    public ObservableCollection<AccountInfo> Accounts { get; } = new();

    /// <summary>是否没有账号：驱动账号管理卡空状态（插画 + 去添加按钮）。</summary>
    public bool HasNoAccounts => Accounts.Count == 0;

    /// <summary>账号管理中当前选中的账号（联动 Token 面板显示对应凭证）。</summary>
    [ObservableProperty]
    private AccountInfo? _selectedAccount;

    // 关于区块属性
    // 「源仓库」统一指向本项目自身的仓库。
    // 历史上这里有两行——「打开原仓库」与「打开我的 Fork 仓库」——分别对应上游 fork 与被 fork 的关系，
    // 但 2.0 起两者实际是同一个地址（本项目仓库），显示重复且用词误导，故合并为单行「项目仓库」。
    public string RepoUrl => "https://github.com/akexin/TraeTools-Android";
    public string IssuesUrl => "https://github.com/akexin/TraeTools-Android/issues";
    public string AppVersion => TraeTools.Services.AppVersion.Display;

    /// <summary>开机自启 / 托盘驻留是否可用：仅 Windows 桌面端（Android 无注册表自启与系统托盘）。</summary>
    public bool IsWindowsDesktopOnlyFeaturesSupported => OperatingSystem.IsWindows();

    public SettingsViewModel()
    {
        // 账号增删时刷新空状态标记
        Accounts.CollectionChanged += (_, __) => OnPropertyChanged(nameof(HasNoAccounts));

        // 从 AutoStartManager 读取开机自启状态
        try
        {
            AutoStartEnabled = AutoStartManager.IsEnabled();
        }
        catch
        {
            AutoStartEnabled = false;
        }

        // 从 AppConfig 读取飞书 webhook / 到点签到 / 最小化到托盘等设置
        try
        {
            var cfg = MainViewModel.AppConfig;
            if (cfg != null)
            {
                if (!string.IsNullOrEmpty(cfg.FeishuWebhook))
                    FeishuWebhook = cfg.FeishuWebhook;
                AutoCheckinEnabled = cfg.AutoCheckinEnabled;
                AutoCheckinTime = cfg.AutoCheckinTime;
                CheckinIntervalSeconds = Math.Clamp(cfg.CheckinIntervalSeconds > 0 ? cfg.CheckinIntervalSeconds : 5, 1, 60);
                MinimizeToTray = cfg.MinimizeToTray;
                ThemeModeIndex = cfg.ThemeMode switch
                {
                    "light" => 1,
                    "dark" => 2,
                    _ => 0,
                };
                LoadNotifySettings(cfg);

                var acc = cfg.Accounts.FirstOrDefault(a => a.Id == cfg.ActiveAccountId)
                          ?? cfg.Accounts.FirstOrDefault();
                if (acc != null && !string.IsNullOrEmpty(acc.Token))
                {
                    TokenString = MaskToken(acc.Token);
                    TokenUpdateTime = acc.TokenUpdatedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "未更新";
                }
            }
        }
        catch { /* 保留默认值 */ }

        PopulateAccounts();

        // 默认选中激活账号，Token 面板随之联动
        try
        {
            var cfg = MainViewModel.AppConfig;
            var active = cfg?.Accounts.FirstOrDefault(a => a.Id == cfg.ActiveAccountId) ?? cfg?.Accounts.FirstOrDefault();
            if (active != null)
                SelectedAccount = Accounts.FirstOrDefault(a => a.Id == active.Id);
            if (SelectedAccount == null && Accounts.Count > 0)
                SelectedAccount = Accounts[0];
        }
        catch { /* 保持未选中 */ }
    }

    /// <summary>
    /// 对齐源 _stateMarks：刷新每个账号的会话/签到状态标记（已签到/待签到/需重登/未登录）。
    /// 供账号管理列表显示，与仪表盘当前激活账号保持一致。
    /// </summary>
    public async Task RefreshAccountStatesAsync()
    {
        try
        {
            var cfg = MainViewModel.AppConfig;
            var api = MainViewModel.CheckinApi;
            if (cfg == null || api == null || cfg.Accounts.Count == 0) return;

            foreach (var acc in cfg.Accounts)
            {
                var item = Accounts.FirstOrDefault(a => a.Id == acc.Id);
                if (item == null) continue;

                // 同步当前激活账号高亮（账号切换后实时框出）
                item.IsCurrent = acc.Id == cfg.ActiveAccountId;

                // 账号资料（昵称/脱敏手机号/学生认证）后台刷新并回填展示
                await AccountHelpers.RefreshProfileAsync(acc);
                item.MobileText = acc.MobileMasked ?? "";
                item.BioText = acc.Bio ?? "";
                item.IsStudent = acc.IsStudent;
                var accName = string.IsNullOrEmpty(acc.Name)
                    ? $"账号@{(acc.AccountUid ?? acc.Id.Substring(0, 6))}"
                    : acc.Name;
                if (!string.Equals(item.Name, accName)) item.Name = accName;

                if (acc.LastCheckinDate.HasValue && acc.LastCheckinDate.Value.Date == DateTime.Today)
                {
                    item.Status = "已签到";
                    item.StatusType = "ok";
                    continue;
                }
                if (string.IsNullOrEmpty(acc.Token))
                {
                    item.Status = "未登录";
                    item.StatusType = "warn";
                    continue;
                }
                bool valid = await AccountHelpers.EnsureValidTokenAsync(acc);   // 失效自动换新
                if (!valid)
                {
                    item.Status = "需重登";
                    item.StatusType = "warn";
                    continue;
                }
                bool checkedIn = false;
                var st = await api.GetStatusAsync(acc.Token ?? "", acc.DeviceId);
                if (st is { } s2 && s2.code == 0) checkedIn = s2.checked_in;
                item.Status = checkedIn ? "已签到" : "待签到";
                item.StatusType = checkedIn ? "ok" : "info";
            }
        }
        catch { /* 状态刷新失败保留原样 */ }

        // 顺手把新出现的头像加载进卡片
        LoadAvatars();

        // 保活/签到是后台跑的，刷新账号状态时一并把详情面板的保活文案与本地统计拉新
        try
        {
            var acc = SelectedTraeAccount();
            if (acc != null)
            {
                RefreshKeepAliveState(acc);
                LoadLocalStats(acc);
            }
        }
        catch { /* 面板刷新失败不影响列表 */ }
    }

    /// <summary>为账号列表卡片异步加载头像（有 AvatarUrl 且未加载过才拉，内存缓存）。</summary>
    private void LoadAvatars()
    {
        var cfg = MainViewModel.AppConfig;
        if (cfg == null) return;
        foreach (var item in Accounts)
        {
            if (item.HasAvatar) continue;
            var acc = cfg.Accounts.FirstOrDefault(a => a.Id == item.Id);
            if (string.IsNullOrEmpty(acc?.AvatarUrl)) continue;
            _ = AvatarLoader.LoadIntoAsync(acc.AvatarUrl, img =>
            {
                if (img != null) item.AvatarImage = img;
            });
        }
    }

    /// <summary>
    /// 选中账号变化 → Token 面板联动显示该账号的凭证与更新时间，
    /// 并把该账号设为**全局激活账号**（首页仪表盘、签到引擎、云端状态随之切换）。
    ///
    /// 说明：2.0 曾把这里改成「仅查看、不切换」，导致设置页无法切换账号；
    /// 现按需求恢复为点击即切换——设置页是账号列表的天然入口，多一个入口对低频操作更友好。
    /// </summary>
    partial void OnSelectedAccountChanged(AccountInfo? value)
    {
        try
        {
            var cfg = MainViewModel.AppConfig;
            var acc = cfg?.Accounts.FirstOrDefault(a => a.Id == value?.Id);
            if (acc != null)
            {
                TokenString = MaskToken(acc.Token);
                TokenUpdateTime = acc.TokenUpdatedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "—";
                DeviceIdText = acc.DeviceId;

                // 切换全局激活账号（仅当确有变化时才保存与刷新，避免无谓的连锁刷新）
                if (cfg != null && cfg.ActiveAccountId != acc.Id)
                {
                    cfg.ActiveAccountId = acc.Id;
                    try { cfg.Save(); } catch { /* 保存失败不影响界面联动 */ }
                    foreach (var item in Accounts) item.IsCurrent = item.Id == acc.Id;
                    MainViewModel.NotifyActiveAccountChanged();
                }
            }
            else
            {
                TokenString = "（无账号数据）";
                TokenUpdateTime = "—";
                DeviceIdText = "—";
            }
        }
        catch { /* 联动失败不影响 */ }

        // 账号详情面板（简介 / 积分数据 / 设备号 / 保活）随选中账号一起刷新
        LoadAccountDetail();
    }

    /// <summary>
    /// 手动更换选中账号的设备号（生成全新 16 位数字）。
    /// 设备号被 Trae 风控标记（签到返回 9074「参与用户太多」）时更换即可解除；
    /// 更换后云端自动签到需重新「云端部署」以同步新设备号。
    /// </summary>
    [RelayCommand]
    private void RegenerateDeviceId()
    {
        try
        {
            var cfg = MainViewModel.AppConfig;
            var acc = cfg?.Accounts.FirstOrDefault(a => a.Id == SelectedAccount?.Id);
            if (acc == null)
            {
                PushStatus = "请先选择账号";
                return;
            }
            acc.DeviceId = Random.Shared.NextInt64(1_000_000_000_000_000L, 10_000_000_000_000_000L).ToString();
            try { cfg?.Save(); } catch { /* 保存失败提示仍展示 */ }
            DeviceIdText = acc.DeviceId;
            PushStatus = $"已更换设备号：{acc.DeviceId}（本地生效；云端需重新部署同步）";
        }
        catch (Exception ex)
        {
            PushStatus = "更换失败：" + ex.Message;
        }
    }

    // ==================== 账号详情面板：简介 / 积分数据 / 设备号 / 保活 ====================

    /// <summary>是否选中了真实账号（驱动详情面板显隐）。</summary>
    [ObservableProperty]
    private bool _hasAccountDetail;

    /// <summary>账号简介（用户自填，双向绑定；编辑后防抖落盘）。</summary>
    [ObservableProperty]
    private string _accountBio = "";

    /// <summary>简介的辅助说明（字数提示 / 保存状态）。</summary>
    [ObservableProperty]
    private string _accountBioHint = "";

    /// <summary>详情面板里的展示名与副标题。</summary>
    [ObservableProperty]
    private string _detailTitle = "";
    [ObservableProperty]
    private string _detailSubtitle = "";

    // ---- 积分数据面板 ----

    [ObservableProperty] private string _statRemaining = "—";
    [ObservableProperty] private string _statQuota = "—";
    [ObservableProperty] private string _statConsumed = "—";
    [ObservableProperty] private string _statTotalGained = "—";
    [ObservableProperty] private string _statTodayGained = "—";
    [ObservableProperty] private string _statSignedDays = "—";
    [ObservableProperty] private string _statStreak = "—";
    [ObservableProperty] private string _statLastCheckin = "—";
    [ObservableProperty] private string _statBadges = "—";
    [ObservableProperty] private string _statUpdatedAt = "云端数据：—";

    /// <summary>正在拉取云端积分数据（防止重复点击）。</summary>
    [ObservableProperty]
    private bool _statsLoading;

    // ---- 自定义设备号 ----

    /// <summary>设备号输入框（暂存，点「保存」才写入账号；便于校验后再落盘）。</summary>
    [ObservableProperty]
    private string _customDeviceId = "";

    /// <summary>设备号校验提示（实时反馈，含与其它账号重复检测）。</summary>
    [ObservableProperty]
    private string _deviceIdHint = "";

    // ---- 保活 ----

    [ObservableProperty] private bool _keepAliveEnabled;
    [ObservableProperty] private string _keepAliveIntervalText = "60";
    [ObservableProperty] private string _keepAliveStateText = "未开启";
    /// <summary>保活间隔的输入提示（非法输入时给出区间说明）。</summary>
    [ObservableProperty] private string _keepAliveHint = "范围 5–1440 分钟";

    // ---- 安全检查 ----

    /// <summary>安全检查结论列表（风险在前）。</summary>
    public ObservableCollection<Services.Security.SecurityFinding> SecurityFindings { get; } = new();

    [ObservableProperty] private string _securitySummary = "尚未检查";
    [ObservableProperty] private bool _securityHasRun;
    [ObservableProperty] private bool _securityRunning;

    private Avalonia.Threading.DispatcherTimer? _bioSaveTimer;

    /// <summary>当前选中的真实账号对象（无则 null）。</summary>
    private TraeCheckin.TraeAccount? SelectedTraeAccount()
        => MainViewModel.AppConfig?.Accounts.FirstOrDefault(a => a.Id == SelectedAccount?.Id);

    /// <summary>把选中账号的简介 / 设备号 / 保活 / 统计一次性灌进详情面板。</summary>
    private void LoadAccountDetail()
    {
        try
        {
            var acc = SelectedTraeAccount();
            if (acc == null)
            {
                HasAccountDetail = false;
                return;
            }
            HasAccountDetail = true;

            DetailTitle = string.IsNullOrWhiteSpace(acc.Name)
                ? (acc.ScreenName ?? (acc.MobileMasked ?? "未命名账号"))
                : acc.Name!;
            DetailSubtitle = BuildSubtitle(acc);

            _loadingDetail = true;   // 抑制这些赋值触发的「立即保存」回调
            // 简介正在编辑（防抖窗口内）时不要用磁盘值覆盖输入框，否则会把用户刚打的字吃掉
            if (!IsEditingBio)
            {
                AccountBio = acc.Bio ?? "";
                AccountBioHint = BioHintFor(acc.Bio);
            }
            CustomDeviceId = acc.DeviceId ?? "";
            DeviceIdHint = ValidateDeviceId(CustomDeviceId, acc);
            KeepAliveEnabled = acc.KeepAliveEnabled;
            KeepAliveIntervalText = TraeCheckin.TraeAccount
                .ClampKeepAliveMinutes(acc.KeepAliveIntervalMinutes).ToString();
            KeepAliveHint = $"范围 {TraeCheckin.TraeAccount.MinKeepAliveMinutes}–{TraeCheckin.TraeAccount.MaxKeepAliveMinutes} 分钟";
            _loadingDetail = false;

            RefreshKeepAliveState(acc);
            LoadLocalStats(acc);
        }
        catch { /* 面板加载失败不影响其它设置 */ }
    }

    private bool _loadingDetail;

    /// <summary>简介是否处于「刚打完字、还没落盘」的防抖窗口内。</summary>
    private bool IsEditingBio => _bioSaveTimer?.IsEnabled == true;

    private static string BuildSubtitle(TraeCheckin.TraeAccount acc)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(acc.MobileMasked)) parts.Add(acc.MobileMasked!);
        if (acc.IsStudent) parts.Add("学生认证");
        parts.Add(acc.IsMember ? "会员" : "非会员");
        if (!string.IsNullOrWhiteSpace(acc.AccountUid)) parts.Add("UID " + acc.AccountUid);
        return string.Join(" · ", parts);
    }

    private static string BioHintFor(string? bio)
    {
        var len = (bio ?? "").Length;
        return len == 0 ? "留空则不显示简介" : $"{len} 字 · 已自动保存";
    }

    /// <summary>设备号校验：必须 16 位数字，且不得与其它账号重复。</summary>
    private string ValidateDeviceId(string? id, TraeCheckin.TraeAccount self)
    {
        var v = (id ?? "").Trim();
        if (v.Length == 0) return "不能为空（留空会被自动补发一个新号）";
        if (v.Length != 16) return $"需要 16 位，当前 {v.Length} 位";
        if (!v.All(char.IsDigit)) return "只能包含数字（0-9）";
        var dup = MainViewModel.AppConfig?.Accounts
            .FirstOrDefault(a => a.Id != self.Id && a.DeviceId == v);
        if (dup != null)
            return "与账号「" + (string.IsNullOrWhiteSpace(dup.Name) ? dup.Id[..6] : dup.Name) + "」重复，会触发风控";
        return "格式正确 ✓";
    }

    private void RefreshKeepAliveState(TraeCheckin.TraeAccount acc)
    {
        if (!acc.KeepAliveEnabled)
        {
            KeepAliveStateText = "未开启（Token 过期后需等下次签到才会续期）";
            return;
        }
        var when = acc.LastKeepAliveAt?.ToString("MM-dd HH:mm") ?? "尚未执行";
        KeepAliveStateText = acc.LastKeepAliveAt == null
            ? "已开启，等待首次执行"
            : $"最近 {when} · {(string.IsNullOrEmpty(acc.LastKeepAliveMessage) ? "—" : acc.LastKeepAliveMessage)}";
    }

    /// <summary>只读本地数据（签到库）算出统计，不发网络请求，切换账号可立即出数。</summary>
    private void LoadLocalStats(TraeCheckin.TraeAccount acc)
    {
        try
        {
            var db = MainViewModel.CheckinDb;
            double total = 0, today = 0;
            int days = 0, streak = 0;
            string last = "—";

            if (db != null)
            {
                var stats = db.GetAccountStats(acc.Id, acc.Name);
                total = stats.TotalCredits;
                today = stats.TodayCredits;
                days = stats.SignedDays;
                streak = db.GetAccountStreakDays(acc.Id, acc.Name);
                last = string.IsNullOrEmpty(stats.LastDate) ? "—" : stats.LastDate;
            }

            StatTotalGained = $"{total:0}";
            StatTodayGained = $"+{today:0}";
            StatSignedDays = days.ToString();
            StatStreak = streak.ToString();
            StatLastCheckin = last;

            // 额度来自上次云端拉取（保活或手动刷新时更新）
            StatRemaining = acc.RemainingCredits >= 0 ? $"{acc.RemainingCredits:0}" : "—";
            StatQuota = acc.QuotaLimit >= 0 ? $"{acc.QuotaLimit:0}" : "—";
            StatConsumed = acc.QuotaUsed >= 0 ? $"{acc.QuotaUsed:0}" : "—";
            StatUpdatedAt = acc.StatsUpdatedAt.HasValue
                ? "云端数据：" + acc.StatsUpdatedAt.Value.ToString("MM-dd HH:mm")
                : "云端数据：尚未获取（点「刷新数据」）";

            var badges = new List<string>();
            badges.Add(acc.IsMember ? "会员" : "非会员");
            if (acc.IsStudent) badges.Add("学生认证");
            badges.Add(acc.LastCheckinDate.HasValue && acc.LastCheckinDate.Value.Date == DateTime.Today
                ? "今日已签" : "今日未签");
            var exp = TraeCheckin.TokenUtils.TryGetExpiry(acc.Token);
            badges.Add(exp.HasValue
                ? (exp.Value > DateTime.Now ? $"Token 有效至 {exp.Value:MM-dd HH:mm}" : "Token 已过期")
                : "Token 有效期未知");
            StatBadges = string.Join(" · ", badges);
        }
        catch { /* 统计失败保留占位符 */ }
    }

    /// <summary>从云端拉取该账号的额度（总配额 / 已消耗 / 剩余）并写回账号，随后刷新面板。</summary>
    [RelayCommand]
    private async Task RefreshAccountStats()
    {
        if (StatsLoading) return;
        try
        {
            var api = MainViewModel.CheckinApi;
            var cfg = MainViewModel.AppConfig;
            var acc = SelectedTraeAccount();
            if (api == null || cfg == null || acc == null)
            {
                PushStatus = "请先选择账号";
                return;
            }
            StatsLoading = true;
            PushStatus = "正在拉取积分数据…";

            bool valid = await AccountHelpers.EnsureValidTokenAsync(acc);
            if (!valid)
            {
                PushStatus = "会话已失效，请重新登录后再刷新";
                return;
            }
            var ent = await api.GetEntitlementAsync(acc.Token ?? "", acc.DeviceId);
            if (ent == null)
            {
                PushStatus = "拉取失败：" + (api.LastError ?? "未知原因");
                return;
            }
            acc.QuotaLimit = ent.Limit;
            acc.QuotaUsed = ent.Used;
            acc.RemainingCredits = ent.Remaining;
            acc.StatsUpdatedAt = DateTime.Now;
            try { MainViewModel.CheckinDb?.InsertSnapshot(acc.Id, ent.Remaining); } catch { /* 快照失败不影响 */ }
            try { cfg.Save(); } catch { /* 保存失败仍刷新界面 */ }
            LoadLocalStats(acc);
            PushStatus = $"积分已刷新：剩余 {ent.Remaining:0}（已用 {ent.Used:0} / 总额 {ent.Limit:0}）✓";
        }
        catch (Exception ex)
        {
            PushStatus = "刷新失败：" + ex.Message;
        }
        finally
        {
            StatsLoading = false;
        }
    }

    /// <summary>保存自定义设备号（16 位数字校验 + 重复校验）。</summary>
    [RelayCommand]
    private void ApplyDeviceId()
    {
        try
        {
            var cfg = MainViewModel.AppConfig;
            var acc = SelectedTraeAccount();
            if (cfg == null || acc == null) { PushStatus = "请先选择账号"; return; }

            var v = (CustomDeviceId ?? "").Trim();
            var hint = ValidateDeviceId(v, acc);
            DeviceIdHint = hint;
            if (hint != "格式正确 ✓") { PushStatus = "设备号不合法：" + hint; return; }

            if (acc.DeviceId == v) { PushStatus = "设备号未变化"; return; }

            var old = acc.DeviceId;
            acc.DeviceId = v;
            try { cfg.Save(); } catch { /* 提示仍展示 */ }
            DeviceIdText = v;
            AccountHelpers.CheckinLog(DisplayOf(acc), $"自定义设备号：{old} → {v}");
            PushStatus = "设备号已保存 ✓（本地生效；云端需重新部署同步）";
        }
        catch (Exception ex)
        {
            PushStatus = "保存失败：" + ex.Message;
        }
    }

    /// <summary>随机生成一个不与其它账号重复的 16 位设备号，填进输入框（仍需点「保存」）。</summary>
    [RelayCommand]
    private void RandomDeviceId()
    {
        try
        {
            var acc = SelectedTraeAccount();
            if (acc == null) { PushStatus = "请先选择账号"; return; }
            var used = new HashSet<string>(
                MainViewModel.AppConfig?.Accounts.Where(a => a.Id != acc.Id).Select(a => a.DeviceId)
                    ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            string id;
            do { id = Random.Shared.NextInt64(1_000_000_000_000_000L, 10_000_000_000_000_000L).ToString(); }
            while (used.Contains(id));
            CustomDeviceId = id;
            DeviceIdHint = ValidateDeviceId(id, acc);
            PushStatus = "已生成新设备号，点「保存设备号」生效";
        }
        catch (Exception ex)
        {
            PushStatus = "生成失败：" + ex.Message;
        }
    }

    /// <summary>立即对选中账号执行一次保活（不等定时器）。</summary>
    [RelayCommand]
    private async Task KeepAliveNow()
    {
        try
        {
            var cfg = MainViewModel.AppConfig;
            var acc = SelectedTraeAccount();
            if (cfg == null || acc == null) { PushStatus = "请先选择账号"; return; }
            if (!acc.KeepAliveEnabled)
            {
                PushStatus = "该账号未开启保活，请先打开开关";
                return;
            }

            PushStatus = "正在保活…";
            var (ok, message) = await TraeTools.Services.KeepAlive.KeepAliveService.KeepAliveOneAsync(acc);
            acc.LastKeepAliveAt = DateTime.Now;
            acc.LastKeepAliveMessage = message;
            try { cfg.Save(); } catch { /* 保存失败仍展示结果 */ }
            RefreshKeepAliveState(acc);
            LoadLocalStats(acc);
            PushStatus = (ok ? "保活成功 ✓ " : "保活失败：") + message;
        }
        catch (Exception ex)
        {
            PushStatus = "保活异常：" + ex.Message;
        }
    }

    /// <summary>运行账号与本地安全检查，把结论填进列表。</summary>
    [RelayCommand]
    private async Task RunSecurityAudit()
    {
        if (SecurityRunning) return;
        try
        {
            SecurityRunning = true;
            SecurityFindings.Clear();
            PushStatus = "正在检查…";

            // 审计需要读配置文件与日志文件末尾（最多 12 × 64KB），放到后台线程避免卡住界面；
            // await 之后自动回到 UI 线程，因此可以直接改 ObservableCollection。
            var cfg = MainViewModel.AppConfig;
            var findings = await Task.Run(() => Services.Security.SecurityAudit.Run(cfg));

            foreach (var f in findings) SecurityFindings.Add(f);
            SecuritySummary = Services.Security.SecurityAudit.Summarize(findings);
            SecurityHasRun = true;

            int risk = findings.Count(f => f.IsRisk);
            int warn = findings.Count(f => f.IsWarn);
            AccountHelpers.AppLog("security", "",
                $"安全检查完成：风险 {risk} 项，警告 {warn} 项，通过 {findings.Count - risk - warn} 项");
            PushStatus = risk > 0 ? $"发现 {risk} 项风险，请查看下方结论" : "安全检查完成 ✓";
        }
        catch (Exception ex)
        {
            PushStatus = "安全检查失败：" + ex.Message;
        }
        finally
        {
            SecurityRunning = false;
        }
    }

    /// <summary>账号简介防抖落盘：边打字边写文件太浪费，停手 800ms 再写。</summary>
    partial void OnAccountBioChanged(string value)
    {
        if (_loadingDetail) return;
        AccountBioHint = BioHintFor(value) + " · 编辑中…";
        _bioSaveTimer ??= new Avalonia.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(800),
        };
        _bioSaveTimer.Stop();
        _bioSaveTimer.Tick -= OnBioSaveTick;
        _bioSaveTimer.Tick += OnBioSaveTick;
        _bioSaveTimer.Start();
    }

    private void OnBioSaveTick(object? sender, EventArgs e)
    {
        _bioSaveTimer?.Stop();
        try
        {
            var cfg = MainViewModel.AppConfig;
            var acc = SelectedTraeAccount();
            if (cfg == null || acc == null) return;
            var text = (AccountBio ?? "").Trim();
            if (string.Equals(acc.Bio ?? "", text, StringComparison.Ordinal)) return;
            acc.Bio = text;
            cfg.Save();
            AccountBioHint = BioHintFor(text);
            MainViewModel.Instance?.RefreshSwitchAccounts();
        }
        catch { /* 保存失败保留「编辑中…」提示，用户可再改一次触发重试 */ }
    }

    /// <summary>设备号输入框变化 → 实时校验提示。</summary>
    partial void OnCustomDeviceIdChanged(string value)
    {
        if (_loadingDetail) return;
        var acc = SelectedTraeAccount();
        if (acc != null) DeviceIdHint = ValidateDeviceId(value, acc);
    }

    /// <summary>保活开关切换 → 立即持久化。</summary>
    partial void OnKeepAliveEnabledChanged(bool value)
    {
        if (_loadingDetail) return;
        try
        {
            var cfg = MainViewModel.AppConfig;
            var acc = SelectedTraeAccount();
            if (cfg == null || acc == null) return;
            acc.KeepAliveEnabled = value;
            // 刚开启时清掉上次的执行时间，让它在下一轮定时器（≤30 秒）里立即跑一次
            if (value) acc.LastKeepAliveAt = null;
            cfg.Save();
            RefreshKeepAliveState(acc);
            AccountHelpers.AppLog("keepalive", DisplayOf(acc), value ? "已开启账号保活" : "已关闭账号保活");
            PushStatus = value ? "已开启保活，稍后自动执行首次续期" : "已关闭保活";
        }
        catch { /* 忽略 */ }
    }

    /// <summary>保活间隔输入 → 合法则持久化，非法则只提示不写入。</summary>
    partial void OnKeepAliveIntervalTextChanged(string value)
    {
        if (_loadingDetail) return;
        try
        {
            var acc = SelectedTraeAccount();
            if (acc == null) return;
            if (!int.TryParse((value ?? "").Trim(), out var minutes))
            {
                KeepAliveHint = "请输入数字（分钟）";
                return;
            }
            var clamped = TraeCheckin.TraeAccount.ClampKeepAliveMinutes(minutes);
            KeepAliveHint = clamped != minutes
                ? $"已收敛到合法区间：{TraeCheckin.TraeAccount.MinKeepAliveMinutes}–{TraeCheckin.TraeAccount.MaxKeepAliveMinutes}"
                : $"范围 {TraeCheckin.TraeAccount.MinKeepAliveMinutes}–{TraeCheckin.TraeAccount.MaxKeepAliveMinutes} 分钟";
            acc.KeepAliveIntervalMinutes = clamped;
            MainViewModel.AppConfig?.Save();
        }
        catch { /* 忽略 */ }
    }

    private static string DisplayOf(TraeCheckin.TraeAccount acc)
        => string.IsNullOrEmpty(acc.Name) ? (acc.Id.Length > 6 ? acc.Id[..6] : acc.Id) : acc.Name!;


    /// <summary>
    /// 一键为全部账号更换全新设备号（#40）：多账号逐个换太麻烦，批量换发规避风控。
    /// 使用 AccountStore.RenewAllDeviceIds 保证每个新号与其它账号互不重复（避免共用触发 9074）。
    /// 换号后云端部署的 TRAE_DEVICE_ID* 需重新「云端部署」同步。
    /// </summary>
    [RelayCommand]
    private async Task RenewAllDeviceIds()
    {
        try
        {
            var cfg = MainViewModel.AppConfig;
            var owner = UiHost.MainWindow;
            if (cfg == null || cfg.Accounts.Count == 0)
            {
                PushStatus = cfg == null || cfg.Accounts.Count == 0 ? "暂无账号，请先添加/登录账号" : "主窗口未就绪";
                return;
            }

            var ask = new TraeTools.Views.PromptWindow(
                "一键更换全部设备码",
                $"将为全部 {cfg.Accounts.Count} 个账号重新生成设备码（互不重复）。\n\n" +
                "用于多账号被风控（-9074 参与用户太多）时批量规避。\n" +
                "更换后云端部署需重新「云端部署」同步新的 TRAE_DEVICE_ID*。\n\n是否继续？",
                "全部更换", "取消");
            bool ok = await OverlayDialog.ShowAsync<bool>(ask, owner);
            if (!ok)
            {
                PushStatus = "已取消";
                return;
            }

            var store = new TraeCheckin.AccountStore(cfg);
            int count = store.RenewAllDeviceIds();
            try { cfg.Save(); } catch { /* 保存失败提示仍展示 */ }

            // 联动 Token 面板显示当前选中账号的新设备号
            var sel = cfg.Accounts.FirstOrDefault(a => a.Id == SelectedAccount?.Id);
            if (sel != null) DeviceIdText = sel.DeviceId;

            PushStatus = $"已为全部 {count} 个账号更换新设备码（互不重复）✓";
            AccountHelpers.AppLog("account", "", $"一键换设备码：已更换全部 {count} 个账号");
            MainViewModel.NotifyActiveAccountChanged();
        }
        catch (Exception ex)
        {
            PushStatus = "更换失败：" + ex.Message;
        }
    }

    private void PopulateAccounts()
    {
        // 重载前必须清空，否则追加导致旧账号重复显示
        Accounts.Clear();
        try
        {
            var cfg = MainViewModel.AppConfig;
            if (cfg != null && cfg.Accounts.Count > 0)
            {
                var colors = new[] { "#3B82F6", "#10B981", "#F59E0B", "#8B5CF6", "#EC4899" };
                int idx = 0;
                foreach (var acc in cfg.Accounts)
                {
                    var name = string.IsNullOrEmpty(acc.Name)
                        ? $"账号@{(acc.AccountUid ?? acc.Id.Substring(0, 6))}"
                        : acc.Name;
                    Accounts.Add(new AccountInfo
                    {
                        Id = acc.Id,
                        Name = name,
                        Initial = name.Length > 0 ? name[0].ToString() : "?",
                        Color = colors[idx % colors.Length],
                        Status = acc.LastCheckinDate.HasValue && acc.LastCheckinDate.Value.Date == DateTime.Today ? "已签到" : "待签到",
                        StatusType = acc.LastCheckinDate.HasValue && acc.LastCheckinDate.Value.Date == DateTime.Today ? "ok" : "info",
                        MobileText = acc.MobileMasked ?? "",
                        BioText = acc.Bio ?? "",
                        IsStudent = acc.IsStudent,
                        CreatedAt = acc.TokenUpdatedAt?.ToString("yyyy-MM-dd HH:mm") ?? "—",
                        IsCurrent = acc.Id == cfg.ActiveAccountId
                    });
                    idx++;
                }
                LoadAvatars();
                return;
            }
        }
        catch { /* 加载失败保持空列表 */ }

        // 无真实账号时不展示示例账号（示例无法被删除，会造成「账号不存在」误导）
    }

    /// <summary>开关变动立即持久化（写注册表），避免不开「保存设置」直接关窗导致状态丢失。#25</summary>
    partial void OnAutoStartEnabledChanged(bool value)
    {
        try { AutoStartManager.SetEnabled(value); } catch { /* 写注册表失败不阻断 */ }
    }

    [RelayCommand]
    private void SaveSettings()
    {
        try
        {
            // 保存开机自启
            AutoStartManager.SetEnabled(AutoStartEnabled);

            // 保存到 AppConfig
            var cfg = MainViewModel.AppConfig;
            if (cfg != null)
            {
                ApplyToConfig(cfg);
                cfg.Save();
                AccountHelpers.AppLog("account", "",
                    $"设置已保存：自动签到={AutoCheckinEnabled}，时间={AutoCheckinTime}，间隔={cfg.CheckinIntervalSeconds}秒，托盘={MinimizeToTray}，" +
                    $"推送={DescribeEnabledChannels(cfg)}");
            }

            PushStatus = "已保存 ✓";
        }
        catch
        {
            PushStatus = "保存失败";
        }
    }

    /// <summary>把设置页的全部字段写回配置对象（不落盘，由调用方决定何时 Save）。</summary>
    private void ApplyToConfig(AppConfig cfg)
    {
        cfg.AutoCheckinEnabled = AutoCheckinEnabled;
        cfg.AutoCheckinTime = AutoCheckinTime;
        cfg.CheckinIntervalSeconds = Math.Clamp(CheckinIntervalSeconds, 1, 60);
        cfg.FeishuWebhook = FeishuWebhook;
        cfg.MinimizeToTray = MinimizeToTray;

        cfg.NotifyOnCheckin = NotifyOnCheckin;
        cfg.NotifySystemEnabled = NotifySystemEnabled;

        cfg.EmailEnabled = EmailEnabled;
        cfg.SmtpHost = SmtpHost.Trim();
        cfg.SmtpPort = SmtpPort <= 0 ? 465 : SmtpPort;
        cfg.SmtpSecurity = SmtpSecurityIndex switch
        {
            1 => "starttls",
            2 => "none",
            _ => "ssl",
        };
        cfg.SmtpUser = SmtpUser.Trim();
        cfg.SmtpPassword = SmtpPassword;
        cfg.SmtpFrom = SmtpFrom.Trim();
        cfg.SmtpTo = SmtpTo.Trim();

        cfg.WebhookEnabled = WebhookEnabled;
        cfg.WebhookUrl = WebhookUrl.Trim();
        cfg.WebhookBodyTemplate = WebhookBodyTemplate;
        cfg.WebhookContentType = WebhookContentType.Trim();

        cfg.DingTalkEnabled = DingTalkEnabled;
        cfg.DingTalkWebhook = DingTalkWebhook.Trim();
        cfg.DingTalkSecret = DingTalkSecret.Trim();

        cfg.WeComEnabled = WeComEnabled;
        cfg.WeComWebhook = WeComWebhook.Trim();

        cfg.TelegramEnabled = TelegramEnabled;
        cfg.TelegramBotToken = TelegramBotToken.Trim();
        cfg.TelegramChatId = TelegramChatId.Trim();
    }

    /// <summary>从配置对象读回全部推送相关字段。</summary>
    private void LoadNotifySettings(AppConfig cfg)
    {
        NotifyOnCheckin = cfg.NotifyOnCheckin;
        NotifySystemEnabled = cfg.NotifySystemEnabled;

        EmailEnabled = cfg.EmailEnabled;
        SmtpHost = cfg.SmtpHost ?? "";
        SmtpPort = cfg.SmtpPort > 0 ? cfg.SmtpPort : 465;
        SmtpSecurityIndex = (cfg.SmtpSecurity ?? "ssl").ToLowerInvariant() switch
        {
            "starttls" => 1,
            "none" => 2,
            _ => 0,
        };
        SmtpUser = cfg.SmtpUser ?? "";
        SmtpPassword = cfg.SmtpPassword ?? "";
        SmtpFrom = cfg.SmtpFrom ?? "";
        SmtpTo = cfg.SmtpTo ?? "";

        WebhookEnabled = cfg.WebhookEnabled;
        WebhookUrl = cfg.WebhookUrl ?? "";
        if (!string.IsNullOrWhiteSpace(cfg.WebhookBodyTemplate)) WebhookBodyTemplate = cfg.WebhookBodyTemplate;
        if (!string.IsNullOrWhiteSpace(cfg.WebhookContentType)) WebhookContentType = cfg.WebhookContentType;

        DingTalkEnabled = cfg.DingTalkEnabled;
        DingTalkWebhook = cfg.DingTalkWebhook ?? "";
        DingTalkSecret = cfg.DingTalkSecret ?? "";

        WeComEnabled = cfg.WeComEnabled;
        WeComWebhook = cfg.WeComWebhook ?? "";

        TelegramEnabled = cfg.TelegramEnabled;
        TelegramBotToken = cfg.TelegramBotToken ?? "";
        TelegramChatId = cfg.TelegramChatId ?? "";
    }

    /// <summary>把当前启用的渠道拼成一句用于日志/状态条的描述。</summary>
    private string DescribeEnabledChannels(AppConfig cfg)
    {
        var list = new System.Collections.Generic.List<string>();
        if (!string.IsNullOrWhiteSpace(cfg.FeishuWebhook)) list.Add("飞书");
        if (cfg.EmailEnabled) list.Add("邮箱");
        if (cfg.WebhookEnabled) list.Add("Webhook");
        if (cfg.DingTalkEnabled) list.Add("钉钉");
        if (cfg.WeComEnabled) list.Add("企业微信");
        if (cfg.TelegramEnabled) list.Add("Telegram");
        if (cfg.NotifySystemEnabled) list.Add("系统通知");
        return list.Count == 0 ? "无" : string.Join("、", list);
    }

    /// <summary>
    /// 测试推送：先把界面上的当前填写值写进配置（否则测的是上次保存的旧值，容易误判），
    /// 再向所有已启用渠道发送一条测试消息，并逐渠道汇总结果。
    /// </summary>
    [RelayCommand]
    private async Task TestNotifyAll()
    {
        var cfg = MainViewModel.AppConfig;
        if (cfg == null)
        {
            PushStatus = "配置未就绪";
            return;
        }
        try
        {
            ApplyToConfig(cfg);
            var enabled = DescribeEnabledChannels(cfg);
            if (enabled == "无")
            {
                PushStatus = "尚未启用任何推送渠道";
                return;
            }

            PushStatus = $"正在测试 {enabled}…";
            var outcomes = await TraeTools.Services.Notify.NotifyDispatcher.SendAsync(
                cfg, "TraeTools 推送测试",
                $"这是一条测试消息，用于确认推送渠道配置正确。\n发送时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            PushStatus = TraeTools.Services.Notify.NotifyDispatcher.Summarize(outcomes);
        }
        catch (Exception ex)
        {
            PushStatus = "测试异常：" + ex.Message;
        }
    }

    /// <summary>测试邮件：只测邮箱渠道，便于在配置 SMTP 时快速验证（不必等其它渠道）。</summary>
    [RelayCommand]
    private async Task TestEmail()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(SmtpHost))
            {
                PushStatus = "请先填写 SMTP 服务器地址";
                return;
            }
            if (string.IsNullOrWhiteSpace(SmtpTo))
            {
                PushStatus = "请先填写收件人";
                return;
            }
            PushStatus = "正在发送测试邮件…";
            var r = await TraeTools.Services.Notify.EmailNotifier.SendAsync(
                SmtpHost.Trim(), SmtpPort <= 0 ? 465 : SmtpPort,
                SmtpSecurityIndex switch { 1 => "starttls", 2 => "none", _ => "ssl" },
                SmtpUser.Trim(), SmtpPassword, SmtpFrom.Trim(), SmtpTo.Trim(),
                "TraeTools 邮件推送测试",
                $"这是一条测试邮件。\n发送时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            PushStatus = r.Ok ? "测试邮件已发送 ✓" : r.Message;
        }
        catch (Exception ex)
        {
            PushStatus = "邮件测试异常：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task CopyToken()
    {
        try
        {
            var cfg = MainViewModel.AppConfig;
            var acc = cfg?.Accounts.FirstOrDefault(a => a.Id == SelectedAccount?.Id);
            var token = acc?.Token;
            if (string.IsNullOrEmpty(token))
            {
                PushStatus = "当前账号尚未登录，无 Token 可复制";
                return;
            }
            var cb = UiHost.Clipboard;
            if (cb != null)
            {
                await cb.SetTextAsync(token);
                PushStatus = "Token 已复制到剪贴板";
            }
            else
            {
                PushStatus = "无法访问剪贴板";
            }
        }
        catch (Exception ex)
        {
            PushStatus = "复制失败：" + ex.Message;
        }
    }

    /// <summary>
    /// 添加/登录账号：打开登录窗口（四种方式：内嵌浏览器自动登录 / 剪贴板识别 /
    /// 读取本机客户端 / 手动粘贴），成功后写入配置并更新列表。
    /// </summary>
    [RelayCommand]
    private async Task AddAccount()
    {
        try
        {
            var cfg = MainViewModel.AppConfig;
            var owner = UiHost.MainWindow;
            if (cfg == null)
            {
                PushStatus = "无法打开登录窗口（配置或主窗口未就绪）";
                return;
            }

            // 新建一个空账号作为登录目标（Token 校验通过后回填）
            var prevActiveId = cfg.ActiveAccountId;
            var acc = new TraeCheckin.TraeAccount();
            cfg.Accounts.Add(acc);
            cfg.ActiveAccountId = acc.Id;
            cfg.Save();

            var dlg = new Views.LoginWindow(acc);
            bool ok = await OverlayDialog.ShowAsync<bool>(dlg, owner);
            if (ok)
            {
                // 同一手机号判重（JWT data.id 跨会话恒定）：重复则拒绝保存本次登录
                try
                {
                    var uid = TraeCheckin.TokenUtils.ParseAccountUid(acc.Token);
                    var store = new TraeCheckin.AccountStore(cfg);
                    var dup = store.FindAccountWithUid(uid, acc.Id);
                    if (dup != null)
                    {
                        cfg.Accounts.Remove(acc);
                        cfg.Save();
                        AccountHelpers.CheckinLog(acc.Id[..6], $"添加账号取消：同一手机号已存在（{dup.Name}）");
                        PushStatus = "该账号已存在（" + dup.Name + "），取消重复添加";
                        PopulateAccounts();
                        return;
                    }
                }
                catch { /* 判重失败不阻断 */ }

                // 新账号登录成功：补发 16 位数字设备号（接口风控要求，避免签到报"参数错误"）
                if (string.IsNullOrWhiteSpace(acc.DeviceId))
                    acc.DeviceId = Random.Shared.NextInt64(1_000_000_000_000_000L, 10_000_000_000_000_000L).ToString();
                cfg.Save();
                var accName = string.IsNullOrEmpty(acc.Name) ? acc.Id[..6] : acc.Name!;
                AccountHelpers.CheckinLog(accName, $"添加账号成功，DeviceId={acc.DeviceId}，Token 长度={acc.Token?.Length ?? 0}");
                // 立即拉取账号资料（昵称/手机尾号/学生认证），让列表马上展示出真名（force：跳过当日缓存）
                await AccountHelpers.RefreshProfileAsync(acc, force: true);
                PushStatus = "账号登录成功 ✓，Token 已保存";
                PopulateAccounts();
                // 让列表选中新账号：Token 面板与「当前」标记立即对齐（会顺带把新账号设为激活）
                SelectedAccount = Accounts.FirstOrDefault(a => a.Id == acc.Id) ?? SelectedAccount;
                // 新账号加入后让仪表盘/顶栏切换器等全局页立即呈现新账号
                MainViewModel.NotifyActiveAccountChanged();
            }
            else
            {
                // 取消：移除刚创建的空账号，并恢复切换前的激活账号
                cfg.Accounts.Remove(acc);
                if (string.IsNullOrEmpty(prevActiveId) || cfg.Accounts.All(a => a.Id != prevActiveId))
                    cfg.ActiveAccountId = cfg.Accounts.FirstOrDefault()?.Id ?? "";
                else
                    cfg.ActiveAccountId = prevActiveId;
                cfg.Save();
                AccountHelpers.CheckinLog("?", "添加账号已取消");
                PushStatus = "已取消添加账号";
            }
        }
        catch (Exception ex)
        {
            PushStatus = "添加账号失败：" + ex.Message;
        }
    }

    /// <summary>删除当前选中账号（AccountStore.Remove 自动复位激活账号，不影响其它账号）。</summary>
    [RelayCommand]
    private void DeleteAccount()
    {
        try
        {
            var cfg = MainViewModel.AppConfig;
            if (cfg == null || SelectedAccount == null)
            {
                PushStatus = "请先在上方列表选中要删除的账号";
                return;
            }
            var store = new TraeCheckin.AccountStore(cfg);
            if (!store.Remove(SelectedAccount.Id))
            {
                PushStatus = "删除失败：账号不存在";
                return;
            }
            AccountHelpers.CheckinLog(SelectedAccount.Name ?? SelectedAccount.Id[..6], "账号已删除");
            cfg.Save();
            PushStatus = "账号已删除";
            PopulateAccounts();
            if (Accounts.Count > 0)
            {
                var active = cfg.Accounts.FirstOrDefault(a => a.Id == cfg.ActiveAccountId) ?? cfg.Accounts.First();
                SelectedAccount = Accounts.FirstOrDefault(a => a.Id == active.Id);
            }
            else
            {
                SelectedAccount = null;
                TokenString = "（未登录或未添加账号）";
                TokenUpdateTime = "—";
            }
            // 账号增删影响全局账号与仪表盘/用量展示，触发统一刷新（不再通过选中切换）
            MainViewModel.NotifyActiveAccountChanged();
        }
        catch (Exception ex)
        {
            PushStatus = "删除账号失败：" + ex.Message;
        }
    }
    // ==================== 账号备份（导出 / 导入） ====================

    /// <summary>
    /// 导出全部账号为加密备份。
    /// 优先弹系统文件保存框；平台不支持文件选择器时退化为「复制到剪贴板」，
    /// 保证任何平台都能把备份内容取走。
    /// </summary>
    [RelayCommand]
    private async Task ExportAccounts()
    {
        if (BackupBusy) return;
        var cfg = MainViewModel.AppConfig;
        if (cfg == null)
        {
            PushStatus = "配置未就绪";
            return;
        }
        if (BackupPassphrase.Length < 6)
        {
            PushStatus = "请先填写备份口令（至少 6 位），它用于加密备份文件";
            return;
        }

        BackupBusy = true;
        try
        {
            var r = TraeTools.Services.Login.AccountBackup.Export(cfg.Accounts, BackupPassphrase);
            if (!r.Ok || r.Text == null)
            {
                PushStatus = r.Message;
                return;
            }
            BackupText = r.Text;

            // 尝试走系统文件保存框
            try
            {
                var sp = UiHost.Root?.StorageProvider;
                if (sp is { CanSave: true })
                {
                    var file = await sp.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
                    {
                        Title = "保存账号备份",
                        SuggestedFileName = TraeTools.Services.Login.AccountBackup.SuggestedFileName(),
                        DefaultExtension = "json",
                        FileTypeChoices = new[]
                        {
                            new Avalonia.Platform.Storage.FilePickerFileType("TraeTools 账号备份")
                            {
                                Patterns = new[] { "*.json" },
                            },
                        },
                    });
                    if (file != null)
                    {
                        await using var stream = await file.OpenWriteAsync();
                        stream.SetLength(0);
                        await using var writer = new System.IO.StreamWriter(stream, System.Text.Encoding.UTF8);
                        await writer.WriteAsync(r.Text);
                        PushStatus = $"备份已保存：{file.Name}（{r.Imported} 个账号）";
                        AccountHelpers.AppLog("account", "", $"导出账号备份：{r.Imported} 个账号 → {file.Name}");
                        return;
                    }
                    PushStatus = "已取消保存";
                    return;
                }
            }
            catch (Exception ex)
            {
                AccountHelpers.AppLog("account", "", "文件保存框不可用，退化为剪贴板：" + ex.Message);
            }

            // 退化方案：复制到剪贴板
            var clip = UiHost.Clipboard;
            if (clip != null)
            {
                await clip.SetTextAsync(r.Text);
                PushStatus = $"文件保存不可用，备份内容（{r.Imported} 个账号）已复制到剪贴板，请自行粘贴保存到 .json 文件";
            }
            else
            {
                PushStatus = "备份已生成但无法保存：文件选择器与剪贴板均不可用";
            }
        }
        catch (Exception ex)
        {
            PushStatus = "导出失败：" + ex.Message;
        }
        finally
        {
            BackupBusy = false;
        }
    }

    /// <summary>选择备份文件并把内容读进文本框（随后由「导入」按钮统一处理）。</summary>
    [RelayCommand]
    private async Task PickBackupFile()
    {
        try
        {
            var sp = UiHost.Root?.StorageProvider;
            if (sp is not { CanOpen: true })
            {
                PushStatus = "当前平台不支持文件选择器，请把备份内容直接粘贴到下方文本框";
                return;
            }
            var files = await sp.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "选择账号备份文件",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("TraeTools 账号备份")
                    {
                        Patterns = new[] { "*.json", "*.txt" },
                    },
                },
            });
            if (files.Count == 0)
            {
                PushStatus = "已取消选择";
                return;
            }
            var file = files[0];
            await using var stream = await file.OpenReadAsync();
            using var reader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8);
            BackupText = await reader.ReadToEndAsync();
            PushStatus = $"已读取备份文件：{file.Name}（请填写口令后点「导入账号」）";
        }
        catch (Exception ex)
        {
            PushStatus = "读取备份文件失败：" + ex.Message;
        }
    }

    /// <summary>用文本框中的备份内容 + 口令执行导入（合并去重，不覆盖本地已有账号）。</summary>
    [RelayCommand]
    private async Task ImportAccounts()
    {
        if (BackupBusy) return;
        var cfg = MainViewModel.AppConfig;
        if (cfg == null)
        {
            PushStatus = "配置未就绪";
            return;
        }
        if (string.IsNullOrWhiteSpace(BackupText))
        {
            PushStatus = "请先选择备份文件，或把备份内容粘贴到文本框";
            return;
        }
        if (string.IsNullOrEmpty(BackupPassphrase))
        {
            PushStatus = "请输入导出该备份时使用的口令";
            return;
        }

        // 覆盖式导入会移除本地账号，属于高风险操作，先确认
        var ask = new PromptWindow(
            "导入账号备份",
            $"将把备份中的账号合并到本机（同账号刷新凭证，新账号追加），不会删除现有账号。\n\n" +
            $"当前本机已有 {cfg.Accounts.Count} 个账号。\n\n是否继续？",
            "导入", "取消");
        bool ok = await OverlayDialog.ShowAsync<bool>(ask, UiHost.MainWindow);
        if (!ok)
        {
            PushStatus = "已取消导入";
            return;
        }

        BackupBusy = true;
        try
        {
            var (readOk, message, accounts) = TraeTools.Services.Login.AccountBackup.Read(BackupText, BackupPassphrase);
            if (!readOk || accounts.Count == 0)
            {
                PushStatus = message;
                return;
            }

            var merged = TraeTools.Services.Login.AccountBackup.Merge(cfg, accounts);
            if (!merged.Ok)
            {
                PushStatus = merged.Message;
                return;
            }

            cfg.Save();
            PopulateAccounts();
            PushStatus = merged.Message;

            // 顺手刷新账号资料与状态，并让首页/切换器同步看到新账号
            await RefreshAccountStatesAsync();
            MainViewModel.NotifyActiveAccountChanged();

            AccountHelpers.AppLog("account", "", $"导入账号备份：{merged.Message}");
        }
        catch (Exception ex)
        {
            PushStatus = "导入失败：" + ex.Message;
        }
        finally
        {
            BackupBusy = false;
        }
    }

    [RelayCommand]
    private void OpenRepo()
    {
        try
        {
            TraeTools.Platform.BrowserLauncher.Open(RepoUrl);
        }
        catch { /* 忽略 */ }
    }

    [RelayCommand]
    private void OpenIssues()
    {
        try
        {
            TraeTools.Platform.BrowserLauncher.Open(IssuesUrl);
        }
        catch { /* 忽略 */ }
    }

    /// <summary>
    /// 重置「用户协议与免责声明」的同意状态：清除本程序与 TraeSwitch 的接受标记后退出，
    /// 下次启动会重新弹出协议要求阅读。调用方（UI 按钮）负责二次确认。
    /// </summary>
    [RelayCommand]
    private async Task ResetAgreement()
    {
        var owner = UiHost.MainWindow;
        var ask = new TraeTools.Views.PromptWindow(
            "重置用户协议",
            "将清除「用户协议与免责声明」的同意记录并退出软件。\n下次启动时会要求你重新阅读并同意。\n\n确定重置并退出？",
            "重置并退出", "取消");
        bool ok = await OverlayDialog.ShowAsync<bool>(ask, owner);
        if (!ok)
        {
            PushStatus = "已取消重置";
            return;
        }

        // 清除本程序的 EULA 标记（MainWindow.OnOpenedEulaCheck 读同路径）
        try
        {
            var flag = Path.Combine(
                TraeTools.Services.DataPaths.Root, "eula_accepted");
            if (File.Exists(flag)) File.Delete(flag);
        }
        catch { /* 删除失败不阻塞退出 */ }

        // 一并重置 TraeSwitch 侧的协议标记
        try
        {
            var s = MainViewModel.SwitchSettings;
            if (s != null)
            {
                s.Data.EulaAccepted = false;
                s.Save();
            }
        }
        catch { /* 忽略 */ }

        try { MainViewModel.AppConfig?.Save(); } catch { /* 忽略 */ }
        PushStatus = "已重置用户协议，即将退出…";
        await Task.Delay(300);
        Environment.Exit(0);
    }

    /// <summary>
    /// 重置软件：清空所有账号、签到历史、日志、数据库、配置等用户数据，恢复到初始安装状态。
    /// 需要两次确认以防止误操作。
    /// </summary>
    [RelayCommand]
    private async Task ResetSoftware()
    {
        var owner = UiHost.MainWindow;

        // ---- 第一次确认：告知影响范围 ----
        var ask1 = new PromptWindow(
            "重置软件",
            "此操作将永久删除以下所有数据：\n\n" +
            "  · 全部账号及登录凭证（Token / Session）\n" +
            "  · 签到历史记录与数据库\n" +
            "  · 用量统计记录\n" +
            "  · 所有调试日志\n" +
            "  · 账号切换配置与登录态备份\n" +
            "  · 所有个性化设置\n\n" +
            "重置后软件将退出，重新启动后回到初始状态。\n\n确定要继续吗？",
            "继续", "取消");
        bool go1 = await OverlayDialog.ShowAsync<bool>(ask1, owner);
        if (!go1) { PushStatus = "已取消重置"; return; }

        // ---- 第二次确认：最终警告 ----
        var ask2 = new PromptWindow(
            "最终确认",
            "最后警告：此操作不可撤销！\n\n所有账号数据、签到记录、设置将被永久删除。\n\n确认执行重置？",
            "确认重置", "取消");
        bool go2 = await OverlayDialog.ShowAsync<bool>(ask2, owner);
        if (!go2) { PushStatus = "已取消重置"; return; }

        // ---- 执行清理 ----
        PushStatus = "正在重置…";
        try
        {
            // 关闭数据库连接
            try { MainViewModel.CheckinDb?.Dispose(); } catch { /* 忽略 */ }

            var root = DataPaths.Root;

            // 删除整个数据根目录
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }

            // 删除 LocalAppData 下的缓存目录
            var localRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TraeTools");
            if (Directory.Exists(localRoot))
            {
                try { Directory.Delete(localRoot, recursive: true); } catch { /* 忽略 */ }
            }

            PushStatus = "重置完成，即将退出…";
            await Task.Delay(500);
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            PushStatus = "重置失败：" + ex.Message;
        }
    }

    /// <summary>
    /// 为源仓库点 star：已授权 GitHub 时调用 GitHub API 点赞（akexin/TraeTools-Android）；
    /// 未授权或仓库 owner 本人时打开仓库主页由用户手动点赞。
    /// </summary>
    [RelayCommand]
    private async Task StarSourceRepo()
    {
        try
        {
            var cfg = MainViewModel.AppConfig;
            var gh = MainViewModel.GitHubApi;
            if (cfg == null || gh == null)
            {
                // 服务未初始化：直接打开仓库主页
                TraeTools.Platform.BrowserLauncher.Open(RepoUrl);
                return;
            }

            if (string.IsNullOrEmpty(cfg.GitHubToken))
            {
                PushStatus = "尚未授权 GitHub，已打开仓库主页，请手动点赞";
                TraeTools.Platform.BrowserLauncher.Open(RepoUrl);
                return;
            }

            string login = cfg.GitHubLogin ?? "";
            if (string.IsNullOrEmpty(login))
            {
                login = await gh.GetLoginAsync(cfg.GitHubToken) ?? "";
                if (!string.IsNullOrEmpty(login)) { cfg.GitHubLogin = login; cfg.Save(); }
            }

            if (GitHubApiClient.ShouldSkipStar(login))
            {
                // 仓库 owner 本人无需自赞，打开主页浏览
                PushStatus = "您是该仓库的维护者，无需为自己点赞";
                TraeTools.Platform.BrowserLauncher.Open(RepoUrl);
                return;
            }

            PushStatus = "正在为源仓库点赞…";
            bool ok = await gh.StarSourceRepoAsync(cfg.GitHubToken, login);
            PushStatus = ok ? "已为源仓库点赞 ★ 感谢支持！" : "点赞失败（可能已点过），请到网页确认";
        }
        catch (Exception ex)
        {
            PushStatus = "点赞失败：" + ex.Message;
        }
    }

    /// <summary>
    /// 检查更新并安装：查询最新版 → 有新版询问 → 下载 → 后台替换并退出。
    /// 结果写入底部状态栏（PushStatus）。
    /// </summary>
    [RelayCommand]
    private async Task CheckUpdate()
    {
        // 联网检查/下载/安装进行中禁止再次触发，避免并发下载相同新版本
        if (UpdateChecking)
        {
            PushStatus = "正在获取或安装更新，请稍候；完成后可再次检查。";
            return;
        }
        UpdateChecking = true;
        try
        {
            PushStatus = "正在检查更新…";
            UpdaterService.ReleaseInfo? rel = null;
            try { rel = await UpdaterService.GetNewerAsync(useCache: false); }
            catch (Exception ex) { PushStatus = "检查更新失败：" + ex.Message; return; }

            if (rel == null)
            {
                PushStatus = $"已是最新版本（{AppVersion}）";
                return;
            }

            PushStatus = $"发现新版本 {rel.Tag}";
            var owner = UiHost.MainWindow;
#if ANDROID
            // Android：不自动下载/安装（应用内无法自行替换 APK，且不做强制更新）。
            // 仅提示并引导到 Release 页面，用户自主决定是否下载安装。
            var ask = new PromptWindow("发现新版本",
                $"发现新版本 {rel.Tag}\n（当前 {AppVersion}，约 {Math.Max(1, (long)(rel.Size / 1048576.0))} MB）\n\n是否前往 GitHub 下载页获取新版 APK？（不会自动安装，下载后手动安装即可）",
                "前往下载", "以后再说");
            bool gotoDownload = await OverlayDialog.ShowAsync<bool>(ask, owner);
            if (!gotoDownload)
            {
                PushStatus = "已取消更新（当前版本可继续使用）";
                return;
            }
            TraeTools.Platform.BrowserLauncher.Open(rel.HtmlUrl);
            PushStatus = "已打开下载页：下载 APK 后手动安装即可";
#else
            var ask = new PromptWindow("发现新版本",
                $"发现新版本 {rel.Tag}\n（当前 {AppVersion}，约 {Math.Max(1, (long)(rel.Size / 1048576.0))} MB）\n\n是否下载并自动安装？",
                "下载并安装", "取消");
            bool ok = await OverlayDialog.ShowAsync<bool>(ask, owner);
            if (!ok)
            {
                PushStatus = "已取消更新";
                return;
            }

            var currentExe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(currentExe))
            {
                PushStatus = "无法定位当前程序路径，更新中止";
                return;
            }
            var destDir = Path.GetDirectoryName(currentExe)!;

            PushStatus = "开始下载新版…（请稍候）";
            try
            {
                var p = new Progress<int>(pct =>
                {
                    if (pct / 10 != _lastUpdatePct) { _lastUpdatePct = pct / 10; PushStatus = $"下载 {pct}%…"; }
                });
                var update = await Task.Run(() => UpdaterService.DownloadAsync(rel.ExeUrl, destDir, p));
                PushStatus = "下载完成：即将退出并自动替换安装。";
                UpdaterService.ApplyInBackground(update, currentExe);
                // 必须彻底退出进程以释放目标 exe 文件锁，否则后台脚本 copy 永远失败
                await Task.Delay(400);
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                PushStatus = "更新失败：" + ex.Message;
            }
#endif // !ANDROID
        }
        catch (Exception ex)
        {
            PushStatus = "更新异常：" + ex.Message;
        }
        finally
        {
            UpdateChecking = false;
        }
    }
}










