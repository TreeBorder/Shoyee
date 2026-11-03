using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shoyee.Contracts.Domain;

namespace Shoyee.Desktop.ViewModels.Items;

/// <summary>
/// 附件卡片：图片加载缩略图、音频可播放、文件可 Quick Look / 打开。
/// 数据来自 <see cref="Shoyee.Contracts.Dtos.MessageAttachment"/>（路径是唯一真源，同机直读）。
/// </summary>
public sealed partial class AttachmentItem : ObservableObject
{
    private bool _imageRequested;

    /// <summary>展示类别。</summary>
    public required AttachmentKind Kind { get; init; }

    /// <summary>文件名。</summary>
    public required string FileName { get; init; }

    /// <summary>副标题（大小 / 时长）。</summary>
    public required string Meta { get; init; }

    /// <summary>本地绝对路径（同机直读；链接气泡用它打开文件）。</summary>
    public string? Path { get; init; }

    /// <summary>是否图片（决定是否画缩略图）。</summary>
    public bool IsImage => Kind == AttachmentKind.Image;

    /// <summary>是否语音。</summary>
    public bool IsAudio => Kind == AttachmentKind.Audio;

    /// <summary>是否视频。</summary>
    public bool IsVideo => Kind == AttachmentKind.Video;

    /// <summary>是否普通文件。</summary>
    public bool IsFile => Kind == AttachmentKind.File;

    /// <summary>左侧图标字形。</summary>
    public string Glyph => Kind switch
    {
        AttachmentKind.Image => "◨",
        AttachmentKind.Audio => "▶",
        AttachmentKind.Video => "▶",
        _ => "▤",
    };

    /// <summary>图片缩略图（解码完成前为 null）。</summary>
    [ObservableProperty]
    public partial Bitmap? Image { get; set; }

    /// <summary>缩略图是否在加载。</summary>
    [ObservableProperty]
    public partial bool IsImageLoading { get; set; }

    /// <summary>缩略图是否加载失败。</summary>
    [ObservableProperty]
    public partial bool ImageFailed { get; set; }

    /// <summary>音频是否正在播放。</summary>
    [ObservableProperty]
    public partial bool IsPlaying { get; set; }

    /// <summary>最近一次操作的错误（空 = 没有错误）。</summary>
    [ObservableProperty]
    public partial string ActionError { get; set; } = "";

    /// <summary>缩略图是否已解码。</summary>
    public bool HasImage => Image is not null;

    /// <summary>图片气泡的最大宽度：横图放宽，竖图保持窄一些，避免把对话区拉高（与正文图片气泡一致）。</summary>
    public double ImageMaxWidth => Image is { } image
        && image.PixelSize.Width > image.PixelSize.Height
            ? 560
            : 360;

    /// <summary>路径是否还在（决定预览 / 播放 / 打开按钮是否可用）。</summary>
    public bool Exists => LocalFilePreview.CanPreview(Path);

    /// <summary>能否 Quick Look 预览。</summary>
    public bool CanPreview => Exists;

    /// <summary>能否用系统播放器播放。</summary>
    public bool CanPlay => IsAudio && Exists && LocalMediaPlayer.IsSupported(Path);

    /// <summary>播放按钮字形。</summary>
    public string PlayGlyph => IsPlaying ? "■" : "▶";

    /// <summary>是否有操作错误。</summary>
    public bool HasActionError => ActionError.Length > 0;

    /// <summary>解码缩略图；失败只置 <see cref="ImageFailed"/>，不影响其他附件。</summary>
    public async Task LoadImageAsync()
    {
        if (!IsImage || _imageRequested || Path is not { Length: > 0 } path)
        {
            return;
        }

        _imageRequested = true;
        IsImageLoading = true;
        // 现在按原图比例完整展示（不再裁成 260×132 的缩略图），解码宽度和气泡图一致。
        var bitmap = await Task.Run(() => BubbleImageLoader.Load(path));
        IsImageLoading = false;
        if (bitmap is null)
        {
            ImageFailed = true;
        }
        else
        {
            Image = bitmap;
        }
    }

    /// <summary>Quick Look 预览（图片 / 视频 / 文档）。</summary>
    [RelayCommand]
    private void Preview()
    {
        ClearError();
        if (!CanPreview)
        {
            ActionError = "文件已移动或删除";
            return;
        }

        LocalFilePreview.QuickLook(Path);
    }

    /// <summary>用系统默认程序打开。</summary>
    [RelayCommand]
    private void Open()
    {
        ClearError();
        if (Path is not { Length: > 0 } path || !Exists)
        {
            ActionError = "文件已移动或删除";
            return;
        }

        LocalFileOpener.Open(path);
    }

    /// <summary>播放 / 停止音频。</summary>
    [RelayCommand]
    private async Task PlayAsync()
    {
        ClearError();
        if (IsPlaying)
        {
            LocalMediaPlayer.Stop();
            IsPlaying = false;
            return;
        }

        if (Path is not { Length: > 0 } path)
        {
            return;
        }

        IsPlaying = true;
        try
        {
            await LocalMediaPlayer.PlayAsync(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or NotSupportedException)
        {
            ActionError = ex.Message;
        }
        finally
        {
            IsPlaying = false;
        }
    }

    partial void OnIsPlayingChanged(bool value) => OnPropertyChanged(nameof(PlayGlyph));

    partial void OnImageChanged(Bitmap? value)
    {
        OnPropertyChanged(nameof(HasImage));
        OnPropertyChanged(nameof(ImageMaxWidth));
    }

    partial void OnActionErrorChanged(string value) => OnPropertyChanged(nameof(HasActionError));

    private void ClearError()
    {
        if (ActionError.Length > 0)
        {
            ActionError = "";
        }
    }
}
