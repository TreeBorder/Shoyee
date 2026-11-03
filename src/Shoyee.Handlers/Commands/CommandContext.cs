using Shoyee.Contracts.Domain;

namespace Shoyee.Handlers.Commands;

/// <summary>
/// 一条预置指令的上下文。
///
/// <paramref name="TargetId"/> 是「这条消息落在哪个上下文中」：工作模式是会话 id，RP 是 Lane id
/// ——审批台与轮次服务都用它定位（工作会话按会话，RP 按 Lane）。
/// </summary>
/// <param name="LaneId">消息来自的 Lane（审计与 <c>decidedBy</c> 用）。</param>
/// <param name="TargetId">会话 id（工作）/ Lane id（RP）。</param>
/// <param name="Arguments">指令名之后的参数（已 trim）。</param>
/// <param name="Mode">这条消息属于哪种模式；默认工作（旧调用点不用改）。</param>
public sealed record CommandContext(
    string LaneId,
    string TargetId,
    string Arguments,
    Mode Mode = Mode.Work);
