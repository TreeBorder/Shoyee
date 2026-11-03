using System.Text;
using Microsoft.Extensions.AI;
using Shoyee.Contracts.Domain;
using Shoyee.Sessions.Persona;
using Shoyee.Storage.Abstractions;

namespace Shoyee.Sessions.Summary;

/// <summary>
/// RP 摘要：每 <see cref="SummarizeEveryRounds"/> 轮触发一次，输出**累积全文**（不是增量），
/// 落 <c>summaries</c>（一行新记录，取最新即当前摘要）。
///
/// RP 只有一条跨 Lane 的时间线，所以摘要也是全局一份：轮次号按全局计数，
/// 不再区分「本 Lane / 其他 Lane」。摘要模型由调用方以委托传入——
/// 本类不建 chat client、不决定模型档位。
/// </summary>
public sealed class RoleplaySummarizer
{
    /// <summary>摘要触发间隔（轮）。</summary>
    public const int SummarizeEveryRounds = 30;

    /// <summary>摘要时附带的已覆盖原始消息条数（冗余参考）。</summary>
    public const int RedundantMessageCount = 60;

    /// <summary>摘要用的 system 提示词。</summary>
    public const string SystemPrompt =
        """
        你是一个对话摘要生成器，负责为角色扮演对话生成并维护摘要。

        输入包含：
        - SOUL / WORLD / MEMORY / USER —— 角色设定、世界观、长期记忆、用户画像，用来判断什么信息重要
        - 历史摘要 —— 此前累积的摘要（可能包含很多轮的要点）
        - 冗余历史消息 —— 已经纳入摘要的原始对话，仅供对照
        - 待纳入的消息 —— 本次新增的对话

        你的任务是输出一份**完整的更新版摘要**，而不是只输出新增部分。

        要求：
        1. 参考角色设定和世界观，判断哪些内容对角色关系和剧情发展重要
        2. 必须保留历史摘要中的关键信息（可以浓缩，不能丢弃）
        3. 把新对话的要点、事件、决定、状态变化整合进去
        4. 简洁连贯，不要逐句抄录原文
        5. 用中文，第三人称概述
        6. 只输出摘要文本，不要标签包裹，不要解释
        """;

    private readonly PersonaFiles _personaFiles;
    private readonly IRoundRepository _rounds;
    private readonly ISummaryRepository _summaries;

    /// <summary>创建摘要器。</summary>
    /// <param name="personaFiles">人设文件（摘要时读取 SOUL/WORLD/MEMORY/USER 作背景）。</param>
    /// <param name="rounds">轮次仓库。</param>
    /// <param name="summaries">摘要仓库。</param>
    public RoleplaySummarizer(
        PersonaFiles personaFiles,
        IRoundRepository rounds,
        ISummaryRepository summaries)
    {
        ArgumentNullException.ThrowIfNull(personaFiles);
        ArgumentNullException.ThrowIfNull(rounds);
        ArgumentNullException.ThrowIfNull(summaries);
        _personaFiles = personaFiles;
        _rounds = rounds;
        _summaries = summaries;
    }

    /// <summary>轮次号是否到了摘要点。</summary>
    /// <param name="maxRoundNo">当前全局最大轮次号。</param>
    public static bool ShouldSummarize(int maxRoundNo)
        => maxRoundNo > 0 && maxRoundNo % SummarizeEveryRounds == 0;

