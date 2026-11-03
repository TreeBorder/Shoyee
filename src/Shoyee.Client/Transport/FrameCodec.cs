using System.Buffers.Binary;
using Shoyee.Contracts.Rpc;

namespace Shoyee.Client.Transport;

/// <summary>帧体超过约定上限（<c>-32003</c>）。</summary>
public sealed class FrameTooLargeException : Exception
{
    /// <summary>创建异常。</summary>
    /// <param name="length">帧头里声明的字节数。</param>
    /// <param name="maxBytes">允许的上限。</param>
    public FrameTooLargeException(int length, int maxBytes)
        : base($"帧体 {length} 字节，超过上限 {maxBytes} 字节")
    {
        Length = length;
        MaxBytes = maxBytes;
    }

    /// <summary>声明的字节数。</summary>
    public int Length { get; }

    /// <summary>允许的上限。</summary>
    public int MaxBytes { get; }
}

/// <summary>
/// 长度前缀帧编解码：<c>4 字节大端无符号长度 + UTF-8 JSON</c>。
/// 服务端与客户端共用同一份实现，避免两边对长度/字节序理解不一致。
/// </summary>
public static class FrameCodec
{
    /// <summary>帧头长度。</summary>
    public const int HeaderSize = 4;

    /// <summary>写一帧。</summary>
    /// <param name="stream">目标流。</param>
    /// <param name="payload">帧体（不复制）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public static async ValueTask WriteAsync(
        Stream stream,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (payload.Length > RpcProtocol.MaxFrameBytes)
        {
            throw new FrameTooLargeException(payload.Length, RpcProtocol.MaxFrameBytes);
        }

        var header = new byte[HeaderSize];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)payload.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        if (payload.Length > 0)
        {
            await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        }

        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 读一帧。连接在帧边界正常关闭返回 null；帧体超限抛 <see cref="FrameTooLargeException"/>
    /// （调用方负责回错误并断开，不再读正文）。
    /// </summary>
    /// <param name="stream">来源流。</param>
    /// <param name="maxBytes">允许的帧体上限。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public static async ValueTask<byte[]?> ReadAsync(
        Stream stream,
        int maxBytes = RpcProtocol.MaxFrameBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var header = new byte[HeaderSize];
        if (!await ReadExactAsync(stream, header, allowCleanEof: true, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var length = BinaryPrimitives.ReadUInt32BigEndian(header);
        if (length > int.MaxValue || length > (uint)maxBytes)
        {
            throw new FrameTooLargeException(length > int.MaxValue ? int.MaxValue : (int)length, maxBytes);
        }

        if (length == 0)
        {
            return [];
        }

        var payload = new byte[length];
        await ReadExactAsync(stream, payload, allowCleanEof: false, cancellationToken).ConfigureAwait(false);
        return payload;
    }

    private static async ValueTask<bool> ReadExactAsync(
        Stream stream,
        Memory<byte> buffer,
        bool allowCleanEof,
        CancellationToken cancellationToken)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer[read..], cancellationToken).ConfigureAwait(false);
            if (count == 0)
            {
                if (read == 0 && allowCleanEof)
                {
                    return false;
                }

                throw new EndOfStreamException("帧未读完连接就断了");
            }

            read += count;
        }

        return true;
    }
}
