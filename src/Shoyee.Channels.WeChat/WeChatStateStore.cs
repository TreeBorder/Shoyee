using System.Text.Json;

namespace Shoyee.Channels.WeChat;

/// <summary>
/// 微信渠道状态（<c>{workspace}/wechat/state.json</c>）：<c>getupdates</c> 游标 +
/// 每个 peer 的 context_token + typing 票据。
///
/// 文件即状态、可人工清零；整文件替换写盘。
/// </summary>
internal sealed class WeChatStateStore
{
    private readonly Lock _gate = new();
    private readonly string _path;
    private readonly State _state;

    private sealed class State
    {
        public string GetUpdatesBuf { get; set; } = string.Empty;
        public Dictionary<string, string> ContextTokens { get; set; } = new();
        public Dictionary<string, Ticket> TypingTickets { get; set; } = new();
    }

    private sealed class Ticket
    {
        public string Value { get; set; } = string.Empty;
        public long ExpiresAtMs { get; set; }
    }

    /// <summary>打开（或初始化）状态文件。</summary>
    /// <param name="workspaceRoot">工作区根目录。</param>
    public WeChatStateStore(string workspaceRoot)
    {
        _path = Path.Combine(workspaceRoot, "wechat", "state.json");
        _state = File.Exists(_path)
            ? JsonSerializer.Deserialize<State>(File.ReadAllText(_path)) ?? new State()
            : new State();
    }

    /// <summary><c>getupdates</c> 游标。</summary>
    public string SyncBuf
    {
        get { lock (_gate) { return _state.GetUpdatesBuf; } }
        set
        {
            lock (_gate)
            {
                _state.GetUpdatesBuf = value;
                Save();
            }
        }
    }

    /// <summary>取 peer 的最新 context_token。</summary>
    public string? GetContextToken(string peer)
    {
        lock (_gate)
        {
            return _state.ContextTokens.TryGetValue(peer, out var token) ? token : null;
        }
    }

    /// <summary>缓存 peer 的最新 context_token（来消息/发送回执刷新）。</summary>
    public void SetContextToken(string peer, string token)
    {
        lock (_gate)
        {
            _state.ContextTokens[peer] = token;
            Save();
        }
    }

    /// <summary>取未过期的 typing 票据。</summary>
    public string? GetTypingTicket(string peer)
    {
        lock (_gate)
        {
            return _state.TypingTickets.TryGetValue(peer, out var ticket)
                && ticket.ExpiresAtMs > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    ? ticket.Value
                    : null;
        }
    }

    /// <summary>缓存 typing 票据（约 600s 有效；过期后 stop 会失灵——务必成对调用）。</summary>
    public void SetTypingTicket(string peer, string ticket, TimeSpan ttl)
    {
        lock (_gate)
        {
            _state.TypingTickets[peer] = new Ticket
            {
                Value = ticket,
                ExpiresAtMs = (DateTimeOffset.UtcNow + ttl).ToUnixTimeMilliseconds(),
            };
            Save();
        }
    }

    private void Save()
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(_path, JsonSerializer.Serialize(_state));
    }
}
