using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Shoyee.Photo.Comfy.Tests;

/// <summary>ComfyUI 协议：模板替换、入队、轮询、取图；用假 handler，不连真服务。</summary>
public sealed class ComfyPhotoProviderTests
{
    [Fact]
    public async Task Generate_SubstitutesTemplateAndReturnsBytes()
    {
        var root = NewRoot();
        try
        {
            WriteWorkflow(root, "sdxl.json", "base");
            WriteWorkflow(root, "sdxl_noface.json", "noface");

            var handler = new FakeHandler();
            var provider = Create(root, handler);

            var result = await provider.GenerateAsync(new PhotoRequest(
                "a, b",
                "bad",
                832,
                1216,
                42,
                "xiaoyi"));

            var image = Assert.Single(result.Images);
            Assert.Equal([7, 8, 9], image.Data);
            Assert.Equal("image/png", image.Mime);

            var queued = handler.QueuedBody!;
            using var document = JsonDocument.Parse(queued);
            var inputs = document.RootElement.GetProperty("prompt").GetProperty("node").GetProperty("inputs");
            Assert.Equal("base", document.RootElement.GetProperty("prompt").GetProperty("node").GetProperty("marker").GetString());
            Assert.Equal("a, b", inputs.GetProperty("positive").GetString());
            Assert.Equal("bad", inputs.GetProperty("negative").GetString());
            Assert.Equal("42", inputs.GetProperty("seed").GetString());
            Assert.Equal("832", inputs.GetProperty("width").GetString());
            Assert.Equal("25", inputs.GetProperty("steps").GetString());
            Assert.Equal("7.0", inputs.GetProperty("cfg").GetString());
            Assert.Equal("xiaoyi", inputs.GetProperty("prefix").GetString());
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Generate_FaceVisible_UsesNoFaceWorkflow()
    {
        var root = NewRoot();
        try
        {
            WriteWorkflow(root, "sdxl.json", "base");
            WriteWorkflow(root, "sdxl_noface.json", "noface");

            var handler = new FakeHandler();
            var provider = Create(root, handler);

            await provider.GenerateAsync(new PhotoRequest("x", "face visible", 1, 1, 1));

            using var document = JsonDocument.Parse(handler.QueuedBody!);
            Assert.Equal(
                "noface",
                document.RootElement.GetProperty("prompt").GetProperty("node").GetProperty("marker").GetString());
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Generate_Timeout_ThrowsActionable()
    {
        var root = NewRoot();
        try
        {
            WriteWorkflow(root, "sdxl.json", "base");
            var handler = new FakeHandler(neverCompletes: true);
            var provider = new ComfyPhotoProvider(
                new ComfyPhotoOptions(TimeoutSeconds: 1, PollIntervalSeconds: 1),
                root,
                new HttpClient(handler));

            var error = await Assert.ThrowsAsync<TimeoutException>(
                () => provider.GenerateAsync(new PhotoRequest("x", "", 1, 1, 1)));

            Assert.Contains("prompt_id", error.Message);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Generate_MissingWorkflow_Throws()
    {
        var root = NewRoot();
        try
        {
            var provider = Create(root, new FakeHandler());

            await Assert.ThrowsAsync<FileNotFoundException>(
                () => provider.GenerateAsync(new PhotoRequest("x", "", 1, 1, 1)));
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static ComfyPhotoProvider Create(string root, FakeHandler handler)
        => new(
            new ComfyPhotoOptions(PollIntervalSeconds: 1),
            root,
            new HttpClient(handler));

    private static void WriteWorkflow(string root, string name, string marker)
    {
        var directory = Path.Combine(root, ".comfyui", "workflows");
        Directory.CreateDirectory(directory);
        var template = """
            {
              "node": {
                "marker": "__MARKER__",
                "inputs": {
                  "positive": "{{PROMPT}}",
                  "negative": "{{NEGATIVE_PROMPT}}",
                  "seed": "{{SEED}}",
                  "width": "{{WIDTH}}",
                  "height": "{{HEIGHT}}",
                  "steps": "{{STEPS}}",
                  "cfg": "{{CFG}}",
                  "prefix": "{{FILENAME_PREFIX}}"
                }
              }
            }
            """;
        File.WriteAllText(Path.Combine(directory, name), template.Replace("__MARKER__", marker));
    }

    private static string NewRoot() => Path.Combine(
        Path.GetTempPath(),
        "shoyee-photo-comfy-" + Guid.NewGuid().ToString("N"));

    private static void TryDelete(string root)
    {
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    private sealed class FakeHandler(bool neverCompletes = false) : HttpMessageHandler
    {
        private int _historyCalls;

        public string? QueuedBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/prompt")
            {
                QueuedBody = request.Content is null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(cancellationToken);
                return Json("""{"prompt_id":"p1"}""");
            }

            if (path.StartsWith("/history/", StringComparison.Ordinal))
            {
                _historyCalls++;
                if (neverCompletes || _historyCalls == 1)
                {
                    return Json("{}");
                }

                return Json(
                    """{"p1":{"outputs":{"9":{"images":[{"filename":"a.png","subfolder":"","type":"output"}]}}}}""");
            }

            if (path == "/view")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent([7, 8, 9])
                    {
                        Headers = { ContentType = new("image/png") },
                    },
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }
}
