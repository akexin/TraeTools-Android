using CommunityToolkit.Mvvm.Input;

namespace TraeTools.ViewModels;

/// <summary>
/// 单页首页：本地签到 + 云端托管 + 签到日志。
/// 组合 CheckinViewModel（签到命令/状态/记录）与 CloudViewModel（云端授权/部署/状态徽章），
/// 自身只承担导航入口（右上角设置）。
/// </summary>
public partial class HomeViewModel : ViewModelBase
{
    public CheckinViewModel Checkin { get; }
    public CloudViewModel Cloud { get; }

    /// <summary>界面展示的应用版本号，与设置页「关于」同源。</summary>
    public string AppVersion => TraeTools.Services.AppVersion.Display;

    public HomeViewModel(CheckinViewModel checkin, CloudViewModel cloud)
    {
        Checkin = checkin;
        Cloud = cloud;
    }

    /// <summary>设计时用无参构造（XAML Design.DataContext）；运行时由 MainViewModel 注入共享实例。</summary>
    public HomeViewModel() : this(new CheckinViewModel(), new CloudViewModel()) { }

    /// <summary>右上角齿轮 → 设置页。</summary>
    [RelayCommand]
    private void OpenSettings() => RaiseNavigateRequested("settings");
}
