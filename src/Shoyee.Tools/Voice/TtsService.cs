using Shoyee.Contracts.Abstractions;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Dtos;
using Shoyee.Contracts.Rpc;
using Shoyee.Storage.Abstractions;
using Shoyee.Tools.Media;
using Shoyee.Voice;

namespace Shoyee.Tools.Voice;

/// <summary>
/// <c>tts.synthesize</c> 的实现：按消息或文本合成，落 <c>assets/audio/YYYY/MM/</c> 并登记资产。
///
/// 落盘策略与 <c>voice_speak</c> 工具一致（provider 不碰文件系统），
/// 区别只是入口在 RPC、结果给桌面播放按钮，不挂到任何助手消息上。
/// </summary>
public sealed class TtsService : ITtsService
{
    /// <summary>单次合成上限，与 <c>voice_speak</c> 一致；长文由客户端分段。</summary>
    public const int MaxChars = 2_000;

    private readonly ISpeechSynthesizer _synthesizer;
    private readonly IMessageRepository? _messages;
    private readonly IAssetStore _assets;
    private readonly string _workspaceRoot;

    /// <summary>创建服务。</summary>
    /// <param name="synthesizer">语音合成供应商。</param>
    /// <param name="assets">资产登记。</param>
    /// <param name="workspaceRoot">工作区根目录（落盘用）。</param>
    /// <param name="messages">消息投影仓库（按 messageId 取正文；可空 = 只能合成直接文本）。</param>
    public TtsService(
        ISpeechSynthesizer synthesizer,
        IAssetStore assets,
        string workspaceRoot,
        IMessageRepository? messages = null)
    {
        ArgumentNullException.ThrowIfNull(synthesizer);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _synthesizer = synthesizer;
        _assets = assets;
        _workspaceRoot = workspaceRoot;
        _messages = messages;
    }

    /// <inheritdoc />
    public async Task<TtsSynthesizeResult> SynthesizeAsync(
        TtsSynthesizeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var text = request.Text;
        if (request.MessageId is { } messageId)
        {
            if (_messages is null)
            {
                throw new RpcException(RpcErrors.Internal, "消息仓库未接入，无法按 messageId 合成");
            }

            var row = await _messages.GetAsync(messageId, cancellationToken).ConfigureAwait(false)
                      ?? throw new RpcException(
                          RpcErrors.NotFound, "消息不存在", new { messageId });

            if (row.Kind is MessageKind.ToolCall or MessageKind.ToolResult or MessageKind.Summary
                or MessageKind.SystemInput
                || string.IsNullOrWhiteSpace(row.Content))
            {
                throw new RpcException(
                    RpcErrors.InvalidParams, "这条消息没有可朗读的正文", new { messageId });
            }

            text = row.Content;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new RpcException(RpcErrors.InvalidParams, "messageId 与 text 至少给一个");
        }

        var length = text.EnumerateRunes().Count();
        if (length > MaxChars)
        {
            throw new RpcException(
                RpcErrors.InvalidParams,
                $"文本 {length} 字，超过单次上限 {MaxChars} 字；请分段合成",
                new { length, max = MaxChars });
        }

        SynthesizedAudio audio;
        try
        {
            audio = await _synthesizer
                .SynthesizeAsync(new SynthesisRequest(text, request.Style, request.Voice), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 供应商的失败原因原样给客户端（key/网络/配额），不折成 internal
            throw new RpcException(RpcErrors.ModelUnavailable, "语音合成失败：" + ex.Message);
        }

        var directory = Path.Combine(
            _workspaceRoot,
            AssetStore.Directory,
            "audio",
            DateTime.Now.ToString("yyyy"),
            DateTime.Now.ToString("MM"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + audio.Extension);
        await File.WriteAllBytesAsync(path, audio.Data, cancellationToken).ConfigureAwait(false);

        var asset = _assets.Register(path);
        return new TtsSynthesizeResult(
            asset.AssetId,
            asset.Path,
            asset.Mime,
            asset.SizeBytes,
            AudioFormats.DurationMilliseconds(audio.Data, audio.Mime));
    }
}
