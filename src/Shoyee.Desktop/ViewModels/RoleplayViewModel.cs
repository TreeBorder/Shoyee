using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shoyee.Client.Rpc;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Options;
using Shoyee.Contracts.Rpc;
using Shoyee.Contracts.Text;
using Shoyee.Desktop.Conversations;
using Shoyee.Desktop.Rpc;
using Shoyee.Desktop.ViewModels.Items;

namespace Shoyee.Desktop.ViewModels;

/// <summary>
/// 角色模式：立绘舞台 / 气泡对话两种形态。
///
/// - 立绘：沉浸式，不显示其他 Lane 的历史，状态以悬浮 chip 呈现；
/// - 气泡：统一时间线，可按 Lane 切换查看（其它 RP Lane 只读），发送渠道固定桌面。
///
/// 连上 daemon 后时间线走 <c>lane.messages</c>，发送走 <c>turn.send</c>（<c>laneId=desktop:roleplay</c>），
/// 正文按 <c>turn.*</c> 事件流增量渲染；没连上时保持空时间线，不再默认塞入 Harem 导入数据。
/// </summary>
public partial class RoleplayViewModel : ConversationLayoutViewModel
{
    private readonly SynchronizationContext? _uiContext = SynchronizationContext.Current;

    private static readonly IReadOnlyDictionary<string, string> SceneNames =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["studio"] = "摄影棚",
            ["bedroom"] = "卧室",
            ["living_room"] = "客厅",
        };

    /// <summary>不展示的全局状态（内部参数，不是给人看的状态条）。</summary>
    private static readonly HashSet<string> HiddenStateKeys =
        new(StringComparer.Ordinal) { "voiceId", "mode" };

    private readonly Dictionary<string, List<RoleplayRow>> _laneRows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _laneNames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StateDto> _stateByKey = new(StringComparer.Ordinal);
    private readonly List<RoleplayRow> _liveRows = [];
    private readonly Dictionary<string, ChatMessageItem> _liveTools = new(StringComparer.Ordinal);
    private IRoleplayChatGateway? _gateway;
    private CancellationTokenSource? _loadCts;
    private ChatMessageItem? _streamingAssistant;
    private ChatMessageItem? _streamingThinking;
    private long? _streamingAssistantSortKey;
    private bool _isTurnRunning;

    /// <summary>本轮正文原文。用 StringBuilder 累计：<c>+=</c> 是 O(n²)，长回复会吃光内存。</summary>
    private readonly StringBuilder _rawStream = new();

    /// <summary>流式期间最多展示的正文长度（完整文本轮末才拆泡）。</summary>
    private const int StreamingDisplayChars = 4_000;

    /// <summary>流式增量合并队列的锁（读取线程写，UI 线程取）。</summary>
    private readonly object _streamLock = new();

    private readonly List<PendingStream> _pendingStream = [];
    private bool _streamFlushPosted;

    private long _liveSortKey = long.MaxValue / 2;
    private bool _loaded;

    /// <summary>装配一个空的角色页（真实内容在连上 daemon 后拉取）。</summary>
    public RoleplayViewModel()
    {
        _laneNames[LaneIds.DesktopRoleplay] = "桌面";
        Lanes.Add(new LaneItem
        {
            Id = LaneIds.DesktopRoleplay,
            Name = "桌面",
            Detail = "本机",
            Glyph = "▣",
            Connected = true,
            IsCurrent = true,
            IsDesktop = true,
        });
    }

    /// <summary>当前是否连着 daemon（真实时间线可发送）。</summary>
    public bool IsLive => _gateway is { IsConnected: true };

    /// <summary>桌面 Lane 是否有一轮在跑（决定显示终止按钮）。</summary>
    public bool IsTurnRunning
    {
        get => _isTurnRunning;
        private set
        {
            if (_isTurnRunning == value)
            {
                return;
            }

            _isTurnRunning = value;
            OnPropertyChanged();
        }
    }

    /// <summary>渠道列表（桌面 / Mattermost · 角色）。</summary>
    public ObservableCollection<LaneItem> Lanes { get; } = [];

    /// <summary>是否有多个 RP Lane（只有一个时头部不显示切换）。</summary>
    public bool HasMultipleLanes => Lanes.Count > 1;

    /// <summary>当前查看的 Lane id（发送始终写桌面 Lane）。</summary>
    public string CurrentLaneId { get; private set; } = LaneIds.DesktopRoleplay;

    /// <summary>当前查看的 Lane 展示名。</summary>
    public string CurrentLaneLabel => _laneNames.GetValueOrDefault(CurrentLaneId, "桌面");

    /// <summary>完整 RP 时间线（跨 Lane，按消息 id 排序）。</summary>
    public ObservableCollection<ChatMessageItem> Messages { get; } = [];

    /// <summary>导入后替换整条时间线（仍可在连上 daemon 后被真实数据替换）。</summary>
    /// <param name="content">导入结果。</param>
    public void ReplaceContent(RoleplayContent content)
    {
        _loadCts?.Cancel();
        _laneRows.Clear();
        _liveRows.Clear();
        _liveTools.Clear();
        ResetTurnState();

        var laneName = string.IsNullOrWhiteSpace(content.LaneName) ? "桌面" : content.LaneName;
        var laneId = Lanes.FirstOrDefault(static lane => lane.IsDesktop)?.Id ?? LaneIds.DesktopRoleplay;
        _laneNames[laneId] = laneName;
        _laneRows[laneId] = PrepareRows(content.Messages
            .Select((message, index) => new RoleplayRow(index, message))
            .ToList());

        Lanes.Clear();
        Lanes.Add(new LaneItem
        {
            Id = laneId,
            Name = laneName,
            Detail = "本机 · 导入",
            Glyph = "▣",
            Connected = true,
            IsCurrent = true,
            IsDesktop = true,
        });
        OnPropertyChanged(nameof(HasMultipleLanes));
        OnPropertyChanged(nameof(CurrentLaneLabel));

        States.Clear();
        foreach (var state in content.States)
        {
            States.Add(state);
        }

        CharacterName = content.CharacterName;
        SceneTitle = content.SceneTitle;
        SceneDetail = content.SceneDetail;

        RebuildTimeline();
    }

    /// <summary>连上 daemon 后挂上网关：拉 Lane、状态与各 Lane 历史，之后事件驱动渲染。</summary>
    /// <param name="gateway">角色模式 RPC 网关。</param>
    public void AttachGateway(IRoleplayChatGateway gateway)
    {
        if (ReferenceEquals(_gateway, gateway))
        {
            return;
        }

        DetachGateway();
        _gateway = gateway;
        gateway.EventReceived += OnGatewayEvent;
        OnPropertyChanged(nameof(IsLive));
        _ = RefreshAsync();
    }

    /// <summary>daemon 断开后摘掉网关（保留当前画面，不把已载入的消息清空）。</summary>
    public void DetachGateway()
    {
        if (_gateway is null)
        {
            return;
        }

        _gateway.EventReceived -= OnGatewayEvent;
        _gateway = null;
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;
        PendingApproval = null;
        ResetTurnState();
        OnPropertyChanged(nameof(IsLive));
    }

    /// <summary>连接状态变化后刷新 <see cref="IsLive"/> 的绑定。</summary>
    public void NotifyConnectionChanged() => OnPropertyChanged(nameof(IsLive));

    /// <summary>切到角色页时补一次加载（启动时 RP 连接可能比工作连接晚就绪）。</summary>
    public void EnsureLoaded()
    {
        OnPropertyChanged(nameof(IsLive));
        if (_loaded || _gateway is not { IsConnected: true })
        {
            return;
        }

        _ = RefreshAsync();
    }

    /// <summary>重新拉 Lane 列表、状态与各 Lane 最近一页历史。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (_gateway is not { IsConnected: true } gateway)
        {
            return;
        }

        _loadCts?.Cancel();
        _loadCts?.Dispose();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loadCts = cts;

        try
        {
            var status = await gateway.GetStatusAsync(cts.Token).ConfigureAwait(true);
            ApplyLanes(status.Lanes);
            ApplyStates(status.States);

            var loaded = new Dictionary<string, List<RoleplayRow>>(StringComparer.Ordinal);
            foreach (var lane in Lanes.ToList())
            {
                try
                {
                    var page = await gateway.GetLaneMessagesAsync(lane.Id, cancellationToken: cts.Token)
                        .ConfigureAwait(true);
                    loaded[lane.Id] = PrepareRows(
                        RoleplayProjection.ToRows(page.Items, lane.Name));
                }
                catch (Exception ex) when (ex is DaemonUnavailableException or RpcClientException)
                {
                    loaded[lane.Id] = [];
                }
            }

            _laneRows.Clear();
            foreach (var (laneId, rows) in loaded)
            {
                _laneRows[laneId] = rows;
            }

            // 重连后以服务端历史为准，丢掉断线前留下的临时流式行。
            _liveRows.Clear();
            _liveTools.Clear();
            ResetTurnState();
            _loaded = true;

            // 桌面 Lane 可能一条都没有（历史都在 IM Lane）：默认切到「全部渠道」，别让人以为记录没了。
            var total = _laneRows.Values.Sum(rows => rows.Count);
            var currentCount = _laneRows.GetValueOrDefault(CurrentLaneId)?.Count ?? 0;
            if (total > 0 && currentCount == 0)
            {
                ShowOtherLanes = true;
            }

            RebuildTimeline();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is DaemonUnavailableException or RpcClientException)
        {
            AddSystemRow($"角色时间线载入失败：{Describe(ex)}");
        }
        finally
        {
            if (ReferenceEquals(_loadCts, cts))
            {
                _loadCts = null;
            }

            cts.Dispose();
        }
    }

    /// <summary>当前是否只看桌面 Lane。</summary>
    public bool IsDesktopLaneSelected => !ShowOtherLanes
        && string.Equals(CurrentLaneId, LaneIds.DesktopRoleplay, StringComparison.Ordinal);

    /// <summary>当前是否显示全部 RP Lane。</summary>
    public bool IsAllLanesSelected => ShowOtherLanes;

    /// <summary>切回只看桌面 Lane。</summary>
    [RelayCommand]
    private void ShowDesktopLane() => SelectLane(LaneIds.DesktopRoleplay);

    /// <summary>切到全部 RP Lane 的合并时间线。</summary>
    [RelayCommand]
    private void ShowAllLanes() => ShowOtherLanes = true;

    /// <summary>按 Lane id 切换查看（只读；发送仍然写桌面 Lane）。</summary>
    /// <param name="laneId">Lane id。</param>
    [RelayCommand]
    private void SelectLane(string? laneId)
    {
        if (string.IsNullOrWhiteSpace(laneId) || !_laneNames.ContainsKey(laneId))
        {
            return;
        }

        CurrentLaneId = laneId;
        ShowOtherLanes = false;
        foreach (var lane in Lanes)
        {
            lane.IsCurrent = string.Equals(lane.Id, laneId, StringComparison.Ordinal);
        }

        OnPropertyChanged(nameof(CurrentLaneLabel));
        OnPropertyChanged(nameof(TimelineTitle));
        OnPropertyChanged(nameof(IsDesktopLaneSelected));
        OnPropertyChanged(nameof(IsAllLanesSelected));
        ApplyLaneFilter();
    }

    /// <summary>当前气泡视图实际显示的消息（按 Lane / 开关过滤）。</summary>
    public ObservableCollection<ChatMessageItem> VisibleMessages { get; } = [];

    /// <summary>立绘旁的悬浮状态条。</summary>
    public ObservableCollection<StateItem> States { get; } = [];

    /// <summary>待审批请求（RP 也会触发写操作审批）。</summary>
    [ObservableProperty]
    public partial ApprovalPromptItem? PendingApproval { get; set; }

    /// <summary>是否有待审批请求。</summary>
    public bool HasPendingApproval => PendingApproval is not null;

    /// <summary>是否立绘模式（默认气泡，立绘需手动切）。</summary>
    [ObservableProperty]
    public partial bool IsPortraitMode { get; set; }

    /// <summary>是否气泡 IM 模式。</summary>
    public bool IsImMode => !IsPortraitMode;

    /// <summary>气泡模式是否显示全部 RP Lane 的消息（立绘永远不显示）。</summary>
    [ObservableProperty]
    public partial bool ShowOtherLanes { get; set; }

    /// <summary>立绘大小倍率（1.0 = 标准；设置项持久化到 desktop.json）。</summary>
    [ObservableProperty]
    public partial double PortraitScale { get; set; } = 1.0;

    /// <summary>立绘大小的显示文案。</summary>
    public string PortraitScaleLabel => $"{Math.Round(PortraitScale * 100)}%";

    /// <summary>角色名。</summary>
    [ObservableProperty]
    public partial string CharacterName { get; set; } = "筱义";

    /// <summary>场景标题。</summary>
    [ObservableProperty]
    public partial string SceneTitle { get; set; } = "";

    /// <summary>场景细节。</summary>
    [ObservableProperty]
    public partial string SceneDetail { get; set; } = "";

    /// <summary>当前台词（时间线里最后一条助手正文）。</summary>
    [ObservableProperty]
    public partial string Dialogue { get; set; } = "";

    /// <summary>台词的时间 / 渠道标记。</summary>
    [ObservableProperty]
    public partial string DialogueMeta { get; set; } = "";

    /// <summary>输入框内容。</summary>
    [ObservableProperty]
    public partial string Draft { get; set; } = "";

    /// <summary>输入框占位文案。</summary>
    public override string ComposerWatermark => $"说点什么…  {ShortcutHint}";

    /// <summary>气泡视图的标题。</summary>
    public string TimelineTitle => ShowOtherLanes ? "全部 Lane" : CurrentLaneLabel;

    /// <summary>切到立绘模式。</summary>
    [RelayCommand]
    private void ShowPortrait() => IsPortraitMode = true;

    /// <summary>切到气泡对话模式。</summary>
    [RelayCommand]
    private void ShowIm() => IsPortraitMode = false;

    /// <summary>发送：真实连接走 <c>turn.send</c>（写桌面 Lane），否则只做本地回显。</summary>
    [RelayCommand]
    private async Task SendAsync()
    {
        var text = Draft.Trim();
        if (text.Length == 0)
        {
            return;
        }

        Draft = "";

        // 发送渠道固定桌面：只在看单个 IM Lane 时切回桌面；「全部 Lane」视图继续合并显示。
        if (!ShowOtherLanes
            && !string.Equals(CurrentLaneId, LaneIds.DesktopRoleplay, StringComparison.Ordinal))
        {
            SelectLane(LaneIds.DesktopRoleplay);
        }

        var laneName = _laneNames.GetValueOrDefault(LaneIds.DesktopRoleplay, "桌面");
        AppendLiveRow(new ChatMessageItem
        {
            Kind = ChatEntryKind.User,
            Text = text,
            Time = DateTimeOffset.Now.ToString("HH:mm"),
            LaneName = laneName,
        });

        if (_gateway is not { IsConnected: true } gateway)
        {
            return;
        }

        try
        {
            await gateway.SendTurnAsync(LaneIds.DesktopRoleplay, text).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is DaemonUnavailableException or RpcClientException)
        {
            AddSystemRow($"发送失败：{Describe(ex)}");
        }
    }

    /// <summary>终止桌面 Lane 当前轮：停就停，连已合并但还没开跑的消息一起丢掉。</summary>
    [RelayCommand]
    private async Task StopAsync()
    {
        if (_gateway is not { IsConnected: true } gateway)
        {
            return;
        }

        try
        {
            await gateway.CancelTurnAsync(LaneIds.DesktopRoleplay).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is DaemonUnavailableException or RpcClientException)
        {
            AddSystemRow($"终止失败：{Describe(ex)}");
        }
    }

    /// <summary>批准本次调用。</summary>
    [RelayCommand]
    private Task ApproveApprovalAsync() => RespondApprovalAsync(ApprovalDecision.Approved);

    /// <summary>拒绝本次调用。</summary>
    [RelayCommand]
    private Task DenyApprovalAsync() => RespondApprovalAsync(ApprovalDecision.Denied);

    /// <summary>本 Lane 内总是允许。</summary>
    [RelayCommand]
    private Task AlwaysApprovalAsync() => RespondApprovalAsync(ApprovalDecision.Always);

    /// <inheritdoc />
    protected override void ApplyMessageFilter() => ApplyLaneFilter();

    partial void OnIsPortraitModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsImMode));
        if (value)
        {
            // 立绘模式不展示其他 Lane 的消息
            ShowOtherLanes = false;
        }
    }

    partial void OnShowOtherLanesChanged(bool value)
    {
        OnPropertyChanged(nameof(TimelineTitle));
        OnPropertyChanged(nameof(IsDesktopLaneSelected));
        OnPropertyChanged(nameof(IsAllLanesSelected));
        ApplyLaneFilter();
    }

    partial void OnPortraitScaleChanged(double value)
        => OnPropertyChanged(nameof(PortraitScaleLabel));

    partial void OnPendingApprovalChanged(ApprovalPromptItem? value)
        => OnPropertyChanged(nameof(HasPendingApproval));

    private void ApplyLaneFilter()
    {
        var laneName = CurrentLaneLabel;
        foreach (var message in Messages)
        {
            message.IsWideBubble = IsWideBubbles;
        }

        SyncVisible(
            VisibleMessages,
            Messages,
            message => (ShowOtherLanes || string.Equals(message.LaneName, laneName, StringComparison.Ordinal))
                        && (ShowToolCalls || !message.IsTool)
                        && (ShowReasoning || !message.IsReasoning)
                        && (ShowSystemInputs || !message.IsSystemInput));

        UpdateDialogue();
    }

    private void UpdateDialogue()
    {
        var last = VisibleMessages.LastOrDefault(static message => message.IsAssistant);
        // 底部对话框只显示三行，不必把整段正文塞进去重新排版
        Dialogue = last is null ? "" : ConversationText.Clip(last.Text, 800);
        DialogueMeta = last is null ? "" : $"{last.LaneName ?? CurrentLaneLabel} · {last.Time}";
    }

    private void RebuildTimeline()
    {
        var rows = new List<RoleplayRow>();
        foreach (var lane in _laneRows.Values)
        {
            rows.AddRange(lane);
        }

        rows.AddRange(_liveRows);

        Messages.Clear();
        foreach (var row in rows.OrderBy(static row => row.SortKey))
        {
            Messages.Add(row.Item);
            if (row.Item.IsImage)
            {
                _ = row.Item.LoadImageAsync();
            }
        }

        ApplyLaneFilter();
    }

    private RoleplayRow AppendLiveRow(ChatMessageItem item, long? sortKey = null)
    {
        var row = new RoleplayRow(sortKey ?? NextLiveSortKey(), item);
        _liveRows.Add(row);
        Messages.Add(item);
        ApplyLaneFilter();
        return row;
    }

    private long NextLiveSortKey() => _liveSortKey++;

    private void ApplyLanes(IReadOnlyList<LaneStatus> statuses)
    {
        var rpLanes = statuses
            .Where(static lane => lane.Mode == Mode.Roleplay)
            .OrderBy(static lane => lane.Id == LaneIds.DesktopRoleplay ? 0 : 1)
            .ThenBy(static lane => lane.DisplayName, StringComparer.Ordinal)
            .ToList();

        if (rpLanes.Count == 0 || rpLanes.All(static lane => lane.Id != LaneIds.DesktopRoleplay))
        {
            // 桌面 Lane 恒存在；服务端没给也补一个，避免页面没有可看的渠道。
            rpLanes.Insert(0, new LaneStatus(
                LaneIds.DesktopRoleplay,
                LaneChannel.Desktop,
                Mode.Roleplay,
                "桌面",
                Connected: true,
                ActiveSessionId: null,
                LastError: null));
        }

        if (rpLanes.All(lane => lane.Id != CurrentLaneId))
        {
            CurrentLaneId = LaneIds.DesktopRoleplay;
        }

        Lanes.Clear();
        _laneNames.Clear();
        foreach (var lane in rpLanes)
        {
            var name = DaemonProjection.LaneName(lane.Channel, lane.Mode);
            _laneNames[lane.Id] = name;
            Lanes.Add(new LaneItem
            {
                Id = lane.Id,
                Name = name,
                Detail = lane.Connected
                    ? "在线"
                    : string.IsNullOrWhiteSpace(lane.LastError) ? "未连接" : lane.LastError,
                Glyph = lane.Channel == LaneChannel.Desktop ? "▣" : "✉",
                Connected = lane.Connected,
                IsDesktop = lane.Channel == LaneChannel.Desktop,
                IsCurrent = string.Equals(lane.Id, CurrentLaneId, StringComparison.Ordinal),
            });
        }

        OnPropertyChanged(nameof(HasMultipleLanes));
        OnPropertyChanged(nameof(CurrentLaneLabel));
        OnPropertyChanged(nameof(TimelineTitle));
    }

    private void ApplyStates(IReadOnlyList<StateDto> states)
    {
        _stateByKey.Clear();
        foreach (var state in states)
        {
            _stateByKey[state.Key] = state;
        }

        RebuildStates();
    }

    private void RebuildStates()
    {
        States.Clear();
        foreach (var state in _stateByKey.Values)
        {
            if (HiddenStateKeys.Contains(state.Key))
            {
                continue;
            }

            var numeric = TryNumber(state, out var number);
            States.Add(new StateItem
            {
                Name = state.Name,
                Value = numeric
                    ? Math.Round(number).ToString(CultureInfo.InvariantCulture)
                    : state.Value,
                Percent = numeric ? Math.Clamp(number, 0, 100) : 0,
                IsNumeric = numeric,
                IsGold = state.Key is "longing" or "master_index",
                IsMint = state.Key is "energy" or "loneliness",
            });
        }

        var scene = _stateByKey.GetValueOrDefault("scene")?.Value;
        if (!string.IsNullOrWhiteSpace(scene))
        {
            SceneTitle = SceneNames.GetValueOrDefault(scene, scene);
        }

        var outfit = _stateByKey.GetValueOrDefault("outfit")?.Value;
        SceneDetail = string.IsNullOrWhiteSpace(outfit) ? "" : $"衣装 · {outfit}";
    }

    private static bool TryNumber(StateDto state, out double number)
    {
        if (!double.TryParse(state.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
        {
            return false;
        }

        // energy 这类状态用 0–1 小数，展示前换算成百分比。
        if (number is >= 0 and <= 1
            && state.Description is { } description
            && description.Contains("0–1", StringComparison.Ordinal))
        {
            number *= 100;
        }

        return true;
    }

    /// <summary>RPC 事件在读取线程上来，统一切回 UI 线程再动集合。</summary>
    private void OnGatewayEvent(string name, JsonElement payload)
    {
        if (_uiContext is null || ReferenceEquals(SynchronizationContext.Current, _uiContext))
        {
            HandleSafely(name, payload);
            return;
        }

        if (name is RpcEvents.TurnDelta or RpcEvents.TurnThinking)
        {
            QueueStreamEvent(name, payload);
            return;
        }

        _uiContext.Post(_ => HandleSafely(name, payload), null);
    }

    /// <summary>
    /// 流式增量的合并队列：模型按 token 出增量，逐条 Post 到 UI 线程会把调度队列灌满；
    /// 同一段的增量先拼起来，一帧只回一次 UI。
    /// </summary>
    private void QueueStreamEvent(string name, JsonElement payload)
    {
        bool thinking;
        string laneId;
        string sessionId;
        string delta;
        if (name == RpcEvents.TurnThinking)
        {
            if (payload.Deserialize<TurnThinkingEvent>(JsonWire.Options) is not { } thinkingEvent)
            {
                return;
            }

            thinking = true;
            laneId = thinkingEvent.LaneId ?? "";
            sessionId = thinkingEvent.SessionId;
            delta = thinkingEvent.Delta;
        }
        else
        {
            if (payload.Deserialize<TurnDeltaEvent>(JsonWire.Options) is not { } deltaEvent)
            {
                return;
            }

            thinking = false;
            laneId = deltaEvent.LaneId ?? "";
            sessionId = deltaEvent.SessionId;
            delta = deltaEvent.Delta;
        }

        if (string.IsNullOrEmpty(delta))
        {
            return;
        }

        lock (_streamLock)
        {
            if (_pendingStream.Count > 0
                && _pendingStream[^1].Thinking == thinking
                && string.Equals(_pendingStream[^1].SessionId, sessionId, StringComparison.Ordinal))
            {
                _pendingStream[^1].Text.Append(delta);
            }
            else
            {
                _pendingStream.Add(new PendingStream(laneId, sessionId, thinking, new StringBuilder(delta)));
            }

            if (_streamFlushPosted)
            {
                return;
            }

            _streamFlushPosted = true;
        }

        _uiContext?.Post(static state => ((RoleplayViewModel)state!).FlushStreamQueue(), this);
    }

    /// <summary>把合并队列在 UI 线程上一次应用完（保持增量到达的先后顺序）。</summary>
    private void FlushStreamQueue()
    {
        List<PendingStream> batch;
        lock (_streamLock)
        {
            batch = [.. _pendingStream];
            _pendingStream.Clear();
            _streamFlushPosted = false;
        }

        foreach (var chunk in batch)
        {
            ApplyStreamChunk(chunk.LaneId, chunk.SessionId, chunk.Thinking, chunk.Text.ToString());
        }
    }

    private void ApplyStreamChunk(string laneId, string sessionId, bool thinking, string text)
    {
        if (LaneOf(laneId, sessionId) != LaneIds.DesktopRoleplay)
        {
            return;
        }

        if (thinking)
        {
            if (_streamingThinking is null)
            {
                _streamingThinking = new ChatMessageItem
                {
                    Kind = ChatEntryKind.Reasoning,
                    Time = DateTimeOffset.Now.ToString("HH:mm"),
                    LaneName = DesktopLaneName,
                    IsStreaming = true,
                };
                AppendLiveRow(_streamingThinking);
            }

            _streamingThinking.Append(text);
            return;
        }

        if (_streamingThinking is not null)
        {
            _streamingThinking.IsStreaming = false;
        }

        _streamingThinking = null;
        _rawStream.Append(text);

        // 流式期间只展示开头一段：气泡和底部对话框都是短预览，
        // 轮末 FinalizeAssistant 才按完整文本清洗、拆泡。
        EnsureStreamingAssistant().Text = MessageText.CleanReply(
            _rawStream.ToString(0, Math.Min(_rawStream.Length, StreamingDisplayChars)));
        UpdateDialogue();
    }

    private void HandleSafely(string name, JsonElement payload)
    {
        try
        {
            HandleGatewayEvent(name, payload);
        }
        catch (JsonException)
        {
            // 服务端新增字段/事件不影响旧客户端
        }
    }

    private void HandleGatewayEvent(string name, JsonElement payload)
    {
        switch (name)
        {
            case RpcEvents.TurnStarted:
            {
                var e = payload.Deserialize<TurnStartedEvent>(JsonWire.Options);
                if (e is null || LaneOf(e.LaneId, e.SessionId) != LaneIds.DesktopRoleplay)
                {
                    return;
                }

                ResetTurnState();
                IsTurnRunning = true;
                return;
            }

            case RpcEvents.TurnDelta:
            {
                var e = payload.Deserialize<TurnDeltaEvent>(JsonWire.Options);
                if (e is null)
                {
                    return;
                }

                ApplyStreamChunk(e.LaneId ?? "", e.SessionId, thinking: false, e.Delta);
                return;
            }

            case RpcEvents.TurnThinking:
            {
                var e = payload.Deserialize<TurnThinkingEvent>(JsonWire.Options);
                if (e is null)
                {
                    return;
                }

                ApplyStreamChunk(e.LaneId ?? "", e.SessionId, thinking: true, e.Delta);
                return;
            }

            case RpcEvents.TurnTool:
            {
                var e = payload.Deserialize<TurnToolEvent>(JsonWire.Options);
                if (e is null || LaneOf(e.LaneId, e.SessionId) != LaneIds.DesktopRoleplay)
                {
                    return;
                }

                ApplyToolEvent(e);
                return;
            }

            case RpcEvents.TurnCompleted:
            {
                var e = payload.Deserialize<TurnCompletedEvent>(JsonWire.Options);
                if (e is null)
                {
                    return;
                }

                var laneId = LaneOf(e.LaneId, e.SessionId);
                if (laneId != LaneIds.DesktopRoleplay)
                {
                    _ = RefreshLaneAsync(laneId);
                    return;
                }

                FinalizeAssistant(e.Attachments);
                IsTurnRunning = false;
                return;
            }

            case RpcEvents.TurnFailed:
            {
                var e = payload.Deserialize<TurnFailedEvent>(JsonWire.Options);
                if (e is null)
                {
                    return;
                }

                var laneId = LaneOf(e.LaneId, e.SessionId);
                if (laneId != LaneIds.DesktopRoleplay)
                {
                    _ = RefreshLaneAsync(laneId);
                    return;
                }

                ResetTurnState();
                IsTurnRunning = false;
                AddSystemRow(e.Code == TurnFailureCodes.Cancelled
                    ? "本轮已取消"
                    : $"本轮失败：{e.Message}");
                return;
            }

            case RpcEvents.MessageAppended:
            {
                var e = payload.Deserialize<MessageAppendedEvent>(JsonWire.Options);
                if (e is null)
                {
                    return;
                }

                var laneId = LaneOf(e.LaneId, null);
                var laneName = _laneNames.GetValueOrDefault(laneId);
                var rows = PrepareRows(RoleplayProjection.ToRows([e.Message], laneName));
                if (!_laneRows.TryGetValue(laneId, out var existing))
                {
                    existing = [];
                    _laneRows[laneId] = existing;
                }

                existing.AddRange(rows);
                RebuildTimeline();
                return;
            }

            case RpcEvents.ApprovalRequest:
            {
                var e = payload.Deserialize<ApprovalRequestDto>(JsonWire.Options);
                if (e is null || LaneOf(e.LaneId, e.SessionId) != LaneIds.DesktopRoleplay)
                {
                    return;
                }

                PendingApproval = new ApprovalPromptItem
                {
                    ApprovalId = e.ApprovalId,
                    TurnId = e.TurnId,
                    ToolName = e.ToolName,
                    Command = e.Command,
                    Arguments = FormatArguments(e.Arguments),
                    ExpiresText = e.ExpiresAt.ToLocalTime().ToString("HH:mm"),
                };
                return;
            }

            case RpcEvents.ApprovalResolved:
            {
                var e = payload.Deserialize<ApprovalResolvedDto>(JsonWire.Options);
                if (e is not null && PendingApproval?.ApprovalId == e.ApprovalId)
                {
                    PendingApproval = null;
                }

                return;
            }

            case RpcEvents.StateUpdated:
            {
                var e = payload.Deserialize<StateDto>(JsonWire.Options);
                if (e is not null)
                {
                    _stateByKey[e.Key] = e;
                    RebuildStates();
                }

                return;
            }

            case RpcEvents.PhotoReady:
            {
                var e = payload.Deserialize<PhotoReadyEvent>(JsonWire.Options);
                if (e is null || e.Assets.Count == 0)
                {
                    return;
                }

                var attachments = e.Assets
                    .Where(static a => !string.IsNullOrWhiteSpace(a.Path))
                    .Select(static a => new AttachmentItem
                    {
                        Kind = AttachmentKind.Image,
                        FileName = Path.GetFileName(a.Path),
                        Meta = a.SizeBytes >= 1024 * 1024
                            ? $"{a.SizeBytes / 1024d / 1024d:0.#} MB"
                            : $"{Math.Max(1, a.SizeBytes / 1024)} KB",
                        Path = a.Path,
                    })
                    .ToList();

                if (attachments.Count == 0)
                {
                    return;
                }

                // 异步出图不补会话消息：挂回发起它的 camera / gallery 工具行
                // （工具结果里带了任务 id，按 id 对上是哪一次调用）。
                if (FindToolRow(e.TaskId) is { } tool)
                {
                    var known = tool.Attachments
                        .Select(static a => a.Path)
                        .OfType<string>()
                        .ToHashSet(StringComparer.Ordinal);
                    var fresh = attachments
                        .Where(a => a.Path is { Length: > 0 } path && !known.Contains(path))
                        .ToList();
                    if (fresh.Count > 0)
                    {
                        tool.Attachments = [.. tool.Attachments, .. fresh];
                        foreach (var attachment in fresh)
                        {
                            _ = attachment.LoadImageAsync();
                        }
                    }

                    tool.IsExpanded = true;
                    return;
                }

                // 找不到工具行（例如旧记录里没有任务 id）：退化成时间线里的临时图片气泡，至少别丢图。
                var laneId = LaneOf(e.LaneId, e.SessionId);
                var laneName = _laneNames.GetValueOrDefault(laneId, DesktopLaneName);
                var time = DateTimeOffset.Now.ToString("HH:mm");
                foreach (var attachment in attachments)
                {
                    var item = new ChatMessageItem
                    {
                        Kind = ChatEntryKind.Image,
                        ImageSource = attachment.Path,
                        Time = time,
                        LaneName = laneName,
                    };
                    PrepareMessage(item);
                    AppendLiveRow(item);
                    _ = item.LoadImageAsync();
                }

                return;
            }

            case RpcEvents.LaneStatus:
            {
                var e = payload.Deserialize<LaneStatus>(JsonWire.Options);
                if (e is null || e.Mode != Mode.Roleplay)
                {
                    return;
                }

                for (var i = 0; i < Lanes.Count; i++)
                {
                    var lane = Lanes[i];
                    if (!string.Equals(lane.Id, e.Id, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    Lanes[i] = new LaneItem
                    {
                        Id = lane.Id,
                        Name = lane.Name,
                        Detail = e.Connected
                            ? "在线"
                            : string.IsNullOrWhiteSpace(e.LastError) ? "未连接" : e.LastError,
                        Glyph = lane.Glyph,
                        Connected = e.Connected,
                        IsDesktop = lane.IsDesktop,
                        IsCurrent = lane.IsCurrent,
                    };
                    break;
                }

                return;
            }
        }
    }

    private void ApplyToolEvent(TurnToolEvent e)
    {
        // 工具调用开始/结束都意味着上一段思考与正文已经收尾。
        // 关掉 IsStreaming，让这一段立即补 Markdown，而不是等整个回合结束。
        if (_streamingThinking is not null)
        {
            _streamingThinking.IsStreaming = false;
        }

        if (_streamingAssistant is not null)
        {
            _streamingAssistant.IsStreaming = false;
        }

        _streamingThinking = null;
        _streamingAssistant = null;
        _streamingAssistantSortKey = null;
        _rawStream.Clear();

        ChatMessageItem? row = null;
        if (!string.IsNullOrWhiteSpace(e.CallId))
        {
            _liveTools.TryGetValue(e.CallId, out row);
        }

        if (e.Phase == TurnToolPhases.Start)
        {
            var item = new ChatMessageItem
            {
                Kind = ChatEntryKind.Tool,
                Text = e.Summary ?? "",
                ToolName = e.ToolName,
                ToolCallId = e.CallId,
                Detail = e.Arguments ?? "",
                Time = DateTimeOffset.Now.ToString("HH:mm"),
                LaneName = DesktopLaneName,
            };
            AppendLiveRow(item);
            if (!string.IsNullOrWhiteSpace(e.CallId))
            {
                _liveTools[e.CallId] = item;
            }

            return;
        }

        row ??= Messages.LastOrDefault(static message => message.IsTool);
        if (row is not null && e.Result is { Length: > 0 })
        {
            row.Result = e.Result;
        }
    }

    private ChatMessageItem EnsureStreamingAssistant()
    {
        if (_streamingAssistant is not null)
        {
            return _streamingAssistant;
        }

        var item = new ChatMessageItem
        {
            Kind = ChatEntryKind.Assistant,
            Time = DateTimeOffset.Now.ToString("HH:mm"),
            LaneName = DesktopLaneName,
            IsStreaming = true,
        };
        PrepareMessage(item);
        _streamingAssistantSortKey = NextLiveSortKey();
        AppendLiveRow(item, _streamingAssistantSortKey);
        _streamingAssistant = item;
        return item;
    }

    /// <summary>轮末把流式正文按 RP 规则拆成气泡，并挂上本轮附件。</summary>
    private void FinalizeAssistant(IReadOnlyList<MessageAttachment>? attachments)
    {
        var assistant = _streamingAssistant;
        var time = assistant?.Time ?? DateTimeOffset.Now.ToString("HH:mm");
        var sortKey = _streamingAssistantSortKey ?? NextLiveSortKey();
        var attachmentsItems = DaemonProjection.ToAttachments(attachments);

        if (assistant is not null)
        {
            _liveRows.RemoveAll(row => ReferenceEquals(row.Item, assistant));
        }

        var rows = RoleplayProjection.ToBubbleRows(
            sortKey,
            MessageText.CleanReply(_rawStream.ToString()),
            time,
            DesktopLaneName,
            attachmentsItems);
        _liveRows.AddRange(PrepareRows(rows));

        ResetTurnState();
        RebuildTimeline();
    }

    private async Task RefreshLaneAsync(string laneId)
    {
        if (_gateway is not { IsConnected: true } gateway || laneId.Length == 0)
        {
            return;
        }

        try
        {
            var page = await gateway.GetLaneMessagesAsync(laneId).ConfigureAwait(true);
            _laneRows[laneId] = PrepareRows(RoleplayProjection
                .ToRows(page.Items, _laneNames.GetValueOrDefault(laneId)));
            RebuildTimeline();
        }
        catch (Exception ex) when (ex is DaemonUnavailableException or RpcClientException)
        {
            // 只读视图刷新失败不打断当前对话
        }
    }

    private async Task RespondApprovalAsync(ApprovalDecision decision)
    {
        var prompt = PendingApproval;
        if (prompt is null || _gateway is not { IsConnected: true } gateway)
        {
            return;
        }

        PendingApproval = null;
        try
        {
            await gateway.RespondApprovalAsync(prompt.ApprovalId, decision).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is DaemonUnavailableException or RpcClientException)
        {
            AddSystemRow($"审批答复失败：{Describe(ex)}");
        }
    }

    private void AddSystemRow(string text)
    {
        AppendLiveRow(new ChatMessageItem
        {
            Kind = ChatEntryKind.System,
            Text = text,
            Time = DateTimeOffset.Now.ToString("HH:mm"),
            LaneName = DesktopLaneName,
        });
    }

    private void ResetTurnState()
    {
        if (_streamingAssistant is not null)
        {
            _streamingAssistant.IsStreaming = false;
        }

        if (_streamingThinking is not null)
        {
            _streamingThinking.IsStreaming = false;
        }

        _streamingAssistant = null;
        _streamingThinking = null;
        _streamingAssistantSortKey = null;
        _rawStream.Clear();
        _liveTools.Clear();
    }

    private string DesktopLaneName => _laneNames.GetValueOrDefault(LaneIds.DesktopRoleplay, "桌面");

    /// <summary>按异步任务 id 找发起它的工具行（结果文本里带了任务 id）。</summary>
    private ChatMessageItem? FindToolRow(string taskId)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return null;
        }

        foreach (var row in _laneRows.Values.SelectMany(static rows => rows).Concat(_liveRows))
        {
            if (row.Item.IsTool
                && row.Item.ResultText.Contains(taskId, StringComparison.Ordinal))
            {
                return row.Item;
            }
        }

        return null;
    }

    private List<RoleplayRow> PrepareRows(IReadOnlyList<RoleplayRow> rows)
    {
        foreach (var row in rows)
        {
            PrepareMessage(row.Item);
        }

        return rows.ToList();
    }

    /// <summary>给助手正文挂上「播放」处理器（没连 daemon 时不挂）。</summary>
    private void PrepareMessage(ChatMessageItem item)
    {
        if (item.IsAssistant)
        {
            item.SpeechSynthesis = SpeakMessageAsync;
        }
    }

    private async Task SpeakMessageAsync(ChatMessageItem item)
    {
        if (_gateway is not { IsConnected: true } gateway)
        {
            throw new DaemonUnavailableException("角色模式未连接 daemon，无法合成语音");
        }

        var audio = await gateway.SynthesizeSpeechAsync(item.Text).ConfigureAwait(true);
        await LocalMediaPlayer.PlayAsync(audio.Path).ConfigureAwait(true);
    }

    private static string LaneOf(string? laneId, string? sessionId)
        => string.IsNullOrWhiteSpace(laneId) ? sessionId ?? "" : laneId;

    private static string? FormatArguments(JsonElement? arguments)
        => arguments is { ValueKind: not JsonValueKind.Undefined and not JsonValueKind.Null } value
            ? JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonWire.Options) { WriteIndented = true })
            : null;

    private static string Describe(Exception ex) => ex switch
    {
        RpcClientException rpc => rpc.Message,
        _ => ex.Message,
    };

    /// <summary>待合并的一条流式增量（同一 Lane / 会话、同一段落）。</summary>
    private sealed record PendingStream(string LaneId, string SessionId, bool Thinking, StringBuilder Text);
}
