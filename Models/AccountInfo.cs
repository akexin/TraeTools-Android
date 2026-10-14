using System.Windows.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TraeTools.Models;

public partial class AccountInfo : ObservableObject
{
    /// <summary>账号概览卡片点击切换全局账号的命令（由 DashboardViewModel 注入）。</summary>
    public ICommand? SelectCommand { get; set; }
    /// <summary>源账号 Id（用于 Token 面板联动定位真实账号）。</summary>
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Initial { get; set; } = string.Empty;
    public string Color { get; set; } = "#3B82F6";
    [ObservableProperty]
    private string _status = string.Empty;
    [ObservableProperty]
    private string _statusType = "ok";
    /// <summary>脱敏手机号（账号管理卡片展示）。</summary>
    [ObservableProperty]
    private string _mobileText = "";

    /// <summary>手机号行是否显示（无数据时隐藏，账号切换页用，#14）。</summary>
    public bool ShowMobile => !string.IsNullOrEmpty(MobileText);

    partial void OnMobileTextChanged(string value) => OnPropertyChanged(nameof(ShowMobile));
    /// <summary>是否学生认证（账号管理卡片展示徽章）。</summary>
    [ObservableProperty]
    private bool _isStudent;
    private IImage? _avatarImage;
    /// <summary>已加载的头像（null=未加载或失败，显示首字母底色兜底）。</summary>
    public IImage? AvatarImage
    {
        get => _avatarImage;
        set
        {
            if (SetProperty(ref _avatarImage, value))
                OnPropertyChanged(nameof(HasAvatar));
        }
    }
    /// <summary>是否已有头像可显示。</summary>
    public bool HasAvatar => AvatarImage != null;
    public string CreatedAt { get; set; } = string.Empty;
    public int Carriers { get; set; }
    public string Similarity { get; set; } = string.Empty;
    /// <summary>是否当前激活账号（账号概览/账号管理据此高亮框出当前账号，可观察以随切换实时刷新）。</summary>
    [ObservableProperty]
    private bool _isCurrent;

    public IBrush AvatarBrush
    {
        get
        {
            try { return new SolidColorBrush(Avalonia.Media.Color.Parse(Color)); }
            catch { return Brushes.CornflowerBlue; }
        }
    }

    public IBrush StatusFgBrush
    {
        get
        {
            return StatusType switch
            {
                "ok" => new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x10, 0xB9, 0x81)),
                "info" => new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x3B, 0x82, 0xF6)),
                "warn" => new SolidColorBrush(Avalonia.Media.Color.FromRgb(0xF5, 0x9E, 0x0B)),
                "danger" => new SolidColorBrush(Avalonia.Media.Color.FromRgb(0xEF, 0x44, 0x44)),
                _ => new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x94, 0xA3, 0xB8))
            };
        }
    }

    public IBrush StatusBgBrush
    {
        get
        {
            return StatusType switch
            {
                "ok" => new SolidColorBrush(Avalonia.Media.Color.FromArgb(0x1A, 0x10, 0xB9, 0x81)),
                "info" => new SolidColorBrush(Avalonia.Media.Color.FromArgb(0x1A, 0x3B, 0x82, 0xF6)),
                "warn" => new SolidColorBrush(Avalonia.Media.Color.FromArgb(0x1A, 0xF5, 0x9E, 0x0B)),
                "danger" => new SolidColorBrush(Avalonia.Media.Color.FromArgb(0x1A, 0xEF, 0x44, 0x44)),
                _ => new SolidColorBrush(Avalonia.Media.Color.FromArgb(0x1A, 0x94, 0xA3, 0xB8))
            };
        }
    }
}

public class CheckinRecord
{
    public string Date { get; set; } = string.Empty;
    public string Account { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public IBrush ResultBrush => Result.StartsWith("+")
        ? new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x10, 0xB9, 0x81))
        : (Result == "—" ? new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x94, 0xA3, 0xB8)) : new SolidColorBrush(Avalonia.Media.Color.FromRgb(0xEF, 0x44, 0x44)));
}

