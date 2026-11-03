using Shoyee.Contracts.Domain;
using Shoyee.Sessions.Persona;
using Xunit;

namespace Shoyee.Sessions.Tests.Persona;

public sealed class WorkspaceBootstrapTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "shoyee-bootstrap-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void EnsurePersonaFiles_CreatesAllTemplates()
    {
        var result = WorkspaceBootstrap.EnsurePersonaFiles(_root);

        Assert.Equal(7, result.CreatedFiles.Count);
        Assert.Single(result.Messages);

        var persona = new PersonaFiles(_root).Load(Mode.Work);
        Assert.Equal(WorkspaceTemplates.SharedAgents, persona.SharedAgents);
        Assert.Equal(WorkspaceTemplates.WorkAgents, persona.ModeAgents);
        Assert.Equal(WorkspaceTemplates.Soul, persona.Soul);
        Assert.Equal(WorkspaceTemplates.User, persona.User);
        Assert.Equal(WorkspaceTemplates.Memory, persona.Memory);
        Assert.Equal(string.Empty, persona.World);

        Assert.Equal(WorkspaceTemplates.RoleplayAgents, new PersonaFiles(_root).Load(Mode.Roleplay).ModeAgents);
        Assert.Equal(WorkspaceTemplates.World, new PersonaFiles(_root).Load(Mode.Roleplay).World);
    }

    [Fact]
    public void EnsurePersonaFiles_DoesNotOverwriteExistingFile()
    {
        var soulPath = Path.Combine(_root, PersonaFiles.SoulFileName);
        Directory.CreateDirectory(_root);
        File.WriteAllText(soulPath, "人工改过的人设");

        var result = WorkspaceBootstrap.EnsurePersonaFiles(_root);

        Assert.Equal("人工改过的人设", File.ReadAllText(soulPath));
        Assert.DoesNotContain(PersonaFiles.SoulFileName, result.CreatedFiles);
        Assert.Equal(6, result.CreatedFiles.Count);
    }

    [Fact]
    public void EnsurePersonaFiles_SecondRunCreatesNothing()
    {
        WorkspaceBootstrap.EnsurePersonaFiles(_root);

        var second = WorkspaceBootstrap.EnsurePersonaFiles(_root);

        Assert.Empty(second.CreatedFiles);
        Assert.Empty(second.Messages);
    }
}
