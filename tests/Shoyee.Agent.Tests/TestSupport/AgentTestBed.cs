using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Shoyee.Agent.Approval;
using Shoyee.Agent.Composition;
using Shoyee.Agent.Media;
using Shoyee.Agent.Runtime;
using Shoyee.Agent.Runtime.Roleplay;
using Shoyee.Agent.Runtime.Work;
using Shoyee.Agent.Sessions;
using Shoyee.Contracts.Domain;
using Shoyee.Sessions.Lifecycle;
using Shoyee.Sessions.Persistence;
using Shoyee.Sessions.Persona;
using Shoyee.Sessions.Roleplay;
using Shoyee.Sessions.Summary;
using Shoyee.Storage.Abstractions;

namespace Shoyee.Agent.Tests.TestSupport;

/// <summary>轮次服务的测试装配：真实 runner + 内存仓储 + 记录型事件出口。</summary>
internal sealed class AgentTestBed : IDisposable
{
    public AgentTestBed(IChatClient client, TimeSpan? approvalTimeout = null, IAttachmentAnalyzer? analyzer = null)
    {
        Root = Path.Combine(Path.GetTempPath(), "shoyee-agent-bed-" + Guid.NewGuid().ToString("N"));
        WorkspaceBootstrap.EnsurePersonaFiles(Root);

        Messages = new RecordingMessageRepository();
        Rounds = new InMemoryRoundRepository(messages: Messages);
        Summaries = new InMemorySummaryRepository();
        Sessions = new InMemorySessionRepository();
        Lanes = new InMemoryLaneRepository();
        States = new InMemorySessionStateRepository();
        Sink = new RecordingTurnEventSink();

        var persona = new PersonaFiles(Root);
        var recorder = new RoundRecorder(Rounds);
        var roleplayAgent = new RoleplayAgentFactory(client).CreateAgent(recorder);
        var roleplay = new RoleplayTurnRunner(
            roleplayAgent,
            new RoleplayContextBuilder(persona, Rounds, Summaries, Lanes),
            recorder,
            new RoleplaySummarizer(persona, Rounds, Summaries),
            Messages);

        WorkFactory = new WorkAgentFactory(client, Root);
        WorkAgent = WorkFactory.CreateAgent();
        Cache = new WorkSessionCache(WorkAgent, new ShoyeeAgentSessionStore(States));
        var work = new WorkTurnRunner(WorkAgent, WorkFactory, Cache, new SessionTranscript(Messages));
        var catalog = new SessionCatalog(Sessions, Messages, Lanes, Root);

        Broker = new ApprovalBroker(Sink, timeout: approvalTimeout);
        Service = new TurnService(Sessions, Lanes, roleplay, work, Broker, Sink, analyzer: analyzer, rounds: Rounds, messages: Messages);
        SessionService = new SessionService(
            catalog, new SessionFactory(Sessions), WorkFactory, WorkAgent, Cache, persona, Lanes);
    }

    public string Root { get; }

    public InMemoryRoundRepository Rounds { get; }

    public InMemorySummaryRepository Summaries { get; }

    public InMemorySessionRepository Sessions { get; }

    public InMemoryLaneRepository Lanes { get; }

    public InMemorySessionStateRepository States { get; }

    public RecordingMessageRepository Messages { get; }

    public RecordingTurnEventSink Sink { get; }

    public ApprovalBroker Broker { get; }

    public TurnService Service { get; }

    public SessionService SessionService { get; }

    public WorkAgentFactory WorkFactory { get; }

    public HarnessAgent WorkAgent { get; }

    public WorkSessionCache Cache { get; }

    /// <summary>播一条工作会话，并把框架 session 放进常驻缓存（相当于 status=已打开）。</summary>
    public async Task SeedWorkSessionAsync(
        string sessionId = "ses_1",
        string laneId = "mattermost:work",
        string prompt = "工作人设")
    {
        Sessions.Seed(new SessionRecord(
            sessionId,
            Mode.Work,
            LaneChannel.Mattermost,
            laneId,
            "mattermost:dm:test",
            "测试会话",
            null,
            DateTimeOffset.UtcNow.AddHours(-1),
            DateTimeOffset.UtcNow.AddHours(-1),
            Archived: false));
        Lanes.Seed(new LaneRecord(
            laneId,
            LaneChannel.Mattermost,
            Mode.Work,
            "test",
            "工作",
            sessionId,
            null,
            null,
            DateTimeOffset.UtcNow));

        var session = await WorkFactory.CreateSessionAsync(WorkAgent, prompt);
        Cache.Put(sessionId, session);
    }

    public void Dispose()
    {
        Broker.Dispose();
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
