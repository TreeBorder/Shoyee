using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shoyee.Client.Rpc;
using Shoyee.Contracts.Dtos;
using Shoyee.Desktop.Rpc;
using Shoyee.Desktop.ViewModels.Items;

namespace Shoyee.Desktop.ViewModels.Settings;

/// <summary>
/// 设置页的运行数据（只读）：运行配置原文、工作区路径、工具面、MCP 状态。
///
/// 连上 daemon 后整份替换静态占位；写入口（config.set / mcp.reload 等）下一步再接。
/// </summary>
public partial class SettingsRuntimeViewModel : ViewModelBase
{
    private ISettingsGateway? _gateway;
    private CancellationTokenSource? _loadCts;
    private readonly Dictionary<string, PromptPreviewItem> _prompts = new(StringComparer.Ordinal);

    /// <summary>装配静态占位数据（未连接时也能看页面结构）。</summary>
    public SettingsRuntimeViewModel()
    {
        foreach (var tool in SettingsPlaceholderData.Tools)
        {
            Tools.Add(tool);
        }

        foreach (var server in SettingsPlaceholderData.McpServers)
        {
            McpServers.Add(server);
        }

        foreach (var state in SettingsPlaceholderData.StateRows)
        {
            StateRows.Add(new StateEditItem(state.Name, state.Name, state.Current, state.Description));
        }

        foreach (var subagent in SettingsPlaceholderData.Subagents)
        {
            Subagents.Add(subagent);
        }
    }

    /// <summary>当前工具面。</summary>
    public ObservableCollection<ToolStatusItem> Tools { get; } = [];

    /// <summary>MCP 服务状态。</summary>
    public ObservableCollection<McpServerItem> McpServers { get; } = [];

    /// <summary>工作区里的固定路径。</summary>
    public ObservableCollection<WorkspacePathItem> WorkspacePaths { get; } = [];

    /// <summary>角色状态表（可编辑；保存走 state.set）。</summary>
    public ObservableCollection<StateEditItem> StateRows { get; } = [];

    /// <summary>资料索引（index.list；启停 / 删除写回）。</summary>
    public ObservableCollection<IndexEntryItem> IndexRows { get; } = [];

    /// <summary>已配置的子智能体（tools.subagents.agents）。</summary>
    public ObservableCollection<SubagentRowItem> Subagents { get; } = [];

    /// <summary>子智能体默认是否异步等待。</summary>
    public string SubagentAutoWait { get; private set; } = "false";

    /// <summary>子智能体等待超时（秒）。</summary>
    public string SubagentWaitTimeout { get; private set; } = "300";

    /// <summary>子智能体循环上限。</summary>
    public string SubagentMaxLoops { get; private set; } = "10";

