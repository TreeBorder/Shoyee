namespace Shoyee.Contracts.Options;

/// <summary>模型档位。三个档都走 OpenAI 兼容端点（自定义 base_url）。</summary>
/// <param name="Main">主模型（对话）。</param>
/// <param name="Small">小模型（摘要/标题/压缩/智能审批）。</param>
/// <param name="Vision">视觉（可空 = 不做图片分析）。</param>
public sealed record ModelProfiles(ModelEndpoint Main, ModelEndpoint Small, ModelEndpoint? Vision = null);

/// <summary>一个模型端点。</summary>
/// <param name="BaseUrl">OpenAI 兼容端点。</param>
/// <param name="ModelId">模型 id。</param>
/// <param name="ApiKeyEnv">存放 API key 的环境变量名。</param>
/// <param name="Temperature">温度（可空 = 端点默认）。</param>
/// <param name="MaxOutputTokens">输出上限（可空 = 端点默认）。按模型规格填，例如 DeepSeek 384K、MiMo-V2.6-Flash 128K。</param>
/// <param name="ContextLength">上下文窗口（可空 = 端点默认），用于压缩预算与桌面上下文占比。</param>
public sealed record ModelEndpoint(
    string BaseUrl,
    string ModelId,
    string ApiKeyEnv,
    double? Temperature = null,
    int? MaxOutputTokens = null,
    int? ContextLength = null);
