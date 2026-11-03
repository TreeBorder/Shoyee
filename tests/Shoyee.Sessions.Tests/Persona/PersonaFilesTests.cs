using Shoyee.Contracts.Domain;
using Shoyee.Sessions.Persona;
using Xunit;

namespace Shoyee.Sessions.Tests.Persona;

public sealed class PersonaFilesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "shoyee-persona-" + Guid.NewGuid().ToString("N"));

    public PersonaFilesTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Load_ReadsExistingFiles_AndSkipsWorldForWork()
    {
        Write("AGENTS.shared.md", "shared");
        Write("SOUL.md", "soul");
        Write("USER.md", "user");
        Write("MEMORY.md", "memory");
        Write("modes/work/AGENTS.md", "work-agents");
        Write("modes/roleplay/AGENTS.md", "rp-agents");
        Write("modes/roleplay/WORLD.md", "world");

        var files = new PersonaFiles(_root);

        var work = files.Load(Mode.Work);
        Assert.Equal("shared", work.SharedAgents);
        Assert.Equal("work-agents", work.ModeAgents);
        Assert.Equal("soul", work.Soul);
        Assert.Equal(string.Empty, work.World);
        Assert.Equal("memory", work.Memory);
        Assert.Equal("user", work.User);

        var roleplay = files.Load(Mode.Roleplay);
        Assert.Equal("rp-agents", roleplay.ModeAgents);
        Assert.Equal("world", roleplay.World);
    }

    [Fact]
    public void Load_MissingFilesBecomeEmptyStrings()
    {
        var content = new PersonaFiles(_root).Load(Mode.Roleplay);

        Assert.Equal(string.Empty, content.SharedAgents);
        Assert.Equal(string.Empty, content.ModeAgents);
        Assert.Equal(string.Empty, content.Soul);
        Assert.Equal(string.Empty, content.World);
        Assert.Equal(string.Empty, content.Memory);
        Assert.Equal(string.Empty, content.User);
    }

    [Fact]
    public void Load_DoesNotCache_SoFileChangesAreVisible()
    {
        Write("SOUL.md", "第一版");
        var files = new PersonaFiles(_root);
        Assert.Equal("第一版", files.Load(Mode.Work).Soul);

        Write("SOUL.md", "第二版");
        Assert.Equal("第二版", files.Load(Mode.Work).Soul);
    }

    private void Write(string relativePath, string content)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