    /// <summary>子智能体默认是否异步等待（配置字符串 → 开关状态）。</summary>
    public bool SubagentAutoWaitEnabled
        => string.Equals(SubagentAutoWait, "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>心跳提示词（来自 prompts.json；可编辑，保存时写回）。</summary>
    public ObservableCollection<HeartbeatPromptGroup> HeartbeatPrompts { get; } = [];

    /// <summary>心跳是否启用。</summary>
    [ObservableProperty]
    public partial bool HeartbeatEnabled { get; set; }

    /// <summary>最短间隔（分钟）。</summary>
    [ObservableProperty]
    public partial string HeartbeatMinMinutes { get; set; } = "45";

    /// <summary>最长间隔（分钟）。</summary>
    [ObservableProperty]
    public partial string HeartbeatMaxMinutes { get; set; } = "75";

    /// <summary>炽热区（分钟）。</summary>
    [ObservableProperty]
    public partial string HeartbeatHotZone { get; set; } = "20";

    /// <summary>冷宫区（分钟）。</summary>
    [ObservableProperty]
    public partial string HeartbeatCooldown { get; set; } = "30";

    /// <summary>普通增量下限。</summary>
    [ObservableProperty]
    public partial string HeartbeatIncreaseMin { get; set; } = "1";

    /// <summary>普通增量上限。</summary>
    [ObservableProperty]
    public partial string HeartbeatIncreaseMax { get; set; } = "7";

    /// <summary>触发后扣减。</summary>
    [ObservableProperty]
    public partial string HeartbeatPenalty { get; set; } = "50";

    /// <summary>触发阈值（思念值低于它时不触发）。</summary>
    [ObservableProperty]
    public partial string HeartbeatThreshold { get; set; } = "80";

    /// <summary>思念上限。</summary>
    [ObservableProperty]
    public partial string HeartbeatLongingCap { get; set; } = "100";

    /// <summary>档位阈值（空格 / 逗号分隔）。</summary>
    [ObservableProperty]
    public partial string HeartbeatThresholds { get; set; } = "15 / 30 / 50 / 70";

    /// <summary>档位波动概率（%）。</summary>
    [ObservableProperty]
    public partial string HeartbeatFluctuation { get; set; } = "20";

    /// <summary>静默开始（HH:mm）。</summary>
    [ObservableProperty]
    public partial string HeartbeatQuietStart { get; set; } = "23:00";

    /// <summary>静默结束（HH:mm）。</summary>
    [ObservableProperty]
    public partial string HeartbeatQuietEnd { get; set; } = "06:30";

    /// <summary>最近一次拉到的工作区路径（原文）。</summary>
    public WorkspacePathsDto? Paths { get; private set; }

    /// <summary>daemon 当前生效的 config.json（原始 JSON）。</summary>
    public JsonElement Config { get; private set; }

    /// <summary>运行状态（Lane / 全局状态 / 用量）；没拉到为 null。</summary>
    public StatusDto? Status { get; private set; }

    /// <summary>daemon 日志尾部（最多 120 行）；读不到为空。</summary>
    public string DaemonLogTail { get; private set; } = "";

    /// <summary>刷新成功（或失败）后触发；设置页据此把运行值写回表单。</summary>
    public event EventHandler? Loaded;

    /// <summary>daemon 当前生效的 config.json（格式化后的原文）。</summary>
    [ObservableProperty]
    public partial string ConfigJson { get; set; } = "";

    /// <summary>config.json 的绝对路径。</summary>
    [ObservableProperty]
    public partial string ConfigPath { get; set; } = "";

    /// <summary>数据状态文案。</summary>
    [ObservableProperty]
    public partial string StatusText { get; set; } = "未连接 daemon · 显示静态占位";

    /// <summary>是否已经从 daemon 拉到运行数据。</summary>
    [ObservableProperty]
    public partial bool IsLive { get; set; }

    /// <summary>是否正在刷新。</summary>
    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    /// <summary>最近一次错误（空 = 没有错误）。</summary>
    [ObservableProperty]
    public partial string ErrorText { get; set; } = "";

    /// <summary>是否有错误。</summary>
    public bool HasError => ErrorText.Length > 0;

    /// <summary>工具数量文案。</summary>
    public string ToolCountText => $"{Tools.Count} 个工具";

    /// <summary>MCP 服务数量文案。</summary>
    public string McpCountText => $"{McpServers.Count} 个服务";

    /// <summary>是否停在占位数据。</summary>
    public bool IsPlaceholder => !IsLive;

    /// <summary>挂上 daemon 网关并拉一次。</summary>
    /// <param name="gateway">设置页 RPC 网关。</param>
    public void AttachGateway(ISettingsGateway gateway)
    {
        if (ReferenceEquals(_gateway, gateway))
        {
            return;
        }

        DetachGateway();
        _gateway = gateway;
        _ = RefreshAsync();
    }

    /// <summary>断开后摘掉网关（保留已拉到的数据）。</summary>
    public void DetachGateway()
    {
        if (_gateway is null)
        {
            return;
        }

        _gateway = null;
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;
        StatusText = WorkspacePaths.Count > 0 ? "未连接 daemon · 显示上次数据" : "未连接 daemon · 显示静态占位";
        IsLive = false;
    }

    /// <summary>连接状态变化后刷新文案。</summary>
    public void NotifyConnectionChanged()
    {
        if (_gateway is null || !_gateway.IsConnected)
        {
            StatusText = WorkspacePaths.Count > 0 ? "未连接 daemon · 显示上次数据" : "未连接 daemon · 显示静态占位";
            IsLive = false;
        }
    }

    /// <summary>打开设置 / 切换设置页时补一次刷新。</summary>
    public void EnsureLoaded()
    {
        if (_gateway is { IsConnected: true })
        {
            _ = RefreshAsync();
        }
    }

    /// <summary>刷新全部运行数据。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    [RelayCommand]
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
        IsLoading = true;
        ErrorText = "";

        try
        {
            var paths = await gateway.GetWorkspacePathsAsync(cts.Token).ConfigureAwait(true);
            ApplyPaths(paths);
            ApplyPrompts(paths);

            var config = await gateway.GetConfigAsync(cts.Token).ConfigureAwait(true);
            Config = config;
            ConfigJson = config.ValueKind == JsonValueKind.Undefined
                ? ""
                : JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            ApplySubagents(config);
            ApplyHeartbeat(config, paths);

            var tools = await gateway.ListToolsAsync(cancellationToken: cts.Token).ConfigureAwait(true);
            Tools.Clear();
            foreach (var tool in tools)
            {
                Tools.Add(ToItem(tool));
            }

            var index = await gateway.ListIndexAsync(cancellationToken: cts.Token).ConfigureAwait(true);
            IndexRows.Clear();
            foreach (var entry in index)
            {
                IndexRows.Add(new IndexEntryItem(entry));
            }

            var servers = await gateway.GetMcpStatusAsync(cts.Token).ConfigureAwait(true);
            ApplyMcp(servers);

            Status = await gateway.GetStatusAsync(cts.Token).ConfigureAwait(true);
            ApplyStates(Status);
            DaemonLogTail = ReadTail(paths.DaemonLog);

            IsLive = true;
            StatusText = "已连接 daemon · 只读（写入口还没接）";
            OnPropertyChanged(nameof(ToolCountText));
            OnPropertyChanged(nameof(McpCountText));
            OnPropertyChanged(nameof(IsPlaceholder));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is DaemonUnavailableException or RpcClientException)
        {
            ErrorText = $"设置数据载入失败：{Describe(ex)}";
            StatusText = "daemon 已连接，但设置数据没拉到";
        }
        finally
        {
            if (ReferenceEquals(_loadCts, cts))
            {
                _loadCts = null;
            }

            cts.Dispose();
            IsLoading = false;
            OnPropertyChanged(nameof(HasError));
            Loaded?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>用系统默认程序打开一个本地路径。</summary>
    /// <param name="path">绝对路径。</param>
    [RelayCommand]
    private void OpenPath(string? path) => LocalFileOpener.Open(path ?? "");

    /// <summary>复制 config.json 原文。</summary>
    [RelayCommand]
    private async Task CopyConfigAsync()
    {
        if (await ClipboardService.SetTextAsync(ConfigJson).ConfigureAwait(true))
        {
            StatusText = "config.json 已复制到剪贴板";
        }
        else
        {
            StatusText = "复制失败（剪贴板不可用）";
        }
    }

    /// <summary>保存改过的角色状态（state.set）。</summary>
    [RelayCommand]
    private async Task SaveStatesAsync()
    {
        if (_gateway is not { IsConnected: true } gateway)
        {
            return;
        }

        var changed = StateRows.Where(state => state.IsDirty).ToList();
        if (changed.Count == 0)
        {
            StatusText = "状态没有改动";
            return;
        }

        try
        {
            foreach (var state in changed)
            {
                var saved = await gateway.SetStateAsync(state.Key, state.Value).ConfigureAwait(true);
                state.Value = saved.Value;
                state.MarkSaved();
            }

            StatusText = $"已保存 {changed.Count} 个状态";
            ErrorText = "";
        }
        catch (Exception ex) when (ex is DaemonUnavailableException or RpcClientException)
        {
            ErrorText = $"状态保存失败：{Describe(ex)}";
        }
    }

    /// <summary>热重载配置（只影响新会话）。</summary>
    [RelayCommand]
    private async Task ReloadConfigAsync()
    {
        if (_gateway is not { IsConnected: true } gateway)
        {
            return;
        }

        try
        {
            var result = await gateway.ReloadConfigAsync().ConfigureAwait(true);
            var changed = result.Changed.Count == 0 ? "没有变化" : $"已生效：{string.Join("、", result.Changed)}";
            var restart = result.RestartRequired.Count == 0
                ? ""
                : $"；需重启：{string.Join("、", result.RestartRequired)}";
            StatusText = $"配置已重载 · {changed}{restart}";
            ErrorText = "";
        }
        catch (Exception ex) when (ex is DaemonUnavailableException or RpcClientException)
        {
            ErrorText = $"配置重载失败：{Describe(ex)}";
        }
    }

    /// <summary>把当前页改过的字段合成局部配置写回（config.set）。</summary>
    /// <param name="page">当前设置子页。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task SaveSettingsPageAsync(
        SettingsPageViewModel page,
        CancellationToken cancellationToken = default)
    {
        if (_gateway is not { IsConnected: true } gateway)
        {
            return;
        }

        var fields = (page.Groups ?? [])
            .SelectMany(group => group.Fields)
            .Where(field => field.CanEdit && field.IsDirty)
            .ToList();
        if (fields.Count == 0)
        {
            StatusText = "这一页没有改动";
            page.IsEditing = false;
            return;
        }

        var patch = new System.Text.Json.Nodes.JsonObject();
        foreach (var field in fields)
        {
            SetPath(patch, field.ConfigPath!, ToJson(field));
        }

        try
        {
            var result = await gateway.SetConfigAsync(patch, cancellationToken).ConfigureAwait(true);
            page.IsEditing = false;
            foreach (var field in fields)
            {
                field.MarkSaved();
            }

            await RefreshAsync(cancellationToken).ConfigureAwait(true);

            var changed = result.Changed.Count == 0 ? "没有立即生效的段" : $"已生效：{string.Join("、", result.Changed)}";
            var restart = result.RestartRequired.Count == 0
                ? ""
                : $"；需重启 daemon：{string.Join("、", result.RestartRequired)}";
            var warnings = result.Warnings.Count == 0 ? "" : $"；提示：{string.Join("；", result.Warnings)}";
            StatusText = $"已保存 {fields.Count} 个字段 · {changed}{restart}{warnings}";
            ErrorText = "";
        }
        catch (Exception ex) when (ex is DaemonUnavailableException or RpcClientException)
        {
            ErrorText = $"配置保存失败：{Describe(ex)}";
        }
    }

    private static System.Text.Json.Nodes.JsonNode? ToJson(SettingsFieldItem field)
    {
        if (field.IsSwitch)
        {
            return System.Text.Json.Nodes.JsonValue.Create(field.IsOn);
        }

        if (field.IsConfigList)
        {
            var array = new System.Text.Json.Nodes.JsonArray();
            foreach (var line in field.Value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                array.Add(line);
            }

            return array;
        }

        if (field.Kind == SettingsFieldKind.Number && long.TryParse(field.Value, out var number))
        {
            return System.Text.Json.Nodes.JsonValue.Create(number);
        }

        return System.Text.Json.Nodes.JsonValue.Create(field.Value);
    }

    private static void SetPath(System.Text.Json.Nodes.JsonObject root, string path, System.Text.Json.Nodes.JsonNode? value)
    {
        var parts = path.Split('.');
        var current = root;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (current[parts[i]] is not System.Text.Json.Nodes.JsonObject next)
            {
                next = new System.Text.Json.Nodes.JsonObject();
                current[parts[i]] = next;
            }

            current = next;
        }

        current[parts[^1]] = value;
    }

