using Xunit;
using System.Buffers.Binary;
using Shoyee.Client.Transport;
using Shoyee.Contracts.Rpc;

namespace Shoyee.Gateway.Tests.Transport;

public sealed class FrameCodecTests
{
    [Fact]
    public async Task WriteThenRead_RoundTrips()
    {
        var payload = "你好，Shoyee"u8.ToArray();
        await using var stream = new MemoryStream();

        await FrameCodec.WriteAsync(stream, payload);
        stream.Position = 0;
        var read = await FrameCodec.ReadAsync(stream);

        Assert.Equal(payload, read);
    }

    [Fact]
    public async Task Read_CleanEof_ReturnsNull()
    {
        await using var stream = new MemoryStream();

        var read = await FrameCodec.ReadAsync(stream);

        Assert.Null(read);
    }

    [Fact]
    public async Task Read_TruncatedHeader_Throws()
    {
        await using var stream = new MemoryStream([0x00, 0x00]);

        await Assert.ThrowsAsync<EndOfStreamException>(async () => await FrameCodec.ReadAsync(stream));
    }

    [Fact]
    public async Task Read_TooLarge_Throws()
    {
        await using var stream = new MemoryStream();
        var header = new byte[FrameCodec.HeaderSize];
        BinaryPrimitives.WriteUInt32BigEndian(header, 1024);
        await stream.WriteAsync(header);
        stream.Position = 0;

        var ex = await Assert.ThrowsAsync<FrameTooLargeException>(async () => await FrameCodec.ReadAsync(stream, maxBytes: 64));

        Assert.Equal(1024, ex.Length);
    }

    [Fact]
    public async Task Write_TooLarge_Throws()
    {
        await using var stream = new MemoryStream();

        await Assert.ThrowsAsync<FrameTooLargeException>(
            async () => await FrameCodec.WriteAsync(stream, new byte[RpcProtocol.MaxFrameBytes + 1]));
    }
}
