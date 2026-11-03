namespace Shoyee.Web.DeepSeek;

/// <summary>DeepSeek 网页能力配置（<c>web.providers.deepseek</c> 节）。</summary>
/// <param name="BaseUrl">API 根地址。</param>
/// <param name="ApiKeyEnv">API key 的环境变量名；留空时回落到主模型/小模型配置的 key。</param>
/// <param name="TimeoutSeconds">单次请求超时（秒）。</param>
public sealed record DeepSeekWebOptions(
    string BaseUrl = "https://api.deepseek.com",
    string ApiKeyEnv = "DEEPSEEK_API_KEY",
    int TimeoutSeconds = 60);
