using Shoyee.Contracts.Domain;

namespace Shoyee.Contracts.Dtos;

/// <summary>一条 Lane 的连接与指针状态。</summary>
/// <param name="Id">Lane id。</param>
/// <param name="Channel">渠道类型。</param>
/// <param name="Mode">绑定的模式。</param>
/// <param name="DisplayName">展示名。</param>
/// <param name="Connected">连接是否正常。</param>
/// <param name="ActiveSessionId">该渠道当前会话指针。</param>
/// <param name="LastError">最近错误（连接失败等）。</param>
public sealed record LaneStatus(
    string Id,
    LaneChannel Channel,
    Mode Mode,
    string? DisplayName,
    bool Connected,
    string? ActiveSessionId,
    string? LastError);

/// <summary>单次模型调用的用量。</summary>
/// <param name="Model">模型名。</param>
/// <param name="InputTokens">输入 token。</param>
/// <param name="OutputTokens">输出 token。</param>
/// <param name="CacheHitTokens">缓存命中输入 token。</param>
/// <param name="CacheMissTokens">缓存未命中输入 token。</param>
/// <param name="At">时间（UTC）。</param>
public sealed record UsageDto(
    string Model,
    int InputTokens,
    int OutputTokens,
    int CacheHitTokens,
    int CacheMissTokens,
    DateTimeOffset At);

/// <summary>用量汇总（日/会话级）。</summary>
/// <param name="Calls">调用次数。</param>
/// <param name="InputTokens">输入 token 合计。</param>
/// <param name="OutputTokens">输出 token 合计。</param>
/// <param name="CacheHitTokens">缓存命中合计。</param>
/// <param name="CacheMissTokens">缓存未命中合计。</param>
public sealed record UsageSummary(
    int Calls,
    int InputTokens,
    int OutputTokens,
    int CacheHitTokens,
    int CacheMissTokens);

/// <summary>
/// 上下文占用：取该会话**最后一次主对话模型调用**的 usage。
///
/// 已用 = 输入 + 输出（输出会进入下一次请求的提示词）；
/// 不是把整轮工具循环的调用累加——那样会随着工具轮数虚高。
/// </summary>
/// <param name="PromptTokens">上次调用的输入 token（system + 历史 + 工具定义）。</param>
/// <param name="CompletionTokens">上次调用的输出 token。</param>
/// <param name="CacheHitTokens">上次输入里命中缓存的部分。</param>
/// <param name="At">该次调用时间（UTC）。</param>
public sealed record ContextUsage(
    int PromptTokens,
    int CompletionTokens,
    int CacheHitTokens,
    DateTimeOffset At)
{
    /// <summary>已经占用的上下文 token（输入 + 输出）。</summary>
    public int Tokens => PromptTokens + CompletionTokens;
}

/// <summary>通用状态（键值 + 说明；新增状态不改代码）。</summary>
/// <param name="Key">稳定键（如 mood / energy / longing）。</param>
/// <param name="Name">展示名。</param>
/// <param name="Value">值（统一字符串，数值由消费者解析）。</param>
/// <param name="Description">给模型的说明与取值约束。</param>
/// <param name="AgentEditable">她是否可以自己改。</param>
/// <param name="UpdatedAt">更新时间（UTC）。</param>
public sealed record StateDto(
    string Key,
    string Name,
    string Value,
    string? Description,
    bool AgentEditable,
    DateTimeOffset UpdatedAt);

/// <summary>数据库路径与体积。</summary>
/// <param name="Path">库文件绝对路径。</param>
/// <param name="SizeBytes">文件字节数。</param>
public sealed record DbInfo(string Path, long SizeBytes);

/// <summary><c>status.get</c> 的返回。</summary>
/// <param name="Version">daemon 版本。</param>
/// <param name="StartedAt">启动时间（UTC）。</param>
/// <param name="UptimeSeconds">运行时秒数。</param>
/// <param name="Workspace">工作区根目录。</param>
/// <param name="Lanes">各 Lane 状态。</param>
/// <param name="UsageToday">今日用量。</param>
/// <param name="States">全部通用状态（心情/精力/思念等）。</param>
/// <param name="Db">数据库信息。</param>
/// <param name="LastError">最近一次全局错误。</param>
public sealed record StatusDto(
    string Version,
    DateTimeOffset StartedAt,
    long UptimeSeconds,
    string Workspace,
    IReadOnlyList<LaneStatus> Lanes,
    UsageSummary UsageToday,
    IReadOnlyList<StateDto> States,
    DbInfo Db,
    string? LastError);

/// <summary><c>daemon.status</c> 的轻量返回（菜单栏用）。</summary>
/// <param name="Version">daemon 版本。</param>
/// <param name="UptimeSeconds">运行时秒数。</param>
/// <param name="SocketPath">UDS 路径。</param>
/// <param name="Lanes">各 Lane 连接状态。</param>
public sealed record DaemonStatusDto(
    string Version,
    long UptimeSeconds,
    string SocketPath,
    IReadOnlyList<LaneStatus> Lanes);
