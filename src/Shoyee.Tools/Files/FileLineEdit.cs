namespace Shoyee.Tools.Files;

/// <summary>一条按行编辑：<paramref name="NewLine"/> 原样写入（空串 = 删该行）。</summary>
/// <param name="LineNumber">1-based 行号。</param>
/// <param name="NewLine">整行新文本（自带换行；空串 = 删行）。</param>
/// <param name="ExpectedLine">可选的当前行文本（防行号过期写错行）。</param>
public sealed record FileLineEdit(int LineNumber, string NewLine, string? ExpectedLine = null);
