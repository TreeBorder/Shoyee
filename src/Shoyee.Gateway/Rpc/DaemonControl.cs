namespace Shoyee.Gateway.Rpc;

/// <summary>daemon 停机闸门：进入停机后拒绝新的轮次/会话创建，但允许审批答复等收尾操作。</summary>
public sealed class ShutdownGate
{
    private int _stopping;

    /// <summary>是否正在停机。</summary>
    public bool IsStopping => Volatile.Read(ref _stopping) != 0;

    /// <summary>标记进入停机（幂等）。</summary>
    public void MarkStopping() => Interlocked.Exchange(ref _stopping, 1);
}

/// <summary><c>daemon.shutdown</c> 的出口：方法处理完先回响应，再由宿主决定何时真正退出。</summary>
public sealed class DaemonControl
{
    /// <summary>收到停机请求时触发。</summary>
    public event Action? ShutdownRequested;

    /// <summary>请求停机。</summary>
    public void RequestShutdown() => ShutdownRequested?.Invoke();
}
