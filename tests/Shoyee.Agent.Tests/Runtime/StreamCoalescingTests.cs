using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Shoyee.Agent.Tests.TestSupport;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Options;
using Shoyee.Contracts.Rpc;
using Xunit;

namespace Shoyee.Agent.Tests.Runtime;

/// <summary>
/// 流式事件合并：模型按 token 出增量，逐条转发会把 UDS / 桌面 UI 队列灌满。
/// 合并只影响事件条数，拼起来的正文必须一字不差。
/// </summary>
public sealed class StreamCoalescingTests
{
    private const int Chunks = 300;

    [Fact]
    public async Task ManySmallChunks_AreCoalesced_WithoutLosingText()
    {
        using var bed = new AgentTestBed(new BurstChatClient(Chunks));

        await bed.Service.SendAsync(new TurnSendRequest("在吗", LaneId: LaneIds.DesktopRoleplay));
        await bed.Sink.WaitForAsync(e => e.EventName == RpcEvents.TurnCompleted);

        var deltas = bed.Sink.Payloads<TurnDeltaEvent>(RpcEvents.TurnDelta);
        var thinking = bed.Sink.Payloads<TurnThinkingEvent>(RpcEvents.TurnThinking);

        Assert.NotEmpty(deltas);
        Assert.NotEmpty(thinking);

        // 合并生效：事件数远小于逐 token 的条数
        Assert.True(deltas.Count < Chunks, $"正文事件没有合并：{deltas.Count} 条");
        Assert.True(thinking.Count < Chunks, $"思考事件没有合并：{thinking.Count} 条");

        // 内容不能丢、也不能重复
        Assert.Equal(new string('答', Chunks), string.Concat(deltas.Select(d => d.Delta)));
        Assert.Equal(new string('想', Chunks), string.Concat(thinking.Select(t => t.Delta)));

        // 段落顺序：先思考后正文
        var events = bed.Sink.Events.ToList();
        var firstThinking = events.FindIndex(e => e.EventName == RpcEvents.TurnThinking);
        var firstDelta = events.FindIndex(e => e.EventName == RpcEvents.TurnDelta);
        Assert.True(firstThinking >= 0 && firstDelta > firstThinking);
    }

    /// <summary>一口气吐很多小片段的假模型（没有网络延迟，靠合并窗口收成少量事件）。</summary>
    private sealed class BurstChatClient(int chunks) : IChatClient
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
            for (var i = 0; i < chunks && !cancellationToken.IsCancellationRequested; i++)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("想")]);
            }

            for (var i = 0; i < chunks && !cancellationToken.IsCancellationRequested; i++)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, "答");
            }

            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceType == typeof(IChatClient) ? this : null;

        public void Dispose()
        {
        }
    }
}
