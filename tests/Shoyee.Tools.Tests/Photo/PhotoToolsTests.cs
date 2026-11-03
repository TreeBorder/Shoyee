using Microsoft.Extensions.AI;
using Shoyee.Photo;
using Shoyee.Storage.Abstractions;
using Shoyee.Tools.Composition;
using Shoyee.Tools.Photo;
using Xunit;

namespace Shoyee.Tools.Tests.Photo;

/// <summary>图片工具面：挂载范围、异步出图、状态维护、工作模式同步生图。</summary>
public sealed class PhotoToolsTests
{
    [Fact]
    public void NoProvider_ProducesNoTools()
    {
        Assert.Empty(PhotoTools.Create(null, null, null, null));
    }

    [Fact]
    public void Configured_ProducesWorkAndRoleplayTools()
    {
        var root = PhotoTestSupport.NewRoot();
        try
        {
            PhotoTestSupport.WriteVocabulary(root);
            var descriptors = PhotoTools.Create(
                PhotoTestSupport.Service(root),
                PhotoTestSupport.Prompts(root),
                new PhotoTaskRunner(PhotoTestSupport.Service(root)),
                new PhotoTestSupport.FakeStates());

            var names = Names(descriptors);
            Assert.Contains(PhotoTools.GenerateToolName, names);
            Assert.Contains(PhotoTools.CameraToolName, names);
            Assert.Contains(PhotoTools.GalleryToolName, names);
            Assert.Contains(PhotoTools.RandomOutfitToolName, names);
            Assert.Contains(PhotoTools.SupportSceneToolName, names);

            Assert.Equal(
                ToolScope.Work,
                descriptors.Single(d => d.Metadata.Name == PhotoTools.GenerateToolName).Metadata.Scope);
            Assert.Equal(
                ToolScope.Roleplay,
                descriptors.Single(d => d.Metadata.Name == PhotoTools.CameraToolName).Metadata.Scope);
        }
        finally
        {
            PhotoTestSupport.TryDelete(root);
        }
    }

    [Fact]
    public async Task Camera_OutsideRoleplay_ExplainsWhy()
    {
        var root = PhotoTestSupport.NewRoot();
        try
        {
            PhotoTestSupport.WriteVocabulary(root);
            var tool = Tool(Create(root, out _), PhotoTools.CameraToolName);
            PhotoScope.LaneId = null;

            var result = await tool.InvokeAsync(new AIFunctionArguments { ["action"] = "standing" });

            Assert.Contains("角色扮演", result?.ToString());
        }
        finally
        {
            PhotoTestSupport.TryDelete(root);
        }
    }

    [Fact]
    public async Task Camera_WithoutState_TellsHowToSetIt()
    {
        var root = PhotoTestSupport.NewRoot();
        try
        {
            PhotoTestSupport.WriteVocabulary(root);
            var service = PhotoTestSupport.Service(root);
            var tool = Tool(
                PhotoTools.Create(
                    service,
                    PhotoTestSupport.Prompts(root),
                    new PhotoTaskRunner(service),
                    new PhotoTestSupport.FakeStates()),
                PhotoTools.CameraToolName);
            PhotoScope.LaneId = PhotoTestSupport.Lane;

            var result = await tool.InvokeAsync(new AIFunctionArguments { ["action"] = "standing" });

            Assert.Contains("update_state", result?.ToString());
        }
        finally
        {
            PhotoScope.LaneId = null;
            PhotoTestSupport.TryDelete(root);
        }
    }

    [Fact]
    public async Task Camera_EnqueuesAndDeliversToOriginalLane()
    {
        var root = PhotoTestSupport.NewRoot();
        try
        {
            PhotoTestSupport.WriteVocabulary(root);
            var tool = Tool(Create(root, out var delivery), PhotoTools.CameraToolName);
            PhotoScope.LaneId = PhotoTestSupport.Lane;

            var result = await tool.InvokeAsync(new AIFunctionArguments
            {
                ["action"] = "standing",
                ["aspectRatio"] = "3:2",
            });

            Assert.Contains("几分钟", result?.ToString());
            var sent = await delivery.First.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(PhotoTestSupport.Lane, sent.Context.LaneId);
            Assert.Single(sent.Assets);
        }
        finally
        {
            PhotoScope.LaneId = null;
            PhotoTestSupport.TryDelete(root);
        }
    }

    [Fact]
    public async Task Gallery_UnknownScene_PointsAtSupportTool()
    {
        var root = PhotoTestSupport.NewRoot();
        try
        {
            PhotoTestSupport.WriteVocabulary(root);
            var tool = Tool(Create(root, out _), PhotoTools.GalleryToolName);
            PhotoScope.LaneId = PhotoTestSupport.Lane;

            var result = await tool.InvokeAsync(new AIFunctionArguments
            {
                ["scene"] = "nope",
                ["outfit"] = "casual",
                ["action"] = "standing",
            });

            Assert.Contains(PhotoTools.SupportSceneToolName, result?.ToString());
        }
        finally
        {
            PhotoScope.LaneId = null;
            PhotoTestSupport.TryDelete(root);
        }
    }

    [Fact]
    public async Task RandomOutfit_UpdatesOutfitState()
    {
        var root = PhotoTestSupport.NewRoot();
        try
        {
            PhotoTestSupport.WriteVocabulary(root);
            var states = new PhotoTestSupport.FakeStates(
                new StateRecord("scene", "场景", "bedroom", null, true, default),
                new StateRecord("outfit", "服装", "casual", null, true, default));
            var runner = new PhotoTaskRunner(PhotoTestSupport.Service(root));
            var tool = Tool(
                PhotoTools.Create(PhotoTestSupport.Service(root), PhotoTestSupport.Prompts(root), runner, states),
                PhotoTools.RandomOutfitToolName);

            var result = await tool.InvokeAsync(new AIFunctionArguments());

            var outfit = await states.GetAsync("outfit");
            Assert.Contains(outfit!.Value, new[] { "casual", "dress" });
            Assert.Contains("随机换装完成", result?.ToString());
        }
        finally
        {
            PhotoTestSupport.TryDelete(root);
        }
    }

