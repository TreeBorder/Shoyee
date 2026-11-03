using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shoyee.Contracts.Abstractions;

namespace Shoyee.Channels.Core;

/// <summary>
/// 渠道宿主：每个 <see cref="IChannelAdapter"/> 一条接收循环，互不牵连。
///
/// 适配器按契约自己重连；万一它没打招呼就返回/抛异常，这里记日志并延迟重启，
/// 而不是让整条渠道静默消失（Harem 的 Clio 停摆就是没这层兜底）。
/// </summary>
public sealed class ChannelHost : BackgroundService
{
    private static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(10);

    private readonly IReadOnlyList<IChannelAdapter> _adapters;
    private readonly IInboundSink _sink;
    private readonly ILogger<ChannelHost> _logger;

    /// <summary>创建渠道宿主。</summary>
    /// <param name="adapters">已注册的适配器（可能为空 = 不启用任何 IM）。</param>
    /// <param name="sink">入站管线。</param>
    /// <param name="logger">日志。</param>
    public ChannelHost(
        IEnumerable<IChannelAdapter> adapters,
        IInboundSink sink,
        ILogger<ChannelHost> logger)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(logger);
        _adapters = adapters.ToArray();
        _sink = sink;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_adapters.Count == 0)
        {
            _logger.LogInformation("没有启用任何 IM 渠道（仅桌面 Lane）");
            return;
        }

        _logger.LogInformation(
            "启动 {Count} 个渠道适配器：{Lanes}",
            _adapters.Count,
            string.Join("、", _adapters.Select(static a => a.LaneId)));

        await Task.WhenAll(_adapters.Select(a => RunGuardedAsync(a, stoppingToken))).ConfigureAwait(false);
    }

    private async Task RunGuardedAsync(IChannelAdapter adapter, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await adapter.RunAsync(_sink, stoppingToken).ConfigureAwait(false);
                if (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogWarning("{LaneId} 接收循环自行返回，准备重启", adapter.LaneId);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{LaneId} 接收循环异常，准备重启", adapter.LaneId);
            }

            try
            {
                await Task.Delay(RestartDelay, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
