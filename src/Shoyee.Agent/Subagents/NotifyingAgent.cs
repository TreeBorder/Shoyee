using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shoyee.Contracts.Abstractions;

namespace Shoyee.Agent.Subagents;

/// <summary>
/// 子智能体终态回推：跑完/失败各推一条纯通知（<see cref="INotificationPusher"/>），
/// 不回聊天会话、不双发。
/// </summary>
/// <remarks>
/// 异步模式下父 agent 派完活就回，结果可能很久之后才出来——没有这条推送，只有用户再开口时
/// 她才会去 <c>background_agents_get_*</c> 查。推送失败只记日志，绝不能影响子任务本身的成功/失败。
/// 取消不推（多半是 daemon 停机或父轮次被打断，不是用户想知道的结论）。
/// </remarks>
public sealed class NotifyingAgent : DelegatingAIAgent
{
    /// <summary>通知正文上限（Server酱一条不宜太长）。</summary>
    private const int BodyCap = 2_000;

    private readonly INotificationPusher _pusher;
    private readonly ILogger _logger;

    /// <summary>创建回推包装。</summary>
    /// <param name="innerAgent">真正的子智能体。</param>
    /// <param name="pusher">通知推送实现。</param>
    /// <param name="logger">日志（可空）。</param>
    public NotifyingAgent(AIAgent innerAgent, INotificationPusher pusher, ILogger? logger = null)
        : base(innerAgent)
    {
        ArgumentNullException.ThrowIfNull(pusher);
        _pusher = pusher;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <inheritdoc />
    protected override async Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await base.RunCoreAsync(messages, session, options, cancellationToken).ConfigureAwait(false);
            await NotifyAsync("完成", response.Text).ConfigureAwait(false);
            return response;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await NotifyAsync("失败", ex.Message).ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var text = new StringBuilder();
        var source = base.RunCoreStreamingAsync(messages, session, options, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        try
        {
            while (true)
            {
                AgentResponseUpdate current;
                try
                {
                    if (!await source.MoveNextAsync().ConfigureAwait(false))
                    {
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    await NotifyAsync("失败", ex.Message).ConfigureAwait(false);
                    throw;
                }

                current = source.Current;
                if (!string.IsNullOrEmpty(current.Text))
                {
                    text.Append(current.Text);
                }

                yield return current;
            }
        }
        finally
        {
            await source.DisposeAsync().ConfigureAwait(false);
        }

        await NotifyAsync("完成", text.ToString()).ConfigureAwait(false);
    }

    private async Task NotifyAsync(string outcome, string text)
    {
        var body = string.IsNullOrWhiteSpace(text) ? "（没有输出）" : text.Trim();
        if (body.Length > BodyCap)
        {
            body = body[..BodyCap] + "…";
        }

        try
        {
            await _pusher.PushAsync($"{Name} 子任务{outcome}", body, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 推送是旁路：脚本缺失/超时只记日志，不影响任务本身的结果
            _logger.LogWarning(ex, "子智能体终态通知推送失败：{Agent}", Name);
        }
    }
}
