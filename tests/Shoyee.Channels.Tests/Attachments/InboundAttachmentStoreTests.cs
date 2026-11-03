using Shoyee.Channels.Attachments;
using Shoyee.Channels.Tests.TestSupport;
using Shoyee.Contracts.Dtos;using Shoyee.Contracts.Options;
using Xunit;

namespace Shoyee.Channels.Tests.Attachments;

/// <summary>入站附件落地：目录约定、文件名清洗、类别判定。</summary>
public sealed class InboundAttachmentStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "shoyee-inbox-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Save_WritesUnderInboxAndSanitizesName()
    {
        var store = new InboundAttachmentStore(Config(), new FixedClock(new DateTimeOffset(2026, 10, 28, 9, 30, 0, TimeSpan.Zero)));

        var saved = await store.SaveAsync("../../evil.png", "image/png", new byte[] { 1, 2, 3 });

        Assert.Equal("evil.png", saved.FileName);
        Assert.True(File.Exists(saved.LocalPath));
        Assert.StartsWith(Path.Combine(_root, "inbox"), saved.LocalPath);
        // 时钟是 UTC 09:30，本地时区可能是前一天；只断言「在 inbox 下且按年月分目录」
        Assert.Matches(@"inbox[/\\]\d{4}[/\\]\d{2}[/\\]\d{8}_\d{6}_[0-9a-f]{8}\.png", saved.LocalPath);
        Assert.Equal(3, saved.SizeBytes);
        Assert.Equal(InboundAttachmentKind.Image, saved.Kind);
    }

    [Fact]
    public async Task Save_ClassifiesAudioByMime()
    {
        var store = new InboundAttachmentStore(Config(), new FixedClock(DateTimeOffset.UnixEpoch));

        var saved = await store.SaveAsync("voice.silk", "audio/silk", new byte[] { 1 });

        Assert.Equal(InboundAttachmentKind.Audio, saved.Kind);
    }

    [Fact]
    public async Task Save_GuessesMimeFromExtensionWhenMissing()
    {
        var store = new InboundAttachmentStore(Config(), new FixedClock(DateTimeOffset.UnixEpoch));

        var saved = await store.SaveAsync("photo.jpg", null, new byte[] { 1 });

        Assert.Equal("image/jpeg", saved.Mime);
        Assert.Equal(InboundAttachmentKind.Image, saved.Kind);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private WorkspaceConfig Config() => new(
        _root,
        new ModelProfiles(
            new ModelEndpoint("http://localhost", "main", "KEY"),
            new ModelEndpoint("http://localhost", "small", "KEY")),
        new ChannelAccounts(),
        new ProactiveOptions(new HeartbeatOptions()),
        new ToolOptions());
}
