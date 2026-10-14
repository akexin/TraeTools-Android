using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using TraeCheckin;
using TraeTools.Platform;
using TraeTools.Services.Login;
using TraeTools.ViewModels;

namespace TraeTools.Views;

/// <summary>
/// 登录窗口，共提供四种获取登录态的方式：
///   1. 内嵌浏览器自动登录（仅 Windows 桌面端，登录后自动读取凭证）；
///   2. 从剪贴板识别导入（自动判定是 JWT 还是会话 Cookie）；
///   3. 读取本机 TRAE 客户端（仅 Windows 桌面端，免手动复制）；
///   4. 手动粘贴（全平台，Android 端的主要方式；可选指定粘贴内容的类型）。
///
/// 所有方式最终都落到同一个出口：把凭证写入 <see cref="TraeAccount"/> 并返回成功，
/// 由调用方（设置页 AddAccount）负责判重、补设备号与持久化。
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
        // Android 无内嵌浏览器，也无桌面客户端数据可读：隐藏前两种方式，手动粘贴成为主要路径
        AutoLoginButton.IsVisible = false;
        AutoLoginHint.IsVisible = false;
        LocalImportButton.IsVisible = false;
        ManualHint.Text = "手机端无内嵌浏览器：请点「打开浏览器登录」，登录 trae.cn 后复制 Cloud-IDE-Token 或会话 Cookie 粘贴保存。";
