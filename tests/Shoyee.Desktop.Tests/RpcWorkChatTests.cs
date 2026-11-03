using System.Text.Json;
using Avalonia;
using Avalonia.Headless;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Rpc;
using Shoyee.Desktop;
using Shoyee.Desktop.Conversations;
using Shoyee.Desktop.Rpc;
using Shoyee.Desktop.ViewModels;
using Shoyee.Desktop.ViewModels.Items;
using Xunit;

#pragma warning disable xUnit1031 // 同步等待：桌面测试共用单 UI 线程，异步会让后续测试换线程

namespace Shoyee.Desktop.Tests;

/// <summary>工作台接 RPC 后的投影与事件流（用假网关，不起 daemon）。</summary>
public sealed class RpcWorkChatTests
{


    [Fact]
    public void ProjectsHistoryWithToolPairsAndThinking()
    {
        var created = new DateTimeOffset(2026, 10, 30, 8, 0, 0, TimeSpan.Zero);
        var items = DaemonProjection.ToChatItems(
        [
            new MessageView(
                0,
                null,
                MessageRole.System,
                MessageKind.Text,
                "# 冻结的开场提示词……",
                null,
                created,
                null),
            new MessageView(1, null, MessageRole.User, MessageKind.Text, "看看磁盘", null, created, null),
            new MessageView(
                2,
                null,
                MessageRole.Assistant,
                MessageKind.ToolCall,
                "先看看磁盘。",
                "shell_run",
                created,
                null,
                ToolCallId: "call_1",
                ToolArguments: """{"command":"df -h"}"""),
            new MessageView(
                3,
                null,
                MessageRole.Tool,
                MessageKind.ToolResult,
                "",
                "shell_run",
                created,
                null,
                ToolCallId: "call_1",
                ToolResult: "/dev/disk1 60%"),
            new MessageView(
                4,
                null,
                MessageRole.Assistant,
                MessageKind.Text,
                "还剩 40%。",
                null,
                created,
                null,
                Thinking: "先看磁盘"),
        ]);

        Assert.Equal(5, items.Count);
        var processText = items.First(item => item.IsAssistant && item.Text == "先看看磁盘。");
        Assert.NotNull(processText);
        var tool = Assert.Single(items, item => item.IsTool);
        Assert.Equal("call_1", tool.ToolCallId);
        Assert.Equal("""{"command":"df -h"}""", tool.Detail);
        Assert.Equal("/dev/disk1 60%", tool.Result);

        var reasoning = Assert.Single(items, item => item.IsReasoning);
        Assert.Equal("先看磁盘", reasoning.Text);

        var assistant = items.Last();
        Assert.True(assistant.IsAssistant);
        Assert.Equal("还剩 40%。", assistant.Text);
    }

    [Fact]
    public void ProjectsSessionListOrderAndSource()
    {
        var summary = new SessionSummary(
            "ses_1",
            Mode.Work,
            LaneChannel.Mattermost,
            "mattermost:work",
            "周报",
            Archived: false,
            new DateTimeOffset(2026, 10, 28, 8, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 30, 8, 0, 0, TimeSpan.Zero),
            42);

        var item = DaemonProjection.ToSessionItem(summary);

        Assert.Equal("ses_1", item.Id);
        Assert.True(item.IsLive);
        Assert.True(item.IsExternal);
        Assert.Equal("Mattermost", item.Source);
        Assert.True(item.HasChannelTag);
        Assert.Equal(42, item.MessageCount);
        // 列表按最后活跃排序
        Assert.Equal(summary.LastActiveAt, item.CreatedAt);
    }

