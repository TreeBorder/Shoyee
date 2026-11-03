using Shoyee.Contracts.Abstractions;

namespace Shoyee.Daemon.Hosting;

/// <summary>
/// <c>notify.provider = none</c> 时的占位实现：保持 <see cref="INotificationPusher"/> 可解析，
/// 真被用到（cron 配了 <c>deliver=push</c> / 子智能体开了终态推送）时给可操作错误，而不是让 daemon 启动失败。
/// </summary>
internal sealed class DisabledNotificationPusher : INotificationPusher
{
    /// <inheritdoc />
    public Task PushAsync(string title, string body, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("通知渠道未配置（notify.provider = none）。");
}
