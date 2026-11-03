using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Shoyee.Agent.Runtime;
using Shoyee.Agent.Runtime.Work;
using Shoyee.Agent.Tests.TestSupport;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Options;
using Shoyee.Contracts.Rpc;
using Shoyee.Storage.Abstractions;
using Xunit;

namespace Shoyee.Agent.Tests.Runtime;

public sealed class TurnServiceTests
{
    private const string RpLane = LaneIds.DesktopRoleplay;

    [Fact]
    public async Task SendAndWait_ReturnsProjectedTextAndMessageId()
    {
        using var bed = new AgentTestBed(new RecordingChatClient());

        var outcome = await bed.Service.SendAndWaitAsync(new TurnSendRequest("在吗", LaneId: RpLane));

        Assert.True(outcome.Succeeded);
        Assert.Equal("应答", outcome.Text);
        Assert.NotNull(outcome.MessageId);
        var projected = await bed.Messages.GetLastAssistantAsync(laneId: RpLane, roundNo: 1);
        Assert.Equal(projected!.Id, outcome.MessageId);
    }

    [Fact]
    public async Task SendAsync_Roleplay_StreamsDeltas_AndPersistsRound()
    {
        using var bed = new AgentTestBed(new RecordingChatClient());

        var turn = await bed.Service.SendAsync(new TurnSendRequest("在吗", LaneId: RpLane));
        var completed = await bed.Sink.WaitForAsync(e => e.EventName == RpcEvents.TurnCompleted);

        var payload = Assert.IsType<TurnCompletedEvent>(completed.Payload);
        Assert.Equal(turn.TurnId, payload.TurnId);
        Assert.Equal(RpLane, payload.SessionId);
        Assert.Equal(RpLane, payload.LaneId);

        var projected = await bed.Messages.GetLastAssistantAsync(laneId: RpLane, roundNo: 1);
        Assert.NotNull(projected);
        Assert.Equal(projected!.Id, payload.MessageId);

        var deltas = bed.Sink.Payloads<TurnDeltaEvent>(RpcEvents.TurnDelta);
        Assert.Contains("应答", string.Concat(deltas.Select(d => d.Delta)), StringComparison.Ordinal);

        var round = Assert.Single(bed.Rounds.Appended);
        Assert.Contains("在吗", round.Request.Single().Text!, StringComparison.Ordinal);

        var events = bed.Sink.Events.Select(e => e.EventName).ToList();
        Assert.Equal(RpcEvents.TurnStarted, events[0]);
        Assert.Equal(RpcEvents.TurnCompleted, events[^1]);
    }

