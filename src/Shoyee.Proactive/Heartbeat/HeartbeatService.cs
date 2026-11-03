using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Options;
using Shoyee.Contracts.Text;
using Shoyee.Storage.Abstractions;

namespace Shoyee.Proactive.Heartbeat;

/// <summary>一次心跳跑完的结果（日志与测试用）。</summary>
/// <param name="Zone">判定分区。</param>
/// <param name="Triggered">是否真的发了消息。</param>
/// <param name="LaneId">选中的 Lane（没触发/没有目标时为 null）。</param>
/// <param name="Text">实际要送达的正文（投递前；桌面事件走的也是这段）。</param>
/// <param name="Delivered">是否已通过 IM 台账投递（桌面在线时为 false，走事件）。</param>
public sealed record HeartbeatRunResult(
    HeartbeatZone Zone,
    bool Triggered,
    string? LaneId = null,
    string? Text = null,
    bool Delivered = false);

/// <summary>
/// 心跳：判定 → 涨/扣思念值 → 选 Lane → 注入一条「系统消息」跑真轮次 → IM 补投递。
///
/// 桌面在线时消息靠 <c>turn.*</c> 事件到桌面，不需要再发一次；桌面离线时轮次落在
/// 最近的 RP IM Lane 上，轮末用投递台账把正文回贴到该 Lane 的 <c>last_target</c>。
/// 参数与判定规则见 <see cref="HeartbeatPolicy"/>。
/// </summary>
public sealed class HeartbeatService
{
    /// <summary>思念值状态的说明（与 <c>StateDefaults</c> 保持一致）。</summary>
    private const string LongingDescription = "0–100 的整数；只由互动间隔驱动，影响心跳频率与强度";

    private readonly ILaneRepository _lanes;
    private readonly IStateRepository _states;
    private readonly HeartbeatTargetSelector _targets;
    private readonly ITurnService _turns;
    private readonly IOutboundDelivery _delivery;
    private readonly HeartbeatPromptBook _prompts;
    private readonly WorkspaceConfig _config;
    private readonly TimeProvider _clock;
    private readonly Func<int, int> _next;
    private readonly ILogger<HeartbeatService> _logger;

    /// <summary>创建心跳服务。</summary>
    /// <param name="lanes">Lane 仓库。</param>
    /// <param name="states">状态仓库（思念值）。</param>
    /// <param name="targets">目标 Lane 选择器。</param>
    /// <param name="turns">轮次服务（注入消息并等它跑完）。</param>
    /// <param name="delivery">IM 投递（带台账）。</param>
    /// <param name="prompts">提示词文件读器。</param>
    /// <param name="config">工作区配置（取心跳参数）。</param>
    /// <param name="clock">时间源（测试可注入）。</param>
    /// <param name="next">随机源（取 <c>[0, max)</c>；测试可注入固定序列）。</param>
    /// <param name="logger">日志。</param>
    public HeartbeatService(
        ILaneRepository lanes,
        IStateRepository states,
        HeartbeatTargetSelector targets,
        ITurnService turns,
        IOutboundDelivery delivery,
        HeartbeatPromptBook prompts,
        WorkspaceConfig config,
        TimeProvider? clock = null,
        Func<int, int>? next = null,
        ILogger<HeartbeatService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(lanes);
        ArgumentNullException.ThrowIfNull(states);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(turns);
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentNullException.ThrowIfNull(prompts);
        ArgumentNullException.ThrowIfNull(config);
        _lanes = lanes;
        _states = states;
        _targets = targets;
        _turns = turns;
        _delivery = delivery;
        _prompts = prompts;
        _config = config;
        _clock = clock ?? TimeProvider.System;
        _next = next ?? Random.Shared.Next;
        _logger = logger ?? NullLogger<HeartbeatService>.Instance;
    }

    /// <summary>跑一次心跳判定（调度器与手动触发共用）。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<HeartbeatRunResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var options = _config.Proactive.Heartbeat;
        var now = _clock.GetUtcNow();

        var state = await _states.GetAsync("longing", cancellationToken).ConfigureAwait(false);
        var longing = Math.Clamp(ParseLonging(state?.Value), 0, Math.Max(1, options.LongingCap));
        var idleMinutes = await GetIdleMinutesAsync(now, cancellationToken).ConfigureAwait(false);

        var decision = HeartbeatPolicy.Decide(
            TimeOnly.FromDateTime(_clock.GetLocalNow().DateTime),
            idleMinutes,
            longing,
            options,
            _next);

        if (decision.Zone is HeartbeatZone.Disabled)
        {
            return new HeartbeatRunResult(decision.Zone, false);
        }

        await SaveLongingAsync(state, decision.Longing, now, cancellationToken).ConfigureAwait(false);

        if (!decision.Triggered)
        {
            _logger.LogInformation(
                "心跳：{Zone}（闲置 {Idle} 分钟，思念 {Longing}/{Cap}）",
                decision.Zone,
                idleMinutes,
                decision.Longing,
                options.LongingCap);
            return new HeartbeatRunResult(decision.Zone, false);
        }

