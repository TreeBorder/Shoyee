using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Shoyee.Contracts.Rpc;

namespace Shoyee.Agent.Runtime;

/// <summary>一次流式回合的结果：用量 + 本轮可见正文全文（不含工具参数）。</summary>
/// <param name="Usage">模型用量。</param>
/// <param name="Text">正文全文（增量拼起来）。</param>
internal sealed record AgentStreamResult(TurnUsage Usage, string Text);

/// <summary>
/// 两个 runner 共用的流式循环：跑一轮 → 遇到待审批就交给 <see cref="ApprovalHandler"/> → 带审批结果续跑，
/// 直到模型不再要求审批为止。正文增量与工具过程通过 <see cref="TurnObserver"/> 外发；
/// 用量由 <see cref="TurnUsage"/> 累加。
///
/// 审批续跑只发「审批决定消息」，不重发原始上下文——框架的 session 里已经有本轮历史了。
/// </summary>
internal static class AgentStreamRunner
{
    /// <summary>工具没有返回文本时的占位（事件里不能留空，桌面会当成「结果没拿到」）。</summary>
    private const string NoOutput = "（无输出）";

    /// <summary>流式增量的合并窗口（毫秒）：窗口内的同一段内容合成一条事件。</summary>
    private const double FlushIntervalMs = 60;

    /// <summary>跑一轮（含任意次审批续跑）。</summary>
    /// <param name="agent">agent。</param>
    /// <param name="messages">本轮输入。</param>
    /// <param name="session">会话。</param>
    /// <param name="options">运行选项。</param>
    /// <param name="observer">增量/过程观察者。</param>
    /// <param name="approvalHandler">审批回调。</param>
    /// <param name="cancellationToken">取消令牌（硬打断）。</param>
    /// <param name="softInterrupt">
    /// 软打断查询（步边界生效）：当前这一步（思考 + 正文 + 工具结果全回齐）跑完才停，
    /// 不掐流式半句、也不在工具执行中停；纯文本回合整条说完。null = 不响应软打断。
    /// </param>
    public static async Task<AgentStreamResult> RunAsync(
        AIAgent agent,
        IReadOnlyList<ChatMessage> messages,
        AgentSession session,
        AgentRunOptions? options,
        TurnObserver observer,
        ApprovalHandler? approvalHandler,
        CancellationToken cancellationToken,
        Func<bool>? softInterrupt = null)
    {
        var usage = new TurnUsage();
        var text = new StringBuilder();
        var toolNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var batch = messages;
        var pendingCalls = 0;
        var stepComplete = false;
        var softStopped = false;

        // 模型按 token 出增量，逐条转发会把 UDS / UI 事件队列灌满（长思考时桌面直接卡死）。
        // 这里按「同一段内容 + 时间窗」合并：正文和思考各自成段，切换段落或改发工具事件前先冲刷，
        // 行序仍与逐条转发一致。合并的是**事件**，正文结果按原文累计，不受影响。
        var chunk = new StringBuilder();
        var chunkPhase = StreamPhase.None;
        var lastFlush = Stopwatch.GetTimestamp();
        var gate = new SemaphoreSlim(1, 1);

        // 调用方可能整轮都在等下一个 token（模型停顿、被取消）：光靠「下一条增量」触发冲刷，
        // 界面会停在半句话上。所以后台按固定节奏补冲一次，保证延迟有上限。
        using var tickerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        async ValueTask FlushInsideAsync()
        {
            if (chunk.Length == 0)
            {
                return;
            }

            var payload = chunk.ToString();
            var phase = chunkPhase;
            chunk.Clear();
            chunkPhase = StreamPhase.None;
            lastFlush = Stopwatch.GetTimestamp();

            if (phase == StreamPhase.Reasoning)
            {
                await observer.OnThinkingAsync(payload, CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                await observer.OnDeltaAsync(payload, CancellationToken.None).ConfigureAwait(false);
            }
        }

        async ValueTask FlushChunkAsync()
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                await FlushInsideAsync().ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }

        async ValueTask AppendChunkAsync(StreamPhase phase, string delta)
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (chunkPhase != StreamPhase.None && chunkPhase != phase)
                {
                    await FlushInsideAsync().ConfigureAwait(false);
                }

                chunkPhase = phase;
                chunk.Append(delta);
                if (Stopwatch.GetElapsedTime(lastFlush).TotalMilliseconds >= FlushIntervalMs)
                {
                    await FlushInsideAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                gate.Release();
            }
        }

        async Task TickAsync()
        {
            try
            {
                while (!tickerCts.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(FlushIntervalMs), tickerCts.Token)
                        .ConfigureAwait(false);
                    await FlushChunkAsync().ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // 轮次收尾：最后那一版交给下面的 finally 冲
            }
        }

        var ticker = Task.Run(TickAsync, CancellationToken.None);

