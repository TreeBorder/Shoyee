using Shoyee.Notify.System;
using Xunit;

#pragma warning disable CA1416 // 这些用例只在 macOS 上跑（系统通知本身也只实现 macOS）

namespace Shoyee.Notify.System.Tests;

public sealed class SystemNotificationPusherTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "shoyee-notify-" + Guid.NewGuid().ToString("N")[..8]);

    public SystemNotificationPusherTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public void BuildArguments_IncludesNotifyTitleBodyAndSilent()
    {
        Assert.Equal(
            ["--notify", "标题", "正文"],
            SystemNotificationPusher.BuildArguments("标题", "正文", sound: true));
        Assert.Equal(
            ["--notify", "标题", "正文", "--silent"],
            SystemNotificationPusher.BuildArguments("标题", "正文", sound: false));
    }

    [Fact]
    public void BuildAppleScript_EscapesQuotesAndLineBreaks()
    {
        var script = SystemNotificationPusher.BuildAppleScript("标\"题", "第一行\n第二行");

        Assert.Equal("display notification \"第一行 第二行\" with title \"标\\\"题\"", script);
    }

    [Fact]
    public void ResolveExecutablePath_DefaultsToLowercaseExecutableInsideBundle()
    {
        var path = SystemNotificationPusher.ResolveExecutablePath("", "/tmp/out");

        Assert.Equal(Path.Combine("/tmp/out", "筱.app", "Contents", "MacOS", "shoyee"), path);
    }

    [Fact]
    public void ResolveExecutablePath_InsideBundle_PrefersSibling()
    {
        var binary = Path.Combine(_dir, "shoyee");
        File.WriteAllText(binary, string.Empty);

        var path = SystemNotificationPusher.ResolveExecutablePath("", _dir);

        Assert.Equal(binary, path);
    }

    [Fact]
    public async Task PushAsync_MissingExecutable_ThrowsActionableError()
    {
        var pusher = new SystemNotificationPusher(new SystemNotifyOptions(
            ExecutablePath: Path.Combine(_dir, "筱.app", "Contents", "MacOS", "shoyee")));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => pusher.PushAsync("t", "b"));

        Assert.Contains("筱.app", ex.Message);
    }

    [Fact]
    public async Task PushAsync_RunsExecutableWithArguments()
    {
        var marker = Path.Combine(_dir, "args.txt");
        var script = Path.Combine(_dir, "fake-shoyee.sh");
        File.WriteAllText(script, $"#!/bin/sh\nprintf '%s\\n' \"$@\" > '{marker}'\n");
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var pusher = new SystemNotificationPusher(new SystemNotifyOptions(ExecutablePath: script));
        await pusher.PushAsync("标题", "正文");

        Assert.Equal(["--notify", "标题", "正文"], File.ReadAllLines(marker));
    }

    [Fact]
    public async Task PushAsync_NonZeroExit_ThrowsWithStderr()
    {
        var script = Path.Combine(_dir, "fail.sh");
        File.WriteAllText(script, "#!/bin/sh\necho 'permission denied' >&2\nexit 1\n");
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var pusher = new SystemNotificationPusher(new SystemNotifyOptions(ExecutablePath: script));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => pusher.PushAsync("t", "b"));

        Assert.Contains("permission denied", ex.Message);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
