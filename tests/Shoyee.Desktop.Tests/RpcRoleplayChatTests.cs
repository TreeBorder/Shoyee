using System.Text.Json;
using Avalonia;
using Avalonia.Headless;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Options;
using Shoyee.Contracts.Rpc;
using Shoyee.Desktop;
using Shoyee.Desktop.Conversations;
using Shoyee.Desktop.Rpc;
using Shoyee.Desktop.ViewModels;
using Shoyee.Desktop.ViewModels.Items;
using Xunit;

#pragma warning disable xUnit1031 // 同步等待：桌面测试共用单 UI 线程，异步会让后续测试换线程

namespace Shoyee.Desktop.Tests;

/// <summary>角色模式接 RPC 后的投影与事件流（用假网关，不起 daemon）。</summary>
public sealed class RpcRoleplayChatTests
{


    [Fact]
    public void ProjectsRoleplayBubblesWithLaneTagAndImages()
    {
        var created = DateTimeOffset.Now;
        var rows = RoleplayProjection.ToRows(
        [
            new MessageView(1, 1, MessageRole.User, MessageKind.Text, "早上好", null, created, null),
            new MessageView(
                2,
                1,
                MessageRole.Assistant,
                MessageKind.Text,
                "早呀\n\n![照片](/tmp/a.png)\n\n今天想你了",
                null,
                created,
                [
                    new MessageAttachment(
                        "/tmp/voice.mp3",
                        "voice.mp3",
                        "audio/mpeg",
                        2048,
                        AttachmentKind.Audio),
                ]),
        ],
        "桌面");

        Assert.Equal(4, rows.Count);
        Assert.Equal("早上好", rows[0].Item.Text);
        Assert.Equal("早呀", rows[1].Item.Text);
        Assert.True(rows[2].Item.IsImage);
        Assert.Equal("/tmp/a.png", rows[2].Item.ImageSource);
        Assert.Equal("今天想你了", rows[3].Item.Text);
        Assert.Single(rows[3].Item.Attachments);
        Assert.All(rows, row => Assert.Equal("桌面", row.Item.LaneName));
    }

    [Fact]
    public void LoadsLanesAndOnlyShowsCurrentLane()
    {
        var gateway = new FakeRoleplayGateway();
        gateway.Status = new StatusDto(
            "test",
            DateTimeOffset.Now,
            10,
            "/tmp",
            [
                Lane(LaneIds.DesktopRoleplay, LaneChannel.Desktop, "桌面"),
                Lane("mattermost:roleplay", LaneChannel.Mattermost, "筱义 · 角色"),
            ],
            new UsageSummary(0, 0, 0, 0, 0),
            [],
            new DbInfo("/tmp/state.db", 0),
            null);
        gateway.Messages[LaneIds.DesktopRoleplay] = Messages(1, "桌面说");
        gateway.Messages["mattermost:roleplay"] = Messages(2, "手机说");

        var viewModel = new RoleplayViewModel();
        viewModel.AttachGateway(gateway);

        Assert.True(viewModel.IsLive);
        Assert.Equal(2, viewModel.Lanes.Count);
        Assert.True(viewModel.HasMultipleLanes);
        Assert.All(viewModel.VisibleMessages, message => Assert.Equal("桌面", message.LaneName));
        Assert.Contains(viewModel.Messages, message => message.Text == "手机说" && message.LaneName == "Mattermost(角色)");
        Assert.Equal("桌面", viewModel.TimelineTitle);
    }

    [Fact]
    public void SystemInputsHiddenByDefaultAndToggleable()
    {
        var gateway = new FakeRoleplayGateway();
        gateway.Status = StatusWithTwoLanes();
        gateway.Messages[LaneIds.DesktopRoleplay] = new SessionMessagesResult(
        [
            new MessageView(
                1,
                1,
                MessageRole.User,
                MessageKind.SystemInput,
                "[系统消息]心跳注入",
                null,
                DateTimeOffset.Now,
                null),
            new MessageView(
                2,
                1,
                MessageRole.User,
                MessageKind.Text,
                "我说的话",
                null,
                DateTimeOffset.Now,
                null),
        ], false);

        var viewModel = new RoleplayViewModel();
        viewModel.AttachGateway(gateway);

        Assert.DoesNotContain(viewModel.VisibleMessages, message => message.IsSystemInput);
        Assert.Contains(viewModel.VisibleMessages, message => message.Text == "我说的话");

        viewModel.ShowSystemInputs = true;
        Assert.Contains(
            viewModel.VisibleMessages,
            message => message.IsSystemInput && message.Text.Contains("心跳注入", StringComparison.Ordinal));
    }

