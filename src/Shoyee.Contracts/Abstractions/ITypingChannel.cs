namespace Shoyee.Contracts.Abstractions;

/// <summary>
/// 可选的 typing 能力：适配器按渠道支持情况实现，管线用它显示「正在输入」。
///
/// 成对调用：回合开始发 <see cref="SendTypingAsync"/>，回合结束（成功/失败）发 <see cref="StopTypingAsync"/>。
/// 自带过期语义的渠道（如 Mattermost 约 5s）把 Stop 实现成空操作即可。
/// </summary>
public interface ITypingChannel
{
    /// <summary>开始 typing。</summary>
    /// <param name="target">回投地址（<see cref="Dtos.InboundMessage.Target"/>）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task SendTypingAsync(string target, CancellationToken cancellationToken = default);

    /// <summary>停止 typing（渠道自动过期时可为空操作）。</summary>
    /// <param name="target">回投地址。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task StopTypingAsync(string target, CancellationToken cancellationToken = default);
}
