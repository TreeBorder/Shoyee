namespace Shoyee.Desktop.ViewModels;

/// <summary>
/// 设置页的一级菜单。分组照 <c>docs/设置页信息架构-v2.md</c>：
/// 本机 / 提示词（共用）/ 角色模式 / 工作模式 / 工具 / MCP / 供应商与配置 / 配置文件。
/// </summary>
public enum SettingsSection
{
    /// <summary>本机：外观、资料与头像、工作区与连接。</summary>
    Local,

    /// <summary>提示词（共用）：AGENTS.shared.md、SOUL.md、USER.md、MEMORY.md。</summary>
    Prompts,

    /// <summary>角色模式：角色提示词、摘要与上下文、心跳与惊喜问候、状态与场景。</summary>
    Roleplay,

    /// <summary>工作模式：工作提示词、资料索引、子智能体、会话与上下文。</summary>
    Work,

    /// <summary>工具：工具集、审批规则。</summary>
    Tools,

    /// <summary>MCP：服务器、工具与白名单。</summary>
    Mcp,

    /// <summary>供应商与配置：模型、渠道、通知、媒体、Shell 等。</summary>
    Providers,

    /// <summary>配置文件：config.json、心跳实际配置、日志、重载与重启。</summary>
    Config,
}
