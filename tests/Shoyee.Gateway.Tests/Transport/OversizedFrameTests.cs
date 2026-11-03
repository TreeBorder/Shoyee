using Xunit;
using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text.Json;
using Shoyee.Client.Transport;
using Shoyee.Contracts.Rpc;
using Shoyee.Gateway.Tests.TestSupport;

namespace Shoyee.Gateway.Tests.Transport;

public sealed class OversizedFrameTests
{
    [Fact]
    public async Task OversizedFrame_ReturnsFrameTooLargeAndDisconnects()
    {
        await using var bed = await GatewayTestBed.StartAsync();

        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(bed.Paths.SocketPath));
        await using var stream = new NetworkStream(socket, ownsSocket: false);

        var header = new byte[FrameCodec.HeaderSize];
        BinaryPrimitives.WriteUInt32BigEndian(header, RpcProtocol.MaxFrameBytes + 1u);
        await stream.WriteAsync(header);

        var frame = await FrameCodec.ReadAsync(stream, cancellationToken: new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);
        Assert.NotNull(frame);

        using var document = JsonDocument.Parse(frame!);
        var error = document.RootElement.GetProperty("error");
        Assert.Equal(RpcErrors.FrameTooLarge, error.GetProperty("code").GetInt32());

        // 服务端随后断开：下一次读取应看到干净的 EOF
        var next = await FrameCodec.ReadAsync(stream, cancellationToken: new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);
        Assert.Null(next);
    }
}
