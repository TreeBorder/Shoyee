using System.Net;
using Microsoft.Extensions.AI;
using Shoyee.Contracts.Options;
using Shoyee.Tools.Media;
using Shoyee.Tools.Tests.Photo;
using Xunit;

namespace Shoyee.Tools.Tests.Media;

/// <summary>
/// 视觉端点空 content 的回归：思考模型把 <c>max_tokens</c> 花在思维链上时，
/// 工具要能识别 <c>finish_reason</c>、加大预算重试，而不是只报一句「没有返回内容」。
/// </summary>
public sealed class ImageAnalysisToolTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "shoyee-image-analysis-" + Guid.NewGuid().ToString("N")[..8]);

    private readonly string _image;

    public ImageAnalysisToolTests()
    {
        Directory.CreateDirectory(_root);
        _image = Path.Combine(_root, "a.png");
        File.WriteAllBytes(_image, [1, 2, 3]);
    }

    [Fact]
    public async Task 输出被截断_加大预算重试并返回文字()
    {
        var handler = new QueueHandler(
            Json("""{"choices":[{"message":{"content":null,"reasoning_content":"想很久"},"finish_reason":"length"}],"usage":{"completion_tokens":4096}}"""),
            Json("""{"choices":[{"message":{"content":"图里是一只猫。"},"finish_reason":"stop"}],"usage":{"completion_tokens":20}}"""));

        var result = await InvokeAsync(handler);

        Assert.Equal("图里是一只猫。", result);
        Assert.Equal(2, handler.Bodies.Count);
        Assert.Contains("\"max_tokens\":8192", handler.Bodies[1]);
        // 首轮默认关掉思考，降延迟也省预算
        Assert.Contains("\"thinking\":{\"type\":\"disabled\"}", handler.Bodies[0]);
    }

    [Fact]
    public async Task 端点不认thinking字段_去掉后重试()
    {
        var handler = new QueueHandler(
            Json("""{"error":{"message":"unknown field thinking"}}""", HttpStatusCode.BadRequest),
            Json("""{"choices":[{"message":{"content":"ok"},"finish_reason":"stop"}]}"""));

        var result = await InvokeAsync(handler);

        Assert.Equal("ok", result);
        Assert.Equal(2, handler.Bodies.Count);
        Assert.DoesNotContain("\"thinking\"", handler.Bodies[1]);
    }

    [Fact]
    public async Task content是分块数组时拼接()
    {
        var handler = new QueueHandler(
            Json("""{"choices":[{"message":{"content":[{"type":"text","text":"上半"},{"type":"text","text":"下半"}]},"finish_reason":"stop"}]}"""));

        Assert.Equal("上半下半", await InvokeAsync(handler));
    }

    [Fact]
    public async Task 空content_把finish_reason报出来()
    {
        var handler = new QueueHandler(
            Json("""{"choices":[{"message":{"content":null},"finish_reason":"stop"}]}"""));

        var result = await InvokeAsync(handler);

        Assert.Contains("finish_reason=stop", result);
    }

    [Fact]
    public async Task 只剩思考内容_提示思考模式()
    {
        var handler = new QueueHandler(
            Json("""{"choices":[{"message":{"content":null,"reasoning_content":"想了半天"},"finish_reason":"stop"}]}"""));

        var result = await InvokeAsync(handler);

        Assert.Contains("思考内容", result);
    }

    [Fact]
    public async Task 拒答_返回拒绝原因()
    {
        var handler = new QueueHandler(
            Json("""{"choices":[{"message":{"content":null,"refusal":"无法处理这张图片"},"finish_reason":"stop"}]}"""));

        var result = await InvokeAsync(handler);

        Assert.Contains("无法处理这张图片", result);
    }

    public void Dispose()
    {
        PhotoTestSupport.TryDelete(_root);
    }

    private async Task<string> InvokeAsync(QueueHandler handler)
    {
        var config = PhotoTestSupport.Config(_root) with
        {
            Models = new ModelProfiles(
                new ModelEndpoint("http://localhost", "m", "KEY"),
                new ModelEndpoint("http://localhost", "s", "KEY"),
                new ModelEndpoint("https://api.deepseek.com", "deepseek-flash", "KEY")),
        };
        var descriptor = ImageAnalysisTool.Create(
            config,
            config.Models.Vision,
            new HttpClient(handler));
        Assert.NotNull(descriptor);

        var result = await ((AIFunction)descriptor.Tool).InvokeAsync(
            new AIFunctionArguments { ["imagePaths"] = new[] { _image } });

        return result?.ToString() ?? string.Empty;
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(body) };

    private sealed class QueueHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Bodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));
            return _responses.Count > 0
                ? _responses.Dequeue()
                : throw new InvalidOperationException("没有排队的响应了");
        }
    }
}
