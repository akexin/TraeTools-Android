using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace TraeTools.Views;

/// <summary>
/// 启动失败 / 上次崩溃后的错误页。
/// 全部由代码构建（不依赖 XAML 解析），确保 Avalonia 已可用的最坏情况下也能显示，
/// 用户可以直接看到异常堆栈并复制出来反馈。
/// 注意：.NET Android 隐式全局 using 了 Android.Widget，Button / Orientation 等
/// 与 Avalonia 同名类型会产生二义（CS0104），故此处一律写全限定名。
/// </summary>
public class StartupErrorView : UserControl
{
    public StartupErrorView(string title, string detail, string? logPath, Action? onRetry = null)
    {
        var stack = new StackPanel { Margin = new Thickness(20), Spacing = 10 };

        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Foreground = Brushes.DarkRed,
        });

        stack.Children.Add(new TextBlock
        {
            Text = "详情如下，可长按选择复制，或截图后反馈给开发者。",
            FontSize = 12,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Foreground = Brushes.Gray,
        });

        // 用只读 TextBox 而非 TextBlock：内容可直接长按选中复制，不依赖剪贴板 API
        var body = new TextBox
        {
            Text = detail,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontSize = 11,
            MaxHeight = 380,
        };
        stack.Children.Add(new ScrollViewer { Content = body, MaxHeight = 400 });

        if (!string.IsNullOrWhiteSpace(logPath))
        {
            stack.Children.Add(new TextBlock
            {
                Text = "日志文件：" + logPath,
                FontSize = 11,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Foreground = Brushes.Gray,
            });
        }

        var buttonBar = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 10
        };

        var copyBtn = new Avalonia.Controls.Button
        {
            Content = "复制错误信息",
            Padding = new Thickness(14, 8)
        };
        copyBtn.Click += (_, _) =>
        {
            // 反射调用剪贴板：不同 Avalonia 版本 IClipboard 的方法签名不一致，
            // 反射可保证编译期不失败、运行时可用即用。
            try
            {
                var top = TopLevel.GetTopLevel(this);
                var clipboard = top?.Clipboard;
                if (clipboard == null) return;
                var mi = clipboard.GetType().GetMethod("SetTextAsync", new[] { typeof(string) });
                var task = mi?.Invoke(clipboard, new object?[] { detail }) as System.Threading.Tasks.Task;
                _ = task;
            }
            catch
            {
                // 剪贴板不可用不应再触发崩溃
            }
        };
        buttonBar.Children.Add(copyBtn);

        if (onRetry != null)
        {
            var retryBtn = new Avalonia.Controls.Button
            {
                Content = "清除并重新加载界面",
                Padding = new Thickness(14, 8)
            };
            retryBtn.Click += (_, _) =>
            {
                try
                {
                    onRetry();
                }
                catch
                {
                }
            };
            buttonBar.Children.Add(retryBtn);
        }

        stack.Children.Add(buttonBar);

        Content = new ScrollViewer { Content = stack };
    }
}