    [Fact]
    public void StreamsTurnEventsIntoBubbles()
    {
        var gateway = new FakeWorkGateway();
        gateway.Sessions.Add(Summary("ses_live", "测试会话"));
        gateway.Messages["ses_live"] = new SessionMessagesResult([], false);

        var viewModel = new WorkViewModel();
        viewModel.AttachGateway(gateway);
        viewModel.SelectedSession = viewModel.Sessions.Single(session => session.Id == "ses_live");

        gateway.Raise(RpcEvents.TurnStarted, new TurnStartedEvent("trn_1", "ses_live"));
        gateway.Raise(RpcEvents.TurnThinking, new TurnThinkingEvent("trn_1", "ses_live", "先想一想"));
        gateway.Raise(RpcEvents.TurnDelta, new TurnDeltaEvent("trn_1", "ses_live", "你"));
        gateway.Raise(RpcEvents.TurnDelta, new TurnDeltaEvent("trn_1", "ses_live", "好"));
        gateway.Raise(RpcEvents.TurnTool, new TurnToolEvent(
            "trn_1", "ses_live", TurnToolPhases.Start, "shell_run", "df -h",
            CallId: "call_1", Arguments: """{"command":"df -h"}"""));
        gateway.Raise(RpcEvents.TurnTool, new TurnToolEvent(
            "trn_1", "ses_live", TurnToolPhases.End, "shell_run", "df -h",
            CallId: "call_1", Result: "ok"));
        gateway.Raise(RpcEvents.TurnCompleted, new TurnCompletedEvent(
            "trn_1", "ses_live", 9, new UsageSummary(1, 1_000, 200, 900, 100)));

        var assistant = Assert.Single(viewModel.Messages, message => message.IsAssistant);
        Assert.Equal("你好", assistant.Text);

        var thinking = Assert.Single(viewModel.Messages, message => message.IsReasoning);
        Assert.Equal("先想一想", thinking.Text);

        var tool = Assert.Single(viewModel.Messages, message => message.IsTool);
        Assert.Equal("ok", tool.Result);

        Assert.True(viewModel.HasCacheUsage);
        Assert.Equal(90, viewModel.CacheHitPercent);
    }

    [Fact]
    public void SplitsReasoningAroundToolCalls()
    {
        var gateway = new FakeWorkGateway();
        gateway.Sessions.Add(Summary("ses_live", "测试会话"));
        gateway.Messages["ses_live"] = new SessionMessagesResult([], false);

        var viewModel = new WorkViewModel();
        viewModel.AttachGateway(gateway);
        viewModel.SelectedSession = null;
        viewModel.SelectedSession = viewModel.Sessions.Single(session => session.Id == "ses_live");

        gateway.Raise(RpcEvents.TurnThinking, new TurnThinkingEvent("trn_1", "ses_live", "先检查磁盘"));
        gateway.Raise(RpcEvents.TurnDelta, new TurnDeltaEvent("trn_1", "ses_live", "我看一下。"));
        gateway.Raise(RpcEvents.TurnTool, new TurnToolEvent(
            "trn_1", "ses_live", TurnToolPhases.Start, "shell_run", "df -h", CallId: "call_1"));
        gateway.Raise(RpcEvents.TurnTool, new TurnToolEvent(
            "trn_1", "ses_live", TurnToolPhases.End, "shell_run", "df -h", CallId: "call_1", Result: "ok"));
        gateway.Raise(RpcEvents.TurnThinking, new TurnThinkingEvent("trn_1", "ses_live", "剩余空间还够"));
        gateway.Raise(RpcEvents.TurnDelta, new TurnDeltaEvent("trn_1", "ses_live", "没问题"));

        var reasoning = viewModel.Messages.Where(message => message.IsReasoning).ToList();
        Assert.Equal(2, reasoning.Count);
        Assert.Equal("先检查磁盘", reasoning[0].Text);
        Assert.Equal("剩余空间还够", reasoning[1].Text);

        var assistants = viewModel.VisibleMessages.Where(message => message.IsAssistant).ToList();
        Assert.Equal(2, assistants.Count);
        Assert.Equal("我看一下。", assistants[0].Text);
        Assert.Equal("没问题", assistants[1].Text);

        // 工具前那段已经收尾：立即按 Markdown 渲染，不能等到整个回合结束
        Assert.False(assistants[0].IsStreaming);
        Assert.True(assistants[1].IsStreaming);
        Assert.DoesNotContain(reasoning, message => message.Text.Contains("没问题"));

        // 时间线顺序：思考 → 正文 → 工具 → 思考 → 正文
        Assert.True(viewModel.Messages.IndexOf(reasoning[0]) < viewModel.Messages.IndexOf(reasoning[1]));
        Assert.Contains(viewModel.Messages, message => message.IsTool);

        gateway.Raise(RpcEvents.TurnCompleted, new TurnCompletedEvent(
            "trn_1", "ses_live", 9, new UsageSummary(1, 1_000, 200, 900, 100)));
        Assert.False(assistants[1].IsStreaming);
    }

