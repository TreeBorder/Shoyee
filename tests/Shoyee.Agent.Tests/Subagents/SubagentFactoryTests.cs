using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Shoyee.Agent.Composition;
using Shoyee.Agent.Subagents;
using Shoyee.Contracts.Abstractions;
using Shoyee.Agent.Tests.TestSupport;
using Shoyee.Contracts.Options;
using Xunit;

namespace Shoyee.Agent.Tests.Subagents;

public sealed class SubagentFactoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "shoyee-subagent-" + Guid.NewGuid().ToString("N"));

    public SubagentFactoryTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static List<AITool> Candidates(params string[] names)
        => names.Select(name => (AITool)AIFunctionFactory.Create(
                static () => "ok",
                new AIFunctionFactoryOptions { Name = name, Description = name }))
            .ToList();

    private static SubagentOptions Options(params SubagentDefinition[] definitions)
        => new(definitions);

    private static SubagentDefinition Definition(string name, params string[] tools) => new()
    {
        Name = name,
        Description = name + " 的说明",
        Instructions = "只做这一件事",
        Tools = tools.ToList(),
    };

    [Fact]
    public void Build_WithoutConfiguration_ReturnsEmpty()
    {
        var client = new RecordingChatClient();

        Assert.Empty(SubagentFactory.Build(null, Candidates("file_access_read"), chatClientFactory: () => client));
        Assert.Empty(SubagentFactory.Build(Options(), Candidates("file_access_read"), chatClientFactory: () => client));
    }

    [Fact]
    public void Build_SkipsBlankAndDuplicateNames()
    {
        var client = new RecordingChatClient();
        var options = Options(
            Definition("Research"),
            Definition("research"),
            Definition("   "),
            Definition("Writer"));

        var agents = SubagentFactory.Build(options, Candidates("file_access_read"), chatClientFactory: () => client);

        Assert.Equal(2, agents.Count);
        Assert.Equal(["Research", "Writer"], agents.Select(a => a.Name));
    }

    [Fact]
    public async Task Build_DefaultTools_AreReadOnlyFileAndMcp()
    {
        var client = new RecordingChatClient();
        var candidates = Candidates(
            "file_access_read", "file_access_write",
            "session_search", "session_read", "index_list", "notes_search", "notes_read",
            "web_search", "web_fetch", "analyze_image", "voice_transcribe",
            "cron_add", "process_start", "voice_speak", "send_file", "image_generate", "terminal",
            "mcp_docs_lookup");
        var agents = SubagentFactory.Build(
            Options(Definition("Researcher")),
            candidates,
            mcpToolNames: ["mcp_docs_lookup"],
            chatClientFactory: () => client);

        var tools = await ToolNamesAsync(agents[0], client);

        Assert.Contains("file_access_read", tools);
        Assert.Contains("file_access_write", tools);
        Assert.Contains("session_search", tools);
        Assert.Contains("web_fetch", tools);
        Assert.Contains("voice_transcribe", tools);
        Assert.Contains("mcp_docs_lookup", tools);
        Assert.DoesNotContain("terminal", tools);
        Assert.DoesNotContain("cron_add", tools);
        Assert.DoesNotContain("process_start", tools);
        Assert.DoesNotContain("voice_speak", tools);
        Assert.DoesNotContain("send_file", tools);
        Assert.DoesNotContain("image_generate", tools);
    }

    [Fact]
    public async Task Build_Whitelist_NarrowsDefaultGroup_ButKeepsFileTools()
    {
        var client = new RecordingChatClient();
        var candidates = Candidates(
            "file_access_read", "file_access_write",
            "session_search", "web_search", "mcp_docs_lookup");
        var agents = SubagentFactory.Build(
            Options(Definition("Researcher", "web_search")),
            candidates,
            mcpToolNames: ["mcp_docs_lookup"],
            chatClientFactory: () => client);

        var tools = await ToolNamesAsync(agents[0], client);

        Assert.Contains("web_search", tools);
        Assert.Contains("file_access_read", tools);
        Assert.Contains("file_access_write", tools);
        Assert.DoesNotContain("session_search", tools);
        Assert.DoesNotContain("mcp_docs_lookup", tools);
    }

    [Fact]
    public async Task Build_AllowShell_AddsTerminal()
    {
        var client = new RecordingChatClient();
        var definition = Definition("Operator");
        definition.AllowShell = true;
        var agents = SubagentFactory.Build(
            Options(definition),
            Candidates("file_access_read", "terminal"),
            chatClientFactory: () => client);

        Assert.Contains("terminal", await ToolNamesAsync(agents[0], client));
    }

    [Fact]
    public async Task WorkAgent_AttachesBackgroundAgentTools_WhenConfigured()
    {
        var client = new RecordingChatClient();
        var subagents = SubagentFactory.Build(
            Options(Definition("Researcher")),
            Candidates("file_access_read"),
            chatClientFactory: () => client);
        var factory = new WorkAgentFactory(
            client,
            _root,
            subagents: subagents,
            subagentOptions: Options(Definition("Researcher")));

        var agent = factory.CreateAgent();
        var session = await factory.CreateSessionAsync(agent, "工作人设");
        await agent.RunAsync("派个活", session);

        var tools = client.OptionCalls[0]!.Tools!.OfType<AIFunction>().Select(t => t.Name).ToList();
        Assert.Contains("background_agents_start_task", tools);
        Assert.Contains("background_agents_get_all_tasks", tools);
    }

    [Fact]
    public async Task WorkAgent_WithoutSubagents_HasNoBackgroundAgentTools()
    {
        var client = new RecordingChatClient();
        var factory = new WorkAgentFactory(client, _root);
        var agent = factory.CreateAgent();
        var session = await factory.CreateSessionAsync(agent, "工作人设");
        await agent.RunAsync("干点活", session);

        var tools = client.OptionCalls[0]!.Tools;
        Assert.DoesNotContain(
            tools?.OfType<AIFunction>() ?? [],
            tool => tool.Name.StartsWith("background_agents_", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NotifyOnComplete_PushesTerminalNotification()
    {
        var client = new RecordingChatClient();
        var pusher = new RecordingPusher();
        var agents = SubagentFactory.Build(
            new SubagentOptions([Definition("Researcher")], NotifyOnComplete: true),
            Candidates("file_access_read"),
            chatClientFactory: () => client,
            notificationPusher: pusher);

        var agent = Assert.IsType<NotifyingAgent>(agents[0]);
        Assert.Equal("Researcher", agent.Name);
        await agent.RunAsync("查点东西", await agent.CreateSessionAsync());

        var push = Assert.Single(pusher.Pushed);
        Assert.Contains("完成", push.Title);
        Assert.Contains("Researcher", push.Title);
        Assert.Equal("应答", push.Body);
    }

    [Fact]
    public async Task NotifyOnComplete_OffOrMissingPusher_DoesNotWrap()
    {
        var client = new RecordingChatClient();

        Assert.IsType<Microsoft.Agents.AI.ChatClientAgent>(
            SubagentFactory.Build(
                Options(Definition("Researcher")),
                Candidates("file_access_read"),
                chatClientFactory: () => client,
                notificationPusher: new RecordingPusher())[0]);
        Assert.IsType<Microsoft.Agents.AI.ChatClientAgent>(
            SubagentFactory.Build(
                new SubagentOptions([Definition("Researcher")], NotifyOnComplete: true),
                Candidates("file_access_read"),
                chatClientFactory: () => client)[0]);
    }

    [Fact]
    public async Task NotifyOnComplete_PushFailureDoesNotFailTask()
    {
        var client = new RecordingChatClient();
        var pusher = new RecordingPusher { Throw = true };
        var agent = SubagentFactory.Build(
            new SubagentOptions([Definition("Researcher")], NotifyOnComplete: true),
            Candidates("file_access_read"),
            chatClientFactory: () => client,
            notificationPusher: pusher)[0];

        var response = await agent.RunAsync("查点东西", await agent.CreateSessionAsync());

        Assert.Equal("应答", response.Text);
        Assert.Empty(pusher.Pushed);
    }

    [Fact]
    public async Task NotifyOnComplete_StreamingPushesOnce()
    {
        var client = new RecordingChatClient();
        var pusher = new RecordingPusher();
        var agent = SubagentFactory.Build(
            new SubagentOptions([Definition("Researcher")], NotifyOnComplete: true),
            Candidates("file_access_read"),
            chatClientFactory: () => client,
            notificationPusher: pusher)[0];

        var session = await agent.CreateSessionAsync();
        await foreach (var _ in agent.RunStreamingAsync("查点东西", session))
        {
        }

        Assert.Equal("应答", Assert.Single(pusher.Pushed).Body);
    }

    private static async Task<List<string>> ToolNamesAsync(AIAgent agent, RecordingChatClient client)
    {
        // 子 agent 复用同一个 RecordingChatClient，跑一次就能拿到它实际收到的工具面
        var session = await agent.CreateSessionAsync();
        await agent.RunAsync("先看看工具", session);
        return client.OptionCalls.LastOrDefault()?.Tools?
                   .OfType<AIFunction>()
                   .Select(tool => tool.Name)
                   .ToList()
               ?? [];
    }
    private sealed class RecordingPusher : INotificationPusher
    {
        public List<(string Title, string Body)> Pushed { get; } = [];

        public bool Throw { get; set; }

        public Task PushAsync(string title, string body, CancellationToken cancellationToken = default)
        {
            if (Throw)
            {
                throw new InvalidOperationException("推送失败");
            }

            Pushed.Add((title, body));
            return Task.CompletedTask;
        }
    }
}
