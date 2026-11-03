using System.Text.Json;
using Shoyee.Contracts.Options;
using Shoyee.Contracts.Rpc;

namespace Shoyee.Daemon.Configuration;

/// <summary>
/// 工作区配置的分段比较：区分「能热更新」与「要重启」的段。
///
/// **热更新只做不碰模型请求前缀的项**：工具路径黑名单、终端硬禁正则、附件自动分析参数。
/// 任何会改变 system/工具定义/消息内容的配置（模型档位、禁用名单、MCP、子智能体、渠道、
/// 出图/语音/通知供应商等）都要求重启——工作模式的 prompt cache 前缀是钱，不能为了「立即生效」
/// 在会话中途换工具定义。
/// </summary>
public static class ConfigSectionDiff
{
    /// <summary>这一版能热更新的键（与配置文件字段对应，子字段用点号）。</summary>
    public static IReadOnlySet<string> HotReloadable { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "tools.deniedPaths",
        "shell.denyPatterns",
        "attachments",
    };

    /// <summary>比较两份配置，返回已热更新 / 需重启的键（按固定顺序，便于客户端展示）。</summary>
    /// <param name="current">当前生效的配置。</param>
    /// <param name="next">刚读到的配置。</param>
    public static (IReadOnlyList<string> Changed, IReadOnlyList<string> RestartRequired) Compare(
        WorkspaceConfig current,
        WorkspaceConfig next)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(next);

        var changed = new List<string>();
        var restart = new List<string>();
        foreach (var (name, before, after) in Sections(current, next))
        {
            if (JsonSerializer.Serialize(before, JsonWire.Options)
                == JsonSerializer.Serialize(after, JsonWire.Options))
            {
                continue;
            }

            if (HotReloadable.Contains(name))
            {
                changed.Add(name);
            }
            else
            {
                restart.Add(name);
            }
        }

        return (changed, restart);
    }

    /// <summary>顶层配置段（<c>root</c> 由 daemon 固定，不参与比较）。</summary>
    private static IEnumerable<(string Name, object? Before, object? After)> Sections(
        WorkspaceConfig current,
        WorkspaceConfig next)
    {
        yield return ("models", current.Models, next.Models);
        yield return ("channels", current.Channels, next.Channels);
        yield return ("proactive", current.Proactive, next.Proactive);

        // tools 拆成两半：deniedPaths 只影响工具内部判断，不动请求前缀；其余都算要重启
        yield return ("tools", WithoutDeniedPaths(current.Tools), WithoutDeniedPaths(next.Tools));
        yield return ("tools.deniedPaths", current.Tools.DeniedPaths, next.Tools.DeniedPaths);

        // shell 同理：硬禁正则由 ProcessRegistry 原地更新；超时/工作目录仍烘在工具里
        yield return ("shell", WithoutDenyPatterns(current.Shell), WithoutDenyPatterns(next.Shell));
        yield return ("shell.denyPatterns", current.Shell?.DenyPatterns, next.Shell?.DenyPatterns);

        yield return ("photo", current.Photo, next.Photo);
        yield return ("voice", current.Voice, next.Voice);
        yield return ("databasePath", current.DatabasePath, next.DatabasePath);
        yield return ("databaseProvider", current.DatabaseProvider, next.DatabaseProvider);
        yield return ("notes", current.Notes, next.Notes);
        yield return ("web", current.Web, next.Web);
        yield return ("attachments", Effective(current.Attachments), Effective(next.Attachments));
        yield return ("notify", current.Notify, next.Notify);
        yield return ("context", current.Context, next.Context);
    }

    private static ToolOptions WithoutDeniedPaths(ToolOptions tools) => tools with { DeniedPaths = null };

    // 没写这一段与写一段全默认值，运行时是同一个东西；不因为「补写默认值」就报重启
    private static ShellOptions WithoutDenyPatterns(ShellOptions? shell)
        => (shell ?? new ShellOptions()) with { DenyPatterns = null };

    private static AttachmentOptions Effective(AttachmentOptions? attachments)
        => attachments ?? new AttachmentOptions();
}
