using System.Text.Json.Serialization;

namespace Shoyee.Photo;

/// <summary>
/// 词表里的一条（<c>.comfyui/prompts/*.json</c>）：场景/服装/动作/人设/环境/质量/视角七类共用。
/// 字段由用户手工编辑，缺省一律按空处理。
/// </summary>
/// <param name="DisplayName">展示名（给人看）。</param>
/// <param name="Note">给模型看的说明。</param>
/// <param name="EnvironmentCategory">仅场景用：环境分类（indoor / outdoor / studio…）。</param>
/// <param name="Prompt">正向词。</param>
/// <param name="Negative">负向词。</param>
public sealed record PromptBlock(
    string? DisplayName = null,
    string? Note = null,
    string? EnvironmentCategory = null,
    [property: JsonPropertyName("prompt")] IReadOnlyList<string>? Prompt = null,
    [property: JsonPropertyName("negative")] IReadOnlyList<string>? Negative = null)
{
    /// <summary>正向词（缺省为空）。</summary>
    public IReadOnlyList<string> PositivePrompts => Prompt ?? [];

    /// <summary>负向词（缺省为空）。</summary>
    public IReadOnlyList<string> NegativePrompts => Negative ?? [];
}
