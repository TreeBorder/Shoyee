namespace Shoyee.Photo;

/// <summary>给帮助工具用的一条词表摘要。</summary>
/// <param name="Key">稳定 key（工具参数用）。</param>
/// <param name="Name">展示名。</param>
/// <param name="Note">说明。</param>
public sealed record PromptEntry(string Key, string Name, string Note);
