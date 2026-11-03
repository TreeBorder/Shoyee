namespace Shoyee.Daemon.Configuration;

/// <summary>daemon 退出码（launchd 与运维脚本据此判断）。</summary>
public static class DaemonExitCodes
{
    /// <summary>正常退出。</summary>
    public const int Success = 0;

    /// <summary>启动失败：配置缺失、工作区不存在、端口/权限错误等。</summary>
    public const int StartupFailure = 1;

    /// <summary>已有实例在跑（不抢占）。</summary>
    public const int AlreadyRunning = 3;
}

/// <summary>启动阶段的致命错误：给人看的消息直接进 stderr / 日志。</summary>
public sealed class DaemonStartupException : Exception
{
    /// <summary>创建异常。</summary>
    /// <param name="message">原因与修复提示。</param>
    public DaemonStartupException(string message)
        : base(message)
    {
    }

    /// <summary>创建异常。</summary>
    /// <param name="message">原因与修复提示。</param>
    /// <param name="innerException">内部异常。</param>
    public DaemonStartupException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
