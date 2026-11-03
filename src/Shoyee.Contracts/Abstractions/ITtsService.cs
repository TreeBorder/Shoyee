using Shoyee.Contracts.Dtos;

namespace Shoyee.Contracts.Abstractions;

/// <summary>
/// 桌面「播放」按钮的语音合成出口：按消息或文本合成一次并落盘，返回资产。
///
/// 与 agent 的 <c>voice_speak</c> 工具走同一个 provider；区别只在于谁触发、谁消费。
/// 未配置合成供应商时不注册本服务，RPC 返回明确的「未配置」。
/// </summary>
public interface ITtsService
{
    /// <summary>合成并落盘。</summary>
    /// <param name="request">消息 id 或直接文本。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<TtsSynthesizeResult> SynthesizeAsync(
        TtsSynthesizeRequest request,
        CancellationToken cancellationToken = default);
}
