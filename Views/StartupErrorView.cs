using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace TraeTools.Views;

/// <summary>
/// 启动失败 / 上次崩溃后的错误页。
/// 全部由代码构建（不依赖 XAML 解析），确保 Avalonia 已可用的最坏情况下也能显示，
/// 用户可以直接看到异常堆栈并复制出来反馈。
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
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.DarkRed,
        });

        stack.Children.Add(new TextBlock
        {
            Text = "详情如下，可截图或复制后反馈给开发者。",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.Gray,
        });

        var body = new TextBox
        {
            Text = detail,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            MaxHeight = 380,
        };

        var scroll = new ScrollViewer { Content = body, MaxHeight = 400 };
        stack.Children.Add(scroll);

        if (!string.IsNullOrWhiteSpace(logPath))
        {
            stack.Children.Add(new TextBlock
            {
                Text = "日志文件：" + logPath,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.Gray,
            });
        }

        var buttonBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };

        var copyBtn = new Button { Content = "复制错误信息", Padding = new Thickness(14, 8) };
        copyBtn.Click += async (_, _) =>
        {
            try
            {
                var top = TopLevel.GetTopLevel(this);
                if (top?.Clipboard != null)
                    await top.Clipboard.SetTextAsync(detail);
            }
            catch
            {
                // 剪贴板不可用时静默失败（不应再触发崩溃）
            }
        };
        buttonBar.Children.Add(copyBtn);

        if (onRetry != null)
        {
            var retryBtn = new Button { Content = "清除并重新启动界面", Padding = new Thickness(14, 8) };
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
