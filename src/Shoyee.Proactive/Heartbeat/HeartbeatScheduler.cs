using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shoyee.Contracts.Options;

namespace Shoyee.Proactive.Heartbeat;

/// <summary>
/// 心跳调度：每 <c>[MinMinutes, MaxMinutes]</c> 分钟随机跑一次判定（不是每次都发消息）。
///
/// 间隔在跑完一次后再抽下一次，节奏不会因为固定周期而显得机械；
/// 静默时段、三档闲置与触发概率都在 <see cref="HeartbeatService"/> / <see cref="HeartbeatPolicy"/> 里判定。
/// </summary>
public sealed class HeartbeatScheduler : BackgroundService
{
    private readonly HeartbeatService _heartbeat;
    private readonly HeartbeatOptions _options;
    private readonly TimeProvider _clock;
    private readonly Func<int, int> _next;
    private readonly ILogger<HeartbeatScheduler> _logger;

    /// <summary>创建调度器。</summary>
    /// <param name="heartbeat">心跳服务。</param>
    /// <param name="config">工作区配置（取间隔）。</param>
    /// <param name="clock">时间源（测试可注入）。</param>
    /// <param name="next">随机源（取 <c>[0, max)</c>）。</param>
    /// <param name="logger">日志。</param>
    public HeartbeatScheduler(
        HeartbeatService heartbeat,
        WorkspaceConfig config,
        TimeProvider? clock = null,
        Func<int, int>? next = null,
        ILogger<HeartbeatScheduler>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(heartbeat);
        ArgumentNullException.ThrowIfNull(config);
        _heartbeat = heartbeat;
        _options = config.Proactive.Heartbeat;
        _clock = clock ?? TimeProvider.System;
        _next = next ?? Random.Shared.Next;
        _logger = logger ?? NullLogger<HeartbeatScheduler>.Instance;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogDebug("心跳未启用，调度器退出");
            return;
        }

        _logger.LogInformation(
            "心跳调度启动：每 {Min}–{Max} 分钟判定一次，冷宫区 {Cooldown} 分钟，静默 {QuietStart:HH\\:mm}–{QuietEnd:HH\\:mm}",
            _options.MinMinutes,
            _options.MaxMinutes,
            _options.CooldownMinutes,
            _options.EffectiveQuietStart,
            _options.EffectiveQuietEnd);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(NextInterval(), _clock, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                await _heartbeat.ExecuteAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // 一次失败不能让调度停摆；下一轮照常
                _logger.LogError(ex, "心跳执行失败");
            }
        }
    }

    private TimeSpan NextInterval()
    {
        var min = Math.Max(1, _options.MinMinutes);
        var max = Math.Max(min, _options.MaxMinutes);
        var minutes = min + _next(max - min + 1);
        return TimeSpan.FromMinutes(minutes);
    }
}
