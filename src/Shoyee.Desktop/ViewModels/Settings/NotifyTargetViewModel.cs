using CommunityToolkit.Mvvm.ComponentModel;

namespace Shoyee.Desktop.ViewModels.Settings;

/// <summary>通知推送的供应商类型。</summary>
public enum NotifyKind
{
    /// <summary>系统通知（macOS 通知中心）。</summary>
    System,

    /// <summary>ServerChan 推送。</summary>
    ServerChan,

    /// <summary>SMTP 邮件。</summary>
    Email,
}

/// <summary>
/// 「通知」里的一条推送目标。类型决定字段：系统通知只有回退开关，
/// ServerChan 要 UID / SendKey，邮件要 SMTP 主机与凭据环境变量。
///
/// 现在只在本次运行内有效；接 daemon 后由 <c>config.set</c> 写 `notify.*`。
/// </summary>
public partial class NotifyTargetViewModel : ObservableObject
{
    /// <summary>创建一条通知目标。</summary>
    /// <param name="kind">供应商类型。</param>
    /// <param name="name">显示名。</param>
    /// <param name="isEnabled">是否启用。</param>
    /// <param name="fallbackToScript">系统通知：是否回退到脚本。</param>
    /// <param name="uidEnv">ServerChan UID 的环境变量名。</param>
    /// <param name="sendKeyEnv">ServerChan SendKey 的环境变量名。</param>
    /// <param name="smtpHost">SMTP 主机。</param>
    /// <param name="smtpPort">SMTP 端口。</param>
    /// <param name="userEnv">SMTP 用户名的环境变量名。</param>
    /// <param name="passwordEnv">SMTP 密码的环境变量名。</param>
    /// <param name="isNew">是否是还没保存的草稿。</param>
    /// <param name="id">内部标识；空 = 自动生成。</param>
    public NotifyTargetViewModel(
        NotifyKind kind,
        string name,
        bool isEnabled = true,
        bool fallbackToScript = false,
        string uidEnv = "SCT_UID",
        string sendKeyEnv = "SCT_SENDKEY",
        string smtpHost = "smtp.example.com",
        int smtpPort = 587,
        string userEnv = "SHOYEE_SMTP_USER",
        string passwordEnv = "SHOYEE_SMTP_PASSWORD",
        bool isNew = false,
        string? id = null)
    {
        _kind = kind;
        Id = id ?? Guid.NewGuid().ToString("N");
        Name = name;
        IsEnabled = isEnabled;
        FallbackToScript = fallbackToScript;
        UidEnv = uidEnv;
        SendKeyEnv = sendKeyEnv;
        SmtpHost = smtpHost;
        SmtpPort = smtpPort;
        UserEnv = userEnv;
        PasswordEnv = passwordEnv;
        IsNew = isNew;
    }

    private NotifyKind _kind;

