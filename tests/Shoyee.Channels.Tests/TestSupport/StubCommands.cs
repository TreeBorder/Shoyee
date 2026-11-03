using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Handlers.Commands;

namespace Shoyee.Channels.Tests.TestSupport;

/// <summary>回显参数的测试指令（只为验证管线拦截）。</summary>
internal sealed class EchoCommand : ICommandHandler
{
    public string CommandName => "echo";

    public string Description => "/echo 回显参数";

    public Task<string> HandleAsync(CommandContext context, CancellationToken cancellationToken = default)
        => Task.FromResult("echo:" + context.Arguments);
}

/// <summary>固定返回一批待审批请求的审批台。</summary>
internal sealed class StubApprovalConsole : IApprovalConsole
{
    public List<ApprovalRequestDto> Pending { get; } = [];

    public List<(string Target, ApprovalDecision Decision)> Resolved { get; } = [];

    public IReadOnlyList<ApprovalRequestDto> Peek(string sessionOrLaneId) => Pending;

    public Task<int> ResolveAllAsync(
        string sessionOrLaneId,
        ApprovalDecision decision,
        string decidedBy,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        var count = Pending.Count;
        Pending.Clear();
        Resolved.Add((sessionOrLaneId, decision));
        return Task.FromResult(count);
    }
}