#endif
    }

    public LoginWindow(TraeAccount? account)
        : this()
    {
        _account = account;
    }

    // ==================== 方式一：内嵌浏览器自动登录 ====================

    // 注意：XAML 中该按钮绑定了 Click="OnAutoLoginClick"，事件处理器在编译期解析，
    // 因此 Android 端也必须存在同名方法（按钮本身已隐藏，仅作为兜底提示）。
    private async void OnAutoLoginClick(object? sender, RoutedEventArgs e)
    {
#if ANDROID
        AutoStatus.Text = "Android 端不支持内嵌浏览器自动登录，请使用「从剪贴板识别」或手动粘贴。";
        await Task.CompletedTask;
        return;
#else
        if (_busy || _account == null) return;

        // 未安装 WebView2 运行库时内嵌登录不可用，明确提示改用其它方式
        try
        {
            _ = Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch
        {
            AutoStatus.Text = "未检测到 WebView2 运行库，请使用「从剪贴板识别」或手动粘贴";
            return;
        }

        _busy = true;
        AutoStatus.Text = "正在启动浏览器…";
        try
        {
            string webViewDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TraeTools", "WebView", "account_" + (_account.Id.Length > 12 ? _account.Id[..12] : _account.Id));

            // 接口探索已完成；false=正常登录（自动识别并关闭窗口）
            var result = await TraeTools.Services.Login.WebViewLoginHost.RunAsync(webViewDir, _account.Token, debugCapture: false);

            if (result == null)
            {
                AutoStatus.Text = "未获取到 Token（已取消或登录未完成）";
                return;
            }

            ApplyCredentials(result.Token, result.Session, "自动登录成功");
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

    // ==================== 方式二：从剪贴板识别导入 ====================

    private async void OnClipboardImportClick(object? sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        ImportStatus.Text = "正在读取剪贴板…";
        try
        {
            var clip = UiHost.Clipboard;
            if (clip == null)
            {
                ImportStatus.Text = "无法访问剪贴板";
                return;
            }
            // 注意：Avalonia 12 把读取剪贴板的入口改名为 TryGetTextAsync（旧名 GetTextAsync 已移除），
            // 写入仍是 SetTextAsync；两者都是 Avalonia.Input.Platform 下的扩展方法，需 using 该命名空间。
            var text = await clip.TryGetTextAsync();
            if (string.IsNullOrWhiteSpace(text))
            {
                ImportStatus.Text = "剪贴板为空，请先复制凭证";
                return;
            }
            TokenBox.Text = text;
            await ImportAsync(text, CredentialKind.Unknown, ImportStatus);
        }
        catch (Exception ex)
        {
            ImportStatus.Text = "读取剪贴板失败：" + ex.Message;
        }
        finally
        {
            _busy = false;
        }
    }

    // ==================== 方式三：读取本机 TRAE 客户端 ====================

    private async void OnLocalImportClick(object? sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        ImportStatus.Text = "正在读取本机 TRAE 客户端数据…";
        try
        {
            // 异步：内部可能用会话换 Token（同步阻塞在 UI 线程上会死锁）
            var r = await LocalClientImporter.TryImportAsync();
            if (!r.Ok || string.IsNullOrEmpty(r.Token))
            {
                ImportStatus.Text = r.Message;
                return;
            }
            ApplyCredentials(r.Token, r.Session, r.Message);
        }
        catch (Exception ex)
        {
            ImportStatus.Text = "读取本机客户端失败：" + ex.Message;
        }
        finally
        {
            _busy = false;
        }
    }

    // ==================== 方式四：手动粘贴保存 ====================

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (_account == null)
        {
            StatusText.Text = "无当前账号，无法保存";
            return;
        }

        var pasted = TokenBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(pasted))
        {
            StatusText.Text = "请先粘贴 Cloud-IDE-Token 或会话 Cookie";
            return;
        }

        if (_busy) return;
        _busy = true;
        StatusText.Text = "正在校验…";
        try
        {
            var forced = MethodCombo.SelectedIndex switch
            {
                1 => CredentialKind.Token,
                2 => CredentialKind.Session,
                _ => CredentialKind.Unknown,
            };

            var r = await CredentialImporter.ImportAsync(pasted, MainViewModel.CheckinApi, forced);
            if (!r.Ok || string.IsNullOrEmpty(r.Token))
            {
                StatusText.Text = r.Message;
                return;
            }

            ApplyCredentials(r.Token, r.Session, r.Message);
            StatusText.Text = r.Message;
        }
        catch (Exception ex)
        {
            StatusText.Text = "保存失败：" + ex.Message;
        }
        finally
        {
            _busy = false;
        }
    }

    // ==================== 公共 ====================

    /// <summary>
    /// 把解析得到的凭证写入当前账号并关闭窗口。
    /// 这里只做「长度是否足够」的可用性校验：重复导入同一凭证是合法操作
    /// （例如用同一会话重新换一次 Token），不应像 WebView 抓取路径那样按「必须与旧值不同」拒绝。
    /// </summary>
    private void ApplyCredentials(string? token, string? session, string statusMessage)
    {
        if (_account == null)
        {
            StatusText.Text = "无当前账号，无法保存";
            return;
        }
        if (!CredentialImporter.IsUsableToken(token))
        {
            StatusText.Text = "凭证长度不足，请确认复制完整";
            return;
        }

        _account.Token = token;
        if (!string.IsNullOrEmpty(session)) _account.Session = session;
        _account.TokenUpdatedAt = DateTime.Now;

        var uid = TokenUtils.ParseAccountUid(_account.Token);
        if (!string.IsNullOrEmpty(uid)) _account.AccountUid = uid;

        MainViewModel.AppConfig?.Save();

        StatusText.Text = statusMessage;
        AutoStatus.Text = statusMessage;
        ImportStatus.Text = statusMessage;

        OverlayDialog.CloseDialog(this, true);
    }

    /// <summary>剪贴板 / 会话导入的统一入口（供方式二与方式三复用业务逻辑）。</summary>
    private async Task ImportAsync(string raw, CredentialKind forced, TextBlock statusTarget)
    {
        var r = await CredentialImporter.ImportAsync(raw, MainViewModel.CheckinApi, forced);
        if (!r.Ok || string.IsNullOrEmpty(r.Token))
        {
            statusTarget.Text = r.Message;
            return;
        }
        ApplyCredentials(r.Token, r.Session, r.Message);
        statusTarget.Text = r.Message;
    }

    private void OnOpenBrowserClick(object? sender, RoutedEventArgs e)
    {
        BrowserLauncher.Open("https://www.trae.cn");
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => OverlayDialog.CloseDialog(this, false);
}
