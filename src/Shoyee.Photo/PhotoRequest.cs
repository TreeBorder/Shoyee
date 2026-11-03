namespace Shoyee.Photo;

/// <summary>一次生成请求（供应商层看得懂的形态）。</summary>
/// <param name="Positive">正向提示词（已按顺序拼好）。</param>
/// <param name="Negative">负向提示词。</param>
/// <param name="Width">宽（像素）。</param>
/// <param name="Height">高（像素）。</param>
/// <param name="Seed">随机种子。</param>
/// <param name="FileNamePrefix">生成器内部文件名前缀（人设 key）。</param>
public sealed record PhotoRequest(
    string Positive,
    string Negative,
    int Width,
    int Height,
    int Seed,
    string? FileNamePrefix = null);
