using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Shoyee.Photo;

/// <summary>
/// 异步出图队列（照 Harem 的「拍完再发」）：工具只提交任务并立即返回；
/// 后台按顺序出图、落盘，完成后交给 <see cref="IPhotoDelivery"/>。
/// 出图慢（几十秒到几分钟），不能把 RP 轮次挂在那里等。
/// </summary>
public sealed class PhotoTaskRunner : BackgroundService
{
    private readonly Channel<PhotoJob> _queue = Channel.CreateUnbounded<PhotoJob>(
        new UnboundedChannelOptions { SingleReader = true });

    private readonly PhotoService _service;
    private readonly IPhotoDelivery? _delivery;
    private readonly ILogger<PhotoTaskRunner> _logger;

    /// <summary>创建队列。</summary>
    /// <param name="service">出图服务。</param>
    /// <param name="delivery">完成后投递（未接入 = 只落盘并记日志）。</param>
    /// <param name="logger">日志。</param>
    public PhotoTaskRunner(
        PhotoService service,
        IPhotoDelivery? delivery = null,
        ILogger<PhotoTaskRunner>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
        _delivery = delivery;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<PhotoTaskRunner>.Instance;
    }

    /// <summary>提交一个任务；立即返回任务 id。</summary>
    /// <param name="plan">取景。</param>
    /// <param name="laneId">所属 Lane（RP 用它过滤事件）。</param>
    /// <param name="sessionId">工作会话 id；RP 为 null。</param>
    /// <param name="caption">图说明。</param>
    public string Enqueue(PhotoPlan plan, string laneId, string? sessionId = null, string? caption = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(laneId);

        var context = new PhotoTaskContext(Guid.NewGuid().ToString("N"), laneId, sessionId, caption);
        if (!_queue.Writer.TryWrite(new PhotoJob(plan, context)))
        {
            throw new InvalidOperationException("出图队列已关闭。");
        }

        return context.TaskId;
    }

    /// <summary>跑一个任务（队列消费者用；测试可直接调）。</summary>
    /// <param name="job">任务。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>成功为 true。</returns>
    public async Task<bool> RunAsync(PhotoJob job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        try
        {
            var assets = await _service
                .GenerateAsync(job.Plan, DateTimeOffset.Now, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "出图完成：{TaskId}（{Count} 张，Lane {LaneId}）",
                job.Context.TaskId,
                assets.Count,
                job.Context.LaneId);

            if (_delivery is not null)
            {
                await _delivery
                    .DeliverAsync(new PhotoDelivery(job.Context, assets), cancellationToken)
                    .ConfigureAwait(false);
            }

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("出图取消：{TaskId}", job.Context.TaskId);
            return false;
        }
        catch (Exception ex)
        {
            // 与 Harem 一致：失败只记日志，不往会话里塞一条失败消息
            _logger.LogWarning(ex, "出图失败：{TaskId}（Lane {LaneId}）", job.Context.TaskId, job.Context.LaneId);
            return false;
        }
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            await RunAsync(job, stoppingToken).ConfigureAwait(false);
        }
    }
}
