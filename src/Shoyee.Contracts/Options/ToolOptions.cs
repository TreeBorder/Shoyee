namespace Shoyee.Contracts.Options;

/// <summary>工具配置。</summary>
/// <param name="Disabled">显式关闭的工具名。</param>
/// <param name="Mcp">MCP 服务器。</param>
/// <param name="Subagents">子智能体。</param>
/// <param name="AutoApproveReadOnly">只读工具是否免审批。</param>
/// <param name="DeniedPaths">额外禁止工具读写的目录（相对工作区根，也可写绝对路径）；图片产出目录已在默认名单里。</param>
public sealed record ToolOptions(
    IReadOnlyList<string>? Disabled = null,
    IReadOnlyList<McpServerConfig>? Mcp = null,
    SubagentOptions? Subagents = null,
    bool AutoApproveReadOnly = true,
    IReadOnlyList<string>? DeniedPaths = null);

/// <summary>一个 MCP 服务器。</summary>
/// <param name="Name">名字（工具名前缀）。</param>
/// <param name="Url">端点。</param>
/// <param name="Enabled">是否启用。</param>
/// <param name="AllowedTools">工具白名单（空 = 全部）。</param>
/// <param name="TokenEnv">凭据环境变量名。</param>
public sealed record McpServerConfig(
    string Name,
    string Url,
    bool Enabled = true,
    IReadOnlyList<string>? AllowedTools = null,
    string? TokenEnv = null);