    [Fact]
    public void SwitchLaneShowsReadOnlyHistoryAndSendStillGoesToDesktop()
    {
        var gateway = new FakeRoleplayGateway();
        gateway.Status = StatusWithTwoLanes();
        gateway.Messages[LaneIds.DesktopRoleplay] = Messages(1, "桌面说");
        gateway.Messages["mattermost:roleplay"] = Messages(2, "手机说");

        var viewModel = new RoleplayViewModel();
        viewModel.AttachGateway(gateway);

        viewModel.SelectLaneCommand.Execute("mattermost:roleplay");
        Assert.Equal("Mattermost(角色)", viewModel.TimelineTitle);
        Assert.DoesNotContain(viewModel.VisibleMessages, message => message.Text == "桌面说");
        Assert.Contains(viewModel.VisibleMessages, message => message.Text == "手机说");

        viewModel.Draft = "我回来了";
        viewModel.SendCommand.Execute(null);

        var sent = Assert.Single(gateway.Sent);
        Assert.Equal(LaneIds.DesktopRoleplay, sent.LaneId);
        Assert.Equal("我回来了", sent.Text);
        Assert.Equal("桌面", viewModel.TimelineTitle);
        Assert.Contains(viewModel.VisibleMessages, message => message.IsUser && message.Text == "我回来了");
    }

    [Fact]
    public void ShowOtherLanesMergesTimeline()
    {
        var gateway = new FakeRoleplayGateway();
        gateway.Status = StatusWithTwoLanes();
        gateway.Messages[LaneIds.DesktopRoleplay] = Messages(1, "桌面说");
        gateway.Messages["mattermost:roleplay"] = Messages(2, "手机说");

        var viewModel = new RoleplayViewModel();
        viewModel.AttachGateway(gateway);
        viewModel.ShowOtherLanes = true;

        Assert.Equal("全部 Lane", viewModel.TimelineTitle);
        Assert.True(viewModel.IsAllLanesSelected);
        Assert.False(viewModel.IsDesktopLaneSelected);
        Assert.Contains(viewModel.VisibleMessages, message => message.Text == "桌面说");
        Assert.Contains(viewModel.VisibleMessages, message => message.Text == "手机说");

        viewModel.ShowDesktopLaneCommand.Execute(null);
        Assert.Equal("桌面", viewModel.TimelineTitle);
        Assert.True(viewModel.IsDesktopLaneSelected);
        Assert.False(viewModel.IsAllLanesSelected);
        Assert.DoesNotContain(viewModel.VisibleMessages, message => message.Text == "手机说");
    }

    [Fact]
    public void StreamsTurnEventsAndCleansInnerVoice()
    {
        var gateway = new FakeRoleplayGateway();
        gateway.Status = StatusWithTwoLanes();
        gateway.Messages[LaneIds.DesktopRoleplay] = new SessionMessagesResult([], false);

        var viewModel = new RoleplayViewModel();
        viewModel.ShowReasoning = true;
        viewModel.AttachGateway(gateway);

        gateway.Raise(RpcEvents.TurnStarted, new TurnStartedEvent("trn_1", LaneIds.DesktopRoleplay, LaneIds.DesktopRoleplay));
        gateway.Raise(RpcEvents.TurnThinking, new TurnThinkingEvent("trn_1", LaneIds.DesktopRoleplay, "先想想", LaneIds.DesktopRoleplay));
        gateway.Raise(RpcEvents.TurnDelta, new TurnDeltaEvent("trn_1", LaneIds.DesktopRoleplay, "我", LaneIds.DesktopRoleplay));
        gateway.Raise(RpcEvents.TurnDelta, new TurnDeltaEvent(
            "trn_1",
            LaneIds.DesktopRoleplay,
            "在呢。<InnerVoice>自检</InnerVoice>",
            LaneIds.DesktopRoleplay));
        gateway.Raise(RpcEvents.TurnCompleted, new TurnCompletedEvent(
            "trn_1",
            LaneIds.DesktopRoleplay,
            9,
            new UsageSummary(1, 10, 5, 0, 10),
            LaneIds.DesktopRoleplay,
            Attachments:
            [
                new MessageAttachment("/tmp/a.png", "a.png", "image/png", 1024, AttachmentKind.Image),
            ]));

        var assistant = viewModel.VisibleMessages.Last(message => message.IsAssistant);
        Assert.Equal("我在呢。", assistant.Text);
        Assert.Single(assistant.Attachments);
        Assert.DoesNotContain(viewModel.VisibleMessages, message => message.Text.Contains("InnerVoice", StringComparison.Ordinal));
        Assert.Contains(viewModel.VisibleMessages, message => message.IsReasoning && message.Text == "先想想");
        Assert.Equal("我在呢。", viewModel.Dialogue);
    }

