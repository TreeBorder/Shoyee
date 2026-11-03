namespace Shoyee.Photo;

/// <summary>落盘后的图片资产（DB 不存二进制）。</summary>
/// <param name="Path">本地绝对路径。</param>
/// <param name="Mime">媒体类型。</param>
/// <param name="SizeBytes">字节数。</param>
public sealed record PhotoAsset(string Path, string Mime, long SizeBytes);
