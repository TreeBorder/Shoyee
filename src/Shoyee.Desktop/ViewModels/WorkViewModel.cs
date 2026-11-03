using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using Shoyee.Client.Rpc;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Rpc;
using Shoyee.Desktop.Conversations;
using Shoyee.Desktop.Rpc;
using Shoyee.Desktop.ViewModels.Items;

namespace Shoyee.Desktop.ViewModels;

/// <summary>
/// 工作模式：中间聊天 + 右侧可折叠状态栏；左侧会话列表在 MainWindow 的共享侧栏里。
///
/// 连上 daemon 后会话列表与历史走 <c>session.list</c> / <c>session.messages</c>，
/// 发送走 <c>turn.send</c> 并按 <c>turn.*</c> 事件流增量渲染；没连上时仍显示随包的示例会话。
/// </summary>
public partial class WorkViewModel : ConversationLayoutViewModel
{
    private readonly SynchronizationContext? _uiContext = SynchronizationContext.Current;

    private SessionTimeFilter _filter = SessionTimeFilter.All;
    private int _contextLimitTokens = 1_000_000;
    private int _contextUsedTokens;
    private int _bodyTokens;
    private int _reasoningTokens;
    private int _toolTokens;
    private long _lastInputTokens;
    private long _lastCachedInputTokens;
    private ContextUsage? _contextUsage;
    private string _mainModelName = "deepseek-flash";
    private string _mainModelBaseUrl = "https://api.deepseek.com";
    private IWorkChatGateway? _gateway;
    private CancellationTokenSource? _loadCts;
    private ChatMessageItem? _streamingAssistant;
    private ChatMessageItem? _streamingThinking;
    private bool _isTurnRunning;
    private readonly Dictionary<string, ChatMessageItem> _liveTools = new(StringComparer.Ordinal);

    /// <summary>流式增量合并队列的锁（读取线程写，UI 线程取）。</summary>
    private readonly object _streamLock = new();

    private readonly List<PendingStream> _pendingStream = [];
    private bool _streamFlushPosted;

    /// <summary>装配随程序打包的示例会话。</summary>
    public WorkViewModel()
    {
        States =
        [
            new StateItem { Name = "心情", Value = "平静", IsNumeric = false },
            new StateItem { Name = "精力", Value = "72", Percent = 72, IsNumeric = true, IsMint = true },
            new StateItem { Name = "思念", Value = "64", Percent = 64, IsNumeric = true, IsGold = true },
        ];

        ReplaceSessions(SecretaryTranscriptReader.ReadSessions());
    }

    /// <summary>当前是否连着 daemon（真实会话可发送）。</summary>
    public bool IsLive => _gateway is { IsConnected: true };

    /// <summary>当前会话是否有一轮在跑（决定显示终止按钮）。</summary>
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

    /// <summary>daemon 连上后挂上网关：拉会话列表，之后事件驱动渲染。</summary>
    /// <param name="gateway">工作 RPC 网关。</param>
    public void AttachGateway(IWorkChatGateway gateway)
    {
        if (ReferenceEquals(_gateway, gateway))
        {
            return;
        }

        DetachGateway();
        _gateway = gateway;
        gateway.EventReceived += OnGatewayEvent;
        OnPropertyChanged(nameof(IsLive));
        _ = RefreshSessionsAsync();
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
        OnPropertyChanged(nameof(IsLive));
    }

    /// <summary>连接状态变化后刷新 <see cref="IsLive"/> 的绑定。</summary>
    public void NotifyConnectionChanged() => OnPropertyChanged(nameof(IsLive));

