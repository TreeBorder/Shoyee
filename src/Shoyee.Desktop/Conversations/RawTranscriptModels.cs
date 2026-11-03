using System.Text.Json;

namespace Shoyee.Desktop.Conversations;

/// <summary>Secretary 会话快照：数据库里的原始行，正文由读取器解析。</summary>
internal sealed class WorkRawFile
{
    /// <summary>会话列表。</summary>
    public List<WorkRawSession> Sessions { get; set; } = [];
}

/// <summary>一个 Secretary 会话的元数据与原始消息行。</summary>
internal sealed class WorkRawSession
{
    /// <summary>会话 id。</summary>
    public string Id { get; set; } = "";

    /// <summary>列表标题。</summary>
    public string Title { get; set; } = "";

    /// <summary>来源渠道展示名。</summary>
    public string Source { get; set; } = "桌面";

    /// <summary>是否外部渠道。</summary>
    public bool External { get; set; }

    /// <summary>Lane id。</summary>
    public string LaneId { get; set; } = "";

    /// <summary>创建时间。</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>最后活跃时间。</summary>
    public DateTimeOffset LastActivityAt { get; set; }

    /// <summary>是否置顶。</summary>
    public bool Pinned { get; set; }

    /// <summary>是否归档。</summary>
    public bool Archived { get; set; }

    /// <summary>数据库里的消息总条数（含工具结果）。</summary>
    public int MessageCount { get; set; }

    /// <summary>上轮 LLM 调用的输入 token（0 = provider 没回 usage）。</summary>
    public long LastInputTokens { get; set; }

    /// <summary>上轮输入中命中缓存的 token。</summary>
    public long LastCachedInputTokens { get; set; }

    /// <summary>快照里的原始消息行。</summary>
    public List<WorkRawMessage> Messages { get; set; } = [];
}

/// <summary>Secretary 的一条原始消息行。</summary>
internal sealed class WorkRawMessage
{
    /// <summary>user / assistant / tool。</summary>
    public string Role { get; set; } = "";

    /// <summary>纯文本内容（用户消息主要看这个）。</summary>
    public string Content { get; set; } = "";

    /// <summary>框架序列化后的消息 JSON（assistant 的 contents 在这里）。</summary>
    public string? MessageJson { get; set; }

    /// <summary>入库时间。</summary>
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Harem 会话快照：rounds.json 的原始轮次 + state.json。</summary>
internal sealed class RoleplayRawFile
{
    /// <summary>角色名。</summary>
    public string AgentName { get; set; } = "筱义";

    /// <summary>state.json 原样内容。</summary>
    public List<RoleplayRawState> State { get; set; } = [];

    /// <summary>原始轮次（RoundId / Request / Response）。</summary>
    public List<JsonElement> Rounds { get; set; } = [];
}

/// <summary>Harem 状态项。</summary>
internal sealed class RoleplayRawState
{
    /// <summary>状态键。</summary>
    public string Key { get; set; } = "";

    /// <summary>展示名。</summary>
    public string Name { get; set; } = "";

    /// <summary>值。</summary>
    public string Value { get; set; } = "";
}
