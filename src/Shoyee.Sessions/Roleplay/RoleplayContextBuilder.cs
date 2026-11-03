using Microsoft.Extensions.AI;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Text;
using Shoyee.Sessions.Persona;
using Shoyee.Storage.Abstractions;

namespace Shoyee.Sessions.Roleplay;

/// <summary>
/// RP 每轮的上下文组装（五区，顺序固定）：
/// 背景区（<see cref="PromptAssembler.BuildRoleplayBackground"/>）→ 摘要区（全局一份）
/// → 系统注入区（调用方给的动态段）→ 最近 <see cref="RecentRoundWindow"/> 轮完整过程 → 本轮输入。
///
/// RP 只有一条跨 Lane 的连续时间线：窗口按全局轮次号取，不按 Lane 过滤。
/// 渠道切换时在段首打一个 <c>&lt;Channel&gt;</c> 标记，让模型知道这段话从哪个入口来；
/// 本轮输入始终带当前渠道。标记只在组装时注入，不写回存储（原文保持干净）。
///
/// 不序列化 session：每轮从 <see cref="IRoundRepository"/> 的窗口重建；裁剪只影响读数，行不删。
/// 情绪/检索/cron 这类动态段由调用方以 <c>systemInjections</c> 传入，本类不查它们，避免和时间/检索模块耦合。
/// </summary>
public sealed class RoleplayContextBuilder
{
    /// <summary>上下文里保留的完整轮数。</summary>
    public const int RecentRoundWindow = 50;

    private readonly PersonaFiles _personaFiles;
    private readonly IRoundRepository _rounds;
    private readonly ISummaryRepository _summaries;
    private readonly ILaneRepository _lanes;

    /// <summary>创建组装器。</summary>
    /// <param name="personaFiles">工作区人设文件（每轮实时读取）。</param>
    /// <param name="rounds">轮次仓库（RP 全局历史真源）。</param>
    /// <param name="summaries">全局摘要仓库。</param>
    /// <param name="lanes">Lane 仓库（取展示名做渠道标记）。</param>
    public RoleplayContextBuilder(
        PersonaFiles personaFiles,
        IRoundRepository rounds,
        ISummaryRepository summaries,
        ILaneRepository lanes)
    {
        ArgumentNullException.ThrowIfNull(personaFiles);
        ArgumentNullException.ThrowIfNull(rounds);
        ArgumentNullException.ThrowIfNull(summaries);
        ArgumentNullException.ThrowIfNull(lanes);
        _personaFiles = personaFiles;
        _rounds = rounds;
        _summaries = summaries;
        _lanes = lanes;
    }

    /// <summary>组装一轮 RP 上下文。</summary>
    /// <param name="laneId">本轮来源 Lane（决定输入消息的渠道标记）。</param>
    /// <param name="userMessage">用户本轮输入原文。</param>
    /// <param name="now">消息时间（调用方传本地时间，带时区）。</param>
    /// <param name="systemInjections">动态系统段（状态、检索、cron 回执……），空的整条跳过。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<RoleplayContext> BuildAsync(
        string laneId,
        string userMessage,
        DateTimeOffset now,
        IReadOnlyList<string>? systemInjections = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(laneId);
        ArgumentNullException.ThrowIfNull(userMessage);

        var messages = new List<ChatMessage>();

        // 1. 背景区：人设文件每轮重读，改动下一轮生效
        messages.AddRange(PromptAssembler.BuildRoleplayBackground(_personaFiles.Load(Mode.Roleplay)));

        // 2. 摘要区：全局一份累积摘要（不再按 Lane 合并）
        var summary = await _summaries.GetLatestAsync(cancellationToken).ConfigureAwait(false);
        if (summary is not null && !string.IsNullOrWhiteSpace(summary.Content))
        {
            messages.Add(new ChatMessage(
                ChatRole.User,
                $"<Summary>\n{summary.Content.TrimEnd()}\n</Summary>"));
        }

        // 3. 系统注入区
        if (systemInjections is { Count: > 0 })
        {
            foreach (var injection in systemInjections)
            {
                if (!string.IsNullOrWhiteSpace(injection))
                {
                    messages.Add(new ChatMessage(ChatRole.System, injection));
                }
            }
        }

        // 4. 最近 N 轮完整过程（全局时间线，含 tool_call / tool_result，按轮次号升序）
        var labels = await LoadLaneLabelsAsync(cancellationToken).ConfigureAwait(false);
        var rounds = (await _rounds.GetRecentAsync(RecentRoundWindow, cancellationToken).ConfigureAwait(false))
            .Select(static round => round with { Response = SanitizeResponse(round.Response) })
            .ToList();
        string? previousLane = null;
        foreach (var round in rounds)
        {
            if (string.Equals(round.LaneId, previousLane, StringComparison.Ordinal))
            {
                messages.AddRange(round.Request);
                messages.AddRange(round.Response);
                continue;
            }

            AppendRoundWithChannelMarker(messages, round, LabelOf(labels, round.LaneId));
            previousLane = round.LaneId;
        }

