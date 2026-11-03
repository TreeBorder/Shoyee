namespace Shoyee.Contracts.Dtos;

/// <summary>提示词与人设文件的绝对路径（桌面同机直读，不走文件内容 RPC）。</summary>
/// <param name="Shared">两个模式共用的规则。</param>
/// <param name="Soul">角色人设。</param>
/// <param name="User">用户人设。</param>
/// <param name="Memory">长期记忆。</param>
/// <param name="RoleplayAgents">角色行为规范。</param>
/// <param name="RoleplayWorld">世界观设定。</param>
/// <param name="WorkAgents">工作指南。</param>
public sealed record PromptPathsDto(
    string Shared,
    string Soul,
    string User,
    string Memory,
    string RoleplayAgents,
    string RoleplayWorld,
    string WorkAgents);

/// <summary>心跳提示词与实际运行日志的绝对路径。</summary>
/// <param name="Prompts">四组问候提示词（Text / Story / Voice / Image）。</param>
/// <param name="Log">心跳运行日志。</param>
public sealed record HeartbeatPathsDto(string Prompts, string Log);

/// <summary>
/// <c>workspace.paths</c> 的返回：桌面要读的本地文件位置。
///
/// 只返回 daemon 自己拼出来的固定路径，不接受客户端传任意路径；文件读取仍受本机文件权限约束。
/// </summary>
/// <param name="Workspace">工作区根目录。</param>
/// <param name="ConfigFile">工作区配置 <c>config.json</c>。</param>
/// <param name="Prompts">提示词与人设文件。</param>
/// <param name="Heartbeat">心跳提示词与日志。</param>
/// <param name="DaemonLog">daemon 日志。</param>
/// <param name="Exports">会话导出目录。</param>
/// <param name="Avatars">桌面头像目录（应用数据）。</param>
public sealed record WorkspacePathsDto(
    string Workspace,
    string ConfigFile,
    PromptPathsDto Prompts,
    HeartbeatPathsDto Heartbeat,
    string DaemonLog,
    string Exports,
    string Avatars);
