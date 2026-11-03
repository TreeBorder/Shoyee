using System.Net;
using System.Text;
using Shoyee.Notify.ServerChan;
using Xunit;

namespace Shoyee.Notify.ServerChan.Tests;

public sealed class ServerChanPusherTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "shoyee-sc-" + Guid.NewGuid().ToString("N")[..8]);

    public ServerChanPusherTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public async Task PushAsync_PostsTitleAndBodyToOfficialUrl()
    {
        var config = WriteConfig("SCT_UID=123\nSCT_SENDKEY=SCTKEY\n");
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"code":0,"message":"ok"}""");
        using var http = new HttpClient(handler);
        using var pusher = new ServerChanPusher(new ServerChanOptions(config), http);

        await pusher.PushAsync("标题", "**正文**");

        Assert.Equal("https://123.push.ft07.com/send/SCTKEY.send", handler.RequestUri);
        Assert.Contains("title=%E6%A0%87%E9%A2%98", handler.Body);
        Assert.Contains("desp=%2A%2A%E6%AD%A3%E6%96%87%2A%2A", handler.Body);
    }

    [Fact]
    public async Task PushAsync_NonZeroCode_ThrowsWithResponse()
    {
        var config = WriteConfig("SCT_UID=123\nSCT_SENDKEY=SCTKEY\n");
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"code":40001,"message":"bad key"}""");
        using var http = new HttpClient(handler);
        using var pusher = new ServerChanPusher(new ServerChanOptions(config), http);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => pusher.PushAsync("t", "b"));

        Assert.Contains("40001", ex.Message);
    }

    [Fact]
    public async Task PushAsync_MissingSendKey_ThrowsActionableError()
    {
        var config = WriteConfig("SCT_UID=123\n");
        using var pusher = new ServerChanPusher(new ServerChanOptions(config));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => pusher.PushAsync("t", "b"));

        Assert.Contains("SCT_SENDKEY", ex.Message);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string WriteConfig(string content)
    {
        var path = Path.Combine(_dir, "config.sh");
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    private sealed class RecordingHandler(HttpStatusCode status, string payload) : HttpMessageHandler
    {
        public string? RequestUri { get; private set; }

        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri?.ToString();
            Body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
        }
    }
}
