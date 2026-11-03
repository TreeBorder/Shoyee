using Xunit;
using Shoyee.Daemon.Hosting;
using Shoyee.Daemon.Tests.TestSupport;

namespace Shoyee.Daemon.Tests.Hosting;

public sealed class SingleInstanceLockTests
{
    [Fact]
    public void SecondAcquire_ReturnsNull()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "run", "shoyee.lock");

        using var first = SingleInstanceLock.TryAcquire(path);

        Assert.NotNull(first);
        Assert.Null(SingleInstanceLock.TryAcquire(path));
    }

    [Fact]
    public void ReleasingLock_AllowsReacquire()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "shoyee.lock");

        var first = SingleInstanceLock.TryAcquire(path);
        Assert.NotNull(first);
        first.Dispose();

        using var second = SingleInstanceLock.TryAcquire(path);
        Assert.NotNull(second);
    }
}
