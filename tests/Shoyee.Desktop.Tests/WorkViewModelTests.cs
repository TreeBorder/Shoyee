using Avalonia.Layout;
using Shoyee.Desktop.ViewModels;
using Shoyee.Desktop.ViewModels.Items;
using Xunit;

namespace Shoyee.Desktop.Tests;

/// <summary>会话列表的创建时间筛选。</summary>
public sealed class WorkViewModelTests
{
    [Fact]
    public void FiltersSessionsByCreatedAt()
    {
        var viewModel = new WorkViewModel();
        var all = viewModel.VisibleSessions.Count;

        viewModel.SetSessionFilterCommand.Execute("Today");
        Assert.All(viewModel.VisibleSessions, s => Assert.True(s.CreatedAt.Date == DateTimeOffset.Now.Date));
        Assert.True(viewModel.VisibleSessions.Count <= all);
        Assert.Equal("今天", viewModel.SessionFilterLabel);

        viewModel.SetSessionFilterCommand.Execute("Last7Days");
        Assert.NotEmpty(viewModel.VisibleSessions);
        Assert.All(viewModel.VisibleSessions, s => Assert.True(s.CreatedAt >= DateTimeOffset.Now.AddDays(-7)));

        viewModel.SetSessionFilterCommand.Execute("All");
        Assert.Equal(all, viewModel.VisibleSessions.Count);
        Assert.Equal("全部时间", viewModel.SessionFilterLabel);
    }

    [Fact]
    public void FiltersToolsAndReasoningAndShowsContextUsage()
    {
        var viewModel = new WorkViewModel();
        var session = viewModel.Sessions.First(item => item.Messages.Any(message => message.IsTool));
        viewModel.SelectedSession = session;

        Assert.True(viewModel.ContextUsedTokens > 0);
        Assert.True(viewModel.ContextUsagePercent > 0);
        Assert.Equal(1_000_000, viewModel.ContextLimitTokens);
        Assert.Equal(
            viewModel.ContextUsedTokens,
            viewModel.BodyTokens + viewModel.ReasoningTokens + viewModel.ToolTokens);

        // 上轮 API 的真实缓存命中率（样例快照来自 Secretary session_usage）
        Assert.True(viewModel.HasCacheUsage);
        Assert.True(viewModel.CacheHitPercent > 90);
        Assert.True(viewModel.IsCacheHigh);

        // 工具默认显示、每行默认折叠；思考默认隐藏
        Assert.Contains(viewModel.VisibleMessages, message => message.IsTool);
        Assert.DoesNotContain(viewModel.VisibleMessages, message => message.IsReasoning);
        Assert.All(viewModel.VisibleMessages.Where(message => message.IsTool), message => Assert.False(message.IsExpanded));

        viewModel.ToggleToolCallsCommand.Execute(null);
        Assert.DoesNotContain(viewModel.VisibleMessages, message => message.IsTool);

        viewModel.ToggleReasoningCommand.Execute(null);
        Assert.Contains(viewModel.VisibleMessages, message => message.IsReasoning);

        // 气泡宽度：当前款 620，平铺时放宽并撑满
        var assistant = viewModel.VisibleMessages.First(message => message.IsAssistant);
        Assert.False(viewModel.IsWideBubbles);
        Assert.Equal(620, assistant.BubbleMaxWidth);
        viewModel.ToggleBubbleWidthCommand.Execute(null);
        Assert.True(viewModel.IsWideBubbles);
        Assert.Equal(4000, assistant.BubbleMaxWidth);
        Assert.Equal(HorizontalAlignment.Stretch, assistant.BubbleHorizontalAlignment);

        viewModel.UpdateModel("test-model", "https://example.com", 32_000);
        Assert.Equal("test-model", viewModel.ModelName);
        Assert.Equal(32_000, viewModel.ContextLimitTokens);

        // 在本地追加一条用户消息，验证发送快捷键走的命令
        viewModel.Draft = "hello";
        viewModel.SendCommand.Execute(null);
        Assert.Equal("", viewModel.Draft);
        Assert.Contains(viewModel.VisibleMessages, message => message.IsUser && message.Text == "hello");
    }

    [Fact]
    public void ContextColorsUseAbsoluteTokenThresholds()
    {
        Assert.True(Colorize(150_000).IsContextGreen);
        Assert.True(Colorize(300_000).IsContextYellow);
        Assert.True(Colorize(500_000).IsContextRed);
        Assert.False(Colorize(500_000).IsContextElectric);
        Assert.True(Colorize(650_000).IsContextElectric);
    }

    private static WorkViewModel Colorize(int tokens)
    {
        var viewModel = new WorkViewModel();
        viewModel.SelectedSession = new SessionItem
        {
            Title = "上下文阈值",
            Preview = "",
            Time = "刚刚",
            Source = "桌面",
            CreatedAt = DateTimeOffset.Now,
            Messages =
            [
                new ChatMessageItem
                {
                    Kind = ChatEntryKind.Tool,
                    ToolName = "test",
                    Detail = new string('a', tokens * 4),
                },
            ],
        };
        return viewModel;
    }

    [Fact]
    public void CacheHitColorsUseGreenYellowRedThresholds()
    {
        var viewModel = new WorkViewModel();
        viewModel.SelectedSession = UsageSession(1000, 950);
        Assert.True(viewModel.IsCacheHigh);
        Assert.False(viewModel.IsCacheMedium);

        viewModel.SelectedSession = UsageSession(1000, 850);
        Assert.True(viewModel.IsCacheMedium);

        viewModel.SelectedSession = UsageSession(1000, 500);
        Assert.True(viewModel.IsCacheLow);

        viewModel.SelectedSession = UsageSession(0, 0);
        Assert.False(viewModel.HasCacheUsage);
        Assert.Equal("—", viewModel.CacheHitText);
    }

    private static SessionItem UsageSession(long input, long cached) => new()
    {
        Title = "缓存测试",
        Preview = "",
        Time = "刚刚",
        Source = "桌面",
        LastInputTokens = input,
        LastCachedInputTokens = cached,
        CreatedAt = DateTimeOffset.Now,
    };
}
