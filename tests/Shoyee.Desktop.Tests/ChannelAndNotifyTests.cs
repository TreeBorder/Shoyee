using Shoyee.Desktop.ViewModels.Settings;
using Xunit;

namespace Shoyee.Desktop.Tests;

/// <summary>「渠道账号」与「通知」：列表 + 弹窗、类型切换与校验。</summary>
public sealed class ChannelAndNotifyTests
{
    [Fact]
    public void ChannelKindSwitchesFieldsAndSavesThroughDialog()
    {
        var channels = new ChannelAccountsViewModel();
        var initial = channels.Entries.Count;

        channels.AddCommand.Execute(null);
        var draft = channels.Editing!;
        Assert.True(channels.IsDialogOpen);
        Assert.True(draft.IsMattermost);

        draft.Kind = ChannelKind.WeChat;
        Assert.True(draft.IsWeChat);
        Assert.Equal("iLink 接入点", draft.BaseUrlHint);

        draft.Name = "微信 · 备用";
        draft.BaseUrl = "https://ilinkai.weixin.qq.com";
        draft.TokenEnv = "SHOYEE_WECHAT_BACKUP_TOKEN";
        channels.SaveDialogCommand.Execute(null);

        Assert.False(channels.IsDialogOpen);
        Assert.Equal(initial + 1, channels.Entries.Count);
        Assert.Equal(ChannelKind.WeChat, channels.Entries[0].Kind);
    }

    [Fact]
    public void ChannelValidationRejectsBadUrlAndKeepsOriginalOnCancel()
    {
        var channels = new ChannelAccountsViewModel();
        var original = channels.Entries[0];

        channels.EditCommand.Execute(original);
        Assert.Equal("编辑渠道", channels.DialogTitle);
        var draft = channels.Editing!;
        draft.BaseUrl = "not-a-url";
        channels.SaveDialogCommand.Execute(null);

        Assert.True(draft.HasError);
        Assert.True(channels.IsDialogOpen);

        draft.BaseUrl = "https://chat.example.com";
        draft.Name = "改过的名字";
        channels.CancelDialogCommand.Execute(null);
        Assert.NotEqual("改过的名字", original.Name);
        Assert.Equal("https://chat.example.com", original.BaseUrl);
    }

    [Fact]
    public void NotifyEmailRequiresSmtpFields()
    {
        var notify = new NotifyTargetsViewModel();
        notify.AddCommand.Execute(null);
        var draft = notify.Editing!;
        draft.Kind = NotifyKind.Email;
        draft.Name = "邮件";

        draft.SmtpHost = string.Empty;
        Assert.False(draft.Validate());
        Assert.True(draft.HasError);

        draft.SmtpHost = "smtp.example.com";
        draft.SmtpPort = 587;
        Assert.True(draft.Validate());

        notify.SaveDialogCommand.Execute(null);
        Assert.Equal(NotifyKind.Email, notify.Entries[0].Kind);
    }

    [Fact]
    public void NotifyOnlyOneTargetCanBeEnabled()
    {
        var notify = new NotifyTargetsViewModel();
        var system = notify.Entries.Single(entry => entry.Kind == NotifyKind.System);
        var serverChan = notify.Entries.Single(entry => entry.Kind == NotifyKind.ServerChan);

        Assert.True(system.IsEnabled);

        serverChan.IsEnabled = true;

        Assert.True(serverChan.IsEnabled);
        Assert.False(system.IsEnabled);
        Assert.Single(notify.Entries, entry => entry.IsEnabled);
    }

    [Fact]
    public void NotifyKindLabelsAndEndpointStayInSync()
    {
        var target = new NotifyTargetViewModel(NotifyKind.ServerChan, "推送");
        Assert.Equal("ServerChan", target.KindLabel);

        target.Kind = NotifyKind.Email;
        target.SmtpHost = "smtp.example.com";
        target.SmtpPort = 465;
        Assert.Equal("邮件", target.KindLabel);
        Assert.Equal("smtp.example.com:465", target.EndpointLabel);
    }
}
