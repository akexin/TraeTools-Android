using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TraeCheckin;
using TraeSwitch.Services;

namespace TraeTools.ViewModels;

/// <summary>
/// 应用主视图模型（TraeTools 2.0 极简重构）。
///
/// 导航结构由「侧栏 + 底部 4-Tab 六页」收敛为**单页仪表盘 + 二级设置**：
/// <list type="bullet">
/// <item>主页面 <see cref="HomeViewModel"/>：本地手动签到 + 云端托管签到 + 最近 3 条记录。</item>
/// <item>二级页 <see cref="SettingsViewModel"/>：由顶栏齿轮进入（低频，仅保留基础设定与账号入口）。</item>
/// </list>
/// 用量统计（UsageViewModel/TraeUsageClient/UsageStore）与账号切换
/// （SwitchViewModel）已从产品中移除；仪表盘聚合页（DashboardViewModel）
/// 因职责被单页吸收而一并移除。
///
/// 本类同时充当**全局服务容器**（静态字段），供各页面 VM 解耦访问，
/// 这一约定在重构中保留，以免波及 CheckinViewModel / CloudViewModel / SettingsViewModel。
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    private readonly CheckinViewModel _checkin;
    private readonly SettingsViewModel _settings;
    private readonly LogViewModel _logs;

    /// <summary>单页仪表盘（本地签到 + 云端托管 + 最近记录）。</summary>
    public HomeViewModel Home { get; }

    /// <summary>日志页（最近日志 / 全部日志）。</summary>
    public LogViewModel Logs => _logs;

    [ObservableProperty]
    private ViewModelBase? _currentPage;

    [ObservableProperty]
    private string _currentPageKey = "home";

    // ==================== 顶栏账号切换器 ====================

    /// <summary>可切换的账号列表（轻量项，不含头像与在线状态）。</summary>
    public System.Collections.ObjectModel.ObservableCollection<Models.AccountSwitchItem> SwitchAccounts { get; } = new();

    /// <summary>切换器是否展开。</summary>
    [ObservableProperty]
    private bool _isAccountSwitcherOpen;

    /// <summary>顶栏显示的当前账号名。</summary>
    [ObservableProperty]
    private string _currentAccountName = "未登录";

    /// <summary>顶栏账号头像占位字符（当前账号名首字）。</summary>
    [ObservableProperty]
    private string _currentAccountInitial = "?";

    /// <summary>账号数大于 1 时才有切换的必要。</summary>
    [ObservableProperty]
    private bool _canSwitchAccount;

    /// <summary>点击顶栏账号区：展开/收起账号列表。</summary>
    [RelayCommand]
    private void ToggleAccountSwitcher()
    {
        // 一个账号都没有：没有可看的列表，直接去设置页添加
        if (AppConfig is null || AppConfig.Accounts.Count == 0)
        {
            Navigate("settings");
            return;
        }
        // ⚠️ 只有 1 个账号时也要展开列表（列表底部带「添加账号 / 管理账号」入口）。
        // 旧实现在这里直接跳设置页，用户点「账号旁边的下三角」时感觉是「被强行带去了登录页」，
        // 与下拉三角的语义不符。
        RefreshSwitchAccounts();
        IsAccountSwitcherOpen = !IsAccountSwitcherOpen;
    }

    /// <summary>账号切换列表底部的「管理账号」入口：收起列表并进入设置页的账号管理区。</summary>
    [RelayCommand]
    private void ManageAccounts()
    {
        IsAccountSwitcherOpen = false;
        Navigate("settings");
    }

    /// <summary>把某个账号设为当前激活账号：签到引擎、仪表盘、云端状态与设置页同步刷新。</summary>
    [RelayCommand]
    private void SwitchAccount(string? id)
    {
        if (string.IsNullOrEmpty(id)) return;
        var cfg = AppConfig;
        if (cfg == null) return;
        if (!cfg.Accounts.Any(a => a.Id == id))
        {
            IsAccountSwitcherOpen = false;
            return;
        }

        try
        {
            if (cfg.ActiveAccountId != id)
            {
                cfg.ActiveAccountId = id;
                cfg.Save();
                var name = DisplayNameOf(cfg.Accounts.First(a => a.Id == id));
                AccountHelpers.AppLog("account", name, "切换当前账号");
            }
        }
        catch { /* 保存失败也继续刷新界面 */ }

        IsAccountSwitcherOpen = false;
        RefreshSwitchAccounts();
        // 复用既有通知链路：重载签到引擎 + 刷新单页与设置页账号状态
        NotifyActiveAccountChanged();
    }

    /// <summary>重建顶栏切换器列表与当前账号文案。</summary>
    public void RefreshSwitchAccounts()
    {
        try
        {
            var cfg = AppConfig;
            SwitchAccounts.Clear();
            if (cfg == null || cfg.Accounts.Count == 0)
            {
                CurrentAccountName = "未登录";
                CurrentAccountInitial = "?";
                CanSwitchAccount = false;
                return;
            }

            var active = cfg.Accounts.FirstOrDefault(a => a.Id == cfg.ActiveAccountId) ?? cfg.Accounts[0];
            CurrentAccountName = DisplayNameOf(active);
            CurrentAccountInitial = CurrentAccountName.Length > 0 ? CurrentAccountName[0].ToString() : "?";
            CanSwitchAccount = cfg.Accounts.Count > 1;

            foreach (var a in cfg.Accounts)
            {
                var display = DisplayNameOf(a);
                var accId = a.Id;
                SwitchAccounts.Add(new Models.AccountSwitchItem
                {
                    Id = accId,
                    Display = display,
                    // 副标题优先显示用户自填的账号简介；没写简介时退回脱敏手机号
                    Subtitle = !string.IsNullOrWhiteSpace(a.Bio)
                        ? a.Bio!.Trim()
                        : (string.IsNullOrEmpty(a.MobileMasked) ? "" : a.MobileMasked!),
                    Initial = display.Length > 0 ? display[0].ToString() : "?",
                    IsCurrent = a.Id == active.Id,
                    CheckedInToday = a.LastCheckinDate.HasValue && a.LastCheckinDate.Value.Date == DateTime.Today,
                    // 每个列表项携带自己的切换命令（闭包捕获账号 Id），避免模板里跨层级绑定父 VM
                    SelectCommand = new RelayCommand(() => SwitchAccount(accId)),
                });
            }
        }
        catch { /* 列表构建失败保留原值 */ }
    }

    /// <summary>账号展示名：备注名 → 平台昵称 → 脱敏手机号 → 短 Id。</summary>
    private static string DisplayNameOf(TraeCheckin.TraeAccount a)
    {
        if (!string.IsNullOrWhiteSpace(a.Name)) return a.Name!;
        if (!string.IsNullOrWhiteSpace(a.ScreenName)) return a.ScreenName!;
        if (!string.IsNullOrWhiteSpace(a.MobileMasked)) return a.MobileMasked!;
        return a.Id.Length > 6 ? a.Id[..6] : a.Id;
    }

    /// <summary>
    /// 紧凑模式（手机屏适配）：宿主宽度 &lt; 640 逻辑像素时由 MainView 按实际 Bounds 切换。
    /// 重构后不再有侧栏/底部标签栏，紧凑模式仅用于收紧顶栏与内容区边距。
    /// </summary>
    [ObservableProperty]
    private bool _isCompact;

    // 全局服务容器：页面 VM 通过静态属性访问
    public static AppConfig? AppConfig;
    public static TraeApiClient? CheckinApi;
    public static SettingsStore? SwitchSettings;
    public static GitHubApiClient? GitHubApi;
    public static CheckinDatabase? CheckinDb;

    /// <summary>界面展示的应用版本号，与"关于/更新检查"（TraeTools.Services.AppVersion）同源。</summary>
    public string AppVersion => Services.AppVersion.Display;

    public MainViewModel()
    {
        // 初始化真实服务（try-catch，失败则为 null，页面 VM 用 mock 兜底）
        try
        {
            AppConfig = AppConfig.Load();
        }
        catch
        {
            AppConfig = new AppConfig();
        }

        try
        {
            CheckinApi = new TraeApiClient();
        }
        catch
        {
            CheckinApi = null;
        }

        try
        {
            SwitchSettings = new SettingsStore(CarrierDefaults.SettingsDir);
        }
        catch
        {
            SwitchSettings = null;
        }

        try
        {
            GitHubApi = new GitHubApiClient();
        }
        catch
        {
            GitHubApi = null;
        }

        try
        {
            CheckinDb = new CheckinDatabase();
            // 首次启动时从旧版文本文件迁移数据到数据库
            // DataPaths.Migrate() 已在 Program.cs 中先行调用，文件已搬入新目录
            // 这里扫描新目录（DataDir）即可；同时兼容扫描旧目录以防迁移未执行
            int migrated = 0;
            var newDataDir = TraeTools.Services.DataPaths.DataDir;
            var oldBaseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TraeCheckin");
            var oldDataDir = Path.Combine(oldBaseDir, "data");
            foreach (var dir in new[] { newDataDir, oldBaseDir, oldDataDir })
            {
                if (Directory.Exists(dir))
                    migrated += CheckinDb.MigrateFromHistoryFiles(dir)
                              + CheckinDb.MigrateFromSnapshotFiles(dir);
            }
            if (migrated > 0)
                AccountHelpers.AppLog("account", "", $"从旧版文本文件迁移了 {migrated} 条记录到数据库");
        }
        catch
        {
            CheckinDb = null;
        }

        Instance = this;

        // 把今天已落盘的日志回填进内存缓冲：进程重启后「最近日志」不至于是一片空白
        try { TraeTools.Services.AppLog.SeedFromTodayFiles(); } catch { /* 回填失败不影响功能 */ }

        // 引擎实例只创建一份：定时自动签到与页面按钮必须共享同一状态
        _checkin = new CheckinViewModel();
        var cloud = new CloudViewModel();
        _settings = new SettingsViewModel();
        _logs = new LogViewModel();

        Home = new HomeViewModel(_checkin, cloud);

        _settings.NavigateRequested += p => Navigate(p);

        RefreshSwitchAccounts();

        CurrentPage = Home;
        CurrentPageKey = "home";

        // 启动日志：记录账号概况，便于排查问题
        try
        {
            if (AppConfig != null)
            {
                var accs = AppConfig.Accounts;
                AccountHelpers.AppLog("account", "", $"===== 程序启动，共 {accs.Count} 个账号 =====");
                foreach (var a in accs)
                {
                    var n = string.IsNullOrEmpty(a.Name) ? (a.Id.Length > 6 ? a.Id[..6] : a.Id) : a.Name!;
                    AccountHelpers.AppLog("account", n, $"DeviceId={a.DeviceId}，Token={(string.IsNullOrEmpty(a.Token) ? "无" : $"有({a.Token!.Length})")}，Enabled={a.Enabled}，LastCheckin={a.LastCheckinDate?.ToString("MM-dd HH:mm") ?? "无"}");
                }
            }
        }
        catch { /* 启动日志失败不影响 */ }
    }

    public bool IsHomeActive => CurrentPageKey == "home";
    public bool IsSettingsActive => CurrentPageKey == "settings";
    public bool IsLogsActive => CurrentPageKey == "logs";

    /// <summary>是否处于任一二级页（设置 / 日志）：决定顶栏显示「返回」还是各入口图标。</summary>
    public bool IsSubPageActive => IsSettingsActive || IsLogsActive;

    partial void OnCurrentPageKeyChanged(string value)
    {
        OnPropertyChanged(nameof(IsHomeActive));
        OnPropertyChanged(nameof(IsSettingsActive));
        OnPropertyChanged(nameof(IsLogsActive));
        OnPropertyChanged(nameof(IsSubPageActive));
        // 切换器在离开首页时收起，避免残留展开态
        if (value != "home") IsAccountSwitcherOpen = false;
    }

    /// <summary>账号管理切换激活账号后：签到引擎重载 + 单页日志与状态同步。</summary>
    public static async void NotifyActiveAccountChanged()
    {
        if (Instance is not { } vm) return;
        try
        {
            vm._checkin.Reload();
            vm.Home.Refresh();
            vm.RefreshSwitchAccounts();
            await vm._settings.RefreshAccountStatesAsync();
        }
        catch { /* 异步刷新失败不应导致崩溃 */ }
    }

    /// <summary>签到完成后同步单页日志与账号状态。</summary>
    public static async Task NotifyAsyncRefresh()
    {
        if (Instance is not { } vm) return;
        try
        {
            vm.Home.RefreshLogs();
            await vm._settings.RefreshAccountStatesAsync();
        }
        catch { /* 异步刷新失败不应导致崩溃 */ }
    }

    /// <summary>当前 VM 实例（供静态通知使用）。</summary>
    public static MainViewModel? Instance { get; private set; }

    /// <summary>由 MainView 定时器定时调用：到点自动签到（TraeCheckin「每日自动签到」能力）。</summary>
    public Task<bool> AutoCheckinIfDue() => _checkin.TryAutoCheckinAsync();

    /// <summary>
    /// 由 MainView 定时器定时调用：对到期的账号执行保活（静默换新 Token + 刷新积分数据）。
    /// 返回本次成功保活的账号数（0 表示无需保活或全部失败）。
    /// </summary>
    public async Task<int> KeepAliveIfDue()
    {
        try
        {
            int n = await TraeTools.Services.KeepAlive.KeepAliveService.RunDueAsync();
            if (n > 0)
            {
                // 保活顺带刷新了积分，界面跟着更新
                Home.Refresh();
                RefreshSwitchAccounts();
                await _settings.RefreshAccountStatesAsync();
            }
            return n;
        }
        catch { return 0; }
    }

    /// <summary>
    /// 原为「客户端退出后静默刷新账号 vault 备份」，随账号切换能力一并移除；
    /// 保留空实现以维持 MainView 定时器的调用契约（桌面端 #if !ANDROID 分支）。
    /// </summary>
    public Task RefreshSnapshotIfNeeded() => Task.CompletedTask;

    /// <summary>托盘「立即签到」：对当前账号执行一次签到（不弹窗，由调用方提示结果）。</summary>
    public async Task<bool> QuickCheckinAsync()
    {
        try
        {
            var cfg = AppConfig;
            var api = CheckinApi;
            if (cfg == null || api == null) return false;
            var acc = cfg.Accounts.FirstOrDefault(a => a.Id == cfg.ActiveAccountId)
                      ?? cfg.Accounts.FirstOrDefault();
            if (acc == null || string.IsNullOrEmpty(acc.Token)) return false;
            var name = string.IsNullOrEmpty(acc.Name) ? (acc.Id.Length > 6 ? acc.Id[..6] : acc.Id) : acc.Name!;
            AccountHelpers.CheckinLog(name, $"[托盘快签] 开始签到，DeviceId={acc.DeviceId}");
            var result = await api.ClaimAsync(acc.Token, acc.DeviceId);
            if (result == null || result.code != 0)
            {
                AccountHelpers.CheckinLog(name, $"[托盘快签] Claim 失败：code={result?.code ?? -1}, message={result?.message ?? "null"}");
                return false;
            }
            acc.LastCheckinDate = DateTime.Now;
            cfg.LastCheckinDate = DateTime.Now;
            double gained = 0;
            try
            {
                // 解析本次所得并写入签到历史（与签到页/自动签到同口径）
                var after = await api.GetStatusAsync(acc.Token, acc.DeviceId);
                gained = TraeCheckin.CheckinEvaluator.ResolveGainedCredits(after ?? result, acc.IsMember);
                AccountHelpers.AppendHistory(acc, gained);
                AccountHelpers.CheckinLog(name, $"[托盘快签] 签到成功，获得 {gained} 积分");
            }
            catch { /* 历史写入失败不影响 */ }
            try
            {
                var credits = await api.GetRemainingCreditsAsync(acc.Token, acc.DeviceId);
                if (credits >= 0)
                {
                    acc.RemainingCredits = credits;   // 按账号持久化
                    cfg.LastRemaining = credits;
                }
            }
            catch { /* 积分刷新失败不影响签到结果 */ }
            try { cfg.Save(); } catch { /* 忽略 */ }
            Home.RefreshLogs();
            // 托盘快签同样走统一推送管道（受设置页各渠道开关与总开关约束）
            try
            {
                await TraeTools.Services.Notify.NotifyDispatcher.SendCheckinBatchAsync(
                    cfg, new List<(string Name, bool Ok, double Gained, string Reason)>
                    {
                        (name, true, gained, ""),
                    });
            }
            catch { /* 推送失败不影响签到结果 */ }
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 导航：三个目的地——首页（单页仪表盘）、设置（二级页）、日志（二级页）。
    /// 其余页面（仪表盘/用量/签到/切换/云端）已从产品移除。
    /// </summary>
    [RelayCommand]
    private void Navigate(string page)
    {
        switch (page)
        {
            case "settings":
                _ = _settings.RefreshAccountStatesAsync();
                CurrentPage = _settings;
                CurrentPageKey = "settings";
                break;
            case "logs":
                _logs.Subscribe();
                _logs.ReloadRecent();
                _logs.RefreshFiles();
                CurrentPage = _logs;
                CurrentPageKey = "logs";
                break;
            default:
                Home.Refresh();     // 每次回到首页都按当前账号真实验证刷新
                RefreshSwitchAccounts();
                CurrentPage = Home;
                CurrentPageKey = "home";
                break;
        }
    }
}
