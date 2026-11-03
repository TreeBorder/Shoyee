using Microsoft.Extensions.AI;
using Shoyee.Contracts.Options;
using Shoyee.Tools.Files;
using Shoyee.Tools.Media;
using Xunit;

namespace Shoyee.Tools.Tests.Photo;

/// <summary>路径黑名单：默认堵图片产出目录，且读写/列目录/grep/看图都走同一闸。</summary>
public sealed class ToolPathPolicyTests
{
    [Fact]
    public void DefaultDeniesImageOutput()
    {
        var root = PhotoTestSupport.NewRoot();
        try
        {
            var policy = ToolPathPolicy.FromConfig(PhotoTestSupport.Config(root));

            Assert.True(policy.IsDenied(Path.Combine(root, "assets", "images", "a.png")));
            Assert.True(policy.IsDenied(Path.Combine(root, "assets", "images", "2026", "10", "a.png")));
            Assert.False(policy.IsDenied(Path.Combine(root, "assets", "audio", "a.wav")));
            Assert.False(policy.IsDenied(Path.Combine(root, "Workspace", "note.md")));
        }
        finally
        {
            PhotoTestSupport.TryDelete(root);
        }
    }

    [Fact]
    public void ExtraDeniedPaths_AreResolvedAgainstWorkspace()
    {
        var root = PhotoTestSupport.NewRoot();
        try
        {
            var policy = ToolPathPolicy.FromConfig(
                PhotoTestSupport.Config(root, denied: ["private", "/tmp/shoyee-absolute-denied"]));

            Assert.True(policy.IsDenied(Path.Combine(root, "private", "x.txt")));
            Assert.True(policy.IsDenied("/tmp/shoyee-absolute-denied/x.txt"));
            Assert.False(policy.IsDenied(Path.Combine(root, "privateer", "x.txt")));
        }
        finally
        {
            PhotoTestSupport.TryDelete(root);
        }
    }

    [Fact]
    public async Task FileRead_IsBlockedInsideImageOutput()
    {
        var root = PhotoTestSupport.NewRoot();
        try
        {
            var images = Path.Combine(root, "assets", "images");
            Directory.CreateDirectory(images);
            File.WriteAllText(Path.Combine(images, "secret.txt"), "不该被读到");
            var tool = Tool(FileAccessTools.Create(PhotoTestSupport.Config(root)), FileAccessTools.ReadToolName);

            var result = await tool.InvokeAsync(new AIFunctionArguments
            {
                ["path"] = Path.Combine(images, "secret.txt"),
            });

            Assert.Contains("禁止访问", result?.ToString());
            Assert.DoesNotContain("不该被读到", result?.ToString());
        }
        finally
        {
            PhotoTestSupport.TryDelete(root);
        }
    }

    [Fact]
    public async Task LsAndGrep_DoNotExposeDeniedDirectory()
    {
        var root = PhotoTestSupport.NewRoot();
        try
        {
            var images = Path.Combine(root, "assets", "images");
            Directory.CreateDirectory(images);
            File.WriteAllText(Path.Combine(images, "secret.txt"), "token: abc");
            Directory.CreateDirectory(Path.Combine(root, "assets", "audio"));
            var tools = FileAccessTools.Create(PhotoTestSupport.Config(root));

            var listing = await Tool(tools, FileAccessTools.LsToolName).InvokeAsync(
                new AIFunctionArguments { ["directory"] = Path.Combine(root, "assets") });
            Assert.DoesNotContain("images", listing?.ToString());

            var grep = await Tool(tools, FileAccessTools.GrepToolName).InvokeAsync(
                new AIFunctionArguments { ["regexPattern"] = "token", ["directory"] = root });
            Assert.DoesNotContain("secret.txt", grep?.ToString());
        }
        finally
        {
            PhotoTestSupport.TryDelete(root);
        }
    }

    [Fact]
    public async Task FileWrite_IsBlockedInsideImageOutput()
    {
        var root = PhotoTestSupport.NewRoot();
        try
        {
            var target = Path.Combine(root, "assets", "images", "x.txt");
            var tool = Tool(FileAccessTools.Create(PhotoTestSupport.Config(root)), FileAccessTools.WriteToolName);

            var result = await tool.InvokeAsync(new AIFunctionArguments
            {
                ["path"] = target,
                ["content"] = "x",
            });

            Assert.Contains("禁止访问", result?.ToString());
            Assert.False(File.Exists(target));
        }
        finally
        {
            PhotoTestSupport.TryDelete(root);
        }
    }

    [Fact]
    public async Task AnalyzeImage_IsBlockedBeforeCallingVisionEndpoint()
    {
        var root = PhotoTestSupport.NewRoot();
        try
        {
            var images = Path.Combine(root, "assets", "images");
            Directory.CreateDirectory(images);
            var file = Path.Combine(images, "a.png");
            await File.WriteAllBytesAsync(file, [1, 2, 3]);

            var config = PhotoTestSupport.Config(root) with
            {
                Models = new ModelProfiles(
                    new ModelEndpoint("http://localhost", "m", "KEY"),
                    new ModelEndpoint("http://localhost", "s", "KEY"),
                    new ModelEndpoint("http://localhost", "vision", "KEY")),
            };
            var descriptor = ImageAnalysisTool.Create(
                config,
                config.Models.Vision,
                new HttpClient(new ThrowingHandler()));
            Assert.NotNull(descriptor);

            var result = await ((AIFunction)descriptor.Tool).InvokeAsync(
                new AIFunctionArguments { ["imagePaths"] = new[] { file } });

            Assert.Contains("禁止访问", result?.ToString());
        }
        finally
        {
            PhotoTestSupport.TryDelete(root);
        }
    }

    [Fact]
    public async Task AllowedPath_StillReadable()
    {
        var root = PhotoTestSupport.NewRoot();
        try
        {
            var file = Path.Combine(root, "Workspace", "note.md");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await File.WriteAllTextAsync(file, "正常内容");
            var tool = Tool(FileAccessTools.Create(PhotoTestSupport.Config(root)), FileAccessTools.ReadToolName);

            var result = await tool.InvokeAsync(new AIFunctionArguments { ["path"] = file });

            Assert.Equal("正常内容", result?.ToString());
        }
        finally
        {
            PhotoTestSupport.TryDelete(root);
        }
    }

    private static AIFunction Tool(IReadOnlyList<Shoyee.Tools.Composition.ToolDescriptor> descriptors, string name)
        => descriptors.Select(static d => d.Tool).OfType<AIFunction>().Single(f => f.Name == name);

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException("黑名单路径不该发到视觉端点");
    }
}