    /// <summary>重连全部 MCP 服务。</summary>
    [RelayCommand]
    private async Task ReloadMcpAsync()
    {
        if (_gateway is not { IsConnected: true } gateway)
        {
            return;
        }

        try
        {
            var result = await gateway.ReloadMcpAsync().ConfigureAwait(true);
            ApplyMcp(result.Servers);
            StatusText = result.RestartRequired
                ? "MCP 已重连 · 工具清单变了，重启 daemon 后才对运行中的会话生效"
                : "MCP 已重连";
            ErrorText = "";
            OnPropertyChanged(nameof(McpCountText));
            Loaded?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is DaemonUnavailableException or RpcClientException)
        {
            ErrorText = $"MCP 重连失败：{Describe(ex)}";
        }
    }

    private void ApplyMcp(IReadOnlyList<McpServerStatus> servers)
    {
        McpServers.Clear();
        foreach (var server in servers)
        {
            McpServers.Add(ToItem(server));
        }

        OnPropertyChanged(nameof(McpCountText));
    }

    /// <summary>保存心跳参数（config.set）与提示词（本地 prompts.json）。</summary>
    [RelayCommand]
    private async Task SaveHeartbeatAsync()
    {
        if (_gateway is not { IsConnected: true } gateway)
        {
            return;
        }

        var heartbeat = new System.Text.Json.Nodes.JsonObject
        {
            ["enabled"] = HeartbeatEnabled,
            ["minMinutes"] = ToInt(HeartbeatMinMinutes),
            ["maxMinutes"] = ToInt(HeartbeatMaxMinutes),
            ["hotZoneMinutes"] = ToInt(HeartbeatHotZone),
            ["cooldownMinutes"] = ToInt(HeartbeatCooldown),
            ["normalIncreaseMin"] = ToInt(HeartbeatIncreaseMin),
            ["normalIncreaseMax"] = ToInt(HeartbeatIncreaseMax),
            ["triggerPenalty"] = ToInt(HeartbeatPenalty),
            ["triggerThreshold"] = ToInt(HeartbeatThreshold),
            ["longingCap"] = ToInt(HeartbeatLongingCap),
            ["levelThresholds"] = Thresholds(HeartbeatThresholds),
            ["levelFluctuationChance"] = ToInt(HeartbeatFluctuation),
            ["quietHoursStart"] = HeartbeatQuietStart,
            ["quietHoursEnd"] = HeartbeatQuietEnd,
        };
        var patch = new System.Text.Json.Nodes.JsonObject
        {
            ["proactive"] = new System.Text.Json.Nodes.JsonObject { ["heartbeat"] = heartbeat },
        };

        try
        {
            var result = await gateway.SetConfigAsync(patch).ConfigureAwait(true);
            var promptsSaved = WritePrompts();

            await RefreshAsync().ConfigureAwait(true);
            var restart = result.RestartRequired.Count == 0
                ? ""
                : $"；需重启 daemon：{string.Join("、", result.RestartRequired)}";
            StatusText = promptsSaved
                ? $"心跳已保存 · 参数已生效，提示词已写回 prompts.json{restart}"
                : $"心跳参数已保存；提示词文件没写（路径不存在）{restart}";
            ErrorText = "";
        }
        catch (Exception ex) when (ex is DaemonUnavailableException or RpcClientException)
        {
            ErrorText = $"心跳保存失败：{Describe(ex)}";
        }
    }

