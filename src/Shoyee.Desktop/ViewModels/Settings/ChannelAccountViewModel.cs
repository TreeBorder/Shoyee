using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Shoyee.Desktop.ViewModels.Settings;

/// <summary>已接入的渠道类型。</summary>
public enum ChannelKind
{
    /// <summary>Mattermost 机器人账号（WebSocket 收帖 + REST 发帖）。</summary>
    Mattermost,

    /// <summary>微信 iLink 长轮询。</summary>
    WeChat,
}

/// <summary>
/// 「渠道账号」里的一条账号。类型决定字段：Mattermost 有 @ 规则，微信用 iLink 地址、发送间隔与窗口兜底。
///
/// 现在只在本次运行内有效；接 daemon 后由 <c>config.set</c> 写 `channels.*`。
/// </summary>
public partial class ChannelAccountViewModel : ObservableObject
{
    /// <summary>创建一条渠道账号。</summary>
    /// <param name="kind">渠道类型。</param>
    /// <param name="name">显示名。</param>
    /// <param name="baseUrl">服务器 / 接入点地址。</param>
    /// <param name="tokenEnv">token 的环境变量名。</param>
    /// <param name="allowedUsers">允许的用户；空 = 不限。</param>
    /// <param name="requireMention">频道里是否必须 @bot。</param>
    /// <param name="minIntervalSeconds">微信最小发送间隔（秒）。</param>
    /// <param name="windowHours">微信窗口期兜底（小时）。</param>
    /// <param name="isEnabled">是否启用。</param>
    /// <param name="isNew">是否是还没保存的草稿。</param>
    /// <param name="id">内部标识；空 = 自动生成。</param>
    public ChannelAccountViewModel(
        ChannelKind kind,
        string name,
        string baseUrl,
        string tokenEnv,
        string allowedUsers = "",
        bool requireMention = true,
        double minIntervalSeconds = 1.2,
        double windowHours = 2,
        bool isEnabled = true,
        bool isNew = false,
        string? id = null)
    {
        _kind = kind;
        Id = id ?? Guid.NewGuid().ToString("N");
        Name = name;
        BaseUrl = baseUrl;
        TokenEnv = tokenEnv;
        AllowedUsers = allowedUsers;
        RequireMention = requireMention;
        MinIntervalSeconds = minIntervalSeconds;
        WindowHours = windowHours;
        IsEnabled = isEnabled;
        IsNew = isNew;
    }

    private ChannelKind _kind;

    /// <summary>渠道类型。</summary>
    public ChannelKind Kind
    {
        get => _kind;
        set
        {
            if (SetProperty(ref _kind, value))
            {
                OnPropertyChanged(nameof(IsMattermost));
                OnPropertyChanged(nameof(IsWeChat));
                OnPropertyChanged(nameof(KindLabel));
                OnPropertyChanged(nameof(BaseUrlHint));
            }
        }
    }

    /// <summary>内部标识（删除 / 编辑定位用）。</summary>
    public string Id { get; }

    /// <summary>是否是还没保存的草稿。</summary>
    public bool IsNew { get; set; }

    /// <summary>显示名。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    public partial string Name { get; set; }

    /// <summary>服务器 / iLink 接入点。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EndpointLabel))]
    public partial string BaseUrl { get; set; }

    /// <summary>token 的环境变量名。</summary>
    [ObservableProperty]
    public partial string TokenEnv { get; set; }

    /// <summary>允许的用户 id；逗号分隔，空 = 不限。</summary>
    [ObservableProperty]
    public partial string AllowedUsers { get; set; }

    /// <summary>Mattermost：频道里是否需要 @bot。</summary>
    [ObservableProperty]
    public partial bool RequireMention { get; set; }

    /// <summary>微信：最小发送间隔（秒）。</summary>
    [ObservableProperty]
    public partial double MinIntervalSeconds { get; set; }

    /// <summary>微信：窗口期兜底（小时）。</summary>
    [ObservableProperty]
    public partial double WindowHours { get; set; }

    /// <summary>是否启用。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    public partial bool IsEnabled { get; set; }

    /// <summary>保存时的字段错误；空 = 没错误。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; set; }

    /// <summary>是否有字段错误可展示。</summary>
    public bool HasError => !string.IsNullOrWhiteSpace(Error);

    /// <summary>是否是 Mattermost。</summary>
    public bool IsMattermost => Kind == ChannelKind.Mattermost;

    /// <summary>是否是微信。</summary>
    public bool IsWeChat => Kind == ChannelKind.WeChat;

    /// <summary>渠道类型标签。</summary>
    public string KindLabel => Kind switch
    {
        ChannelKind.Mattermost => "Mattermost",
        _ => "微信",
    };

    /// <summary>列表里显示的名字；空白时回落到类型标签。</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? KindLabel : Name.Trim();

    /// <summary>地址的简短提示。</summary>
    public string BaseUrlHint => IsMattermost ? "服务器地址" : "iLink 接入点";

    /// <summary>列表里的一行摘要。</summary>
    public string EndpointLabel => string.IsNullOrWhiteSpace(BaseUrl) ? "未填写地址" : BaseUrl.Trim();

    /// <summary>启用状态标签。</summary>
    public string StatusLabel => IsEnabled ? "已启用" : "已停用";

    /// <summary>复制一份用于编辑弹窗。</summary>
    /// <returns>字段相同的副本。</returns>
    public ChannelAccountViewModel CloneForEdit() => new(
        Kind,
        Name,
        BaseUrl,
        TokenEnv,
        AllowedUsers,
        RequireMention,
        MinIntervalSeconds,
        WindowHours,
        IsEnabled,
        id: Id);

    /// <summary>把弹窗里改完的值写回这条记录。</summary>
    /// <param name="source">编辑副本。</param>
    public void ApplyFrom(ChannelAccountViewModel source)
    {
        Kind = source.Kind;
        Name = source.Name;
        BaseUrl = source.BaseUrl;
        TokenEnv = source.TokenEnv;
        AllowedUsers = source.AllowedUsers;
        RequireMention = source.RequireMention;
        MinIntervalSeconds = source.MinIntervalSeconds;
        WindowHours = source.WindowHours;
        IsEnabled = source.IsEnabled;
        Error = null;
    }

    /// <summary>校验字段；返回 false 时错误写进 <see cref="Error"/>。</summary>
    /// <returns>是否可以保存。</returns>
    public bool Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            Error = "给这个账号起个名字，列表里靠它区分。";
            return false;
        }

        if (!IsHttpUrl(BaseUrl))
        {
            Error = $"{BaseUrlHint}需要是 http(s) 绝对地址。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(TokenEnv))
        {
            Error = "token 只存环境变量名，不能留空。";
            return false;
        }

        if (IsWeChat && MinIntervalSeconds < 0)
        {
            Error = "最小发送间隔不能为负。";
            return false;
        }

        Error = null;
        return true;
    }

    private static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
