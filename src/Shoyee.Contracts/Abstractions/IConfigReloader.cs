using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Options;

namespace Shoyee.Contracts.Abstractions;

/// <summary>
/// 工作区配置的读写：<c>config.get</c> / <c>config.set</c> / <c>config.reload</c>。
/// 写盘与应用在同一把锁里完成，避免两个客户端同时改配置时后写覆盖前写。
/// </summary>
public interface IConfigReloader
{
    /// <summary>当前生效的配置（<c>config.get</c> 用它，避免读到启动时的旧快照）。</summary>
    WorkspaceConfig Current { get; }

    /// <summary>
    /// 写 <c>{workspace}/config.json</c> 并按热更新规则应用（<c>config.set</c>）。
    /// 校验失败抛 <c>invalid_params</c>，文件不动。
    /// </summary>
    /// <param name="request">局部合并或整份原文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<ConfigSetResult> SetAsync(ConfigSetRequest request, CancellationToken cancellationToken = default);

    /// <summary>从 <c>{workspace}/config.json</c> 重读并应用。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<ConfigReloadResult> ReloadAsync(CancellationToken cancellationToken = default);
}
