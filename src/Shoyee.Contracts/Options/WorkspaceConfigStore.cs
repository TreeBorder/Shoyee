namespace Shoyee.Contracts.Options;

/// <summary>
/// 当前生效的工作区配置。启动时写入初始配置，<c>config.reload</c> 成功后替换。
///
/// 只放「可热更新」的读取入口：长期持有配置快照的组件（渠道适配器、出图/语音供应商等）
/// 仍在启动时拿一份，改动需要重启 daemon 才生效。
/// </summary>
public sealed class WorkspaceConfigStore
{
    private WorkspaceConfig _current;

    /// <summary>用启动时读到的配置创建。</summary>
    /// <param name="initial">初始配置。</param>
    public WorkspaceConfigStore(WorkspaceConfig initial)
    {
        ArgumentNullException.ThrowIfNull(initial);
        _current = initial;
    }

    /// <summary>当前配置（引用替换是原子的）。</summary>
    public WorkspaceConfig Current => _current;

    /// <summary>替换当前配置。</summary>
    /// <param name="config">新配置（工作区根必须与旧配置一致）。</param>
    public void Replace(WorkspaceConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (!string.Equals(config.Root, _current.Root, StringComparison.Ordinal))
        {
            throw new ArgumentException("不能通过 config.reload 更换工作区根目录。", nameof(config));
        }

        _current = config;
    }
}
