namespace Shoyee.Desktop.Theming;

/// <summary>
/// 一档模型的 OpenAI 兼容配置（桌面侧的展示与编辑；接 daemon 后由 config.get 提供）。
/// </summary>
/// <param name="BaseUrl">兼容端点，例如 https://api.deepseek.com。</param>
/// <param name="Model">模型名。</param>
/// <param name="KeyEnv">API key 的环境变量名（key 本身不落盘）。</param>
/// <param name="ContextLength">模型支持的上下文长度（token）。</param>
public sealed record ModelProfileSettings(
    string BaseUrl = "https://api.deepseek.com",
    string Model = "deepseek-flash",
    string KeyEnv = "DEEPSEEK_API_KEY",
    int ContextLength = 1_000_000);