    /// <summary>供应商类型。</summary>
    public NotifyKind Kind
    {
        get => _kind;
        set
        {
            if (SetProperty(ref _kind, value))
            {
                OnPropertyChanged(nameof(IsSystem));
                OnPropertyChanged(nameof(IsServerChan));
                OnPropertyChanged(nameof(IsEmail));
                OnPropertyChanged(nameof(KindLabel));
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

    /// <summary>是否启用。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    public partial bool IsEnabled { get; set; }

    /// <summary>系统通知：是否回退到脚本。</summary>
    [ObservableProperty]
    public partial bool FallbackToScript { get; set; }

    /// <summary>ServerChan UID 的环境变量名。</summary>
    [ObservableProperty]
    public partial string UidEnv { get; set; }

    /// <summary>ServerChan SendKey 的环境变量名。</summary>
    [ObservableProperty]
    public partial string SendKeyEnv { get; set; }

    /// <summary>SMTP 主机。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EndpointLabel))]
    public partial string SmtpHost { get; set; }

    /// <summary>SMTP 端口。</summary>
    [ObservableProperty]
    public partial int SmtpPort { get; set; }

    /// <summary>SMTP 用户名的环境变量名。</summary>
    [ObservableProperty]
    public partial string UserEnv { get; set; }

    /// <summary>SMTP 密码的环境变量名。</summary>
    [ObservableProperty]
    public partial string PasswordEnv { get; set; }

    /// <summary>保存时的字段错误；空 = 没错误。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; set; }

    /// <summary>是否有字段错误可展示。</summary>
    public bool HasError => !string.IsNullOrWhiteSpace(Error);

    /// <summary>是否是系统通知。</summary>
    public bool IsSystem => Kind == NotifyKind.System;

    /// <summary>是否是 ServerChan。</summary>
    public bool IsServerChan => Kind == NotifyKind.ServerChan;

    /// <summary>是否是邮件。</summary>
    public bool IsEmail => Kind == NotifyKind.Email;

    /// <summary>类型标签。</summary>
    public string KindLabel => Kind switch
    {
        NotifyKind.System => "系统通知",
        NotifyKind.ServerChan => "ServerChan",
        _ => "邮件",
    };

    /// <summary>列表里显示的名字；空白时回落到类型标签。</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? KindLabel : Name.Trim();

    /// <summary>列表里的一行摘要。</summary>
    public string EndpointLabel => Kind switch
    {
        NotifyKind.System => "走系统通知中心",
        NotifyKind.ServerChan => "ServerChan 推送",
        _ => string.IsNullOrWhiteSpace(SmtpHost) ? "未填写 SMTP 主机" : $"{SmtpHost}:{SmtpPort}",
    };

    /// <summary>启用状态标签。</summary>
    public string StatusLabel => IsEnabled ? "已启用" : "已停用";

    /// <summary>复制一份用于编辑弹窗。</summary>
    /// <returns>字段相同的副本。</returns>
    public NotifyTargetViewModel CloneForEdit() => new(
        Kind,
        Name,
        IsEnabled,
        FallbackToScript,
        UidEnv,
        SendKeyEnv,
        SmtpHost,
        SmtpPort,
        UserEnv,
        PasswordEnv,
        id: Id);

    /// <summary>把弹窗里改完的值写回这条记录。</summary>
    /// <param name="source">编辑副本。</param>
    public void ApplyFrom(NotifyTargetViewModel source)
    {
        Kind = source.Kind;
        Name = source.Name;
        IsEnabled = source.IsEnabled;
        FallbackToScript = source.FallbackToScript;
        UidEnv = source.UidEnv;
        SendKeyEnv = source.SendKeyEnv;
        SmtpHost = source.SmtpHost;
        SmtpPort = source.SmtpPort;
        UserEnv = source.UserEnv;
        PasswordEnv = source.PasswordEnv;
        Error = null;
    }

    /// <summary>校验字段；返回 false 时错误写进 <see cref="Error"/>。</summary>
    /// <returns>是否可以保存。</returns>
    public bool Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            Error = "给这条推送起个名字，列表里靠它区分。";
            return false;
        }

        if (IsServerChan && (string.IsNullOrWhiteSpace(UidEnv) || string.IsNullOrWhiteSpace(SendKeyEnv)))
        {
            Error = "ServerChan 的 UID / SendKey 只存环境变量名，不能留空。";
            return false;
        }

        if (IsEmail)
        {
            if (string.IsNullOrWhiteSpace(SmtpHost))
            {
                Error = "SMTP 主机不能为空。";
                return false;
            }

            if (SmtpPort is <= 0 or > 65535)
            {
                Error = "SMTP 端口需要在 1–65535 之间。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(UserEnv) || string.IsNullOrWhiteSpace(PasswordEnv))
            {
                Error = "SMTP 凭据只存环境变量名，不能留空。";
                return false;
            }
        }

        Error = null;
        return true;
    }
}
