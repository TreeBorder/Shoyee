namespace Shoyee.Tools.Process;

/// <summary>
/// 后台进程完成的投递口。Channels / Agent 接入后实现它（把回执变成一条入站消息）；
/// 未挂时进程照常跑、日志留档，用 <c>process_status</c> 取结果。
/// </summary>
public interface IProcessCompletionSink
{
    /// <summary>投递一条完成回执。</summary>
    /// <param name="completion">回执内容。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task NotifyAsync(ProcessCompletion completion, CancellationToken cancellationToken = default);
}
