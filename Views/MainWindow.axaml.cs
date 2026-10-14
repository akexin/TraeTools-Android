using System;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Platform;
using TraeTools.ViewModels;

namespace TraeTools.Views;

/// <summary>
/// Windows 桌面端主窗口：只负责桌面专属行为（尺寸/图标/托盘/关闭最小化），
/// 界面内容与业务逻辑在 <see cref="MainView"/>（UserControl）中，供 Android 单视图复用。
/// ⚠️ Android 上不能实例化本类：Avalonia 单视图平台的 WindowingPlatformStub.CreateWindow()
/// 会抛 NotSupportedException，Android 端请用 MainView。
/// </summary>
public partial class MainWindow : Window
{
#if !ANDROID
    private TrayIcon? _trayIcon;
    /// <summary>true=用户从托盘「退出」真正退出；false=关闭窗口仅最小化到托盘。 </summary>
    private bool _realExit;
    private readonly WindowNotificationManager _notify;
#endif

    public MainWindow()
    {
#if !ANDROID
        _notify = new WindowNotificationManager(this) { Position = NotificationPosition.BottomRight, MaxItems = 3 };
        // 把通知能力交出去，供 Services/Notify/SystemNotifier 复用（它无法自行拿到窗口实例）
        App.DesktopNotifier = ShowDesktopNotification;
#endif
        InitializeComponent();

#if !ANDROID
        // 桌面端固定窗口尺寸与居中
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

        BuildTray();
        Closing += OnWindowClosing;
#endif
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        TraeTools.Platform.CrashLog.Boot("MainWindow.OnOpened");
    }

    protected override void OnClosed(EventArgs e)
    {
#if !ANDROID
        // 窗口已销毁，及时摘掉委托，避免 SystemNotifier 调到已失效的 NotificationManager
        App.DesktopNotifier = null;
        _trayIcon?.Dispose();
#endif
        base.OnClosed(e);
    }

#if !ANDROID
    /// <summary>桌面端通知：显示在主窗口右下角，数秒后自动消失。</summary>
    private void ShowDesktopNotification(string title, string body)
    {
        _notify.Show(new Notification(title, body, NotificationType.Information, TimeSpan.FromSeconds(6)));
    }

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

    private async System.Threading.Tasks.Task TrayCheckinAsync()
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
}
