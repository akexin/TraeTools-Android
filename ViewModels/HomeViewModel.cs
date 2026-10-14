using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TraeTools.Models;

namespace TraeTools.ViewModels;

/// <summary>
/// 极简单页视图模型（TraeTools 2.0 重构）。
///
/// 设计取舍：本应用只保留「本地手动签到」与「云端托管签到」两条核心链路，
/// 不再承载用量统计、账号切换与复杂设置。因此本 VM 不做业务实现，只做
/// **聚合**——直接复用既有的 <see cref="CheckinViewModel"/>（本地签到引擎）
/// 与 <see cref="CloudViewModel"/>（GitHub Actions 托管引擎），再补一层
/// 面向单页布局的展示态（状态徽章、多账号开关、最近 3 条记录）。
///
/// 复用同一实例（而非各自 new）是有意为之：定时自动签到
/// （MainViewModel.AutoCheckinIfDue → CheckinViewModel.TryAutoCheckinAsync）
/// 与页面按钮必须作用在同一份状态上，否则自动签到完成后界面不会刷新。
/// </summary>
public partial class HomeViewModel : ViewModelBase
{
    /// <summary>本地签到引擎（原「每日签到」页 VM），保留其全部签到/登录能力。</summary>
    public CheckinViewModel Local { get; }

    /// <summary>云端托管引擎（原「云端签到」页 VM），保留设备码授权/PAT/部署能力。</summary>
    public CloudViewModel Cloud { get; }

    /// <summary>最近 3 条签到记录（极简日志：只做追溯，不做统计）。</summary>
    public ObservableCollection<CheckinRecord> RecentLogs { get; } = new();

    /// <summary>顶部状态徽章文案：云端运行中 / 待部署 / 云端未部署。</summary>
    [ObservableProperty]
    private string _cloudBadgeText = "云端未部署";

    /// <summary>云端是否已部署完成（绿色徽章 + 呼吸灯动效）。</summary>
    [ObservableProperty]
    private bool _isCloudRunning;

    /// <summary>已授权但尚未部署（黄色徽章）。</summary>
    [ObservableProperty]
    private bool _isCloudWarning;

    /// <summary>是否已授权 GitHub（决定展示「授权引导」还是「部署流程清单」）。</summary>
    [ObservableProperty]
    private bool _isCloudAuthorized;

    /// <summary>云端引擎是否忙碌（镜像 Cloud.IsBusy，避免视图层写取反路径）。</summary>
    [ObservableProperty]
    private bool _isCloudBusy;

    /// <summary>是否展示账号勾选行：单账号用户不需要看到它。</summary>
    [ObservableProperty]
    private bool _showAccountPicker;

    /// <summary>顶部状态徽章展开的云端详情。</summary>
    [ObservableProperty]
    private bool _showCloudDetail;

    /// <summary>日志区空状态。</summary>
    public bool HasNoRecentLogs => RecentLogs.Count == 0;

    public HomeViewModel(CheckinViewModel local, CloudViewModel cloud)
    {
        Local = local;
        Cloud = cloud;

        Local.PropertyChanged += OnLocalChanged;
        Local.CheckAccounts.CollectionChanged += (_, _) => UpdateAccountPicker();
        Cloud.PropertyChanged += OnCloudChanged;

        Local.Reload();
        RefreshLogs();
        UpdateAccountPicker();
        RefreshCloudBadge();
    }

    /// <summary>进入本页时调用：同步账号勾选、记录与云端徽章。</summary>
    public void Refresh()
    {
        try { Local.Reload(); } catch { /* 保留原值 */ }
        RefreshLogs();
        UpdateAccountPicker();
        RefreshCloudBadge();
    }

    /// <summary>重建「最近 3 条」日志。签到完成后由 MainViewModel.NotifyAsyncRefresh 触发。</summary>
    public void RefreshLogs()
    {
        // 绑定到界面的 ObservableCollection 只能在 UI 线程改动（托盘快签可能来自后台线程）
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RefreshLogs);
            return;
        }

        RecentLogs.Clear();
        foreach (var r in Local.Records.Take(3))
            RecentLogs.Add(r);
        OnPropertyChanged(nameof(HasNoRecentLogs));
    }

    /// <summary>同步云端状态徽章（绿=运行中 / 黄=待部署 / 红=未授权）。</summary>
    public void RefreshCloudBadge()
    {
        var cfg = MainViewModel.AppConfig;
        bool hasToken = cfg != null && !string.IsNullOrEmpty(cfg.GitHubToken);
        bool deployed = Cloud.StateText.Contains("已部署完成");

        IsCloudAuthorized = hasToken;
        IsCloudRunning = deployed;
        IsCloudWarning = hasToken && !deployed;
        CloudBadgeText = deployed ? "云端运行中" : (hasToken ? "待部署" : "云端未部署");
    }

    private void UpdateAccountPicker()
        => ShowAccountPicker = Local.CheckAccounts.Count > 1;

    private void OnLocalChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 本地签到完成：IsCheckingIn 由 true 落回 false，或出现结果文案 → 刷新日志
        if (e.PropertyName == nameof(CheckinViewModel.IsCheckingIn) && !Local.IsCheckingIn)
        {
            RefreshLogs();
            RefreshCloudBadge();
        }
        else if (e.PropertyName == nameof(CheckinViewModel.StatusMessage))
        {
            RefreshLogs();
        }
    }

    private void OnCloudChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CloudViewModel.IsBusy))
            IsCloudBusy = Cloud.IsBusy;
        else if (e.PropertyName is nameof(CloudViewModel.StateText) or nameof(CloudViewModel.GitHubUser))
            RefreshCloudBadge();
    }

    /// <summary>点击顶部状态徽章：展开/收起云端详情。</summary>
    [RelayCommand]
    private void ToggleCloudDetail() => ShowCloudDetail = !ShowCloudDetail;
}