    /// <summary>拼摘要请求；没到触发点/已经覆盖过/没有新轮次时返回 null。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<RoleplaySummaryRequest?> TryBuildRequestAsync(
        CancellationToken cancellationToken = default)
    {
        var maxRoundNo = await _rounds.GetMaxRoundNoAsync(cancellationToken).ConfigureAwait(false);
        if (!ShouldSummarize(maxRoundNo))
        {
            return null;
        }

        var latest = await _summaries.GetLatestAsync(cancellationToken).ConfigureAwait(false);
        if (latest is not null && latest.ThroughRound >= maxRoundNo)
        {
            return null;
        }

        var throughRound = latest?.ThroughRound ?? 0;
        var newRounds = (await _rounds.GetAfterAsync(throughRound, SummarizeEveryRounds, cancellationToken)
                .ConfigureAwait(false))
            .Where(round => round.RoundNo <= maxRoundNo)
            .ToList();
        if (newRounds.Count == 0)
        {
            return null;
        }

        // 冗余参考：已覆盖轮次里最近 60 条可见消息（不把工具调用/结果喂给摘要模型）
        var recent = await _rounds.GetRecentAsync(50, cancellationToken).ConfigureAwait(false);
        var redundantRounds = recent.Where(round => round.RoundNo <= throughRound).ToList();

        var prompt = BuildPrompt(latest?.Content, redundantRounds, newRounds);
        return new RoleplaySummaryRequest(prompt, maxRoundNo, newRounds.Count);
    }

    /// <summary>触发摘要：拼 prompt、调调用方的模型委托、追加一条累积摘要。</summary>
    /// <param name="generate">摘要模型调用（参数为 user prompt，返回纯文本摘要）。</param>
    /// <param name="now">摘要时间（默认本地当前时间）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<SummaryRecord?> TrySummarizeAsync(
        Func<string, CancellationToken, Task<string>> generate,
        DateTimeOffset? now = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(generate);

        var request = await TryBuildRequestAsync(cancellationToken).ConfigureAwait(false);
        if (request is null)
        {
            return null;
        }

        var text = (await generate(request.Prompt, cancellationToken).ConfigureAwait(false))?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var at = now ?? DateTimeOffset.Now;
        var content = $"{text}\n\n[SummaryUpdated]{at:yyyy-MM-dd HH:mm zzz}[/SummaryUpdated]";
        return await _summaries.AppendAsync(
                request.ThroughRound,
                request.RoundCount,
                content,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private string BuildPrompt(
        string? currentSummary,
        IReadOnlyList<RoundRecord> redundantRounds,
        IReadOnlyList<RoundRecord> newRounds)
    {
        var builder = new StringBuilder();

        var persona = _personaFiles.Load(Mode.Roleplay);
        AppendSection(builder, "SOUL", persona.Soul);
        AppendSection(builder, "WORLD", persona.World);
        AppendSection(builder, "MEMORY", persona.Memory);
        AppendSection(builder, "USER", persona.User);

        if (!string.IsNullOrWhiteSpace(currentSummary))
        {
            builder.AppendLine("## 历史摘要（已涵盖之前全部对话）");
            builder.AppendLine(currentSummary.TrimEnd());
            builder.AppendLine();
        }

        var redundant = Flatten(redundantRounds, RedundantMessageCount);
        if (redundant.Count > 0)
        {
            builder.AppendLine("## 冗余历史消息（已纳入摘要的原始对话，供参考）");
            foreach (var line in redundant)
            {
                builder.AppendLine(line);
            }

            builder.AppendLine();
        }

        builder.AppendLine("## 还未摘要的消息（待纳入摘要）");
        foreach (var line in Flatten(newRounds, int.MaxValue))
        {
            builder.AppendLine(line);
        }

        return builder.ToString();
    }

    private static void AppendSection(StringBuilder builder, string name, string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        builder.Append("## ").AppendLine(name);
        builder.AppendLine(content.TrimEnd());
        builder.AppendLine();
    }

    /// <summary>把轮次摊平成「[时间][角色] 正文」，只保留可见文本，取最后 <paramref name="limit"/> 条。</summary>
    private static IReadOnlyList<string> Flatten(IReadOnlyList<RoundRecord> rounds, int limit)
    {
        var lines = new List<string>();
        foreach (var round in rounds)
        {
            foreach (var message in round.Request.Concat(round.Response))
            {
                var text = StripTags(message.Text);
                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                var role = message.Role == ChatRole.User ? "用户" : "AI";
                lines.Add($"[{round.CreatedAt.ToLocalTime():MM-dd HH:mm}][{role}] {text}");
            }
        }

        return limit >= lines.Count || limit <= 0
            ? lines
            : lines.Skip(lines.Count - limit).ToList();
    }

    private static string StripTags(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", string.Empty).Trim();
    }
}
