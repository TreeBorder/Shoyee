using Microsoft.Extensions.AI;
using Shoyee.Agent.Approval;
using Shoyee.Agent.Tests.TestSupport;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Rpc;
using Xunit;

namespace Shoyee.Agent.Tests.Approval;

public sealed class ApprovalBrokerTests
{
    [Fact]
    public async Task RequestAsync_PublishesRequest_AndApprovalResolvesBatch()
    {
        var sink = new RecordingTurnEventSink();
        using var broker = new ApprovalBroker(sink);
        var request = CreateRequest("req-1", "call-1", "shell_run", "rm -rf ./build");

        var pending = broker.RequestAsync("ses_1", "trn_1", "desktop:work", [request]);

        var raised = await sink.WaitForAsync(e => e.EventName == RpcEvents.ApprovalRequest);
        var dto = Assert.IsType<ApprovalRequestDto>(raised.Payload);
        Assert.Equal("shell_run", dto.ToolName);
        Assert.Equal("rm -rf ./build", dto.Command);
        Assert.Equal("desktop:work", dto.LaneId);
        Assert.True(broker.HasPending("ses_1"));

        Assert.True(await broker.ResolveAsync(dto.ApprovalId, ApprovalDecision.Approved, "desktop", "准了"));

        var contents = await pending;
        var response = Assert.IsType<ToolApprovalResponseContent>(Assert.Single(contents));
        Assert.True(response.Approved);
        Assert.False(broker.HasPending("ses_1"));

        var resolved = sink.Payloads<ApprovalResolvedDto>(RpcEvents.ApprovalResolved);
        Assert.Equal(ApprovalDecision.Approved, Assert.Single(resolved).Decision);
    }

    [Fact]
    public async Task RequestAsync_WithoutAnswer_ExpiresAndDenies()
    {
        var sink = new RecordingTurnEventSink();
        using var broker = new ApprovalBroker(sink, timeout: TimeSpan.FromMilliseconds(50));

        var contents = await broker.RequestAsync(
            "ses_1", "trn_1", null, [CreateRequest("req-1", "call-1", "shell_run", "ls")]);

        var response = Assert.IsType<ToolApprovalResponseContent>(Assert.Single(contents));
        Assert.False(response.Approved);
        Assert.False(broker.HasPending("ses_1"));

        var resolved = Assert.Single(sink.Payloads<ApprovalResolvedDto>(RpcEvents.ApprovalResolved));
        Assert.Equal(ApprovalDecision.Expired, resolved.Decision);
    }

    [Fact]
    public async Task RequestAsync_CancelledTurn_ResolvesAsDenied()
    {
        var sink = new RecordingTurnEventSink();
        using var broker = new ApprovalBroker(sink);
        using var cts = new CancellationTokenSource();

        var pending = broker.RequestAsync(
            "ses_1", "trn_1", null, [CreateRequest("req-1", "call-1", "shell_run", "ls")], cts.Token);

        await sink.WaitForAsync(e => e.EventName == RpcEvents.ApprovalRequest);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.False(broker.HasPending("ses_1"));
        Assert.Equal(
            ApprovalDecision.Expired,
            Assert.Single(sink.Payloads<ApprovalResolvedDto>(RpcEvents.ApprovalResolved)).Decision);
    }

    [Fact]
    public async Task ResolveAsync_UnknownApproval_ReturnsFalse()
    {
        using var broker = new ApprovalBroker(new RecordingTurnEventSink());

        Assert.False(await broker.ResolveAsync("apv_missing", ApprovalDecision.Approved, "desktop"));
    }

    private static ToolApprovalRequestContent CreateRequest(
        string requestId,
        string callId,
        string name,
        string command)
        => new(
            requestId,
            new FunctionCallContent(callId, name, new Dictionary<string, object?> { ["command"] = command }));
}