    [Fact]
    public async Task SendAsync_Work_UsesSessionHistory_AndSavesState()
    {
        var client = new RecordingChatClient();
        using var bed = new AgentTestBed(client);
        await bed.SeedWorkSessionAsync();

        await bed.Service.SendAsync(new TurnSendRequest("整理周报", SessionId: "ses_1"));
        var completed = await bed.Sink.WaitForAsync(e => e.EventName == RpcEvents.TurnCompleted);

        var payload = Assert.IsType<TurnCompletedEvent>(completed.Payload);
        Assert.Equal("ses_1", payload.SessionId);
        Assert.Equal("mattermost:work", payload.LaneId);
        Assert.NotNull(payload.MessageId);
        var projected = await bed.Messages.GetLastAssistantAsync(sessionId: "ses_1");
        Assert.Equal(projected!.Id, payload.MessageId);
        Assert.Contains("应答", string.Concat(
            bed.Sink.Payloads<TurnDeltaEvent>(RpcEvents.TurnDelta).Select(d => d.Delta)), StringComparison.Ordinal);

        Assert.True(bed.States.Rows.ContainsKey("ses_1"));
        Assert.Equal("工作人设", client.Calls[0][0].Text);
        Assert.Contains("整理周报", client.Calls[0][^1].Text!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancelAsync_StopsTurn_AndPublishesCancelled()
    {
        using var bed = new AgentTestBed(new HangingChatClient());
        var turn = await bed.Service.SendAsync(new TurnSendRequest("说点什么", LaneId: RpLane));
        await bed.Sink.WaitForAsync(e => e.EventName == RpcEvents.TurnDelta);

        await bed.Service.CancelAsync(RpLane, turn.TurnId);

        var failed = await bed.Sink.WaitForAsync(e => e.EventName == RpcEvents.TurnFailed);
        var payload = Assert.IsType<TurnFailedEvent>(failed.Payload);
        Assert.Equal(TurnFailureCodes.Cancelled, payload.Code);
        Assert.Equal(turn.TurnId, payload.TurnId);
    }

    [Fact]
    public async Task SendAsync_WithPendingApproval_Rejected()
    {
        using var bed = new AgentTestBed(new RecordingChatClient());
        var request = new ToolApprovalRequestContent(
            "req-1",
            new FunctionCallContent("call-1", "shell_run", new Dictionary<string, object?> { ["command"] = "ls" }));
        var pending = bed.Broker.RequestAsync(RpLane, "trn_pending", RpLane, [request]);
        var raised = await bed.Sink.WaitForAsync(e => e.EventName == RpcEvents.ApprovalRequest);
        var dto = Assert.IsType<ApprovalRequestDto>(raised.Payload);

        var error = await Assert.ThrowsAsync<RpcException>(
            () => bed.Service.SendAsync(new TurnSendRequest("新消息", LaneId: RpLane)));

        Assert.Equal(RpcErrors.ApprovalPending, error.Code);

        await bed.Broker.ResolveAsync(dto.ApprovalId, ApprovalDecision.Denied, "test");
        await pending;
    }

    [Fact]
    public async Task SendAsync_Roleplay_AttachesOutboundFilesToLastAssistantMessage()
    {
        var attachment = new MessageAttachment(
            "/tmp/voice.wav", "voice.wav", "audio/wav", 10, AttachmentKind.Audio);
        using var bed = new AgentTestBed(new OutboundAttachmentClient(attachment));

        var turn = await bed.Service.SendAsync(new TurnSendRequest("在吗", LaneId: RpLane));
        var completed = await bed.Sink.WaitForAsync(e => e.EventName == RpcEvents.TurnCompleted);

        var payload = Assert.IsType<TurnCompletedEvent>(completed.Payload);
        var fromEvent = Assert.Single(payload.Attachments!);
        Assert.Equal(attachment.Path, fromEvent.Path);

        var projected = await bed.Messages.GetLastAssistantAsync(laneId: RpLane, roundNo: 1);
        var fromRow = Assert.Single(projected!.Attachments!);
        Assert.Equal(attachment.Path, fromRow.Path);
        Assert.Equal(turn.TurnId, payload.TurnId);
    }

    [Fact]
    public async Task SendAsync_Work_AttachesOutboundFilesToLastAssistantMessage()
    {
        var attachment = new MessageAttachment(
            "/tmp/report.pdf", "report.pdf", "application/pdf", 20, AttachmentKind.File);
        using var bed = new AgentTestBed(new OutboundAttachmentClient(attachment));
        await bed.SeedWorkSessionAsync();

        await bed.Service.SendAsync(new TurnSendRequest("发我报告", SessionId: "ses_1"));
        var completed = await bed.Sink.WaitForAsync(e => e.EventName == RpcEvents.TurnCompleted);

        var payload = Assert.IsType<TurnCompletedEvent>(completed.Payload);
        Assert.Single(payload.Attachments!);

        var projected = await bed.Messages.GetLastAssistantAsync(sessionId: "ses_1");
        Assert.Single(projected!.Attachments!);
    }

    [Fact]
    public async Task SendAsync_AnalyzerEnrichesModelText_ButKeepsDisplayTextClean()
    {
        var client = new RecordingChatClient();
        using var bed = new AgentTestBed(client, analyzer: new FakeAnalyzer("图片内容：一只橘猫"));
        var attachment = new MessageAttachment(
            "/tmp/cat.png", "cat.png", "image/png", 3, AttachmentKind.Image);

        await bed.Service.SendAsync(new TurnSendRequest("看看这个", LaneId: RpLane, Attachments: [attachment]));
        var completed = await bed.Sink.WaitForAsync(e => e.EventName == RpcEvents.TurnCompleted);

        var modelText = client.Calls[0][^1].Text!;
        Assert.Contains("[附件] /tmp/cat.png", modelText);
        Assert.Contains("图片内容：一只橘猫", modelText);

        var projected = await bed.Messages.GetPageAsync(laneId: RpLane);
        var userRow = projected.First(static row => row.Role == "user");
        Assert.Equal("看看这个", userRow.Content);
        Assert.IsType<TurnCompletedEvent>(completed.Payload);
    }

    [Fact]
    public async Task SendAsync_UnknownSession_ThrowsNotFound()
    {
        using var bed = new AgentTestBed(new RecordingChatClient());

        var error = await Assert.ThrowsAsync<RpcException>(
            () => bed.Service.SendAsync(new TurnSendRequest("在吗", SessionId: "ses_missing")));

        Assert.Equal(RpcErrors.NotFound, error.Code);
    }

    [Fact]
    public async Task SendAsync_Work_ApprovalRoundTrip_ContinuesTurn()
    {
        var client = new FunctionCallThenTextClient();
        using var bed = new AgentTestBed(client);
        await bed.SeedWorkSessionAsync();

        var tool = new ApprovalRequiredAIFunction(
            AIFunctionFactory.Create((string command) => "ok", "shell_run"));
        var options = new ChatOptions { Tools = [tool] };

        // TurnService 不直接收 tools（工具按 profile 组装属于 Tools 模块），
        // 这里直接验 WorkTurnRunner 的审批续跑。
        var session = await bed.Cache.GetAsync("ses_1");
        Assert.NotNull(session);

        var approvals = new List<ApprovalRequestDto>();
        var runner = new WorkTurnRunner(bed.WorkAgent, bed.WorkFactory, bed.Cache);
        var turnTask = runner.RunStreamingAsync(
            "ses_1",
            "mattermost:work",
            new ChatMessage(ChatRole.User, "跑一下命令"),
            TurnObserver.None,
            options,
            async (requests, ct) =>
            {
                var contents = await bed.Broker.RequestAsync("ses_1", "trn_1", "mattermost:work", requests, ct);
                approvals.AddRange(bed.Sink.Payloads<ApprovalRequestDto>(RpcEvents.ApprovalRequest));
                return contents;
            });

        var raised = await bed.Sink.WaitForAsync(e => e.EventName == RpcEvents.ApprovalRequest);
        var dto = Assert.IsType<ApprovalRequestDto>(raised.Payload);
        Assert.Equal("shell_run", dto.ToolName);
        Assert.Equal("echo hi", dto.Command);

        Assert.True(await bed.Broker.ResolveAsync(dto.ApprovalId, ApprovalDecision.Approved, "desktop"));
        var result = await turnTask;

        Assert.Contains("完成", result.Text, StringComparison.Ordinal);
        Assert.Single(approvals);
    }

    /// <summary>第一次调用要求执行需要审批的工具，批准后第二次回正文。</summary>
    private sealed class FunctionCallThenTextClient : IChatClient
    {
        private int _calls;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant,
                [
                    new FunctionCallContent("call-1", "shell_run",
                        new Dictionary<string, object?> { ["command"] = "echo hi" }),
                ]);
            }
            else
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, "完成");
            }

            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceType == typeof(IChatClient) ? this : null;

        public void Dispose()
        {
        }
    }

    /// <summary>先吐半句再挂起，用于验证打断。</summary>
    private sealed class HangingChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, "半句");
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceType == typeof(IChatClient) ? this : null;

        public void Dispose()
        {
        }
    }

    [Fact]
    public async Task AppendSpoken_AppendsAssistantRound_AndEmitsMessageAppended()
    {
        using var bed = new AgentTestBed(new RecordingChatClient());
        bed.Lanes.Seed(new LaneRecord(
            RpLane,
            LaneChannel.Desktop,
            Mode.Roleplay,
            "desktop",
            "桌面",
            null,
            null,
            null,
            DateTimeOffset.UtcNow));

        var outcome = await bed.Service.AppendSpokenAsync(RpLane, "早上好呀");

        Assert.True(outcome.Succeeded);
        Assert.Equal("早上好呀", outcome.Text);
        Assert.NotNull(outcome.MessageId);
        var round = Assert.Single(bed.Rounds.Appended);
        Assert.Empty(round.Request);
        Assert.Equal("早上好呀", round.Response.Single().Text);
        var appended = Assert.Single(bed.Sink.Payloads<MessageAppendedEvent>(RpcEvents.MessageAppended));
        Assert.Equal("早上好呀", appended.Message.Text);
        Assert.Equal(RpLane, appended.LaneId);
    }

    [Fact]
    public async Task Retry_失败轮次_先退回历史再重跑_不重复用户消息()
    {
        var client = new FailOnceChatClient();
        using var bed = new AgentTestBed(client);
        await bed.SeedWorkSessionAsync("ses_retry");

        var first = await bed.Service.SendAndWaitAsync(
            new TurnSendRequest("再来一次", SessionId: "ses_retry"));

        Assert.False(first.Succeeded);

        var retry = await bed.Service.RetryAsync("ses_retry");
        var completed = await bed.Sink.WaitForAsync(e => e.EventName == RpcEvents.TurnCompleted);

        Assert.Equal(retry.TurnId, Assert.IsType<TurnCompletedEvent>(completed.Payload).TurnId);
        // 重跑后历史里只有一条用户消息，失败时的半截回复没有留下
        var users = bed.Messages.Rows.Where(static r => r.Role == "user").ToList();
        Assert.Single(users);
        Assert.Equal("再来一次", users[0].Content);
        Assert.DoesNotContain(bed.Messages.Rows, static r => r.Content.Contains("半截", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Retry_上一轮成功_报invalid_params()
    {
        using var bed = new AgentTestBed(new RecordingChatClient());
        await bed.SeedWorkSessionAsync("ses_ok");

        await bed.Service.SendAndWaitAsync(new TurnSendRequest("在吗", SessionId: "ses_ok"));
        var error = await Assert.ThrowsAsync<RpcException>(() => bed.Service.RetryAsync("ses_ok"));

        Assert.Equal(RpcErrors.InvalidParams, error.Code);
    }

    [Fact]
    public async Task Retry_没有上一轮_报invalid_params()
    {
        using var bed = new AgentTestBed(new RecordingChatClient());
        await bed.SeedWorkSessionAsync("ses_empty");

        var error = await Assert.ThrowsAsync<RpcException>(() => bed.Service.RetryAsync("ses_empty"));

        Assert.Equal(RpcErrors.InvalidParams, error.Code);
    }

    [Fact]
    public async Task Retry_RP_被拒绝()
    {
        using var bed = new AgentTestBed(new RecordingChatClient());
        var error = await Assert.ThrowsAsync<RpcException>(() => bed.Service.RetryAsync(RpLane));

        Assert.Equal(RpcErrors.InvalidParams, error.Code);
    }

    [Fact]
    public async Task SendAsync_WhileBusy_MergesMessages_IntoOneFollowUpTurn()
    {
        var client = new GatedChatClient();
        using var bed = new AgentTestBed(client);

        await bed.Service.SendAsync(new TurnSendRequest("第一条", LaneId: RpLane));
        await bed.Sink.WaitForAsync(e => e.EventName == RpcEvents.TurnDelta);

        // 她正忙：这两条不各排一轮，合并进待跑槽（一次发送）
        await bed.Service.SendAsync(new TurnSendRequest("第二条", LaneId: RpLane));
        await bed.Service.SendAsync(new TurnSendRequest("第三条", LaneId: RpLane));

        client.Gate.Release();

        await WaitUntilAsync(
            () => bed.Sink.Payloads<TurnCompletedEvent>(RpcEvents.TurnCompleted).Count == 2);

        Assert.Equal(2, client.Calls);
        Assert.Contains("第二条", client.UserTexts[1], StringComparison.Ordinal);
        Assert.Contains("第三条", client.UserTexts[1], StringComparison.Ordinal);
        Assert.Equal(2, bed.Sink.Payloads<TurnStartedEvent>(RpcEvents.TurnStarted).Count);
    }

    [Fact]
    public async Task CancelAsync_DiscardsMergedPendingTurn()
    {
        var client = new GatedChatClient();
        using var bed = new AgentTestBed(client);

        await bed.Service.SendAsync(new TurnSendRequest("第一条", LaneId: RpLane));
        await bed.Sink.WaitForAsync(e => e.EventName == RpcEvents.TurnDelta);
        await bed.Service.SendAsync(new TurnSendRequest("第二条", LaneId: RpLane));

        await bed.Service.CancelAsync(RpLane, turnId: null);

        await WaitUntilAsync(
            () => bed.Sink.Payloads<TurnFailedEvent>(RpcEvents.TurnFailed).Count == 2);
        Assert.All(
            bed.Sink.Payloads<TurnFailedEvent>(RpcEvents.TurnFailed),
            failed => Assert.Equal(TurnFailureCodes.Cancelled, failed.Code));

        // 待跑槽已被丢掉：放行也不会再跑第二轮
        client.Gate.Release();
        await Task.Delay(100);
        Assert.Equal(1, client.Calls);
    }

    [Fact]
    public async Task SendAndWait_HealsStaleApprovalResidueAndDanglingToolCall()
    {
        using var bed = new AgentTestBed(new RecordingChatClient());
        await bed.SeedWorkSessionAsync("ses_poison");

        var session = await bed.Cache.GetAsync("ses_poison");
        var history = bed.WorkFactory.GetHistory(session).ToList();
        history.Add(new ChatMessage(ChatRole.Assistant,
        [
            new FunctionCallContent("call_stale", "terminal",
                new Dictionary<string, object?> { ["command"] = "sleep 10" }),
        ]));
        bed.WorkFactory.SetHistory(session, history);
        session.StateBag.SetValue("_pendingApprovalRequests", "stale");
        session.StateBag.SetValue("_autoApprovedFunctionCalls", "stale");

        var outcome = await bed.Service.SendAndWaitAsync(
            new TurnSendRequest("继续", SessionId: "ses_poison"));

        Assert.True(outcome.Succeeded);
        Assert.Null(session.StateBag.GetValue<string>("_pendingApprovalRequests"));
        Assert.Null(session.StateBag.GetValue<string>("_autoApprovedFunctionCalls"));
        Assert.Contains(
            bed.WorkFactory.GetHistory(session).SelectMany(static m => m.Contents).OfType<FunctionResultContent>(),
            result => result.CallId == "call_stale");
    }

    [Fact]
    public async Task WorkRunner_SoftInterrupt_StopsAtStepBoundary()
    {
        var client = new ToolThenTextClient();
        using var bed = new AgentTestBed(client);
        await bed.SeedWorkSessionAsync();

        var tool = AIFunctionFactory.Create((string command) => "ok", "shell_run");
        var options = new ChatOptions { Tools = [tool] };
        var runner = new WorkTurnRunner(bed.WorkAgent, bed.WorkFactory, bed.Cache);

        var result = await runner.RunStreamingAsync(
            "ses_1",
            "mattermost:work",
            new ChatMessage(ChatRole.User, "跑一下命令"),
            TurnObserver.None,
            options,
            approvalHandler: null,
            softInterrupt: static () => true);

        // 工具这一步回齐后软停：不该再发起第二次模型调用
        Assert.Equal(1, client.Calls);
        Assert.DoesNotContain("完成", result.Text, StringComparison.Ordinal);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
            {
                throw new TimeoutException("等待条件超时");
            }

            await Task.Delay(20);
        }
    }

    /// <summary>第一轮吐半句后挂住，放行后才收尾；用来模拟「她正忙」。</summary>
    private sealed class GatedChatClient : IChatClient
    {
        private int _calls;

        public SemaphoreSlim Gate { get; } = new(0);

        public int Calls => _calls;

        public List<string> UserTexts { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref _calls);
            lock (UserTexts)
            {
                UserTexts.Add(messages.LastOrDefault(static m => m.Role == ChatRole.User)?.Text ?? string.Empty);
            }

            if (call == 1)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, "先说着");
                await Gate.WaitAsync(cancellationToken);
            }

            yield return new ChatResponseUpdate(ChatRole.Assistant, "应答");
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceType == typeof(IChatClient) ? this : null;

        public void Dispose() => Gate.Dispose();
    }

    /// <summary>先调工具，工具结果回齐后再回正文；用来验证步边界软打断。</summary>
    private sealed class ToolThenTextClient : IChatClient
    {
        private int _calls;

        public int Calls => _calls;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant,
                [
                    new FunctionCallContent("call-1", "shell_run",
                        new Dictionary<string, object?> { ["command"] = "echo hi" }),
                ]);
            }
            else
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, "完成");
            }

            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceType == typeof(IChatClient) ? this : null;

        public void Dispose()
        {
        }
    }

    /// <summary>第一次流式调用抛错（模拟模型端点失败），之后正常应答。</summary>
    private sealed class FailOnceChatClient : IChatClient    {
        private int _calls;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "应答")));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                // 先说半句再炸：历史里会留下「用户消息 + 半截回复」，重试必须把这段退回
                yield return new ChatResponseUpdate(ChatRole.Assistant, "半截");
                throw new InvalidOperationException("模型端点不可用（测试）");
            }

            yield return new ChatResponseUpdate(ChatRole.Assistant, "应答");
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceType == typeof(IChatClient) ? this : null;

        public void Dispose()
        {
        }
    }

    /// <summary>在轮次里登记一个出站附件，然后正常回一句话（模拟 voice_speak/image_generate）。</summary>
    private sealed class OutboundAttachmentClient(MessageAttachment attachment) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Shoyee.Tools.Media.OutboundScope.Current?.Add(attachment);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "给你")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Shoyee.Tools.Media.OutboundScope.Current?.Add(attachment);
            yield return new ChatResponseUpdate(ChatRole.Assistant, "给你");
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceType == typeof(IChatClient) ? this : null;

        public void Dispose()
        {
        }
    }

    private sealed class FakeAnalyzer(string text) : Shoyee.Agent.Media.IAttachmentAnalyzer
    {
        public Task<IReadOnlyList<Shoyee.Agent.Media.AttachmentAnalysis>> AnalyzeAsync(
            IReadOnlyList<MessageAttachment> attachments,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Shoyee.Agent.Media.AttachmentAnalysis>>(
                attachments.Select(a => new Shoyee.Agent.Media.AttachmentAnalysis(a.Path, text)).ToArray());
    }
}
