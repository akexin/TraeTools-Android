using System;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Platform;
using Avalonia.Threading;
using TraeTools.ViewModels;

namespace TraeTools.Views;

public partial class MainWindow : Window
{
    /// <summary>自动签到定时器（双平台）：每 30 秒检查一次，到点且当天未签时自动签到。</summary>
    private readonly DispatcherTimer _autoCheckinTimer;
#if !ANDROID
    private TrayIcon? _trayIcon;
    /// <summary>true=用户从托盘「退出」真正退出；false=关闭窗口仅最小化到托盘。 </summary>
    private bool _realExit;
    /// <summary>计时器重入守卫：上一轮任务（可能数十秒）未结束前跳过本轮 Tick（#33）。</summary>
    private bool _tickBusy;
    private readonly WindowNotificationManager _notify;
#endif

    public MainWindow()
    {
#if !ANDROID
        _notify = new WindowNotificationManager(this) { Position = NotificationPosition.BottomRight, MaxItems = 3 };
#endif
        InitializeComponent();

#if ANDROID
        // Android：挂接覆盖层对话框宿主（单视图平台不支持 Window.ShowDialog）
        OverlayDialog.Attach(OverlayRoot, OverlayContent);
        TraeTools.Platform.CrashLog.Boot("MainWindow: overlay attached");
#endif

#if !ANDROID
        // 桌面端固定窗口尺寸与居中（Android 端为全屏单视图，Width/Height 不适用）
        Width = 1100;
        Height = 720;
        MinWidth = 940;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        // 任务栏窗口图标：复用托盘同款 avalonia-logo.ico（缺省时任务栏不显示图标）
        try
        {
            using var iconStream = AssetLoader.Open(new Uri("avares://TraeTools/Assets/avalonia-logo.ico"));
            Icon = new WindowIcon(iconStream);
        }
        catch { /* 图标加载失败不影响主程序 */ }
#endif

        _autoCheckinTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        // 防重入：上一轮未结束则跳过本轮，避免长任务在常驻托盘场景下叠加（#33）
        _autoCheckinTimer.Tick += async (_, _) =>
        {
#if !ANDROID
            if (_tickBusy) return;
            _tickBusy = true;
#endif
            try
            {
                if (DataContext is MainViewModel vm)
                {
                    await vm.AutoCheckinIfDue();
#if !ANDROID
                    await vm.RefreshSnapshotIfNeeded();
#endif
                }
            }
            catch (Exception ex)
            {
                // 定时任务异常不允许带崩 UI（Android 上 async void 未捕获异常会直接闪退）
                TraeTools.Platform.CrashLog.Write("AutoCheckin timer tick", ex);
            }
            finally
            {
#if !ANDROID
                _tickBusy = false;
#endif
            }
        };

#if !ANDROID
        BuildTray();
        Closing += OnWindowClosing;
#endif
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        TraeTools.Platform.CrashLog.Boot("MainWindow.OnOpened");
        OnOpenedEulaCheck();
        // Android 端自动签到仅 App 运行期间有效；无人值守场景请使用「云端签到」
        _autoCheckinTimer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _autoCheckinTimer.Stop();
#if !ANDROID
        _trayIcon?.Dispose();
#endif
        base.OnClosed(e);
    }

#if !ANDROID
    /// <summary>关闭窗口默认最小化到托盘（与 TraeCheckin 一致）；仅托盘「退出」真正退出。</summary>
    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_realExit) return;
        e.Cancel = true;
        Hide();
        _notify.Show(new Notification("TraeTools", "已最小化到托盘，后台继续自动签到", NotificationType.Information, TimeSpan.FromSeconds(2)));
    }

    private void BuildTray()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://TraeTools/Assets/avalonia-logo.ico"));
            var menu = new NativeMenu();
            var show = new NativeMenuItem("显示主窗口");
            show.Click += (_, _) => ShowAndActivate();
            var checkin = new NativeMenuItem("立即签到");
            checkin.Click += async (_, _) => await TrayCheckinAsync();
            var exit = new NativeMenuItem("退出");
            exit.Click += (_, _) =>
            {
                _realExit = true;
                Close();
            };
            menu.Items.Add(show);
            menu.Items.Add(checkin);
            menu.Items.Add(new NativeMenuItemSeparator());
            menu.Items.Add(exit);

            _trayIcon = new TrayIcon
            {
                Icon = new WindowIcon(stream),
                ToolTipText = "TraeTools 签到 + 账号切换",
                Menu = menu,
                IsVisible = true
            };
            // 左键单击托盘图标 → 显示/激活主窗口
            _trayIcon.Clicked += (_, _) => ShowAndActivate();
            App.TrayRef = _trayIcon;
        }
        catch
        {
            // 托盘初始化失败（如系统不支持）不影响主程序
        }
    }

    private void ShowAndActivate()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private async Task TrayCheckinAsync()
    {
        if (DataContext is MainViewModel vm)
        {
            bool ok = await vm.QuickCheckinAsync();
            _notify.Show(new Notification(
                ok ? "签到成功" : "签到失败",
                ok ? "今日积分已到账（会员含连签加成）" : "请检查网络或 Token 是否有效",
                ok ? NotificationType.Success : NotificationType.Error));
        }
    }
#endif

    /// <summary>
    /// 首次启动弹出免责声明协议（EULA）。同意标记独立存放于数据根目录 eula_accepted，
    /// 与 TraeCheckin 的已同意配置互相独立，保证本程序首次启动必定展示协议；
    /// 不同意则直接退出。
    /// </summary>
    private async void OnOpenedEulaCheck()
    {
        var flag = System.IO.Path.Combine(
            TraeTools.Services.DataPaths.Root, "eula_accepted");
        if (System.IO.File.Exists(flag)) return;

        TraeTools.Platform.CrashLog.Boot("EULA: showing agreement");
        var eula = new EulaWindow();
        bool agreed;
        try
        {
            // Android：单视图平台走覆盖层；桌面：原生 ShowDialog
            agreed = await OverlayDialog.ShowAsync<bool>(eula, this);
        }
        catch (Exception ex)
        {
            // 兜底：Android 上对话框呈现失败则记录并视为同意，保证应用可用（日志留痕）
            TraeTools.Platform.CrashLog.Write("EULA dialog failed", ex);
            agreed = true;
        }
        TraeTools.Platform.CrashLog.Boot($"EULA: agreed={agreed}");
        if (agreed)
        {
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(flag)!);
                System.IO.File.WriteAllText(flag, DateTime.UtcNow.ToString("o"));
            }
            catch { /* 标记写失败下次仍会弹，可接受 */ }
        }
        else
        {
#if !ANDROID
            _realExit = true;
#endif
            Close();
        }
    }
}
