using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Options;
using Shoyee.Ctl.Cli;
using Xunit;

namespace Shoyee.Ctl.Tests.Cli;

/// <summary>终端状态输出：未连接/有错误的 Lane 也要看得见原因，别只给个圆点。</summary>
public sealed class StatusReportTests
{
    [Fact]
    public void 渲染版本时长与Lane()
    {
        var status = new DaemonStatusDto(
            "1.0.0",
            3 * 3600 + 25 * 60,
            "/tmp/shoyee.sock",
            [
                new LaneStatus(LaneIds.DesktopRoleplay, LaneChannel.Desktop, Mode.Roleplay, "桌面", true, "ses_1", null),
                new LaneStatus("mattermost:roleplay", LaneChannel.Mattermost, Mode.Roleplay, "手机", false, null, "401"),
            ]);

        var text = StatusReport.Format(status);

        Assert.Contains("v1.0.0", text, StringComparison.Ordinal);
        Assert.Contains("3 小时 25 分", text, StringComparison.Ordinal);
        Assert.Contains("● 桌面 (RP)", text, StringComparison.Ordinal);
        Assert.Contains("○ 手机 (RP)", text, StringComparison.Ordinal);
        Assert.Contains("401", text, StringComparison.Ordinal);
        Assert.Contains("会话 ses_1", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(5, "5 秒")]
    [InlineData(90, "1 分 30 秒")]
    [InlineData(3700, "1 小时 1 分")]
    [InlineData(90061, "1 天 1 小时")]
    public void 时长按最大单位给(int seconds, string expected)
        => Assert.Equal(expected, StatusReport.FormatDuration(TimeSpan.FromSeconds(seconds)));
}
