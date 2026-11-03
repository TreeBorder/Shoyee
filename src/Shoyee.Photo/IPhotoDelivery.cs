namespace Shoyee.Photo;

/// <summary>
/// 出图完成后的投递口。当前 Daemon 把它实现成 <c>photo.ready</c> 事件；
/// 渠道接入后同一口子再写 IM（与 Harem 的 ReplyMessage 同语义：只发给用户，不伪造历史消息）。
/// </summary>
public interface IPhotoDelivery
{
    /// <summary>投递完成的图片。</summary>
    /// <param name="delivery">任务去向与图片。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task DeliverAsync(PhotoDelivery delivery, CancellationToken cancellationToken = default);
}