        var lane = await _targets.SelectAsync(cancellationToken).ConfigureAwait(false);
        if (lane is null)
        {
            _logger.LogInformation("心跳触发，但桌面不在线且没有可投递的 RP IM Lane，这轮不发");
            return new HeartbeatRunResult(decision.Zone, false);
        }

        var instruction = BuildInstruction(decision, longing, idleMinutes);
        var outcome = await _turns
            .SendAndWaitAsync(new TurnSendRequest(instruction, LaneId: lane.Id, SystemInput: true), cancellationToken)
            .ConfigureAwait(false);

        var text = MessageText.CleanReply(outcome.Text);
        if (!outcome.Succeeded
            || (string.IsNullOrWhiteSpace(text) && outcome.Attachments is not { Count: > 0 }))
        {
            _logger.LogWarning(
                "心跳轮次没有产出可投递的正文或附件（{LaneId}，succeeded={Succeeded}）",
                lane.Id,
                outcome.Succeeded);
            return new HeartbeatRunResult(decision.Zone, false, lane.Id);
        }

        var delivered = await DeliverToChannelAsync(lane, text, outcome.Attachments, cancellationToken)
            .ConfigureAwait(false);
        _logger.LogInformation(
            "心跳已发出：{LaneId}（思念 {Longing}/{Cap}，档位 l{Level}，{Length} 字，IM 投递={Delivered}）",
            lane.Id,
            decision.Longing,
            options.LongingCap,
            decision.Level + 1,
            text.Length,
            delivered);

        return new HeartbeatRunResult(decision.Zone, true, lane.Id, text, delivered);
    }

    private async Task<int> GetIdleMinutesAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var last = await _lanes.GetLastInteractionAsync(cancellationToken).ConfigureAwait(false);
        if (last is null)
        {
            return int.MaxValue;
        }

        return (int)Math.Max(0, (now - last.Value).TotalMinutes);
    }

    private async Task<bool> DeliverToChannelAsync(
        LaneRecord lane,
        string text,
        IReadOnlyList<MessageAttachment>? attachments,
        CancellationToken cancellationToken)
    {
        // 桌面 Lane 只靠事件；回投地址是 IM 才有的东西
        if (lane.Channel == LaneChannel.Desktop || string.IsNullOrEmpty(lane.LastTarget))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            await _delivery
                .SendTextAsync(lane.Id, null, lane.LastTarget, text, "heartbeat", cancellationToken)
                .ConfigureAwait(false);
        }

        if (attachments is { Count: > 0 })
        {
            foreach (var attachment in attachments)
            {
                await _delivery
                    .SendFileAsync(
                        lane.Id,
                        null,
                        lane.LastTarget,
                        attachment.Path,
                        caption: null,
                        kind: "heartbeat",
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        return true;
    }

    private async Task SaveLongingAsync(
        StateRecord? current,
        int value,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (current is not null)
        {
            if (current.Value != value.ToString(CultureInfo.InvariantCulture))
            {
                await _states.SetAsync("longing", value.ToString(CultureInfo.InvariantCulture), byAgent: false, cancellationToken)
                    .ConfigureAwait(false);
            }

            return;
        }

        // 状态行被删了：补回来（思念不是她能改的）
        await _states
            .UpsertAsync(new StateRecord("longing", "思念", value.ToString(CultureInfo.InvariantCulture), LongingDescription, false, now), cancellationToken)
            .ConfigureAwait(false);
    }

    private string BuildInstruction(HeartbeatDecision decision, int longingBefore, int idleMinutes)
    {
        var prompt = _prompts.Resolve(decision.PromptType ?? "text", decision.Level)
                     ?? "请以角色身份主动发一条消息，表达此刻的心情。";
        var idle = idleMinutes == int.MaxValue ? "很久（还没有过互动）" : FormatMinutes(idleMinutes);
        return "[系统消息]\n"
               + "现在是心跳时刻：用户已经 " + idle + " 没有联系你了，"
               + $"你的思念值 {longingBefore}/100（档位 l{decision.Level + 1}）。\n"
               + prompt + "\n"
               + "要求：以你自己的身份直接说话，不要提到系统消息、心跳或提示词，不要解释你在做什么设置；"
               + "一条自然的消息即可。\n"
               + "[/]";
    }

    private static string FormatMinutes(int minutes)
    {
        if (minutes < 60)
        {
            return $"{minutes} 分钟";
        }

        var hours = minutes / 60;
        var rest = minutes % 60;
        return rest == 0 ? $"{hours} 小时" : $"{hours} 小时 {rest} 分钟";
    }

    /// <summary>解析思念值：兼容 0–1 的旧小数（×100）与 0–100 的整数。</summary>
    public static int ParseLonging(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        var text = value.Trim();
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole))
        {
            return Math.Max(0, whole);
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var fractional))
        {
            var scaled = fractional <= 1 ? fractional * 100 : fractional;
            return (int)Math.Round(Math.Max(0, scaled), MidpointRounding.AwayFromZero);
        }

        return 0;
    }
}
