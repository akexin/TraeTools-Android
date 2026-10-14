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

    /// <summary>单页仪表盘（本地签到 + 云端托管 + 最近记录）。</summary>
    public HomeViewModel Home { get; }

    [ObservableProperty]
    private ViewModelBase? _currentPage;

    [ObservableProperty]
    private string _currentPageKey = "home";

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

        // 引擎实例只创建一份：定时自动签到与页面按钮必须共享同一状态
        _checkin = new CheckinViewModel();
        var cloud = new CloudViewModel();
        _settings = new SettingsViewModel();

        Home = new HomeViewModel(_checkin, cloud);

        _settings.NavigateRequested += p => Navigate(p);

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

    partial void OnCurrentPageKeyChanged(string value)
    {
        OnPropertyChanged(nameof(IsHomeActive));
        OnPropertyChanged(nameof(IsSettingsActive));
    }

    /// <summary>账号管理切换激活账号后：签到引擎重载 + 单页日志与状态同步。</summary>
    public static async void NotifyActiveAccountChanged()
    {
        if (Instance is not { } vm) return;
        try
        {
            vm._checkin.Reload();
            vm.Home.Refresh();
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
            try
            {
                // 解析本次所得并写入签到历史（与签到页/自动签到同口径）
                var after = await api.GetStatusAsync(acc.Token, acc.DeviceId);
                double gained = TraeCheckin.CheckinEvaluator.ResolveGainedCredits(after ?? result, acc.IsMember);
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
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 导航：重构后仅有两个目的地——首页（单页仪表盘）与设置（二级页）。
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
            default:
                Home.Refresh();     // 每次回到首页都按当前账号真实验证刷新
                CurrentPage = Home;
                CurrentPageKey = "home";
                break;
        }
    }
}
