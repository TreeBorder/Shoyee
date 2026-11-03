using Microsoft.Extensions.Logging;
using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Storage.Abstractions;
using Shoyee.Tools.Process;

namespace Shoyee.Agent.Process;

/// <summary>
/// 后台进程完成回执 → 原会话的一轮系统输入：像用户说了句「进程跑完了」一样把结果送回上下文，
/// 她据此决定要不要接着做。
/// </summary>
/// <remarks>
/// <para>回执用 <see cref="TurnSendRequest.SystemInput"/>（进模型上下文，但投影成
/// <see cref="MessageKind.SystemInput"/>：桌面默认隐藏，可在「系统消息」开关里查看）。</para>
/// <para>回投地址沿用会话所属 Lane 的 <c>last_target</c>：IM 会话起的进程，结果回贴到同一个 IM 会话；
/// 桌面会话不带 target（桌面上靠 <c>turn.*</c> 事件看到她的回应）。</para>
/// <para>会话已归档/不存在就丢弃（不在一个用户已经关掉的话题里自说自话），只记日志。</para>
/// <para><see cref="ITurnService"/> 用 <see cref="Lazy{T}"/> 拿：进程表在构建 agent 时就被创建，
/// 直接注入会形成 <c>TurnService → WorkTurnRunner → … → ProcessRegistry → sink → TurnService</c> 的环。</para>
/// </remarks>
public sealed class SessionProcessCompletionSink : IProcessCompletionSink
{
    private readonly Lazy<ITurnService> _turns;
    private readonly ISessionRepository _sessions;
    private readonly ILaneRepository _lanes;
    private readonly ILogger<SessionProcessCompletionSink> _logger;

    /// <summary>创建回执投递口。</summary>
    /// <param name="turns">轮次服务（懒取，避免 DI 环）。</param>
    /// <param name="sessions">会话仓库。</param>
    /// <param name="lanes">Lane 仓库（取回投地址）。</param>
    /// <param name="logger">日志。</param>
    public SessionProcessCompletionSink(
        Lazy<ITurnService> turns,
        ISessionRepository sessions,
        ILaneRepository lanes,
        ILogger<SessionProcessCompletionSink> logger)
    {
        ArgumentNullException.ThrowIfNull(turns);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(lanes);
        ArgumentNullException.ThrowIfNull(logger);
        _turns = turns;
        _sessions = sessions;
        _lanes = lanes;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task NotifyAsync(ProcessCompletion completion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completion);

        var session = await _sessions.GetAsync(completion.SessionKey, cancellationToken).ConfigureAwait(false);
        if (session is null || session.Archived)
        {
            _logger.LogInformation(
                "后台进程 {ProcessId} 完成，但原会话 {SessionId} 不存在或已归档，回执丢弃",
                completion.ProcessId,
                completion.SessionKey);
            return;
        }

        await _turns.Value.SendAsync(
            new TurnSendRequest(
                Compose(completion),
                SessionId: session.Id,
                ReplyTarget: await ResolveReplyTargetAsync(session, cancellationToken).ConfigureAwait(false),
                SystemInput: true),
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "后台进程 {ProcessId}（{Status}）的回执已投回会话 {SessionId}",
            completion.ProcessId,
            completion.Status,
            session.Id);
    }

    /// <summary>IM 会话用它最近一次入站的回投地址；桌面会话返回 null（走事件）。</summary>
    private async Task<string?> ResolveReplyTargetAsync(SessionRecord session, CancellationToken cancellationToken)
    {
        if (session.LaneId is not { Length: > 0 } laneId)
        {
            return null;
        }

        var lane = await _lanes.GetAsync(laneId, cancellationToken).ConfigureAwait(false);
        return lane is { Channel: not LaneChannel.Desktop }
            ? lane.LastTarget
            : null;
    }

    /// <summary>把回执拼成一条系统注入文本（命令、备注、退出码、输出尾部）。</summary>
    internal static string Compose(ProcessCompletion completion)
    {
        var status = completion.Status switch
        {
            "done" => "已完成",
            "failed" => "失败",
            "killed" => "已终止",
            "timeout" => "超时被回收",
            _ => completion.Status,
        };

        var builder = new System.Text.StringBuilder();
        builder.Append("[系统消息]后台进程 ").Append(completion.ProcessId)
            .Append(' ').Append(status)
            .Append("（退出码 ").Append(completion.ExitCode?.ToString() ?? "未知").Append("）。")
            .Append("\n命令：").Append(completion.Command);

        if (!string.IsNullOrWhiteSpace(completion.Note))
        {
            builder.Append("\n备注：").Append(completion.Note);
        }

        if (!string.IsNullOrWhiteSpace(completion.OutputTail))
        {
            builder.Append("\n输出尾部：\n").Append(completion.OutputTail.Trim());
        }

        builder.Append("[/]");
        return builder.ToString();
    }
}
