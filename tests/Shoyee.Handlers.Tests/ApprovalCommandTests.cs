using System.Text.Json;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Handlers.Commands;
using Shoyee.Handlers.Tests.TestSupport;
using Xunit;

namespace Shoyee.Handlers.Tests;

/// <summary>审批指令：批量批准/拒绝、always 语义、没有待审批的提示。</summary>
public sealed class ApprovalCommandTests
{
    private static readonly CommandContext Context = new("mattermost:work", "ses_1", string.Empty);

    [Fact]
    public async Task Approve_WithoutPending_RepliesButDoesNotResolve()
    {
        var approvals = new FakeApprovalConsole();
        var command = new ApproveCommand(approvals);

        var reply = await command.HandleAsync(Context with { Arguments = string.Empty });

        Assert.Contains("没有", reply);
        Assert.Empty(approvals.Resolved);
    }

    [Fact]
    public async Task Approve_ResolvesWholeBatchOnce()
    {
        var approvals = new FakeApprovalConsole();
        approvals.Pending.Add(Request("apv_1", "terminal"));
        approvals.Pending.Add(Request("apv_2", "file_access_write"));
        var command = new ApproveCommand(approvals);

        var reply = await command.HandleAsync(Context with { Arguments = string.Empty });

        var resolved = Assert.Single(approvals.Resolved);
        Assert.Equal("ses_1", resolved.Target);
        Assert.Equal(ApprovalDecision.Approved, resolved.Decision);
        Assert.Equal("im:mattermost:work", resolved.DecidedBy);
        Assert.Contains("2", reply);
    }

    [Fact]
    public async Task ApproveAlways_UsesAlwaysDecision()
    {
        var approvals = new FakeApprovalConsole();
        approvals.Pending.Add(Request("apv_1", "terminal"));
        var command = new ApproveCommand(approvals);

        var reply = await command.HandleAsync(Context with { Arguments = "ALWAYS" });

        Assert.Equal(ApprovalDecision.Always, Assert.Single(approvals.Resolved).Decision);
        Assert.Contains("不再问", reply);
    }

    [Fact]
    public async Task Reject_UsesDeniedDecision()
    {
        var approvals = new FakeApprovalConsole();
        approvals.Pending.Add(Request("apv_1", "terminal"));
        var command = new RejectCommand(approvals);

        var reply = await command.HandleAsync(Context with { Arguments = string.Empty });

        Assert.Equal(ApprovalDecision.Denied, Assert.Single(approvals.Resolved).Decision);
        Assert.Contains("已拒绝", reply);
    }

    [Fact]
    public async Task Reject_AlreadyResolved_RepliesWithoutDecision()
    {
        var approvals = new FakeApprovalConsole();
        var command = new RejectCommand(approvals);

        var reply = await command.HandleAsync(Context);

        Assert.Contains("没有", reply);
        Assert.Empty(approvals.Resolved);
    }

    [Fact]
    public async Task Stop_CancelsCurrentTurnOfTarget()
    {
        var turns = new StubTurnService();
        var command = new StopCommand(turns);

        var reply = await command.HandleAsync(Context);

        Assert.Equal(("ses_1", (string?)null), Assert.Single(turns.Cancelled));
        Assert.Contains("停", reply);
    }

    private static ApprovalRequestDto Request(string id, string tool) => new(
        id,
        "ses_1",
        "trn_1",
        tool,
        Command: "rm -rf /tmp/x",
        Arguments: JsonSerializer.SerializeToElement(new { command = "rm -rf /tmp/x" }),
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch.AddMinutes(10),
        "mattermost:work");
}
