namespace Shoyee.Contracts.Abstractions;

/// <summary>
/// 纯通知推送（不经过聊天渠道、不受窗口期限制）。实现方（系统通知 / Server酱 / 邮件等）在
/// <c>Shoyee.Notify.*</c> 供应商项目里，使用方（cron、子智能体终态回推）只依赖这个契约。
/// </summary>
public interface INotificationPusher
{
    /// <summary>推一条通知；失败抛异常，由调用方决定记日志还是记到执行台账。</summary>
    /// <param name="title">标题。</param>
    /// <param name="body">正文（Markdown）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task PushAsync(string title, string body, CancellationToken cancellationToken = default);
}
