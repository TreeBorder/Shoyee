using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Shoyee.Channels.Delivery;

/// <summary>周期性补发到期挂账（15s 一次；微信窗口期由渠道自己的规则决定，不在这里）。</summary>
public sealed class DeliveryRetryService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    private readonly ReliableDelivery _delivery;
    private readonly ILogger<DeliveryRetryService> _logger;

    /// <summary>创建补发服务。</summary>
    /// <param name="delivery">投递层。</param>
    /// <param name="logger">日志。</param>
    public DeliveryRetryService(ReliableDelivery delivery, ILogger<DeliveryRetryService> logger)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentNullException.ThrowIfNull(logger);
        _delivery = delivery;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await _delivery.FlushAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // 补发失败不该让后台服务死掉：台账还在，下一轮再来
                _logger.LogError(ex, "补发轮询失败");
            }
        }
    }
}