    [Fact]
    public void OtherLaneCompletionRefreshesThatLaneOnly()
    {
        var gateway = new FakeRoleplayGateway();
        gateway.Status = StatusWithTwoLanes();
        gateway.Messages[LaneIds.DesktopRoleplay] = Messages(1, "桌面说");
        gateway.Messages["mattermost:roleplay"] = Messages(2, "手机说");

        var viewModel = new RoleplayViewModel();
        viewModel.AttachGateway(gateway);

        gateway.Messages["mattermost:roleplay"] = Messages(2, "手机说", "手机又说");
        gateway.Raise(RpcEvents.TurnCompleted, new TurnCompletedEvent(
            "trn_2",
            "mattermost:roleplay",
            11,
            new UsageSummary(1, 10, 5, 0, 10),
            "mattermost:roleplay"));

        Assert.Contains(viewModel.Messages, message => message.Text == "手机又说");
        Assert.Contains(viewModel.Messages, message => message.Text == "桌面说");
    }

    [Fact]
    public void ProjectsStatesAndAppliesStateUpdates()
    {
        var gateway = new FakeRoleplayGateway();
        gateway.Status = StatusWithTwoLanes() with
        {
            States =
            [
                new StateDto("longing", "思念", "69", null, true, DateTimeOffset.UtcNow),
                new StateDto("energy", "精力", "0.7", "0–1 的小数", true, DateTimeOffset.UtcNow),
                new StateDto("mood", "心情", "平静", null, true, DateTimeOffset.UtcNow),
                new StateDto("scene", "场景", "bedroom", null, true, DateTimeOffset.UtcNow),
            ],
        };

        var viewModel = new RoleplayViewModel();
        viewModel.AttachGateway(gateway);

        Assert.Equal("卧室", viewModel.SceneTitle);
        var longing = viewModel.States.Single(state => state.Name == "思念");
        Assert.True(longing.IsNumeric);
        Assert.Equal(69, longing.Percent);
        Assert.Equal("70", viewModel.States.Single(state => state.Name == "精力").Value);
        Assert.False(viewModel.States.Single(state => state.Name == "心情").IsNumeric);

        gateway.Raise(RpcEvents.StateUpdated, new StateDto(
            "longing",
            "思念",
            "88",
            null,
            true,
            DateTimeOffset.UtcNow));

        Assert.Equal(88, viewModel.States.Single(state => state.Name == "思念").Percent);
    }

    [Fact]
    public void SpeaksAssistantMessageThroughGateway()
    {
        var gateway = new FakeRoleplayGateway();
        gateway.Status = StatusWithTwoLanes();
        gateway.Messages[LaneIds.DesktopRoleplay] = Messages(1, "早上好", "早呀，今天想去哪？");

        var viewModel = new RoleplayViewModel();
        viewModel.AttachGateway(gateway);

        var assistant = viewModel.VisibleMessages.Single(message => message.IsAssistant);
        Assert.True(assistant.CanSpeak);

        assistant.SpeakCommand.ExecuteAsync(null).GetAwaiter().GetResult();

        Assert.Equal("早呀，今天想去哪？", Assert.Single(gateway.Spoken));
        Assert.True(assistant.HasSpeakError);
        Assert.False(assistant.IsSpeaking);
    }

    [Fact]
    public void FallsBackToAllLanesWhenDesktopLaneIsEmpty()
    {
        var gateway = new FakeRoleplayGateway();
        gateway.Status = StatusWithTwoLanes();
        gateway.Messages[LaneIds.DesktopRoleplay] = new SessionMessagesResult([], false);
        gateway.Messages["mattermost:roleplay"] = Messages(1, "导入的历史", "她还记得");

        var viewModel = new RoleplayViewModel();
        viewModel.AttachGateway(gateway);

        Assert.True(viewModel.ShowOtherLanes);
        Assert.Equal("全部 Lane", viewModel.TimelineTitle);
        Assert.Contains(viewModel.VisibleMessages, message => message.Text == "导入的历史");
    }

