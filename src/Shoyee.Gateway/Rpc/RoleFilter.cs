using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Rpc;

namespace Shoyee.Gateway.Rpc;

/// <summary>
/// 角色策略：**服务端强制**方法与事件可见性，不靠 UI 自觉。
/// 规则见 <c>docs/IPC契约.md</c> §2–§4。
/// </summary>
public static class RoleFilter
{
    private static readonly HashSet<string> WorkSessionMethods = new(StringComparer.Ordinal)
    {
        RpcMethods.SessionList,
        RpcMethods.SessionGet,
        RpcMethods.SessionOpen,
        RpcMethods.SessionCreate,
        RpcMethods.SessionRename,
        RpcMethods.SessionArchive,
        RpcMethods.SessionSetActiveForLane,
        RpcMethods.SessionMessages,
        RpcMethods.SessionSearch,
        RpcMethods.SessionSnapshot,
        RpcMethods.SessionExport,
    };

    /// <summary>桌面两种角色都能用的媒体/资产读取（同机直读，不涉及写入）。</summary>
    private static readonly HashSet<string> MediaMethods = new(StringComparer.Ordinal)
    {
        RpcMethods.TtsSynthesize,
        RpcMethods.AssetGet,
    };

    /// <summary>桌面「生成一张图」按钮：通用生图只属于工作模式（RP 走 camera/gallery 工具）。</summary>
    private static readonly HashSet<string> ImageMethods = new(StringComparer.Ordinal)
    {
        RpcMethods.ImageRender,
    };

    private static readonly HashSet<string> IndexMethods = new(StringComparer.Ordinal)
    {
        RpcMethods.IndexList,
        RpcMethods.IndexUpsert,
        RpcMethods.IndexRemove,
    };

    private static readonly HashSet<string> TurnMethods = new(StringComparer.Ordinal)
    {
        RpcMethods.LaneMessages,
        RpcMethods.TurnSend,
        RpcMethods.TurnCancel,
        RpcMethods.ApprovalRespond,
        // RP 桌面可以检索自己的 Lane 历史（分发时强制 laneId）
        RpcMethods.SessionSearch,
        // 重试本身只对工作会话生效，分发时明确拒绝 RP；放在这里只是为了统一走桌面轮次方法
        RpcMethods.TurnRetry,
    };

    private static readonly HashSet<string> CronMethods = new(StringComparer.Ordinal)
    {
        RpcMethods.CronList,
        RpcMethods.CronGet,
        RpcMethods.CronExecutions,
        RpcMethods.CronSetEnabled,
        RpcMethods.CronRemove,
    };

    private static readonly HashSet<string> NotifyMethods = new(StringComparer.Ordinal)
    {
        RpcMethods.NotifyTest,
    };

    /// <summary>工作台运行时视图：工具清单、MCP、状态、用量、重启（设置页用）。</summary>
    private static readonly HashSet<string> RuntimeViewMethods = new(StringComparer.Ordinal)
    {
        RpcMethods.ToolsList,
        RpcMethods.McpStatus,
        RpcMethods.McpReload,
        RpcMethods.StateSet,
        RpcMethods.UsageGet,
        RpcMethods.DaemonRestart,
    };

    /// <summary>桌面要读工作区文件的固定路径；两种桌面角色都给。</summary>
    private static readonly HashSet<string> WorkspaceMethods = new(StringComparer.Ordinal)
    {
        RpcMethods.WorkspacePaths,
    };

    /// <summary>握手前唯一允许的方法。</summary>
    /// <param name="method">方法名。</param>
    public static bool IsHandshakeMethod(string method)
        => method is RpcMethods.Hello or RpcMethods.Ping;

    /// <summary>该角色的连接能否调用该方法。</summary>
    /// <param name="role">连接角色（hello 之前为 null）。</param>
    /// <param name="method">方法名。</param>
    public static bool CanCall(ClientRole? role, string method)
    {
        if (role is null)
        {
            return IsHandshakeMethod(method);
        }

        return role switch
        {
            ClientRole.Test => true,
            _ when IsHandshakeMethod(method) || method == RpcMethods.StatusGet => true,
            ClientRole.DesktopRoleplay => TurnMethods.Contains(method)
                || MediaMethods.Contains(method)
                || WorkspaceMethods.Contains(method),
            ClientRole.DesktopWork => WorkSessionMethods.Contains(method)
                || TurnMethods.Contains(method)
                || MediaMethods.Contains(method)
                || IndexMethods.Contains(method)
                || ImageMethods.Contains(method)
                || CronMethods.Contains(method)
                || NotifyMethods.Contains(method)
                || WorkspaceMethods.Contains(method)
                || RuntimeViewMethods.Contains(method)
                || method == RpcMethods.ConfigGet
                || method == RpcMethods.ConfigSet
                || method == RpcMethods.ConfigReload,
            ClientRole.Menu => method == RpcMethods.DaemonStatus
                || NotifyMethods.Contains(method),
            ClientRole.Ctl => method == RpcMethods.DaemonStatus
                || method == RpcMethods.DaemonShutdown
                || RuntimeViewMethods.Contains(method)
                || method == RpcMethods.ConfigGet
                || method == RpcMethods.ConfigSet
                || method == RpcMethods.ConfigReload
                || IndexMethods.Contains(method)
                || NotifyMethods.Contains(method),
            _ => false,
        };
    }

    /// <summary>该连接能否收到这条事件。</summary>
    /// <param name="role">连接角色。</param>
    /// <param name="connectionLaneId">桌面 RP 连接绑定（写操作）的 Lane；读事件不再按它过滤。</param>
    /// <param name="eventName">事件名。</param>
    /// <param name="eventLaneId">事件自带的 Lane（RP 事件用 Lane id 占位，恒有值）。</param>
    /// <param name="fromRoleplayLane">事件是否来自 RP Lane。</param>
    public static bool CanReceive(
        ClientRole? role,
        string? connectionLaneId,
        string eventName,
        string? eventLaneId,
        bool fromRoleplayLane)
    {
        switch (role)
        {
            case null:
                return false;
            case ClientRole.Test:
                return true;
            case ClientRole.DesktopRoleplay:
                // 可以切换查看所有 RP Lane 的历史与事件；工作 Lane 的事件仍然看不到。
                // 状态（心情/思念等）是 RP 页自己展示的全局状态，与 Lane 无关，单独放行。
                return fromRoleplayLane || eventName == RpcEvents.StateUpdated;
            case ClientRole.DesktopWork:
                return eventName switch
                {
                    RpcEvents.DaemonStatus => false,
                    RpcEvents.LaneStatus or RpcEvents.StateUpdated or RpcEvents.UsageUpdated => true,
                    _ => !fromRoleplayLane,
                };
            case ClientRole.Menu:
            case ClientRole.Ctl:
                return eventName is RpcEvents.DaemonStatus or RpcEvents.LaneStatus or RpcEvents.StateUpdated;
            default:
                return false;
        }
    }
}
