using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using TraeCheckin;
using TraeTools.Platform;
using TraeTools.ViewModels;

namespace TraeTools.Views;

/// <summary>
/// 登录窗口：
///   Windows 桌面端：内嵌 WebView2 自动登录（自动读取 token + session）+ 手动粘贴备选；
///   Android：无 WebView2，隐藏自动登录入口，仅保留「打开浏览器登录 + 粘贴 Token」手动方案。
/// </summary>
public partial class LoginWindow : UserControl
{
    /// <summary>桌面端承载窗口的标题（Android 单视图无窗口标题栏，不使用）。</summary>
    public string DialogTitle { get; set; } = "登录 TRAE 账号";

    private readonly TraeAccount? _account;
    private bool _busy;

    public LoginWindow()
    {
        InitializeComponent();
#if ANDROID
        // Android 无内嵌浏览器：隐藏自动登录区，手动方案升级为唯一方式
        AutoLoginButton.IsVisible = false;
        AutoLoginHint.IsVisible = false;
        ManualHint.Text = "手机端请点击下方「打开浏览器登录」，登录后按开发者教程复制 Cloud-IDE-Token 粘贴保存。";
#endif
    }

    public LoginWindow(TraeAccount? account)
        : this()
    {
        _account = account;
    }

    // 注意：XAML 中该按钮绑定了 Click="OnAutoLoginClick"，事件处理器在编译期解析，
    // 因此 Android 端也必须存在同名方法（按钮本身已隐藏，仅作为兜底提示）。
    private async void OnAutoLoginClick(object? sender, RoutedEventArgs e)
    {
#if ANDROID
        AutoStatus.Text = "Android 端不支持内嵌浏览器自动登录，请使用下方「打开浏览器登录 + 粘贴 Token」方案。";
        await Task.CompletedTask;
        return;
#else
        if (_busy || _account == null) return;
        if (string.IsNullOrEmpty(_account.Token))
        {
            // 首次登录：为账号准备独立的 WebView2 用户数据目录（账号间登录态隔离）
            _ = _account;
        }

        // 未安装 WebView2 运行库时内嵌登录不可用，明确提示改用下方手动粘贴方案
        try
        {
            _ = Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch
        {
            AutoStatus.Text = "未检测到 WebView2 运行库，请使用下方「手动方案」粘贴 Cloud-IDE-Token";
            return;
        }

        _busy = true;
        AutoStatus.Text = "正在启动浏览器…";
        try
        {
            string webViewDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TraeTools", "WebView", "account_" + (_account.Id.Length > 12 ? _account.Id[..12] : _account.Id));

            // 接口探索已完成；false=正常登录（自动识别并关闭窗口）。需要再抓包时改为 true
            var result = await TraeTools.Services.Login.WebViewLoginHost.RunAsync(webViewDir, _account.Token, debugCapture: false);

            if (result == null)
            {
                AutoStatus.Text = "未获取到 Token（已取消或登录未完成）";
                return;
            }

            _account.Token = result.Token;
            _account.Session = result.Session;
            _account.TokenUpdatedAt = DateTime.Now;
            var uid = TokenUtils.ParseAccountUid(result.Token);
            if (!string.IsNullOrEmpty(uid)) _account.AccountUid = uid;
            MainViewModel.AppConfig?.Save();
            OverlayDialog.CloseDialog(this, true);
        }
        catch (Exception ex)
        {
            AutoStatus.Text = "自动登录失败：" + ex.Message;
        }
        finally
        {
            _busy = false;
        }
#endif
    }

    private void OnOpenBrowserClick(object? sender, RoutedEventArgs e)
    {
        BrowserLauncher.Open("https://www.trae.cn");
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        var pasted = TokenBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(pasted))
        {
            StatusText.Text = "请先粘贴 Cloud-IDE-Token";
            return;
        }
        var initial = _account?.Token;
        if (!TokenUtils.ShouldAcceptNewToken(initial, pasted))
        {
            StatusText.Text = "Token 无效（长度不足或与当前账号相同），请重新复制";
            return;
        }
        if (_account == null)
        {
            StatusText.Text = "无当前账号，无法保存";
            return;
        }
        _account.Token = pasted;
        _account.TokenUpdatedAt = DateTime.Now;
        var uid = TokenUtils.ParseAccountUid(pasted);
        if (!string.IsNullOrEmpty(uid)) _account.AccountUid = uid;
        MainViewModel.AppConfig?.Save();
        OverlayDialog.CloseDialog(this, true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => OverlayDialog.CloseDialog(this, false);
}
