namespace Shoyee.Desktop.Theming;

/// <summary>模型适用场景，对应 daemon 配置里的三个档位。</summary>
public enum ModelScene
{
    /// <summary>主模型：对话与工具调用。</summary>
    Chat,

    /// <summary>小模型：压缩摘要、会话标题、智能审批。</summary>
    Small,

    /// <summary>视觉模型：附件图片与 analyze_image。</summary>
    Vision,
}

/// <summary>
/// 本机接入的一个 OpenAI 兼容模型。同一个模型可以同时承担多个场景；
/// 接 daemon 后按场景绑定换算成 <c>models.main / small / vision</c>。
/// </summary>
/// <param name="Id">本机稳定标识（只用于场景绑定，不发给后端）。</param>
/// <param name="Name">显示名，如「DeepSeek Flash」。</param>
/// <param name="BaseUrl">OpenAI 兼容端点。</param>
/// <param name="Model">模型 id。</param>
/// <param name="KeyEnv">API key 的环境变量名（key 本身不落盘）。</param>
/// <param name="ContextLength">上下文长度（token），同时用于对话页上下文占比。</param>
/// <param name="Scenes">可承担的适用场景。</param>
public sealed record ModelEntrySettings(
    string Id,
    string Name,
    string BaseUrl,
    string Model,
    string KeyEnv,
    int ContextLength,
    IReadOnlyList<ModelScene> Scenes);

/// <summary>
/// 本机模型配置：接入的模型列表 + 每个场景用哪一个。
/// 场景绑定存模型 Id，不复制端点；改一个模型只改一处。
/// </summary>
/// <param name="Entries">接入的模型列表。</param>
/// <param name="ChatEntryId">主模型绑定的模型 Id（空 = 未绑定）。</param>
/// <param name="SmallEntryId">小模型绑定的模型 Id（空 = 未绑定）。</param>
/// <param name="VisionEntryId">视觉模型绑定的模型 Id（空 = 不做图片分析）。</param>
public sealed record ModelCatalogSettings(
    IReadOnlyList<ModelEntrySettings> Entries,
    string? ChatEntryId = null,
    string? SmallEntryId = null,
    string? VisionEntryId = null)
{
    /// <summary>首次启动或旧配置迁移后的默认：一个 DeepSeek 端点同时承担三个场景。</summary>
    public static ModelCatalogSettings Default { get; } = new(
        [
            new(
                "default",
                "DeepSeek Flash",
                "https://api.deepseek.com",
                "deepseek-flash",
                "DEEPSEEK_API_KEY",
                1_000_000,
                [ModelScene.Chat, ModelScene.Small, ModelScene.Vision]),
        ],
        "default",
        "default",
        "default");
}
