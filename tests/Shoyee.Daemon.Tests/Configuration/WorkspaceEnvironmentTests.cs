using Shoyee.Daemon.Configuration;
using Xunit;

namespace Shoyee.Daemon.Tests.Configuration;

/// <summary>工作区 .env 的读取规则。</summary>
public sealed class WorkspaceEnvironmentTests
{
    [Fact]
    public void LoadsMissingVariablesWithoutOverridingProcessEnvironment()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"shoyee-env-{Guid.NewGuid():N}");
        var suffix = Guid.NewGuid().ToString("N");
        var freshKey = $"SHOYEE_TEST_ENV_{suffix}";
        var keptKey = $"SHOYEE_TEST_KEEP_{suffix}";

        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, ".env"), $"""
            # 注释行
            {freshKey}=fresh-value

            export {keptKey}='already-set-in-process'
            INVALID KEY=ignored
            """);

        Environment.SetEnvironmentVariable(keptKey, "from-process");
        try
        {
            var applied = WorkspaceEnvironment.Apply(directory);

            Assert.Contains(freshKey, applied);
            Assert.DoesNotContain(keptKey, applied);
            Assert.Equal("fresh-value", Environment.GetEnvironmentVariable(freshKey));
            Assert.Equal("from-process", Environment.GetEnvironmentVariable(keptKey));
        }
        finally
        {
            Environment.SetEnvironmentVariable(freshKey, null);
            Environment.SetEnvironmentVariable(keptKey, null);
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void MissingFileIsNotAnError()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"shoyee-env-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            Assert.Empty(WorkspaceEnvironment.Apply(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
