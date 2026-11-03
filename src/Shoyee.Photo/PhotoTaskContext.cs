namespace Shoyee.Photo;

/// <summary>出图任务的去向：RP Lane（桌面角色图）或工作会话。</summary>
/// <param name="TaskId">任务 id。</param>
/// <param name="LaneId">所属 Lane（RP 必填，事件可见性过滤用）。</param>
/// <param name="SessionId">工作会话 id；RP 为 null。</param>
/// <param name="Caption">图说明。</param>
public sealed record PhotoTaskContext(
    string TaskId,
    string LaneId,
    string? SessionId = null,
    string? Caption = null);
