namespace Shoyee.Photo;

/// <summary>一张（或多张）出图完成后的投递内容。</summary>
/// <param name="Context">原任务去向。</param>
/// <param name="Assets">落盘图片。</param>
public sealed record PhotoDelivery(PhotoTaskContext Context, IReadOnlyList<PhotoAsset> Assets);
