using Shoyee.Contracts.Domain;
using Shoyee.Desktop.ViewModels.Items;
using Xunit;

#pragma warning disable xUnit1031 // 同步等待：桌面测试共用单 UI 线程，异步会让后续测试换线程

namespace Shoyee.Desktop.Tests;

/// <summary>附件卡片：存在性判断、播放能力与加载失败降级。</summary>
public sealed class AttachmentItemTests
{
    [Fact]
    public void DetectsMissingFilesAndUnsupportedAudio()
    {
        var mp3 = TempFile(".mp3");
        var silk = TempFile(".silk");
        try
        {
            var audio = new AttachmentItem
            {
                Kind = AttachmentKind.Audio,
                FileName = "voice.mp3",
                Meta = "24 KB",
                Path = mp3,
            };
            Assert.True(audio.Exists);
            Assert.True(audio.CanPlay);
            Assert.Equal("▶", audio.PlayGlyph);

            var weChatVoice = new AttachmentItem
            {
                Kind = AttachmentKind.Audio,
                FileName = "voice.silk",
                Meta = "8 KB",
                Path = silk,
            };
            Assert.False(weChatVoice.CanPlay);

            var missing = new AttachmentItem
            {
                Kind = AttachmentKind.File,
                FileName = "gone.pdf",
                Meta = "1 MB",
                Path = mp3 + ".missing",
            };
            Assert.False(missing.CanPreview);
            missing.PreviewCommand.Execute(null);
            Assert.Equal("文件已移动或删除", missing.ActionError);
        }
        finally
        {
            File.Delete(mp3);
            File.Delete(silk);
        }
    }

    [Fact]
    public void ImageFailureDoesNotThrow()
    {
        var item = new AttachmentItem
        {
            Kind = AttachmentKind.Image,
            FileName = "gone.png",
            Meta = "1 MB",
            Path = "/tmp/shoyee-missing-image.png",
        };

        item.LoadImageAsync().GetAwaiter().GetResult();

        Assert.True(item.ImageFailed);
        Assert.False(item.HasImage);
        Assert.False(item.IsImageLoading);
    }

    [Fact]
    public void ImagePreviewOnMissingFileShowsError()
    {
        var item = new AttachmentItem
        {
            Kind = AttachmentKind.Image,
            FileName = "gone.png",
            Meta = "1 MB",
            Path = "/tmp/shoyee-missing-image.png",
        };

        item.PreviewCommand.Execute(null);

        Assert.True(item.HasActionError);
    }

    private static string TempFile(string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"shoyee-{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(path, [0, 1, 2, 3]);
        return path;
    }
}
