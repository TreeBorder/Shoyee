using ModelContextProtocol.Client;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Options;

namespace Shoyee.Tools.Mcp;

/// <summary>
/// MCP 连接管理：持有当前工具集，按**当前生效的配置**重连（<c>mcp.reload</c>）。
///
/// 运行中的 agent 在启动时就把工具定义烘进了请求前缀，所以重连只换「设置页看到的状态/工具清单」
/// 与之后的子智能体候选；新工具面真正进对话仍要重启 daemon（由 <c>mcp.reload</c> 的
/// <c>restartRequired</c> 告知桌面）。
/// </summary>
public sealed class McpManager : Shoyee.Contracts.Abstractions.IMcpControl, IAsyncDisposable
{
    private readonly Func<WorkspaceConfig> _config;
    private readonly Func<McpServerConfig, IClientTransport>? _transportFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<McpToolset> _retired = [];
    private McpToolset _current;

    private McpManager(Func<WorkspaceConfig> config, Func<McpServerConfig, IClientTransport>? transportFactory, McpToolset initial)
    {
        _config = config;
        _transportFactory = transportFactory;
        _current = initial;
    }

    /// <summary>按配置连接一次并创建管理器。</summary>
    /// <param name="config">读取当前生效配置（<c>config.set</c> 之后要拿到新值）。</param>
    /// <param name="transportFactory">测试可注入传输工厂。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public static async Task<McpManager> CreateAsync(
        Func<WorkspaceConfig> config,
        Func<McpServerConfig, IClientTransport>? transportFactory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        var initial = await McpConnector
            .ConnectAsync(config(), transportFactory, cancellationToken)
            .ConfigureAwait(false);
        return new McpManager(config, transportFactory, initial);
    }

    /// <summary>当前工具集（启动时那一代）。</summary>
    public McpToolset Current => Volatile.Read(ref _current);

    /// <inheritdoc />
    public IReadOnlyList<McpServerStatus> Status => Current.Servers;

    /// <inheritdoc />
    public async Task<McpReloadResult> ReloadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var before = ToolNames(Current);
            var next = await McpConnector
                .ConnectAsync(_config(), _transportFactory, cancellationToken)
                .ConfigureAwait(false);
            var old = Interlocked.Exchange(ref _current, next);
            // 旧连接可能还有在跑的调用：不立刻 Dispose，攒到管理器释放时一起关
            _retired.Add(old);

            var restartRequired = !before.SetEquals(ToolNames(next));
            return new McpReloadResult(next.Servers, restartRequired);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>断开当前与历次重连留下的全部连接。</summary>
    public async ValueTask DisposeAsync()
    {
        var all = new List<McpToolset>(_retired) { Current };
        foreach (var toolset in all.Distinct())
        {
            await toolset.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static HashSet<string> ToolNames(McpToolset toolset)
        => toolset.Servers
            .SelectMany(static server => server.Tools)
            .ToHashSet(StringComparer.Ordinal);
}
