using Xunit;
using Shoyee.Client.Transport;
using Shoyee.Daemon.Configuration;
using Shoyee.Daemon.Tests.TestSupport;

namespace Shoyee.Daemon.Tests.Configuration;

public sealed class DaemonConfigLoaderTests
{
    [Fact]
    public void MissingConfigFile_Throws()
    {
        using var dir = new TempDir();
        var paths = new ShoyeePaths(dir.Path);

        var ex = Assert.Throws<DaemonStartupException>(() => DaemonConfigLoader.Load(paths));

        Assert.Contains(paths.ConfigPath, ex.Message);
    }

    [Fact]
    public void MissingWorkspace_ThrowsInsteadOfFallingBack()
    {
        using var dir = new TempDir();
        var paths = new ShoyeePaths(dir.Path);
        var missing = Path.Combine(dir.Path, "not-mounted");
        File.WriteAllText(paths.ConfigPath, $$"""{ "workspace": "{{missing}}" }""");

        var ex = Assert.Throws<DaemonStartupException>(() => DaemonConfigLoader.Load(paths));

        Assert.Contains("工作区不存在", ex.Message);
        Assert.Contains(missing, ex.Message);
    }

    [Fact]
    public void ExistingWorkspace_IsResolved()
    {
        using var dir = new TempDir();
        var workspace = Path.Combine(dir.Path, "workspace");
        Directory.CreateDirectory(workspace);
        var paths = new ShoyeePaths(dir.Path);
        File.WriteAllText(paths.ConfigPath, $$"""{ "workspace": "{{workspace}}" }""");

        var config = DaemonConfigLoader.Load(paths);

        Assert.Equal(workspace, config.Workspace);
    }

    [Fact]
    public void BlankWorkspace_Throws()
    {
        using var dir = new TempDir();
        var paths = new ShoyeePaths(dir.Path);
        File.WriteAllText(paths.ConfigPath, """{ "workspace": "  " }""");

        Assert.Throws<DaemonStartupException>(() => DaemonConfigLoader.Load(paths));
    }
}