    [Fact]
    public void UsesProviderContextForUsageAndAutoTitle()
    {
        var gateway = new FakeWorkGateway();
        gateway.Sessions.Add(Summary("ses_live", "会话 2026-10-30 16:30"));
        gateway.Messages["ses_live"] = new SessionMessagesResult(
            [],
            false,
            new ContextUsage(48_000, 1_000, 47_000, DateTimeOffset.UtcNow));

        var viewModel = new WorkViewModel();
        viewModel.AttachGateway(gateway);
        viewModel.SelectedSession = null;
        viewModel.SelectedSession = viewModel.Sessions.Single(session => session.Id == "ses_live");

        Assert.True(viewModel.IsContextMeasured);
        Assert.Equal(49_000, viewModel.ContextUsedTokens);
        Assert.StartsWith("49k /", viewModel.ContextUsageText);
        Assert.DoesNotContain("≈", viewModel.ContextUsageText);
        Assert.True(viewModel.CacheHitPercent > 90);

        gateway.Raise(RpcEvents.SessionRenamed, new SessionRenamedEvent("ses_live", "磁盘排查"));
        Assert.Equal("磁盘排查", viewModel.SelectedSession!.Title);
        Assert.Equal("磁盘排查", viewModel.HeaderTitle);
    }

    [Fact]
    public void MeasuredContextFromTurnOverridesEstimate()
    {
        var gateway = new FakeWorkGateway();
        gateway.Sessions.Add(Summary("ses_live", "测试会话"));
        gateway.Messages["ses_live"] = new SessionMessagesResult([], false);

        var viewModel = new WorkViewModel();
        viewModel.AttachGateway(gateway);
        viewModel.SelectedSession = viewModel.Sessions.Single(session => session.Id == "ses_live");

        gateway.Raise(RpcEvents.TurnDelta, new TurnDeltaEvent("trn_1", "ses_live", "一段回答"));
        gateway.Raise(RpcEvents.TurnCompleted, new TurnCompletedEvent(
            "trn_1",
            "ses_live",
            1,
            new UsageSummary(2, 400_000, 5_000, 380_000, 20_000),
            Context: new ContextUsage(200_000, 5_000, 180_000, DateTimeOffset.UtcNow)));

        Assert.True(viewModel.IsContextMeasured);
        Assert.Equal(205_000, viewModel.ContextUsedTokens);
        Assert.Equal(20.5, viewModel.ContextUsagePercent, 3);
    }

    [Fact]
    public void SendsAndAnswersApprovalThroughGateway()
    {
        var gateway = new FakeWorkGateway();
        gateway.Sessions.Add(Summary("ses_live", "测试会话"));
        gateway.Messages["ses_live"] = new SessionMessagesResult([], false);

        var viewModel = new WorkViewModel();
        viewModel.AttachGateway(gateway);
        viewModel.SelectedSession = viewModel.Sessions.Single(session => session.Id == "ses_live");

        viewModel.Draft = "你好";
        viewModel.SendCommand.Execute(null);

        Assert.Equal("你好", Assert.Single(gateway.Sent).Text);
        Assert.Contains(viewModel.VisibleMessages, message => message.IsUser && message.Text == "你好");
        Assert.Equal("", viewModel.Draft);

        gateway.Raise(RpcEvents.ApprovalRequest, new ApprovalRequestDto(
            "apv_1",
            "ses_live",
            "trn_1",
            "shell_run",
            "rm -rf ./dist",
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMinutes(10)));

        Assert.True(viewModel.HasPendingApproval);
        Assert.Equal("shell_run", viewModel.PendingApproval!.ToolName);
        Assert.Equal("rm -rf ./dist", viewModel.PendingApproval.Command);

        viewModel.ApproveApprovalCommand.Execute(null);
        Assert.Equal(ApprovalDecision.Approved, Assert.Single(gateway.Approvals).Decision);
        Assert.False(viewModel.HasPendingApproval);
    }

