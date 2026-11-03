namespace Shoyee.Photo;

/// <summary>一次生成的结果。</summary>
/// <param name="Images">图片（至少一张；供应商返回空视为失败）。</param>
public sealed record PhotoResult(IReadOnlyList<PhotoImage> Images);
