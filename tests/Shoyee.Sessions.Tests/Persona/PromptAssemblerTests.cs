using Microsoft.Extensions.AI;
using Shoyee.Sessions.Persona;
using Xunit;

namespace Shoyee.Sessions.Tests.Persona;

public sealed class PromptAssemblerTests
{
    private static readonly PersonaContent Full = new(
        SharedAgents: "共用守则",
        ModeAgents: "模式守则",
        Soul: "人设正文",
        World: "世界观正文",
        Memory: "记忆正文",
        User: "画像正文");

    [Fact]
    public void BuildWorkSystemPrompt_OrdersSectionsFromStableToVolatile()
    {
        var prompt = PromptAssembler.BuildWorkSystemPrompt(Full, "索引目录");

        var shared = prompt.IndexOf("共用守则", StringComparison.Ordinal);
        var work = prompt.IndexOf("模式守则", StringComparison.Ordinal);
        var soul = prompt.IndexOf("人设正文", StringComparison.Ordinal);
        var user = prompt.IndexOf("画像正文", StringComparison.Ordinal);
        var index = prompt.IndexOf("索引目录", StringComparison.Ordinal);
        var memory = prompt.IndexOf("记忆正文", StringComparison.Ordinal);

        Assert.True(shared >= 0 && shared < work);
        Assert.True(work < soul);
        Assert.True(soul < user);
        Assert.True(user < index);
        Assert.True(index < memory);
    }

    [Fact]
    public void BuildWorkSystemPrompt_SkipsEmptySections()
    {
        var content = Full with { SharedAgents = "", ModeAgents = "", Memory = "", User = "", World = "" };

        var prompt = PromptAssembler.BuildWorkSystemPrompt(content, indexDirectory: null);

        Assert.Contains("人设正文", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("AGENTS.shared.md", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("modes/work/AGENTS.md", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("MEMORY.md", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("USER.md", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("INDEX", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildWorkSystemPrompt_EmptyIndexMeansNoIndexSection()
    {
        var prompt = PromptAssembler.BuildWorkSystemPrompt(Full, "   ");

        Assert.DoesNotContain("INDEX", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildRoleplayBackground_CombinesAgents_AndOrdersStableToVolatile()
    {
        var messages = PromptAssembler.BuildRoleplayBackground(Full);

        Assert.Equal(5, messages.Count);
        Assert.All(messages, m => Assert.Equal(ChatRole.System, m.Role));
        Assert.Contains("共用守则", messages[0].Text!, StringComparison.Ordinal);
        Assert.Contains("模式守则", messages[0].Text!, StringComparison.Ordinal);
        Assert.Equal("人设正文", messages[1].Text);
        Assert.Equal("世界观正文", messages[2].Text);
        Assert.Equal("画像正文", messages[3].Text);
        Assert.Equal("记忆正文", messages[4].Text);
    }

    [Fact]
    public void BuildRoleplayBackground_SkipsEmptySections()
    {
        var content = Full with { SharedAgents = "", ModeAgents = "", World = "", Memory = "", User = "" };

        var messages = PromptAssembler.BuildRoleplayBackground(content);

        var only = Assert.Single(messages);
        Assert.Equal("人设正文", only.Text);
    }
}