    /// <summary>重新拉工作会话列表，尽量停在原来选中的会话。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task RefreshSessionsAsync(CancellationToken cancellationToken = default)
    {
        if (_gateway is not { IsConnected: true } gateway)
        {
            return;
        }

        var selectedId = SelectedSession?.Id;
        try
        {
            var summaries = await gateway.ListSessionsAsync(cancellationToken).ConfigureAwait(true);
            var items = summaries.Select(DaemonProjection.ToSessionItem).ToList();
            ReplaceSessions(items);

            if (!string.IsNullOrWhiteSpace(selectedId))
            {
                var again = items.FirstOrDefault(item => item.Id == selectedId);
                if (again is not null)
                {
                    SelectedSession = again;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is DaemonUnavailableException or RpcClientException)
        {
            AddSystemRow($"会话列表载入失败：{Describe(ex)}");
        }
    }

    /// <summary>导入后替换整个会话列表。</summary>
    /// <param name="sessions">按最后活跃时间排好的会话。</param>
    public void ReplaceSessions(IReadOnlyList<SessionItem> sessions)
    {
        Sessions.Clear();
        foreach (var session in sessions)
        {
            Sessions.Add(session);
        }

        if (Sessions.Count == 0 && _gateway is null)
        {
            Sessions.Add(new SessionItem
            {
                Title = "新的工作会话",
                Preview = "还没有会话",
                Time = "刚刚",
                Source = "桌面",
                CreatedAt = DateTimeOffset.Now,
            });
        }

        ApplyFilter();
    }

    /// <summary>全部会话（筛选前）。</summary>
    public ObservableCollection<SessionItem> Sessions { get; } = [];

    /// <summary>当前筛选结果（左侧列表绑定这个）。</summary>
    public ObservableCollection<SessionItem> VisibleSessions { get; } = [];

    /// <summary>当前消息列表（随选中会话切换，包含工具与思考行）。</summary>
    public ObservableCollection<ChatMessageItem> Messages { get; } = [];

    /// <summary>按「显示工具 / 显示思考」过滤后的消息（聊天区绑定这个）。</summary>
    public ObservableCollection<ChatMessageItem> VisibleMessages { get; } = [];

    /// <summary>右侧状态条。</summary>
    public ObservableCollection<StateItem> States { get; }

    /// <summary>当前挂起的审批（没有 = 不显示审批条）。</summary>
    [ObservableProperty]
    public partial ApprovalPromptItem? PendingApproval { get; set; }

    /// <summary>是否有待审批请求。</summary>
    public bool HasPendingApproval => PendingApproval is not null;

    /// <summary>选中的会话。</summary>
    [ObservableProperty]
    public partial SessionItem? SelectedSession { get; set; }

    /// <summary>输入框内容。</summary>
    [ObservableProperty]
    public partial string Draft { get; set; } = "";

    /// <summary>右侧状态栏是否展开。</summary>
    [ObservableProperty]
    public partial bool IsRightPanelOpen { get; set; }

    /// <summary>聊天区标题。</summary>
    public string HeaderTitle => SelectedSession?.Title ?? "未选择会话";

    /// <summary>聊天区副标题。</summary>
    public string HeaderSubtitle => SelectedSession is null
        ? "从左侧选一个会话"
        : $"{SelectedSession.Source} · 最后活跃 {SelectedSession.Time} · {SelectedSession.MessageCount} 条消息";

    /// <summary>输入框占位文案。</summary>
    public override string ComposerWatermark => $"说点什么…  {ShortcutHint}";

    /// <summary>当前主模型名（来自设置 · 模型配置）。</summary>
    public string ModelName => _mainModelName;

    /// <summary>模型名 + 端点，作为 header 的提示。</summary>
    public string ModelDetail => $"{_mainModelName} · {_mainModelBaseUrl}";

    /// <summary>模型上下文上限（token）。</summary>
    public int ContextLimitTokens => _contextLimitTokens;

    /// <summary>当前会话已载入消息的估算 token 数。</summary>
    public int ContextUsedTokens => _contextUsedTokens;

    /// <summary>上下文占比（0–100）。</summary>
    public double ContextUsagePercent => _contextLimitTokens <= 0
        ? 0
        : Math.Clamp(_contextUsedTokens * 100.0 / _contextLimitTokens, 0, 100);

    /// <summary>上下文占比的短文本（如 12.4k / 1M · 4%）。</summary>
    public string ContextUsageText =>
        $"{(IsContextMeasured ? "" : "≈")}{TokenEstimator.Format(_contextUsedTokens)} / " +
        $"{TokenEstimator.Format(_contextLimitTokens)} · {ContextPercentText}";

    /// <summary>已用 token 是否来自 provider 的真实 usage。</summary>
    public bool IsContextMeasured => _contextUsage is not null;

    /// <summary>上下文数字的来源说明（弹层与右侧状态栏共用）。</summary>
    public string ContextSourceNote => IsContextMeasured
        ? "已用 token 取自上次主模型调用的 API usage（输入 + 输出）；正文/思考/工具占比按已载入消息估算。"
        : "尚未拿到 provider usage，已用 token 按已载入消息粗略估算。";

    /// <summary>只显示百分比。</summary>
    public string ContextPercentText => ContextUsagePercent < 1 && _contextUsedTokens > 0
        ? "<1%"
        : $"{ContextUsagePercent:0}%";

    /// <summary>占比是否已经偏高（给电量条换色）。</summary>
    public bool IsContextHigh => ContextUsagePercent >= 75;

    /// <summary>上下文提示的详细说明；点击电量条看完整组成与缓存。</summary>
    public string ContextUsageTip => string.IsNullOrWhiteSpace(ModelName)
        ? "未配置主模型"
        : $"{ModelName} · 上下文 {ContextUsageText}\n" +
          $"正文 {BodyTokenText} · 思考 {ReasoningTokenText} · 工具 {ToolTokenText}\n" +
          (IsContextMeasured
              ? $"上次调用：输入 {TokenEstimator.Format(_lastInputTokens)} · 输出 " +
                $"{TokenEstimator.Format(Math.Max(0, _contextUsage!.CompletionTokens))}\n"
              : "") +
          "≤200k 绿 · ≤400k 黄 · >400k 红 · ≥600k 闪电";

    /// <summary>正文（用户 / 助手 / 系统）估算 token。</summary>
    public int BodyTokens => _bodyTokens;

    /// <summary>思考过程估算 token。</summary>
    public int ReasoningTokens => _reasoningTokens;

    /// <summary>工具参数与返回结果估算 token。</summary>
    public int ToolTokens => _toolTokens;

    /// <summary>正文占已用上下文的比例。</summary>
    public double BodyPercent => Percent(_bodyTokens);

    /// <summary>思考占已用上下文的比例。</summary>
    public double ReasoningPercent => Percent(_reasoningTokens);

    /// <summary>工具占已用上下文的比例。</summary>
    public double ToolPercent => Percent(_toolTokens);

    /// <summary>正文的 token / 占比文案。</summary>
    public string BodyTokenText => $"{TokenEstimator.Format(_bodyTokens)} · {BodyPercent:0}%";

    /// <summary>思考的 token / 占比文案。</summary>
    public string ReasoningTokenText => $"{TokenEstimator.Format(_reasoningTokens)} · {ReasoningPercent:0}%";

    /// <summary>工具的 token / 占比文案。</summary>
    public string ToolTokenText => $"{TokenEstimator.Format(_toolTokens)} · {ToolPercent:0}%";

    /// <summary>10 段上下文条已点亮的格数（有内容至少亮 1 格）。</summary>
    private int FilledContextBars => _contextUsedTokens <= 0
        ? 0
        : Math.Clamp((int)Math.Ceiling(ContextUsagePercent / 10), 1, 10);

    /// <summary>上下文条第 1 格。</summary>
    public bool ContextBar1 => FilledContextBars >= 1;

    /// <summary>上下文条第 2 格。</summary>
    public bool ContextBar2 => FilledContextBars >= 2;

    /// <summary>上下文条第 3 格。</summary>
    public bool ContextBar3 => FilledContextBars >= 3;

    /// <summary>上下文条第 4 格。</summary>
    public bool ContextBar4 => FilledContextBars >= 4;

    /// <summary>上下文条第 5 格。</summary>
    public bool ContextBar5 => FilledContextBars >= 5;

    /// <summary>上下文条第 6 格。</summary>
    public bool ContextBar6 => FilledContextBars >= 6;

    /// <summary>上下文条第 7 格。</summary>
    public bool ContextBar7 => FilledContextBars >= 7;

    /// <summary>上下文条第 8 格。</summary>
    public bool ContextBar8 => FilledContextBars >= 8;

    /// <summary>上下文条第 9 格。</summary>
    public bool ContextBar9 => FilledContextBars >= 9;

    /// <summary>上下文条第 10 格。</summary>
    public bool ContextBar10 => FilledContextBars >= 10;

    /// <summary>上下文 ≤200k：绿色。</summary>
    public bool IsContextGreen => _contextUsedTokens <= 200_000;

    /// <summary>上下文 200k–400k：黄色。</summary>
    public bool IsContextYellow => _contextUsedTokens is > 200_000 and <= 400_000;

    /// <summary>上下文 &gt;400k：红色。</summary>
    public bool IsContextRed => _contextUsedTokens > 400_000;

    /// <summary>上下文 ≥600k：红色 + 闪电纹路。</summary>
    public bool IsContextElectric => _contextUsedTokens >= 600_000;

    /// <summary>上轮 API 是否返回了输入 token。</summary>
    public bool HasCacheUsage => _lastInputTokens > 0;

    /// <summary>上轮输入的缓存命中率。</summary>
    public double CacheHitPercent => HasCacheUsage
        ? Math.Clamp(_lastCachedInputTokens * 100.0 / _lastInputTokens, 0, 100)
        : 0;

    /// <summary>缓存命中率文本。</summary>
    public string CacheHitText => HasCacheUsage ? $"{CacheHitPercent:0}%" : "—";

    /// <summary>缓存命中 ≥95%（绿色）。</summary>
    public bool IsCacheHigh => HasCacheUsage && CacheHitPercent >= 95;

    /// <summary>缓存命中 80%–95%（黄色）。</summary>
    public bool IsCacheMedium => HasCacheUsage && CacheHitPercent is >= 80 and < 95;

    /// <summary>缓存命中 &lt;80%（红色）。</summary>
    public bool IsCacheLow => HasCacheUsage && CacheHitPercent < 80;

    /// <summary>缓存命中提示。</summary>
    public string CacheHitTip => HasCacheUsage
        ? $"{(IsContextMeasured ? "上次调用" : "上轮 LLM")}输入 {_lastInputTokens} tokens，" +
          $"其中缓存命中 {_lastCachedInputTokens}（{CacheHitText}）\n来自 provider usage；不是本机统计"
        : "该会话没有 API 用量记录";

    /// <summary>上轮 API 的缓存明细。</summary>
    public string CacheUsageText => HasCacheUsage
        ? $"{(IsContextMeasured ? "上次调用" : "上轮 API")}：{TokenEstimator.Format(_lastCachedInputTokens)} / " +
          $"{TokenEstimator.Format(_lastInputTokens)} 命中"
        : "provider 未返回缓存字段";

    private double Percent(int value) => _contextUsedTokens <= 0
        ? 0
        : Math.Clamp(value * 100.0 / _contextUsedTokens, 0, 100);

    /// <summary>是否选中了会话。</summary>
    public bool HasSelection => SelectedSession is not null;

    /// <summary>当前筛选的显示名。</summary>
    public string SessionFilterLabel => _filter switch
    {
        SessionTimeFilter.Today => "今天",
        SessionTimeFilter.Last7Days => "近 7 天",
        SessionTimeFilter.Last30Days => "近 30 天",
        _ => "全部时间",
    };

    /// <summary>新建会话：连着 daemon 就调 <c>session.create</c>，否则只插一条本地占位。</summary>
    [RelayCommand]
    private async Task NewSessionAsync()
    {
        if (_gateway is { IsConnected: true } gateway)
        {
            try
            {
                var detail = await gateway.CreateSessionAsync().ConfigureAwait(true);
                await RefreshSessionsAsync().ConfigureAwait(true);
                var item = Sessions.FirstOrDefault(candidate => candidate.Id == detail.Id);
                if (item is null)
                {
                    item = DaemonProjection.ToSessionItem(new SessionSummary(
                        detail.Id,
                        detail.Mode,
                        detail.Origin,
                        detail.LaneId,
                        detail.Title,
                        detail.Archived,
                        detail.CreatedAt,
                        detail.LastActiveAt,
                        detail.MessageCount));
                    Sessions.Insert(0, item);
                    ApplyFilter();
                }

                SelectedSession = item;
                return;
            }
            catch (Exception ex) when (ex is DaemonUnavailableException or RpcClientException)
            {
                AddSystemRow($"新建会话失败：{Describe(ex)}");
                return;
            }
        }

        var placeholder = new SessionItem
        {
            Title = "新的工作会话",
            Preview = "占位会话 · 连上 daemon 后会调用 session.create",
            Time = "刚刚",
            Source = "桌面",
            MessageCount = 0,
            CreatedAt = DateTimeOffset.Now,
            CreatedText = $"{DateTimeOffset.Now:MM-dd HH:mm}",
            LaneId = "desktop:work",
        };
        Sessions.Insert(0, placeholder);
        ApplyFilter();
        SelectedSession = placeholder;
    }

    /// <summary>切换会话列表的创建时间筛选。</summary>
    /// <param name="filter">筛选名：All / Today / Last7Days / Last30Days。</param>
    [RelayCommand]
    private void SetSessionFilter(string? filter)
    {
        if (Enum.TryParse<SessionTimeFilter>(filter, ignoreCase: true, out var parsed))
        {
            _filter = parsed;
            OnPropertyChanged(nameof(SessionFilterLabel));
            ApplyFilter();
        }
    }

    /// <summary>展开/收起右侧状态栏。</summary>
    [RelayCommand]
    private void ToggleRightPanel() => IsRightPanelOpen = !IsRightPanelOpen;

    /// <summary>发送：真实会话走 <c>turn.send</c>（正文随后由事件流回），否则只做本地回显。</summary>
    [RelayCommand]
    private async Task SendAsync()
    {
        var text = Draft.Trim();
        if (text.Length == 0)
        {
            return;
        }

        Draft = "";
        AppendUserRow(text);

        if (_gateway is { IsConnected: true } gateway && SelectedSession is { IsLive: true } session)
        {
            try
            {
                await gateway.SendTurnAsync(session.Id, text).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is DaemonUnavailableException or RpcClientException)
            {
                AddSystemRow($"发送失败：{Describe(ex)}");
            }
        }
    }

    /// <summary>终止当前轮：停就停，连已合并但还没开跑的消息一起丢掉。</summary>
    [RelayCommand]
    private async Task StopAsync()
    {
        if (_gateway is not { IsConnected: true } gateway || SelectedSession is not { IsLive: true } session)
        {
            return;
        }

        try
        {
            await gateway.CancelTurnAsync(session.Id).ConfigureAwait(true);
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

    /// <summary>本会话内总是允许。</summary>
    [RelayCommand]
    private Task AlwaysApprovalAsync() => RespondApprovalAsync(ApprovalDecision.Always);

    /// <summary>把设置里的主模型与上下文上限同步到聊天页。</summary>
    /// <param name="name">模型名。</param>
    /// <param name="baseUrl">兼容端点。</param>
    /// <param name="contextLimitTokens">上下文上限（token）。</param>
    public void UpdateModel(string name, string baseUrl, int contextLimitTokens)
    {
        _mainModelName = string.IsNullOrWhiteSpace(name) ? "未配置模型" : name.Trim();
        _mainModelBaseUrl = string.IsNullOrWhiteSpace(baseUrl) ? "未配置端点" : baseUrl.Trim();
        _contextLimitTokens = Math.Max(1, contextLimitTokens);
        OnPropertyChanged(nameof(ModelName));
        OnPropertyChanged(nameof(ModelDetail));
        OnPropertyChanged(nameof(ContextLimitTokens));
        OnPropertyChanged(nameof(ContextUsageTip));
        UpdateContextUsage();
    }

    /// <inheritdoc />
    protected override void ApplyMessageFilter()
    {
        foreach (var message in Messages)
        {
            message.IsWideBubble = IsWideBubbles;
        }

        SyncVisible(
            VisibleMessages,
            Messages,
            message => (ShowToolCalls || !message.IsTool)
                       && (ShowReasoning || !message.IsReasoning)
                       && (ShowSystemInputs || !message.IsSystemInput));
    }

    private void UpdateContextUsage(ContextUsage? context = null)
    {
        if (context is not null)
        {
            ApplyContext(context);
        }
        else if (_contextUsage is not null)
        {
            _contextUsage = null;
        }

        var breakdown = TokenEstimator.Breakdown(Messages);
        var estimated = breakdown.Total;
        var total = _contextUsage?.Tokens ?? estimated;
        if (total < 0)
        {
            total = 0;
        }

        if (estimated > 0 && total != estimated)
        {
            // 总量用真实 usage，组成按估算比例缩放（比例是估算的，但三项相加等于真实总量）
            _bodyTokens = (int)Math.Round(breakdown.Body * (double)total / estimated);
            _reasoningTokens = (int)Math.Round(breakdown.Reasoning * (double)total / estimated);
            _toolTokens = Math.Max(0, total - _bodyTokens - _reasoningTokens);
        }
        else
        {
            _bodyTokens = breakdown.Body;
            _reasoningTokens = breakdown.Reasoning;
            _toolTokens = breakdown.Tool;
        }

        _contextUsedTokens = total;

        OnPropertyChanged(nameof(ContextUsedTokens));
        OnPropertyChanged(nameof(BodyTokens));
        OnPropertyChanged(nameof(ReasoningTokens));
        OnPropertyChanged(nameof(ToolTokens));
        OnPropertyChanged(nameof(BodyPercent));
        OnPropertyChanged(nameof(ReasoningPercent));
        OnPropertyChanged(nameof(ToolPercent));
        OnPropertyChanged(nameof(BodyTokenText));
        OnPropertyChanged(nameof(ReasoningTokenText));
        OnPropertyChanged(nameof(ToolTokenText));
        OnPropertyChanged(nameof(ContextUsagePercent));
        OnPropertyChanged(nameof(ContextUsageText));
        OnPropertyChanged(nameof(ContextPercentText));
        OnPropertyChanged(nameof(IsContextMeasured));
        OnPropertyChanged(nameof(ContextSourceNote));
        OnPropertyChanged(nameof(IsContextHigh));
        OnPropertyChanged(nameof(ContextUsageTip));
        OnPropertyChanged(nameof(ContextBar1));
        OnPropertyChanged(nameof(ContextBar2));
        OnPropertyChanged(nameof(ContextBar3));
        OnPropertyChanged(nameof(ContextBar4));
        OnPropertyChanged(nameof(ContextBar5));
        OnPropertyChanged(nameof(ContextBar6));
        OnPropertyChanged(nameof(ContextBar7));
        OnPropertyChanged(nameof(ContextBar8));
        OnPropertyChanged(nameof(ContextBar9));
        OnPropertyChanged(nameof(ContextBar10));
        OnPropertyChanged(nameof(IsContextGreen));
        OnPropertyChanged(nameof(IsContextYellow));
        OnPropertyChanged(nameof(IsContextRed));
        OnPropertyChanged(nameof(IsContextElectric));
    }

    /// <summary>把 provider 的上下文用量同步到缓存命中指标。</summary>
    private void ApplyContext(ContextUsage context)
    {
        _contextUsage = context;
        _lastInputTokens = context.PromptTokens;
        _lastCachedInputTokens = context.CacheHitTokens;
        UpdateCacheUsage();
    }

    private void UpdateCacheUsage()
    {
        OnPropertyChanged(nameof(HasCacheUsage));
        OnPropertyChanged(nameof(CacheHitPercent));
        OnPropertyChanged(nameof(CacheHitText));
        OnPropertyChanged(nameof(IsCacheHigh));
        OnPropertyChanged(nameof(IsCacheMedium));
        OnPropertyChanged(nameof(IsCacheLow));
        OnPropertyChanged(nameof(CacheHitTip));
        OnPropertyChanged(nameof(CacheUsageText));
    }

    private void ApplyFilter()
    {
        var now = DateTimeOffset.Now;
        var cutoff = _filter switch
        {
            SessionTimeFilter.Today => new DateTimeOffset(now.Date, now.Offset),
            SessionTimeFilter.Last7Days => now.AddDays(-7),
            SessionTimeFilter.Last30Days => now.AddDays(-30),
            _ => DateTimeOffset.MinValue,
        };

        VisibleSessions.Clear();
        foreach (var session in Sessions
                     .Where(s => s.CreatedAt >= cutoff)
                     .OrderByDescending(s => s.IsPinned)
                     .ThenByDescending(s => s.CreatedAt))
        {
            VisibleSessions.Add(session);
        }

        if (SelectedSession is null || !VisibleSessions.Contains(SelectedSession))
        {
            SelectedSession = VisibleSessions.FirstOrDefault();
        }
    }

    partial void OnPendingApprovalChanged(ApprovalPromptItem? value)
        => OnPropertyChanged(nameof(HasPendingApproval));

    partial void OnSelectedSessionChanged(SessionItem? value)
    {
        Messages.Clear();
        if (value is not null)
        {
            foreach (var message in value.Messages)
            {
                Messages.Add(message);
            }
        }

        _lastInputTokens = value?.LastInputTokens ?? 0;
        _lastCachedInputTokens = value?.LastCachedInputTokens ?? 0;
        _contextUsage = null;
        ResetTurnState();
        IsTurnRunning = false;
        PendingApproval = null;

        ApplyMessageFilter();
        UpdateContextUsage();
        UpdateCacheUsage();
        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(HeaderSubtitle));
        OnPropertyChanged(nameof(HasSelection));

        if (value is { IsLive: true })
        {
            _ = LoadSessionMessagesAsync(value);
        }
    }

    private async Task LoadSessionMessagesAsync(SessionItem session)
    {
        if (_gateway is not { IsConnected: true } gateway)
        {
            return;
        }

        _loadCts?.Cancel();
        _loadCts?.Dispose();
        var cts = new CancellationTokenSource();
        _loadCts = cts;

        try
        {
            var result = await gateway.GetMessagesAsync(session.Id, cts.Token).ConfigureAwait(true);
            if (cts.IsCancellationRequested || SelectedSession?.Id != session.Id)
            {
                return;
            }

            Messages.Clear();
            foreach (var item in DaemonProjection.ToChatItems(result.Items))
            {
                PrepareMessage(item);
                Messages.Add(item);
                if (item.IsImage)
                {
                    _ = item.LoadImageAsync();
                }
            }

            ApplyMessageFilter();
            UpdateContextUsage(result.Context);

            // 没有 usage 时保留列表里带过来的（导入快照）缓存值
            if (result.Context is null)
            {
                UpdateCacheUsage();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is DaemonUnavailableException or RpcClientException)
        {
            if (SelectedSession?.Id == session.Id)
            {
                AddSystemRow($"历史载入失败：{Describe(ex)}");
            }
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
    /// 流式增量的合并队列。模型按 token 出增量，逐条 Post 到 UI 线程会把调度队列灌满，
    /// 长思考时桌面看起来就是「未响应」；这里同一段 / 同一会话的增量先拼起来，一帧只回一次 UI。
    /// </summary>
    private void QueueStreamEvent(string name, JsonElement payload)
    {
        bool thinking;
        string sessionId;
        string delta;
        if (name == RpcEvents.TurnThinking)
        {
            if (payload.Deserialize<TurnThinkingEvent>(JsonWire.Options) is not { } thinkingEvent)
            {
                return;
            }

            thinking = true;
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
            sessionId = deltaEvent.SessionId;
            delta = deltaEvent.Delta;
        }

        if (string.IsNullOrEmpty(delta))
        {
            return;
        }

        // 本轮事件已经到达：历史请求即使晚回也不能再把聊天区清空重画。
        _loadCts?.Cancel();

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
                _pendingStream.Add(new PendingStream(sessionId, thinking, new StringBuilder(delta)));
            }

            if (_streamFlushPosted)
            {
                return;
            }

            _streamFlushPosted = true;
        }

        _uiContext?.Post(static state => ((WorkViewModel)state!).FlushStreamQueue(), this);
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
            ApplyStreamChunk(chunk.SessionId, chunk.Thinking, chunk.Text.ToString());
        }
    }

    private void ApplyStreamChunk(string sessionId, bool thinking, string text)
    {
        if (!IsCurrentSession(sessionId))
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
                    IsStreaming = true,
                };
                Messages.Add(_streamingThinking);
                ApplyMessageFilter();
            }

            _streamingThinking.Append(text);
            return;
        }

        // 正文开始 = 上一段思考结束；下一次 thinking 另起一行，保证行位置跟工具调用一致。
        if (_streamingThinking is not null)
        {
            _streamingThinking.IsStreaming = false;
        }

        _streamingThinking = null;
        EnsureAssistant().Append(text);
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
        // 本轮事件已经到达：历史请求即使晚回也不能再把聊天区清空重画。
        if (name is RpcEvents.TurnStarted or RpcEvents.TurnDelta or RpcEvents.TurnThinking
            or RpcEvents.TurnTool or RpcEvents.TurnCompleted or RpcEvents.TurnFailed)
        {
            _loadCts?.Cancel();
        }

        switch (name)
        {
            case RpcEvents.TurnStarted:
            {
                var e = payload.Deserialize<TurnStartedEvent>(JsonWire.Options);
                if (e is null || !IsCurrentSession(e.SessionId))
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

                ApplyStreamChunk(e.SessionId, thinking: false, e.Delta);
                return;
            }

            case RpcEvents.TurnThinking:
            {
                var e = payload.Deserialize<TurnThinkingEvent>(JsonWire.Options);
                if (e is null)
                {
                    return;
                }

                ApplyStreamChunk(e.SessionId, thinking: true, e.Delta);
                return;
            }

            case RpcEvents.TurnTool:
            {
                var e = payload.Deserialize<TurnToolEvent>(JsonWire.Options);
                if (e is null || !IsCurrentSession(e.SessionId))
                {
                    return;
                }

                ApplyToolEvent(e);
                return;
            }

            case RpcEvents.TurnCompleted:
            {
                var e = payload.Deserialize<TurnCompletedEvent>(JsonWire.Options);
                if (e is null || !IsCurrentSession(e.SessionId))
                {
                    return;
                }

                if (e.Attachments is { Count: > 0 })
                {
                    EnsureAssistant().Attachments = DaemonProjection.ToAttachments(e.Attachments);
                }

                // 流式期间正文在一个气泡里长出来；轮末再把本地文件链接拆成独立气泡
                SplitAssistantBubbles();

                if (e.Context is { } context)
                {
                    ApplyContext(context);
                }
                else
                {
                    _lastInputTokens = e.Usage.InputTokens;
                    _lastCachedInputTokens = e.Usage.CacheHitTokens;
                    UpdateCacheUsage();
                }

                ResetTurnState();
                IsTurnRunning = false;
                ApplyMessageFilter();
                UpdateContextUsage(e.Context);
                return;
            }

            case RpcEvents.TurnFailed:
            {
                var e = payload.Deserialize<TurnFailedEvent>(JsonWire.Options);
                if (e is null || !IsCurrentSession(e.SessionId))
                {
                    return;
                }

                ResetTurnState();
                IsTurnRunning = false;
                AddSystemRow(e.Code == TurnFailureCodes.Cancelled
                    ? "本轮已取消"
                    : $"本轮失败：{e.Message}");
                return;
            }

            case RpcEvents.ApprovalRequest:
            {
                var e = payload.Deserialize<ApprovalRequestDto>(JsonWire.Options);
                if (e is null || !IsCurrentSession(e.SessionId))
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

            case RpcEvents.SessionRenamed:
            {
                var e = payload.Deserialize<SessionRenamedEvent>(JsonWire.Options);
                if (e is null || string.IsNullOrWhiteSpace(e.Title))
                {
                    return;
                }

                var item = Sessions.FirstOrDefault(session => session.Id == e.SessionId);
                if (item is null)
                {
                    return;
                }

                item.Title = e.Title;
                if (ReferenceEquals(SelectedSession, item))
                {
                    OnPropertyChanged(nameof(HeaderTitle));
                }

                return;
            }
        }
    }

    private void ApplyToolEvent(TurnToolEvent e)
    {
        // 工具调用开始/结束都意味着上一段思考与正文已经收尾：
        // 后续 thinking / 正文另起一行，保证时间线顺序和工具调用对得上。
        // 收尾时必须把 IsStreaming 关掉，否则这一段只留在纯文本模式，轮末也不会补 Markdown。
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
            };
            Messages.Add(item);
            ApplyMessageFilter();
            if (!string.IsNullOrWhiteSpace(e.CallId))
            {
                _liveTools[e.CallId] = item;
            }

            return;
        }