    [Fact]
    public async Task SupportAction_ListsKeys()
    {
        var root = PhotoTestSupport.NewRoot();
        try
        {
            PhotoTestSupport.WriteVocabulary(root);
            var tool = Tool(Create(root, out _), PhotoTools.SupportActionToolName);

            var result = await tool.InvokeAsync(new AIFunctionArguments());

            Assert.Contains("standing", result?.ToString());
            Assert.Contains("站立", result?.ToString());
        }
        finally
        {
            PhotoTestSupport.TryDelete(root);
        }
    }

    [Fact]
    public async Task ImageGenerate_WritesFileAndReturnsPath()
    {
        var root = PhotoTestSupport.NewRoot();
        try
        {
            var provider = new PhotoTestSupport.FakeProvider();
            var tool = Tool(
                PhotoTools.Create(
                    PhotoTestSupport.Service(root, provider),
                    null,
                    null,
                    null),
                PhotoTools.GenerateToolName);

            var result = await tool.InvokeAsync(new AIFunctionArguments { ["prompt"] = "a cat" });

            var text = result?.ToString();
            Assert.Contains("已生成图片", text);
            Assert.Equal("a cat", provider.LastRequest!.Positive);
            var path = text!.Split('\n', StringSplitOptions.RemoveEmptyEntries)[1].TrimStart('-', ' ');
            path = path[..path.IndexOf('（')];
            Assert.True(File.Exists(path), path);
            Assert.StartsWith(Path.Combine(root, "assets", "images"), path);
        }
        finally
        {
            PhotoTestSupport.TryDelete(root);
        }
    }

    [Fact]
    public async Task ImageGenerate_RegistersOutboundAttachment()
    {
        var root = PhotoTestSupport.NewRoot();
        try
        {
            var collector = new Shoyee.Tools.Media.OutboundAttachments();
            Shoyee.Tools.Media.OutboundScope.Current = collector;
            try
            {
                var tool = Tool(
                    PhotoTools.Create(PhotoTestSupport.Service(root), null, null, null),
                    PhotoTools.GenerateToolName);

                await tool.InvokeAsync(new AIFunctionArguments { ["prompt"] = "a cat" });

                var attachment = Assert.Single(collector.Items);
                Assert.Equal("image/png", attachment.Mime);
                Assert.Equal(Shoyee.Contracts.Domain.AttachmentKind.Image, attachment.Kind);
                Assert.StartsWith(Path.Combine(root, "assets", "images"), attachment.Path);
            }
            finally
            {
                Shoyee.Tools.Media.OutboundScope.Current = null;
            }
        }
        finally
        {
            PhotoTestSupport.TryDelete(root);
        }
    }

    [Fact]
    public void Toolset_MountsPhotoToolsPerProfile()
    {
        var root = PhotoTestSupport.NewRoot();
        try
        {
            PhotoTestSupport.WriteVocabulary(root);
            var states = new PhotoTestSupport.FakeStates(
                new StateRecord("scene", "场景", "bedroom", null, true, default),
                new StateRecord("outfit", "服装", "casual", null, true, default));
            var builder = new ToolsetBuilder(
                PhotoTestSupport.Config(root),
                new ToolsetServices(
                    States: states,
                    Photo: PhotoTestSupport.Service(root),
                    PhotoVocabulary: PhotoTestSupport.Prompts(root),
                    PhotoTasks: new PhotoTaskRunner(PhotoTestSupport.Service(root))));

            var work = Names(builder.Build(ToolProfile.Work).Tools);
            var roleplay = Names(builder.Build(ToolProfile.Roleplay).Tools);

            Assert.Contains(PhotoTools.GenerateToolName, work);
            Assert.DoesNotContain(PhotoTools.CameraToolName, work);
            Assert.Contains(PhotoTools.CameraToolName, roleplay);
            Assert.DoesNotContain(PhotoTools.GenerateToolName, roleplay);
        }
        finally
        {
            PhotoTestSupport.TryDelete(root);
        }
    }

    private static IReadOnlyList<ToolDescriptor> Create(string root, out PhotoTestSupport.FakeDelivery delivery)
    {
        delivery = new PhotoTestSupport.FakeDelivery();
        var service = PhotoTestSupport.Service(root);
        var runner = new PhotoTaskRunner(service, delivery);
        runner.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        var states = new PhotoTestSupport.FakeStates(
            new StateRecord("scene", "场景", "bedroom", null, true, default),
            new StateRecord("outfit", "服装", "casual", null, true, default));
        return PhotoTools.Create(service, PhotoTestSupport.Prompts(root), runner, states);
    }

    private static AIFunction Tool(IReadOnlyList<ToolDescriptor> descriptors, string name)
        => descriptors.Select(static d => d.Tool).OfType<AIFunction>().Single(f => f.Name == name);

    private static IReadOnlyList<string> Names(IReadOnlyList<ToolDescriptor> descriptors)
        => descriptors.Select(static d => d.Metadata.Name).ToArray();

    private static IReadOnlyList<string> Names(IReadOnlyList<AITool> tools)
        => tools.OfType<AIFunction>().Select(static f => f.Name).ToArray();
}
