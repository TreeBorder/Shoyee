using Shoyee.Sessions.Lifecycle;
using Xunit;

namespace Shoyee.Sessions.Tests.Lifecycle;

public sealed class MessageDisplayTests
{
    [Fact]
    public void RemovesInjectedTimeAndChannelPrefix()
    {
        var raw = "<Time>[消息时间: 2026-10-29(周四) 14:00 +08:00]</Time>"
                  + "<Channel>[渠道: 桌面]</Channel>今天有点累";

        Assert.Equal("今天有点累", MessageDisplay.Clean(raw));
    }

    [Fact]
    public void KeepsPlainTextUntouched()
    {
        Assert.Equal("今天有点累", MessageDisplay.Clean("今天有点累"));
    }

    [Fact]
    public void OnlyStripsLeadingSegments_NotUserText()
    {
        // 用户在正文里提到标签名，不能被误删
        var raw = "<Time>[消息时间: 2026-10-29 14:00 +08:00]</Time>这个 <Channel> 标签是干嘛的？";

        Assert.Equal("这个 <Channel> 标签是干嘛的？", MessageDisplay.Clean(raw));
    }

    [Fact]
    public void UnwrapsSummary()
    {
        Assert.Equal("累计摘要内容", MessageDisplay.Clean("<Summary>\n累计摘要内容\n</Summary>"));
    }

    [Fact]
    public void HandlesEmptyAndUnclosedTags()
    {
        Assert.Equal(string.Empty, MessageDisplay.Clean(null));
        Assert.Equal("<Time>没有闭合", MessageDisplay.Clean("<Time>没有闭合"));
    }

    [Fact]
    public void RemovesEveryInnerVoiceBlockInOneBody()
    {
        var raw = """
            前半句😌

            <InnerVoice>
            自检：①这里不能出现
            </InnerVoice>

            中间句

            <InnerVoice>第二段自检，也不能出现</InnerVoice>

            后半句
            """;

        var cleaned = MessageDisplay.Clean(raw);

        Assert.DoesNotContain("InnerVoice", cleaned, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("自检", cleaned);
        Assert.Contains("前半句", cleaned);
        Assert.Contains("中间句", cleaned);
        Assert.Contains("后半句", cleaned);
    }

    [Fact]
    public void DropsUnclosedInnerVoiceTail()
    {
        var cleaned = MessageDisplay.Clean("说完了\n<InnerVoice>\n自检写到一半被截断");

        Assert.Equal("说完了", cleaned);
    }

    [Fact]
    public void ExtractsContentWrapper()
    {
        var cleaned = MessageDisplay.Clean("<InnerVoice>自检</InnerVoice><Content>正文只有这句</Content>");

        Assert.Equal("正文只有这句", cleaned);
    }
}