    [Fact]
    public void SpeaksAssistantMessageThroughGateway()
    {
        var gateway = new FakeWorkGateway();
        gateway.Sessions.Add(Summary("ses_live", "语音测试"));
        gateway.Messages["ses_live"] = new SessionMessagesResult(
            [
                new MessageView(
                    1,
                    null,
                    MessageRole.Assistant,
                    MessageKind.Text,
                    "今天也要好好休息。",
                    null,
                    DateTimeOffset.Now,
                    null),
            ],
            false);

        var viewModel = new WorkViewModel();
        viewModel.AttachGateway(gateway);
        viewModel.SelectedSession = viewModel.Sessions.Single(session => session.Id == "ses_live");

        var assistant = viewModel.VisibleMessages.Single(message => message.IsAssistant);
        Assert.True(assistant.CanSpeak);

        assistant.SpeakCommand.ExecuteAsync(null).GetAwaiter().GetResult();

        Assert.Equal("今天也要好好休息。", Assert.Single(gateway.Spoken));
        Assert.True(assistant.HasSpeakError);
        Assert.Contains("不存在", assistant.SpeakError, StringComparison.Ordinal);
        Assert.False(assistant.IsSpeaking);
    }

    private static SessionSummary Summary(string id, string title) => new(
        id,
        Mode.Work,
        LaneChannel.Desktop,
        null,
        title,
        Archived: false,
        DateTimeOffset.Now.AddHours(-1),
        DateTimeOffset.Now,
        0);

    private sealed class FakeWorkGateway : IWorkChatGateway
    {
        public bool IsConnected { get; set; } = true;

        public List<SessionSummary> Sessions { get; } = [];

        public Dictionary<string, SessionMessagesResult> Messages { get; } = [];

        public List<(string SessionId, string Text)> Sent { get; } = [];

        public List<(string ApprovalId, ApprovalDecision Decision)> Approvals { get; } = [];

        public event Action<string, JsonElement>? EventReceived;

        public Task<IReadOnlyList<SessionSummary>> ListSessionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SessionSummary>>(Sessions);

        public Task<SessionMessagesResult> GetMessagesAsync(
            string sessionId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Messages.TryGetValue(sessionId, out var result)
                ? result
                : new SessionMessagesResult([], false));

        public Task<SessionDetail> CreateSessionAsync(
            string? title = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<string> SendTurnAsync(
            string sessionId,
            string text,
            CancellationToken cancellationToken = default)
        {
            Sent.Add((sessionId, text));
            return Task.FromResult("trn_1");
        }

        public Task RespondApprovalAsync(
            string approvalId,
            ApprovalDecision decision,
            CancellationToken cancellationToken = default)
        {
            Approvals.Add((approvalId, decision));
            return Task.CompletedTask;
        }

        public Task CancelTurnAsync(
            string sessionId,
            string? turnId = null,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public List<string> Spoken { get; } = [];

        public Task<TtsSynthesizeResult> SynthesizeSpeechAsync(
            string text,
            CancellationToken cancellationToken = default)
        {
            Spoken.Add(text);
            return Task.FromResult(new TtsSynthesizeResult(
                "asset_1",
                "/tmp/not-a-real-audio.mp3",
                "audio/mpeg",
                1));
        }

        public void Raise(string name, object payload)
            => EventReceived?.Invoke(name, JsonSerializer.SerializeToElement(payload, JsonWire.Options));
    }
}