        // 5. 本轮输入：发给模型的带当前渠道；落库的只带时间（渠道在组装时按切换注入）
        var input = BuildInputMessage(userMessage, now, LabelOf(labels, laneId));
        var storedInput = BuildInputMessage(userMessage, now);
        messages.Add(input);

        return new RoleplayContext(messages, input, storedInput);
    }

    /// <summary>
    /// 历史里的助手正文在进上下文前剥掉 &lt;InnerVoice&gt; 自检块。
    /// 存储保留原文（<c>rounds</c> 是真源），但把自检再喂回模型会强化这个旧习惯。
    /// </summary>
    private static IReadOnlyList<ChatMessage> SanitizeResponse(IReadOnlyList<ChatMessage> response)
    {
        var result = new List<ChatMessage>(response.Count);
        foreach (var message in response)
        {
            if (message.Role != ChatRole.Assistant)
            {
                result.Add(message);
                continue;
            }

            List<AIContent> contents = [];
            foreach (var content in message.Contents)
            {
                if (content is TextContent text)
                {
                    var cleaned = MessageText.CleanReply(text.Text);
                    if (!string.IsNullOrEmpty(cleaned))
                    {
                        contents.Add(new TextContent(cleaned));
                    }
                }
                else
                {
                    contents.Add(content);
                }
            }

            if (contents.Count == 0)
            {
                continue;
            }

            result.Add(new ChatMessage(message.Role, contents)
            {
                AuthorName = message.AuthorName,
                MessageId = message.MessageId,
                AdditionalProperties = message.AdditionalProperties,
            });
        }

        return result;
    }

    /// <summary>本轮输入消息：时间标签 + 当前渠道 + 原文。</summary>
    /// <param name="userMessage">用户原文。</param>
    /// <param name="now">消息时间（本地时间）。</param>
    /// <param name="channelLabel">渠道展示名；null = 不打渠道标记。</param>
    public static ChatMessage BuildInputMessage(string userMessage, DateTimeOffset now, string? channelLabel = null)
    {
        ArgumentNullException.ThrowIfNull(userMessage);
        var channel = string.IsNullOrWhiteSpace(channelLabel)
            ? string.Empty
            : $"<Channel>[渠道: {channelLabel}]</Channel>";
        return new ChatMessage(
            ChatRole.User,
            $"<Time>[消息时间: {now:yyyy-MM-dd(ddd) HH:mm zzz}]</Time>{channel}{userMessage}");
    }

    private async Task<Dictionary<string, string>> LoadLaneLabelsAsync(CancellationToken cancellationToken)
    {
        var lanes = await _lanes.ListAsync(Mode.Roleplay, cancellationToken).ConfigureAwait(false);
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var lane in lanes)
        {
            labels[lane.Id] = string.IsNullOrWhiteSpace(lane.DisplayName) ? lane.Id : lane.DisplayName;
        }

        return labels;
    }

    private static string LabelOf(IReadOnlyDictionary<string, string> labels, string laneId)
        => labels.TryGetValue(laneId, out var label) ? label : laneId;

    /// <summary>在段首那条 user 文本前打渠道标记；打不上（非纯文本）就单独插一条标记。</summary>
    private static void AppendRoundWithChannelMarker(
        List<ChatMessage> messages,
        RoundRecord round,
        string label)
    {
        var marker = $"<Channel>[渠道: {label}]</Channel>";
        // 历史里已经带渠道标记（或旧数据）就不再重复插
        var marked = round.Request.Any(HasChannelTag);
        foreach (var message in round.Request)
        {
            if (!marked && TryPrefixText(message, marker, out var prefixed))
            {
                messages.Add(prefixed);
                marked = true;
            }
            else
            {
                messages.Add(message);
            }
        }

        if (!marked)
        {
            messages.Add(new ChatMessage(ChatRole.User, marker));
        }

        messages.AddRange(round.Response);
    }

    private static bool HasChannelTag(ChatMessage message)
        => message.Contents.Any(static content =>
            content is TextContent text
            && text.Text.Contains("<Channel", StringComparison.Ordinal));

    private static bool TryPrefixText(ChatMessage message, string prefix, out ChatMessage result)
    {
        if (message.Role == ChatRole.User
            && message.Contents.Count == 1
            && message.Contents[0] is TextContent text
            && !text.Text.Contains("<Channel", StringComparison.Ordinal))
        {
            result = new ChatMessage(ChatRole.User, prefix + text.Text);
            return true;
        }

        result = message;
        return false;
    }
}
