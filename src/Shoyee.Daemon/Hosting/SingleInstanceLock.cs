using Shoyee.Client.Transport;

namespace Shoyee.Daemon.Hosting;

/// <summary>
/// 单实例锁：以 <see cref="FileShare.None"/> 独占打开 <c>run/shoyee.lock</c>。
///
/// .NET 在 Unix 上用 <c>flock</c> 实现 FileShare 语义，所以第二个进程（或同一进程的第二次打开）
/// 会直接拿到 <see cref="IOException"/>，不需要自己 P/Invoke。进程退出（含崩溃）时内核释放，
/// 不会留下需要手工清理的死锁。
/// </summary>
public sealed class SingleInstanceLock : IDisposable
{
    private readonly FileStream _stream;
    private int _disposed;

    private SingleInstanceLock(FileStream stream)
    {
        _stream = stream;
    }

    /// <summary>尝试获取锁；已有实例持有时返回 null。</summary>
    /// <param name="lockPath">锁文件路径（目录会按需创建）。</param>
    public static SingleInstanceLock? TryAcquire(string lockPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockPath);
        var directory = Path.GetDirectoryName(lockPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        FileStream stream;
        try
        {
            stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return null;
        }

        ShoyeePaths.RestrictFile(lockPath);
        return new SingleInstanceLock(stream);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _stream.Dispose();
        }
    }
}
