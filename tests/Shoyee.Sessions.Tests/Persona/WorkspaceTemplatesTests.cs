using Shoyee.Sessions.Persona;
using Xunit;

namespace Shoyee.Sessions.Tests.Persona;

public sealed class WorkspaceTemplatesTests
{
    [Fact]
    public void Templates_CallTheHumanUser_NotBoss()
    {
        string[] templates =
        [
            WorkspaceTemplates.SharedAgents,
            WorkspaceTemplates.WorkAgents,
            WorkspaceTemplates.RoleplayAgents,
            WorkspaceTemplates.World,
            WorkspaceTemplates.Soul,
            WorkspaceTemplates.User,
            WorkspaceTemplates.Memory,
        ];

        Assert.All(templates, template => Assert.DoesNotContain("老板", template, StringComparison.Ordinal));
    }
}