        row ??= Messages.LastOrDefault(message => message.IsTool);
        if (row is not null && e.Result is { Length: > 0 })
        {
            row.Result = e.Result;
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

    private bool IsCurrentSession(string sessionId) => SelectedSession?.Id == sessionId;

    private ChatMessageItem EnsureAssistant()
    {
        if (_streamingAssistant is not null)
        {
            return _streamingAssistant;
        }

        _streamingAssistant = new ChatMessageItem
        {
            Kind = ChatEntryKind.Assistant,
            Time = DateTimeOffset.Now.ToString("HH:mm"),
            IsWideBubble = IsWideBubbles,
            IsStreaming = true,
        };
        PrepareMessage(_streamingAssistant);
        Messages.Add(_streamingAssistant);
        ApplyMessageFilter();
        return _streamingAssistant;
    }

    /// <summary>
    /// 轮末把流式期间的单条助手气泡按本地文件链接拆开：
    /// 一个文件一个气泡，链接前后的文字各成一段；没有本地文件链接时不动。
    /// </summary>
    private void SplitAssistantBubbles()
    {
        var assistant = _streamingAssistant;
        if (assistant is null)
        {
            return;
        }

        var bubbles = LocalFileLinkParser.Split(assistant.Text);
        if (!bubbles.Any(static bubble => bubble.IsFile))
        {
            return;
        }

        var index = Messages.IndexOf(assistant);
        if (index < 0)
        {
            return;
        }

        var items = DaemonProjection.ToBubbleItems(assistant.Text, assistant.Time, assistant.Attachments);
        Messages.RemoveAt(index);
        for (var i = 0; i < items.Count; i++)
        {
            items[i].IsWideBubble = IsWideBubbles;
            PrepareMessage(items[i]);
            Messages.Insert(index + i, items[i]);
            if (items[i].IsImage)
            {
                _ = items[i].LoadImageAsync();
            }
        }

        _streamingAssistant = items.LastOrDefault(static item => item.IsAssistant);
        ApplyMessageFilter();
    }

    private void AppendUserRow(string text)
    {
        Messages.Add(new ChatMessageItem
        {
            Kind = ChatEntryKind.User,
            Text = text,
            Time = DateTimeOffset.Now.ToString("HH:mm"),
            IsWideBubble = IsWideBubbles,
        });
        ApplyMessageFilter();
        UpdateContextUsage();
    }

    private void AddSystemRow(string text)
    {
        Messages.Add(new ChatMessageItem
        {
            Kind = ChatEntryKind.System,
            Text = text,
            Time = DateTimeOffset.Now.ToString("HH:mm"),
        });
        ApplyMessageFilter();
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
        _liveTools.Clear();
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
            throw new DaemonUnavailableException("daemon 未连接，无法合成语音");
        }

        var audio = await gateway.SynthesizeSpeechAsync(item.Text).ConfigureAwait(true);
        await LocalMediaPlayer.PlayAsync(audio.Path).ConfigureAwait(true);
    }

    private static string? FormatArguments(JsonElement? arguments)
        => arguments is { ValueKind: not JsonValueKind.Undefined and not JsonValueKind.Null } value
            ? JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonWire.Options) { WriteIndented = true })
            : null;

    private static string Describe(Exception ex) => ex switch
    {
        RpcClientException rpc => rpc.Message,
        _ => ex.Message,
    };

    /// <summary>待合并的一条流式增量（同一会话、同一段落）。</summary>
    private sealed record PendingStream(string SessionId, bool Thinking, StringBuilder Text);
}
