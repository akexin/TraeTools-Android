using System;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using TraeTools.ViewModels;

namespace TraeTools;

/// <summary>
/// Given a view model, returns the corresponding view if possible.
/// </summary>
[RequiresUnreferencedCode(
    "Default implementation of ViewLocator involves reflection which may be trimmed away.",
    Url = "https://docs.avaloniaui.net/docs/concepts/view-locator")]
public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        var name = param.GetType().FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
        try
        {
            var type = Type.GetType(name);
            if (type != null)
                return (Control)Activator.CreateInstance(type)!;
        }
        catch (Exception ex)
        {
            // 反射构建失败一律降级为占位文本：
            // AOT/裁剪环境下 Activator.CreateInstance 可能抛 MissingRuntimeArtifactException，
            // 若在此处冒泡会直接带崩 UI 线程（Android 上表现为闪退）。
            TraeTools.Platform.CrashLog.Write("ViewLocator.Build(" + name + ")", ex);
            return new TextBlock { Text = "加载失败: " + name };
        }

        return new TextBlock { Text = "Not Found: " + name };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