    private bool WritePrompts()
    {
        var path = Paths?.Heartbeat.Prompts;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var root = new System.Text.Json.Nodes.JsonObject();
        foreach (var group in HeartbeatPrompts)
        {
            var bag = new System.Text.Json.Nodes.JsonObject();
            foreach (var line in group.Lines)
            {
                bag[line.Level] = line.Text;
            }

            root[group.Kind] = bag;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(
                path,
                root.ToJsonString(new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                }));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorText = $"提示词写入失败：{ex.Message}";
            return false;
        }
    }

    private static int ToInt(string value)
        => int.TryParse(value, out var parsed) ? parsed : 0;

    private static System.Text.Json.Nodes.JsonArray Thresholds(string value)
    {
        var array = new System.Text.Json.Nodes.JsonArray();
        foreach (var part in value.Split(['/', ',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, out var number))
            {
                array.Add(number);
            }
        }

        return array;
    }

    /// <summary>是否正在等重启确认。</summary>
    [ObservableProperty]
    public partial bool IsConfirmingRestart { get; set; }

    /// <summary>请求重启 daemon（先进入确认态）。</summary>
    [RelayCommand]
    private void RequestRestart() => IsConfirmingRestart = true;

    /// <summary>取消重启。</summary>
    [RelayCommand]
    private void CancelRestart() => IsConfirmingRestart = false;

    /// <summary>确认重启 daemon。</summary>
    [RelayCommand]
    private async Task ConfirmRestartAsync()
    {
        IsConfirmingRestart = false;
        if (_gateway is not { IsConnected: true } gateway)
        {
            return;
        }

        try
        {
            await gateway.RestartDaemonAsync().ConfigureAwait(true);
            StatusText = "已请求重启 daemon；launchd 拉起后桌面会自动重连";
            ErrorText = "";
        }
        catch (Exception ex) when (ex is DaemonUnavailableException or RpcClientException)
        {
            ErrorText = $"重启请求失败：{Describe(ex)}";
        }
    }

    partial void OnErrorTextChanged(string value) => OnPropertyChanged(nameof(HasError));

    partial void OnIsLiveChanged(bool value) => OnPropertyChanged(nameof(IsPlaceholder));

    /// <summary>按设置子页键取提示词预览（连上后是真实文件内容）。</summary>
    /// <param name="key">子页键。</param>
    public PromptPreviewItem? GetPrompt(string key)
        => _prompts.GetValueOrDefault(key)
           ?? SettingsPlaceholderData.Prompts.FirstOrDefault(item => item.Key == key);

    /// <summary>切换索引条目的启用状态（index.upsert）。</summary>
    /// <param name="item">索引行。</param>
    [RelayCommand]
    private async Task ToggleIndexAsync(IndexEntryItem? item)
    {
        if (item is null || _gateway is not { IsConnected: true } gateway)
        {
            return;
        }

        try
        {
            var updated = await gateway
                .UpsertIndexAsync(item.Source with { Enabled = !item.IsEnabled })
                .ConfigureAwait(true);
            item.Update(updated);
            StatusText = updated.Enabled ? $"已启用 {item.Name}" : $"已停用 {item.Name}";
        }
        catch (Exception ex) when (ex is DaemonUnavailableException or RpcClientException)
        {
            ErrorText = $"索引启停失败：{Describe(ex)}";
        }
    }

    /// <summary>删除索引条目（index.remove）。</summary>
    /// <param name="item">索引行。</param>
    [RelayCommand]
    private async Task DeleteIndexAsync(IndexEntryItem? item)
    {
        if (item is null || _gateway is not { IsConnected: true } gateway)
        {
            return;
        }

        try
        {
            await gateway.RemoveIndexAsync(item.Key).ConfigureAwait(true);
            IndexRows.Remove(item);
            StatusText = $"已删除 {item.Name}";
        }
        catch (Exception ex) when (ex is DaemonUnavailableException or RpcClientException)
        {
            ErrorText = $"索引删除失败：{Describe(ex)}";
        }
    }

    private void ApplyPrompts(WorkspacePathsDto paths)
    {
        _prompts.Clear();
        Add("prompts.agents", paths.Prompts.Shared);
        Add("roleplay.behavior", paths.Prompts.RoleplayAgents);
        Add("roleplay.world", paths.Prompts.RoleplayWorld);
        Add("work.guide", paths.Prompts.WorkAgents);
        Add("prompts.soul", paths.Prompts.Soul);
        Add("prompts.user", paths.Prompts.User);
        Add("prompts.memory", paths.Prompts.Memory);

        void Add(string key, string path)
        {
            var placeholder = SettingsPlaceholderData.Prompts.FirstOrDefault(item => item.Key == key);
            if (placeholder is null)
            {
                return;
            }

            var preview = placeholder;
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    preview = new PromptPreviewItem
                    {
                        Key = placeholder.Key,
                        Title = placeholder.Title,
                        Source = path,
                        Description = placeholder.Description,
                        Preview = File.ReadAllText(path),
                        Path = path,
                    };
                }
                else
                {
                    preview = new PromptPreviewItem
                    {
                        Key = placeholder.Key,
                        Title = placeholder.Title,
                        Source = path,
                        Description = placeholder.Description,
                        Preview = "（文件不存在或还没有读权限）",
                        Path = path,
                    };
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                preview = new PromptPreviewItem
                {
                    Key = placeholder.Key,
                    Title = placeholder.Title,
                    Source = path,
                    Description = placeholder.Description,
                    Preview = $"（读取失败：{ex.Message}）",
                    Path = path,
                };
            }

            _prompts[key] = preview;
        }
    }

    private void ApplySubagents(JsonElement config)
    {
        if (config.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        SubagentAutoWait = (Text(config, "tools.subagents.autoWait") ?? "false").ToLowerInvariant();
        SubagentWaitTimeout = Text(config, "tools.subagents.waitTimeoutSeconds") ?? "300";
        SubagentMaxLoops = Text(config, "tools.subagents.maxLoopIterations") ?? "10";

        if (!config.TryGetProperty("tools", out var tools)
            || !tools.TryGetProperty("subagents", out var subagents)
            || !subagents.TryGetProperty("agents", out var agents)
            || agents.ValueKind != JsonValueKind.Array)
        {
            Subagents.Clear();
            NotifySubagents();
            return;
        }

        Subagents.Clear();
        foreach (var agent in agents.EnumerateArray())
        {
            if (agent.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var name = Text(agent, "name") ?? "未命名";
            var toolsValue = agent.TryGetProperty("tools", out var list) && list.ValueKind == JsonValueKind.Array
                ? string.Join(" · ", list.EnumerateArray().Select(item => item.ToString()))
                : "全部工具";
            Subagents.Add(new SubagentRowItem(
                name,
                name,
                Text(agent, "description") ?? "",
                Text(agent, "instructions") ?? "",
                toolsValue,
                string.Equals(Text(agent, "allowShell"), "true", StringComparison.OrdinalIgnoreCase)));
        }

        NotifySubagents();
    }

    private void ApplyHeartbeat(JsonElement config, WorkspacePathsDto paths)
    {
        if (config.ValueKind == JsonValueKind.Object)
        {
            HeartbeatEnabled = string.Equals(
                Text(config, "proactive.heartbeat.enabled"),
                "true",
                StringComparison.OrdinalIgnoreCase);
            HeartbeatMinMinutes = Text(config, "proactive.heartbeat.minMinutes") ?? HeartbeatMinMinutes;
            HeartbeatMaxMinutes = Text(config, "proactive.heartbeat.maxMinutes") ?? HeartbeatMaxMinutes;
            HeartbeatHotZone = Text(config, "proactive.heartbeat.hotZoneMinutes") ?? HeartbeatHotZone;
            HeartbeatCooldown = Text(config, "proactive.heartbeat.cooldownMinutes") ?? HeartbeatCooldown;
            HeartbeatIncreaseMin = Text(config, "proactive.heartbeat.normalIncreaseMin") ?? HeartbeatIncreaseMin;
            HeartbeatIncreaseMax = Text(config, "proactive.heartbeat.normalIncreaseMax") ?? HeartbeatIncreaseMax;
            HeartbeatPenalty = Text(config, "proactive.heartbeat.triggerPenalty") ?? HeartbeatPenalty;
            HeartbeatThreshold = Text(config, "proactive.heartbeat.triggerThreshold") ?? HeartbeatThreshold;
            HeartbeatLongingCap = Text(config, "proactive.heartbeat.longingCap") ?? HeartbeatLongingCap;
            HeartbeatThresholds = Text(config, "proactive.heartbeat.levelThresholds") ?? HeartbeatThresholds;
            HeartbeatFluctuation = Text(config, "proactive.heartbeat.levelFluctuationChance") ?? HeartbeatFluctuation;
            HeartbeatQuietStart = Text(config, "proactive.heartbeat.quietHoursStart") ?? HeartbeatQuietStart;
            HeartbeatQuietEnd = Text(config, "proactive.heartbeat.quietHoursEnd") ?? HeartbeatQuietEnd;
        }

        HeartbeatPrompts.Clear();
        var loaded = TryReadPrompts(paths.Heartbeat.Prompts);
        foreach (var group in loaded ?? SettingsPlaceholderData.HeartbeatPrompts)
        {
            HeartbeatPrompts.Add(group);
        }
    }

    private static IReadOnlyList<HeartbeatPromptGroup>? TryReadPrompts(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var groups = new List<HeartbeatPromptGroup>();
            foreach (var kind in document.RootElement.EnumerateObject())
            {
                if (kind.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var lines = kind.Value.EnumerateObject()
                    .Select(level => new HeartbeatPromptLine(level.Name, level.Value.GetString() ?? ""))
                    .ToList();
                groups.Add(new HeartbeatPromptGroup(kind.Name, DescriptionOf(kind.Name), lines));
            }

            return groups;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string DescriptionOf(string kind)
        => SettingsPlaceholderData.HeartbeatPrompts
               .FirstOrDefault(group => string.Equals(group.Kind, kind, StringComparison.OrdinalIgnoreCase))
               ?.Description
           ?? "系统消息注入：触发时以用户身份交给她的指令。";

    private void NotifySubagents()
    {
        OnPropertyChanged(nameof(SubagentAutoWait));
        OnPropertyChanged(nameof(SubagentWaitTimeout));
        OnPropertyChanged(nameof(SubagentMaxLoops));
        OnPropertyChanged(nameof(SubagentAutoWaitEnabled));
    }

    private void ApplyStates(StatusDto? status)
    {
        if (status is null)
        {
            return;
        }

        StateRows.Clear();
        foreach (var state in status.States)
        {
            StateRows.Add(new StateEditItem(state.Key, state.Name, state.Value, state.Description));
        }
    }

    private void ApplyPaths(WorkspacePathsDto paths)
    {
        Paths = paths;
        ConfigPath = paths.ConfigFile;
        WorkspacePaths.Clear();
        Add("工作区", paths.Workspace);
        Add("config.json", paths.ConfigFile);
        Add("共用规则", paths.Prompts.Shared);
        Add("SOUL.md", paths.Prompts.Soul);
        Add("USER.md", paths.Prompts.User);
        Add("MEMORY.md", paths.Prompts.Memory);
        Add("角色行为规范", paths.Prompts.RoleplayAgents);
        Add("世界观设定", paths.Prompts.RoleplayWorld);
        Add("工作指南", paths.Prompts.WorkAgents);
        Add("心跳提示词", paths.Heartbeat.Prompts);
        Add("心跳日志", paths.Heartbeat.Log);
        Add("daemon 日志", paths.DaemonLog);
        Add("会话导出", paths.Exports);
        Add("头像目录", paths.Avatars);

        void Add(string label, string path)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                WorkspacePaths.Add(new WorkspacePathItem { Label = label, Path = path });
            }
        }
    }

    private static ToolStatusItem ToItem(ToolInfo tool) => new()
    {
        Name = tool.Name,
        Group = tool.Source.StartsWith("mcp:", StringComparison.Ordinal) ? tool.Source[4..] : "内置",
        Description = tool.Description,
        IsEnabled = tool.Enabled,
        RequiresApproval = tool.RequiresApproval,
        Scope = tool.Scope,
        Writes = tool.Writes,
        Source = tool.Source,
    };

    private static McpServerItem ToItem(McpServerStatus server) => new()
    {
        Name = server.Name,
        Endpoint = server.Url,
        Status = !server.Enabled
            ? "已停用"
            : server.Connected
                ? "已连接"
                : string.IsNullOrWhiteSpace(server.Error) ? "未连接" : "连接失败",
        IsHealthy = server.Connected,
        IsEnabled = server.Enabled,
        ToolCount = server.Tools.Count,
        Tools = server.Tools,
        Error = server.Error ?? "",
    };

    private static string Describe(Exception ex) => ex switch
    {
        RpcClientException rpc => rpc.Message,
        _ => ex.Message,
    };

    private static string? Text(JsonElement root, string path)
    {
        var current = root;
        foreach (var part in path.Split('.'))
        {
            if (current.ValueKind != JsonValueKind.Object
                || !current.TryGetProperty(part, out current))
            {
                return null;
            }
        }

        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString(),
            JsonValueKind.Number => current.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => current.GetRawText(),
        };
    }

    private static string ReadTail(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return "";
            }

            var lines = File.ReadAllLines(path);
            var start = Math.Max(0, lines.Length - 120);
            return string.Join(Environment.NewLine, lines[start..]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }
}
