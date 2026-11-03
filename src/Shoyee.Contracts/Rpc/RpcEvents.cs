namespace Shoyee.Contracts.Rpc;

/// <summary>服务端推送事件名常量。</summary>
public static class RpcEvents
{
    /// <summary>轮次开始。</summary>
    public const string TurnStarted = "turn.started";

    /// <summary>正文增量。</summary>
    public const string TurnDelta = "turn.delta";

    /// <summary>思考（reasoning）增量。</summary>
    public const string TurnThinking = "turn.thinking";

    /// <summary>工具调用阶段（start/end）。</summary>
    public const string TurnTool = "turn.tool";

    /// <summary>轮次完成。</summary>
    public const string TurnCompleted = "turn.completed";

    /// <summary>轮次失败。</summary>
    public const string TurnFailed = "turn.failed";

    /// <summary>需要人工审批。</summary>
    public const string ApprovalRequest = "approval.request";

    /// <summary>审批已有结论。</summary>
    public const string ApprovalResolved = "approval.resolved";

    /// <summary>消息落库（入站/出站/主动）。</summary>
    public const string MessageAppended = "message.appended";

    /// <summary>会话标题变化（当前只有自动起名会推）。</summary>
    public const string SessionRenamed = "session.renamed";

    /// <summary>Lane 连接状态变化。</summary>
    public const string LaneStatus = "lane.status";

    /// <summary>daemon 周期状态（菜单栏）。</summary>
    public const string DaemonStatus = "daemon.status";

    /// <summary>通用状态变化（心情/精力/思念等）。</summary>
    public const string StateUpdated = "state.updated";

    /// <summary>单次模型调用用量。</summary>
    public const string UsageUpdated = "usage.updated";

    /// <summary>角色图生成完成（异步出图，客户端按需展示）。</summary>
    public const string PhotoReady = "photo.ready";
}