    [Fact]
    public void KeepsSystemInjectionsHiddenByDefaultAndUnwrapsTimeTags()
    {
        var rows = RoleplayProjection.ToRows(
        [
            new MessageView(
                1,
                1,
                MessageRole.User,
                MessageKind.Text,
                "<SystemMessage>[系统消息]用 camera 出图[/]</SystemMessage><Time>[消息时间: 2026-11-02 22:00]</Time>",
                null,
                DateTimeOffset.Now,
                null),
            new MessageView(
                2,
                1,
                MessageRole.User,
                MessageKind.Text,
                "<Time>[消息时间: 2026-11-02 22:47]</Time><UserMessage>晚安，老婆</UserMessage>",
                null,
                DateTimeOffset.Now,
                null),
        ],
        "筱义 · 角色");

        var injection = Assert.Single(rows, row => row.Item.IsSystemInput);
        Assert.Equal(ChatEntryKind.SystemInput, injection.Item.Kind);
        Assert.Contains("[系统消息]", injection.Item.Text, StringComparison.Ordinal);

        var user = Assert.Single(rows, row => !row.Item.IsSystemInput);
        Assert.Equal("晚安，老婆", user.Item.Text);
        Assert.DoesNotContain("SystemMessage", user.Item.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnswersApprovalThroughGateway()
    {
        var gateway = new FakeRoleplayGateway();
        gateway.Status = StatusWithTwoLanes();
        gateway.Messages[LaneIds.DesktopRoleplay] = new SessionMessagesResult([], false);

        var viewModel = new RoleplayViewModel();
        viewModel.AttachGateway(gateway);

        gateway.Raise(RpcEvents.ApprovalRequest, new ApprovalRequestDto(
            "apv_1",
            LaneIds.DesktopRoleplay,
            "trn_1",
            "shell_run",
            "rm -rf ./dist",
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMinutes(10),
            LaneIds.DesktopRoleplay));

        Assert.True(viewModel.HasPendingApproval);
        viewModel.ApproveApprovalCommand.Execute(null);

        Assert.Equal(ApprovalDecision.Approved, Assert.Single(gateway.Approvals).Decision);
        Assert.False(viewModel.HasPendingApproval);
    }

    private static LaneStatus Lane(string id, LaneChannel channel, string name)
        => new(id, channel, Mode.Roleplay, name, Connected: true, ActiveSessionId: null, LastError: null);

    private static StatusDto StatusWithTwoLanes() => new(
        "test",
        DateTimeOffset.Now,
        10,
        "/tmp",
        [
            Lane(LaneIds.DesktopRoleplay, LaneChannel.Desktop, "桌面"),
            Lane("mattermost:roleplay", LaneChannel.Mattermost, "筱义 · 角色"),
        ],
        new UsageSummary(0, 0, 0, 0, 0),
        [],
        new DbInfo("/tmp/state.db", 0),
        null);

    private static SessionMessagesResult Messages(long firstId, params string[] texts)
        => new(
            texts
                .Select((text, index) => new MessageView(
                    firstId + index,
                    (int)(firstId + index),
                    index % 2 == 0 ? MessageRole.User : MessageRole.Assistant,
                    MessageKind.Text,
                    text,
                    null,
                    DateTimeOffset.Now.AddMinutes(index),
                    null))
                .ToList(),
            false);

    private sealed class FakeRoleplayGateway : IRoleplayChatGateway
    {
        public bool IsConnected { get; set; } = true;

        public StatusDto Status { get; set; } = StatusWithTwoLanes();

        public Dictionary<string, SessionMessagesResult> Messages { get; } = [];

        public List<(string LaneId, string Text)> Sent { get; } = [];

        public List<(string ApprovalId, ApprovalDecision Decision)> Approvals { get; } = [];

        public event Action<string, JsonElement>? EventReceived;

        public Task<StatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Status);

        public Task<SessionMessagesResult> GetLaneMessagesAsync(
            string laneId,
            long? beforeId = null,
            int limit = 200,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Messages.TryGetValue(laneId, out var result)
                ? result
                : new SessionMessagesResult([], false));

        public Task<string> SendTurnAsync(
            string laneId,
            string text,
            CancellationToken cancellationToken = default)
        {
            Sent.Add((laneId, text));
            return Task.FromResult("trn_1");
        }

        public Task RespondApprovalAsync(
            string approvalId,
            ApprovalDecision decision,
            CancellationToken cancellationToken = default)
        {
            Approvals.Add((approvalId, decision));
            return Task.CompletedTask;
        }

        public Task CancelTurnAsync(
            string laneId,
            string? turnId = null,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public List<string> Spoken { get; } = [];

        public Task<TtsSynthesizeResult> SynthesizeSpeechAsync(
            string text,
            CancellationToken cancellationToken = default)
        {
            Spoken.Add(text);
            return Task.FromResult(new TtsSynthesizeResult(
                "asset_1",
                "/tmp/not-a-real-audio.mp3",
                "audio/mpeg",
                1));
        }

        public void Raise(string name, object payload)
            => EventReceived?.Invoke(name, JsonSerializer.SerializeToElement(payload, JsonWire.Options));
    }
}
