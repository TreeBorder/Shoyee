namespace Shoyee.Desktop.ViewModels.Items;

/// <summary>设置页「提示词」预览的一篇文件（占位数据，接 daemon 后换成真实文件内容）。</summary>
public sealed class PromptPreviewItem
{
    /// <summary>对应的设置子页键（如 <c>prompts.soul</c>）。</summary>
    public required string Key { get; init; }

    /// <summary>提示词名称（文件名）。</summary>
    public required string Title { get; init; }

    /// <summary>来源（文件或会话快照位置）。</summary>
    public required string Source { get; init; }

    /// <summary>一句话说明它在这套提示词里的作用。</summary>
    public required string Description { get; init; }

    /// <summary>预览正文。</summary>
    public required string Preview { get; init; }

    /// <summary>本地文件路径（连上 daemon 后来自 workspace.paths；占位时为 null）。</summary>
    public string? Path { get; init; }

    /// <summary>文件是否存在（决定「用系统编辑器打开」是否可用）。</summary>
    public bool HasFile => Path is { Length: > 0 } && File.Exists(Path);

    /// <summary>字符数。</summary>
    public int CharCount => Preview.Length;
}
