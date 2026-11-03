using Microsoft.Extensions.Logging;
using Shoyee.Agent.Media;
using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Options;
using Shoyee.Contracts.Rpc;
using Shoyee.Tools.Composition;
using Shoyee.Tools.Files;
using Shoyee.Tools.Process;

namespace Shoyee.Daemon.Configuration;

/// <summary>
/// <c>config.set</c> / <c>config.reload</c> 的实现：编辑并写回 <c>{workspace}/config.json</c>，
/// 再把能热更新的部分应用到运行时。
///
/// **刻意只热更新不碰模型请求前缀的项**（路径黑名单、终端硬禁正则、附件分析参数）。
/// 工具定义、模型档位、MCP、子智能体、渠道、通知等一律要求重启：工作模式的 prompt cache
/// 前缀里含 system + 工具定义，中途换工具等于让后续所有请求缓存失效。
/// </summary>
internal sealed class ConfigReloader : IConfigReloader
{
    private readonly WorkspaceConfigStore _store;
    private readonly ToolsetBuilder _toolsets;
    private readonly ProcessRegistry? _processes;
    private readonly IAttachmentAnalyzer? _analyzer;
    private readonly ILogger<ConfigReloader> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ConfigReloader(
        WorkspaceConfigStore store,
        ToolsetBuilder toolsets,
        ILogger<ConfigReloader> logger,
        ProcessRegistry? processes = null,
        IAttachmentAnalyzer? analyzer = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(toolsets);
        ArgumentNullException.ThrowIfNull(logger);
        _store = store;
        _toolsets = toolsets;
        _processes = processes;
        _analyzer = analyzer;
        _logger = logger;
    }

    /// <inheritdoc />
    public WorkspaceConfig Current => _store.Current;

    /// <inheritdoc />
    public async Task<ConfigSetResult> SetAsync(
        ConfigSetRequest request,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var root = _store.Current.Root;
            var edited = WorkspaceConfigEditor.Edit(root, request);

            // 先落盘再应用：应用失败（理论上不会，校验与解析用的是同一套）时文件已经是新值，
            // 下次启动仍然生效，不会出现「运行期是新的、磁盘是旧的」这种反方向不一致。
            WorkspaceConfigWriter.Write(root, edited.Document);

            var (changed, restart) = Apply(edited.Config);

            _logger.LogInformation(
                "config.set 完成：已应用 [{Changed}]，需重启 [{Restart}]，提醒 {WarningCount} 条",
                string.Join("、", changed),
                string.Join("、", restart),
                edited.Warnings.Count);

            return new ConfigSetResult(
                changed,
                restart,
                Path.Combine(root, "config.json"),
                edited.Warnings);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<ConfigReloadResult> ReloadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            WorkspaceConfig next;
            try
            {
                next = WorkspaceConfigLoader.Load(_store.Current.Root);
            }
            catch (DaemonStartupException ex)
            {
                throw new RpcException(RpcErrors.InvalidParams, ex.Message);
            }

            var (changed, restart) = Apply(next);
            _logger.LogInformation(
                "config.reload 完成：已应用 [{Changed}]，需重启 [{Restart}]",
                string.Join("、", changed),
                string.Join("、", restart));
            return new ConfigReloadResult(changed, restart);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>比较并应用（调用方已持有 <see cref="_gate"/>）。</summary>
    private (IReadOnlyList<string> Changed, IReadOnlyList<string> RestartRequired) Apply(WorkspaceConfig next)
    {
        var (changed, restart) = ConfigSectionDiff.Compare(_store.Current, next);

        if (changed.Count > 0)
        {
            // 只更新黑名单/正则/分析参数：不重建工具、不换 agent、不动框架上下文
            if (changed.Contains("tools.deniedPaths"))
            {
                _toolsets.ReloadPathPolicy(next);
            }

            if (changed.Contains("shell.denyPatterns"))
            {
                // 工作目录用**当前生效值**：shell 的其他字段属于「需重启」，不能顺带热更新
                _processes?.Reload(
                    FileAccessTools.DefaultDirectory(_store.Current),
                    next.Shell?.DenyPatterns);
            }

            if (changed.Contains("attachments") && _analyzer is AttachmentAnalyzer analyzer)
            {
                analyzer.Reload(next.Attachments);
            }
        }

        _store.Replace(next);

        if (restart.Count > 0)
        {
            _logger.LogWarning(
                "config.json 中以下配置段已修改，但需要重启 daemon 才生效（避免破坏 prompt cache）：{Sections}",
                string.Join("、", restart));
        }

        return (changed, restart);
    }
}
