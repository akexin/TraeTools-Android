using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using TraeTools.ViewModels;
using TraeTools.Views;

namespace TraeTools;

public partial class App : Application
{
    /// <summary>系统托盘图标全局引用（TrayIcon 无源头引用会被 GC 回收，故存于此；仅 Windows 桌面端使用）。</summary>
    public static TrayIcon? TrayRef;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(),
            };
        }
#if ANDROID
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            // Android：单视图生命周期，MainWindow 直接作为主视图全屏展示
            singleView.MainView = new MainWindow
            {
                DataContext = new MainViewModel(),
            };
        }
#endif

        base.OnFrameworkInitializationCompleted();
    }
}