        try
        {
            while (true)
            {
                var pending = new List<ToolApprovalRequestContent>();

                await foreach (var update in agent
                    .RunStreamingAsync(batch, session, options, cancellationToken)
                    .ConfigureAwait(false))
                {
                    stepComplete = false;

                    if (!string.IsNullOrEmpty(update.Text))
                    {
                        text.Append(update.Text);
                        await AppendChunkAsync(StreamPhase.Text, update.Text).ConfigureAwait(false);
                    }

                    foreach (var content in update.Contents)
                    {
                        switch (content)
                        {
                            case TextReasoningContent reasoning when !string.IsNullOrEmpty(reasoning.Text):
                                await AppendChunkAsync(StreamPhase.Reasoning, reasoning.Text).ConfigureAwait(false);
                                break;

                            case FunctionCallContent call:
                                // 工具过程行要和它前面的正文 / 思考保持先后顺序
                                await FlushChunkAsync().ConfigureAwait(false);
                                toolNames[call.CallId] = call.Name;
                                pendingCalls++;
                                var arguments = Clip(JsonSerializer.Serialize(call.Arguments));
                                await observer.OnToolAsync(
                                        TurnToolPhases.Start,
                                        call.Name,
                                        SummarizeArguments(call.Arguments),
                                        call.CallId,
                                        arguments,
                                        cancellationToken: CancellationToken.None)
                                    .ConfigureAwait(false);
                                break;

                            case FunctionResultContent result:
                                await FlushChunkAsync().ConfigureAwait(false);
                                var name = toolNames.TryGetValue(result.CallId, out var known) ? known : "tool";
                                pendingCalls = Math.Max(0, pendingCalls - 1);
                                if (pendingCalls == 0)
                                {
                                    // 工具结果全回齐 = 这一步（思考 + 正文 + 工具）跑完，软打断只在这里生效
                                    stepComplete = true;
                                }

                                var display = Clip(FormatResult(result.Result));
                                await observer.OnToolAsync(
                                        TurnToolPhases.End,
                                        name,
                                        null,
                                        result.CallId,
                                        // 工具返回 null / 空串是合法的（有的函数就是没有返回值），但事件里必须给一句
                                        // 文字：桌面拿「结果为空」当「结果没拿到」，会显示成「没有保存工具返回」。
                                        result: display.Trim().Length == 0 ? NoOutput : display,
                                        cancellationToken: CancellationToken.None)
                                    .ConfigureAwait(false);
                                break;

                            case ToolApprovalRequestContent request:
                                await FlushChunkAsync().ConfigureAwait(false);
                                pending.Add(request);
                                break;

                            case UsageContent reported:
                                usage.Add(reported.Details);
                                break;
                        }
                    }

                    if (stepComplete && softInterrupt?.Invoke() == true)
                    {
                        // 步边界软打断：当前步已完整跑完、工具结果全回齐，此刻停、不开下一轮。
                        // 新消息（含进程回执）已在待跑槽里，回合结束立刻续跑它。
                        softStopped = true;
                        break;
                    }
                }

                // 本轮流结束：把最后不足一个时间窗的正文 / 思考冲出去，再决定是否续跑
                await FlushChunkAsync().ConfigureAwait(false);

                if (softStopped)
                {
                    return new AgentStreamResult(usage, text.ToString());
                }

                if (pending.Count == 0)
                {
                    return new AgentStreamResult(usage, text.ToString());
                }

                var responses = approvalHandler is null
                    ? pending.Select(static r => (AIContent)r.CreateResponse(false, "没有可用的审批通道，已拒绝")).ToList()
                    : await approvalHandler(pending, cancellationToken).ConfigureAwait(false);

                if (responses.Count == 0)
                {
                    // 回调没给任何决定：不续跑，交给上层按失败处理（历史由 runner 的修补兜底）
                    return new AgentStreamResult(usage, text.ToString());
                }

                batch = [new ChatMessage(ChatRole.User, responses.ToList())];
            }
        }
        finally
        {
            await tickerCts.CancelAsync().ConfigureAwait(false);
            await ticker.ConfigureAwait(false);

            // 取消 / 异常路径也要把已经产生的半句话送出去（「已产生的内容不回滚」）
            await FlushChunkAsync().ConfigureAwait(false);
            gate.Dispose();
        }
    }

    /// <summary>过程行摘要：<c>command</c> 给裸原文，其余参数压成一行 JSON。</summary>
    private static string? SummarizeArguments(IDictionary<string, object?>? arguments)
    {
        if (arguments is null || arguments.Count == 0)
        {
            return null;
        }

        if (arguments.TryGetValue("command", out var command) && command is not null)
        {
            var text = ToText(command);
            return text.Length <= 200 ? text : text[..200] + "…";
        }

        var json = JsonSerializer.Serialize(arguments);
        return json.Length <= 200 ? json : json[..200] + "…";
    }

    /// <summary>工具返回的展示文本（与 <c>MessageView.ToolResult</c> 同源规则）。</summary>
    private static string FormatResult(object? result) => result switch
    {
        null => string.Empty,
        string text => text,
        JsonElement { ValueKind: JsonValueKind.String } element => element.GetString() ?? string.Empty,
        JsonElement element => element.GetRawText(),
        _ => JsonSerializer.Serialize(result),
    };

    private static string Clip(string text, int max = 4000)
        => text.Length <= max ? text : text[..max] + "…";

    private static string ToText(object raw) => raw switch
    {
        JsonElement { ValueKind: JsonValueKind.String } element => element.GetString() ?? string.Empty,
        string text => text,
        _ => raw.ToString() ?? string.Empty,
    };

    /// <summary>缓冲区里这段增量属于正文还是思考。</summary>
    private enum StreamPhase
    {
        /// <summary>缓冲区空。</summary>
        None,

        /// <summary>正文增量。</summary>
        Text,

        /// <summary>思考（reasoning）增量。</summary>
        Reasoning,
    }
}
