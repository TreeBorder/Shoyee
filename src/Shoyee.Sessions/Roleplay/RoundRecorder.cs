using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Shoyee.Contracts.Dtos;
using Shoyee.Sessions.Turns;
using Shoyee.Storage.Abstractions;

namespace Shoyee.Sessions.Roleplay;

/// <summary>
/// RP 轮次记录中间件：<see cref="AIContextProvider.StoreAIContextAsync"/> 把每次模型调用的
/// <c>ResponseMessages</c> 累积到当前 session 的 StateBag，轮末由 <see cref="CompleteRoundAsync"/> 取走并落库。
///
/// 不累积就会丢工具调用/结果，下一轮上下文残缺——这是 RP 走「每轮重建」方案必须守住的一条。
/// 落库前的悬空调用修补走 <see cref="ToolCallRepair"/>，工作模式中断时也复用它。
/// </summary>
public sealed class RoundRecorder : AIContextProvider
{
    /// <summary>累积缓冲在 StateBag 里的键。</summary>
    public const string StateKey = "Shoyee.RoundRecorder.Response";

    private readonly IRoundRepository _rounds;
    private readonly ProviderSessionState<List<ChatMessage>> _state;

    /// <summary>创建中间件。</summary>
    /// <param name="rounds">轮次仓库。</param>
    public RoundRecorder(IRoundRepository rounds)
    {
        ArgumentNullException.ThrowIfNull(rounds);
        _rounds = rounds;
        _state = new ProviderSessionState<List<ChatMessage>>(static _ => [], StateKey);
    }

    /// <inheritdoc />
    public override IReadOnlyList<string> StateKeys => [_state.StateKey];

    /// <inheritdoc />
    protected override ValueTask StoreAIContextAsync(InvokedContext context, CancellationToken cancellationToken = default)
    {
        if (context.Session is not null && context.ResponseMessages is not null)
        {
            var buffer = _state.GetOrInitializeState(context.Session);
            buffer.AddRange(context.ResponseMessages);
            _state.SaveState(context.Session, buffer);
        }

        return default;
    }

    /// <summary>取走本轮累积的响应并清空缓冲（不落库，供测试或调用方自行处理）。</summary>
    /// <param name="session">当前 session。</param>
    public IReadOnlyList<ChatMessage> TakeResponse(AgentSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var buffer = _state.GetOrInitializeState(session);
        var messages = buffer.ToArray();
        buffer.Clear();
        _state.SaveState(session, buffer);
        return messages;
    }

    /// <summary>轮末落库：修补悬空工具调用后，把「本轮输入 + 本轮响应」写成一轮。</summary>
    /// <param name="laneId">当前 Lane。</param>
    /// <param name="session">跑完本轮的 session。</param>
    /// <param name="input">本轮输入消息。</param>
    /// <param name="attachments">本轮她说出口的附件（语音/图片/文件）；挂到最后一条可见正文上。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>落库的一轮；没有响应时返回 null（不写空轮）。</returns>
    public async Task<RoundRecord?> CompleteRoundAsync(
        string laneId,
        AgentSession session,
        ChatMessage input,
        IReadOnlyList<MessageAttachment>? attachments = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(laneId);
        ArgumentNullException.ThrowIfNull(input);

        var response = ToolCallRepair.RepairDanglingToolCalls(TakeResponse(session));
        if (response.Count == 0)
        {
            return null;
        }

        TurnAttachments.AttachToLastAssistantText(response, attachments);

        return await _rounds.AppendAsync(laneId, [input], response, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }
}