/// <summary>
/// 顶栏账号切换器的列表项。
/// 刻意做得比 <see cref="AccountInfo"/> 轻：切换器只需要「一眼看清是哪个号、当前选中哪个」，
/// 不加载头像、不查询签到状态，避免顶栏展开时发起网络请求。
/// </summary>
public sealed class AccountSwitchItem
{
    /// <summary>源账号 Id。</summary>
    public required string Id { get; init; }
    /// <summary>展示名（昵称或脱敏手机号，均无则回退短 Id）。</summary>
    public required string Display { get; init; }
    /// <summary>副标题：脱敏手机号 / 学生标记等辅助识别信息。</summary>
    public string Subtitle { get; init; } = "";
    /// <summary>首字母头像占位字符。</summary>
    public string Initial { get; init; } = "?";
    /// <summary>是否为当前激活账号（决定列表中的高亮与右侧「当前」标记）。</summary>
    public bool IsCurrent { get; init; }
    /// <summary>今日是否已签到（用轻量符号提示，不触发网络请求）。</summary>
    public bool CheckedInToday { get; init; }
    /// <summary>
    /// 点击本项切换到该账号的命令（由 MainViewModel 注入）。
    /// 采用「命令挂在数据项上」而非在模板里向父级 DataContext 反查：
    /// 后者依赖 $parent 绑定语法，数据模板层级一变就会静默失效。
    /// </summary>
    public System.Windows.Input.ICommand? SelectCommand { get; set; }
    /// <summary>是否已签到今日（驱动行内 ✓ 标记的显示）。</summary>
    public bool ShowCheckMark => CheckedInToday;
    /// <summary>是否有副标题（脱敏手机号等）：无数据时整行隐藏，避免出现空行。</summary>
    public bool HasSubtitle => !string.IsNullOrEmpty(Subtitle);
}

/// <summary>签到页账号勾选项：IsChecked 与 TraeAccount.Enabled 双向同步（勾谁签谁，手动/自动一致）。</summary>
public sealed partial class AccountCheckItem : ObservableObject
{
    /// <summary>源账号 Id。</summary>
    public required string Id { get; init; }
    /// <summary>展示名（随账号改名同步刷新）。</summary>
    public string Display { get; set; } = string.Empty;
    [ObservableProperty]
    private bool _isChecked;

    /// <summary>勾选变化回调（由 VM 提供：同步 acc.Enabled 并保存）。</summary>
    public Action<AccountCheckItem>? OnChanged;

    partial void OnIsCheckedChanged(bool value) => OnChanged?.Invoke(this);
}

public class CalendarDay
{
    public int Day { get; set; }
    public string State { get; set; } = "empty";

    // ===== 日历视觉状态布尔位：供视图 Classes 驱动样式（颜色统一走主题令牌，支持暗色模式） =====
    public bool IsChecked => State == "checked";
    public bool IsMissed => State == "missed";
    public bool IsToday => State == "today";
    public bool IsBlank => State == "blank" || Day == 0;

    public IBrush DayBgBrush
    {
        get
        {
            return State switch
            {
                "checked" => new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x3B, 0x82, 0xF6)),
                "missed" => new SolidColorBrush(Avalonia.Media.Color.FromRgb(0xEF, 0x44, 0x44)),
                "today" => new SolidColorBrush(0xFFFFFFFF),
                "blank" => Brushes.Transparent,
                _ => new SolidColorBrush(Avalonia.Media.Color.FromRgb(0xF1, 0xF5, 0xF9))
            };
        }
    }
    public IBrush DayFgBrush
    {
        get
        {
            return State switch
            {
                "checked" => new SolidColorBrush(0xFFFFFFFF),
                "missed" => new SolidColorBrush(0xFFFFFFFF),
                "today" => new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x3B, 0x82, 0xF6)),
                "blank" => Brushes.Transparent,
                _ => new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x94, 0xA3, 0xB8))
            };
        }
    }
    public IBrush DayBorderBrush => State == "today"
        ? new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x3B, 0x82, 0xF6))
        : new SolidColorBrush(0x00FFFFFF);
    public string DisplayDay => Day == 0 ? "" : Day.ToString();

    /// <summary>该格对应日期（blank 无）。</summary>
    public DateTime? Date { get; set; }

    /// <summary>悬浮提示：当天签到账号及积分情况。</summary>
    public string ToolTipText { get; set; } = "";
}

public class CloudStep
{
    public int Index { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public string StatusText => Status switch
    {
        "ok" => "已完成 ✓",
        "progress" => "进行中 ◐",
        "pending" => "待启用 ○",
        _ => Status
    };
    public IBrush StatusFgBrush
    {
        get
        {
            return Status switch
            {
                "ok" => new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x10, 0xB9, 0x81)),
                "progress" => new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x3B, 0x82, 0xF6)),
                "warn" => new SolidColorBrush(Avalonia.Media.Color.FromRgb(0xF5, 0x9E, 0x0B)),
                _ => new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x94, 0xA3, 0xB8))
            };
        }
    }
    public IBrush StatusBgBrush
    {
        get
        {
            return Status switch
            {
                "ok" => new SolidColorBrush(Avalonia.Media.Color.FromArgb(0x1A, 0x10, 0xB9, 0x81)),
                "progress" => new SolidColorBrush(Avalonia.Media.Color.FromArgb(0x1A, 0x3B, 0x82, 0xF6)),
                "warn" => new SolidColorBrush(Avalonia.Media.Color.FromArgb(0x1A, 0xF5, 0x9E, 0x0B)),
                _ => new SolidColorBrush(Avalonia.Media.Color.FromArgb(0x1A, 0x94, 0xA3, 0xB8))
            };
        }
    }
}


