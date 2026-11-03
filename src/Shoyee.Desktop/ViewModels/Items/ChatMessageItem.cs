using System.Text;
using Avalonia;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shoyee.Desktop.Conversations;

namespace Shoyee.Desktop.ViewModels.Items;

/// <summary>
/// 聊天区的一条消息。
///
/// 工具调用和思考过程默认折叠：<see cref="IsExpanded"/> 由消息模板里的头部按钮切换，
/// 展开后看完整参数 / 返回结果。全局的「显示工具 / 显示思考」开关在会话页的输入框上方。
/// </summary>
public partial class ChatMessageItem : ObservableObject
{
    /// <summary>展示类别。</summary>
    public required ChatEntryKind Kind { get; init; }

    /// <summary>正文（工具行是摘要，思考行是完整思考）；流式输出时逐段追加。</summary>
    [ObservableProperty]
    public partial string Text { get; set; } = "";

    /// <summary>时间文本。</summary>
    public string Time { get; init; } = "";

    /// <summary>工具名（只有过程行有）。</summary>
    public string? ToolName { get; init; }

    /// <summary>工具调用的 callId（用来把返回结果对回调用）。</summary>
    public string? ToolCallId { get; init; }

    /// <summary>工具调用的完整参数（格式化后的 JSON）。</summary>
    public string Detail { get; init; } = "";

    /// <summary>工具的返回结果（原始文本 / JSON）；结果行可能晚于调用行到达。</summary>
    [ObservableProperty]
    public partial string Result { get; set; } = "";

    /// <summary>RP 气泡里的来源渠道（工作模式为 null）。</summary>
    public string? LaneName { get; init; }

    /// <summary>附件（历史消息带回来，或本轮 <c>turn.completed</c> 下发）。</summary>
    [ObservableProperty]
    public partial IReadOnlyList<AttachmentItem> Attachments { get; set; } = [];

    /// <summary>图片气泡的原始地址（Markdown 括号里的内容）。</summary>
    public string? ImageSource { get; init; }

    /// <summary>文件气泡的本地绝对路径（非文件气泡为 null）。</summary>
    public string? FilePath { get; init; }

    /// <summary>文件气泡的展示数据（文件名 / 大小 / 类别）。</summary>
    public AttachmentItem? FileCard { get; init; }

    /// <summary>图片气泡已解码的图像。</summary>
    [ObservableProperty]
    public partial Bitmap? Image { get; set; }

    /// <summary>图片是否仍在加载。</summary>
    [ObservableProperty]
    public partial bool IsImageLoading { get; set; }

    /// <summary>图片是否加载失败。</summary>
    [ObservableProperty]
    public partial bool ImageFailed { get; set; }

    /// <summary>是否仍在流式输出（决定正文块用纯文本增量渲染，轮末再做 Markdown 重排）。</summary>
    [ObservableProperty]
    public partial bool IsStreaming { get; set; }

    /// <summary>是否展开详情（工具参数 / 返回结果、完整思考）。</summary>
    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    /// <summary>气泡是否平铺（由会话页的切换按钮统一设置）。</summary>
    [ObservableProperty]
    public partial bool IsWideBubble { get; set; }

    /// <summary>
    /// 气泡最大宽度：用户气泡固定 560，不受平铺影响；
    /// 助手气泡当前款 620，平铺时放到很宽。
    /// </summary>
    public double BubbleMaxWidth => IsUser ? 560 : IsWideBubble ? 4000 : 620;

    /// <summary>气泡对齐：用户始终靠右；助手当前靠左、平铺时撑满。</summary>
    public HorizontalAlignment BubbleHorizontalAlignment => IsUser
        ? HorizontalAlignment.Right
        : IsWideBubble ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;

    /// <summary>
    /// 助手气泡平铺时只在右侧（对端用户头像方向）留距离：
    /// 对端头像列 + 一个头像宽度，自己的头像一侧保持原来的贴靠。
    /// </summary>
    public Thickness AssistantBubbleMargin => IsWideBubble ? new Thickness(0, 0, 72, 0) : default;

    /// <summary>用户气泡不参与平铺，始终贴着自己的头像。</summary>
    public Thickness UserBubbleMargin => default;

    /// <summary>工具 / 思考行沿用助手气泡的左右边界。</summary>
    public Thickness ToolRowMargin => IsWideBubble
        ? new Thickness(42, 0, 72, 0)
        : new Thickness(42, 0, 0, 0);

    /// <summary>是否用户发言。</summary>
    public bool IsUser => Kind == ChatEntryKind.User;

    /// <summary>是否助手正文。</summary>
    public bool IsAssistant => Kind == ChatEntryKind.Assistant;

    /// <summary>是否图片气泡。</summary>
    public bool IsImage => Kind == ChatEntryKind.Image;

    /// <summary>是否本地文件气泡。</summary>
    public bool IsFileBubble => Kind == ChatEntryKind.File && FileCard is not null;

    /// <summary>文件卡片：展示名。</summary>
    public string FileName => FileCard?.FileName ?? "";

    /// <summary>文件卡片：大小或「已移动或删除」。</summary>
    public string FileMeta => FileCard?.Meta ?? "";

    /// <summary>文件卡片：类别图标字形。</summary>
    public string FileGlyph => FileCard?.Glyph ?? "▤";

    /// <summary>文件当前是否还能打开。</summary>
    public bool CanOpenFile => LocalFileOpener.CanOpen(FilePath);

    /// <summary>文件卡片上的操作提示。</summary>
    public string FileActionTip => !CanOpenFile
        ? "文件已移动或删除"
        : LocalFileOpener.ShouldRevealOnly(FilePath!)
            ? "点击在 Finder 中显示"
            : "点击打开";

    /// <summary>图片是否已加载完成。</summary>
    public bool HasImage => Image is not null;

    /// <summary>图片气泡的最大宽度：横图放宽，竖图保持窄一些，避免把对话区拉高。</summary>
    public double ImageMaxWidth => Image is { } image
        && image.PixelSize.Width > image.PixelSize.Height
            ? 560
            : 360;

    /// <summary>是否系统注入（心跳 / 定时任务 / 后台进程回执）：像思考/工具一样折叠成过程行，默认隐藏。</summary>
    public bool IsSystemInput => Kind == ChatEntryKind.SystemInput;

    /// <summary>是否工具过程行。</summary>
    public bool IsTool => Kind == ChatEntryKind.Tool;

    /// <summary>是否思考过程。</summary>
    public bool IsReasoning => Kind == ChatEntryKind.Reasoning;

    /// <summary>是否系统行。</summary>
    public bool IsSystem => Kind == ChatEntryKind.System;

    /// <summary>是否有正文（拆出附件气泡后，空正文不再画一个空气泡）。</summary>
    public bool HasText => !string.IsNullOrWhiteSpace(Text);

    /// <summary>是否有附件。</summary>
    public bool HasAttachments => Attachments.Count > 0;

    /// <summary>是否显示来源渠道徽标。</summary>
    public bool ShowLaneBadge => !string.IsNullOrWhiteSpace(LaneName);

    /// <summary>工具行前缀。</summary>
    public string ToolLabel => ToolName is null ? "工具" : $"{ToolName} · 过程";

    /// <summary>思考行前缀。</summary>
    public string ReasoningLabel => "思考过程";

    /// <summary>系统注入行前缀。</summary>
    public string SystemInputLabel => "系统消息";

    /// <summary>折叠时的单行预览。</summary>
    public string Preview => Clip(Text, 110);

    /// <summary>展开后显示的参数；没有就说明只有摘要。</summary>
    public string DetailText => string.IsNullOrWhiteSpace(Detail) ? "（没有记录参数）" : Detail;

    /// <summary>展开后显示的返回结果。</summary>
    public string ResultText => string.IsNullOrWhiteSpace(Result)
        ? "（该会话快照没有保存工具返回）"
        : Result;

    /// <summary>是否有真实返回结果（决定结果区是否用弱化色）。</summary>
    public bool HasResult => !string.IsNullOrWhiteSpace(Result);

    /// <summary>展开 / 折叠箭头。</summary>
    public string ExpandGlyph => IsExpanded ? "▾" : "▸";

    /// <summary>切换展开状态。</summary>
    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;

    /// <summary>打开（或定位）文件气泡指向的本地文件。</summary>
    [RelayCommand]
    private void OpenFile()
    {
        if (FilePath is { Length: > 0 } path)
        {
            LocalFileOpener.Open(path);
        }
    }

    private Func<ChatMessageItem, Task>? _speechSynthesis;

    /// <summary>朗读这条消息的处理器（由会话页注入；没连接时为 null）。</summary>
    public Func<ChatMessageItem, Task>? SpeechSynthesis
    {
        get => _speechSynthesis;
        set
        {
            if (ReferenceEquals(_speechSynthesis, value))
            {
                return;
            }

            _speechSynthesis = value;
            OnPropertyChanged(nameof(CanSpeak));
            OnPropertyChanged(nameof(HasMessageActions));
        }
    }

    /// <summary>是否显示「播放」按钮（助手正文且已注入合成处理器）。</summary>
    public bool CanSpeak => SpeechSynthesis is not null && IsAssistant && !string.IsNullOrWhiteSpace(Text);

    /// <summary>是否正在合成 / 播放。</summary>
    [ObservableProperty]
    public partial bool IsSpeaking { get; set; }

    /// <summary>合成或播放失败的说明（空 = 没有错误）。</summary>
    [ObservableProperty]
    public partial string SpeakError { get; set; } = "";

    /// <summary>是否有播放错误。</summary>
    public bool HasSpeakError => SpeakError.Length > 0;

    /// <summary>播放按钮字形。</summary>
    public string SpeakGlyph => IsSpeaking ? "■" : "▶";

    /// <summary>朗读这条消息：先合成语音，再本地播放。</summary>
    [RelayCommand]
    private async Task SpeakAsync()
    {
        if (IsSpeaking)
        {
            LocalMediaPlayer.Stop();
            IsSpeaking = false;
            return;
        }

        if (SpeechSynthesis is not { } synthesize)
        {
            return;
        }

        SpeakError = "";
        IsSpeaking = true;
        try
        {
            await synthesize(this);
        }
        catch (Exception ex)
        {
            SpeakError = ex.Message;
        }
        finally
        {
            IsSpeaking = false;
        }
    }

    /// <summary>打开图片气泡的原始地址（本地走 Quick Look，远程交给浏览器）。</summary>
    [RelayCommand]
    private void OpenImage()
    {
        if (ImageSource is { Length: > 0 } source)
        {
            LocalFilePreview.Open(source);
        }
    }

    /// <summary>能否复制（用户 / 助手正文且有内容）。</summary>
    public bool CanCopy => (IsUser || IsAssistant) && !string.IsNullOrWhiteSpace(Text);

    /// <summary>是否显示悬浮操作条（有朗读或复制能力）。</summary>
    public bool HasMessageActions => CanSpeak || CanCopy;

    /// <summary>纯文本形态（去掉 Markdown 标记，供「复制文本」）。</summary>
    public string PlainText => ConversationText.ToPlainText(Text);

    /// <summary>Markdown 原文（助手正文本身就是 Markdown；用户输入按原样复制）。</summary>
    public string MarkdownText => Text;

    /// <summary>复制结果提示（一两秒后自动清空）。</summary>
    [ObservableProperty]
    public partial string CopyStatus { get; set; } = "";

    /// <summary>是否有复制结果提示。</summary>
    public bool HasCopyStatus => CopyStatus.Length > 0;

    /// <summary>复制纯文本。</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task CopyTextAsync() => CopyAsync(PlainText, "已复制文本");

    /// <summary>复制 Markdown 原文。</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task CopyMarkdownAsync() => CopyAsync(MarkdownText, "已复制 Markdown");

    private int _copyStatusToken;

    private async Task CopyAsync(string text, string success)
    {
        var copied = await ClipboardService.SetTextAsync(text);
        var token = ++_copyStatusToken;
        CopyStatus = copied ? success : "复制失败";
        await Task.Delay(1600);
        if (token == _copyStatusToken)
        {
            CopyStatus = "";
        }
    }

    partial void OnCopyStatusChanged(string value) => OnPropertyChanged(nameof(HasCopyStatus));

    partial void OnIsSpeakingChanged(bool value) => OnPropertyChanged(nameof(SpeakGlyph));

    partial void OnSpeakErrorChanged(string value) => OnPropertyChanged(nameof(HasSpeakError));

    /// <summary>追加一段流式正文。</summary>
    /// <param name="delta">增量文本。</param>
    public void Append(string delta)
    {
        if (delta.Length == 0 || _streamTruncated)
        {
            return;
        }

        if (_streamed.Length == 0 && Text.Length > 0)
        {
            // 外部先赋过初值（例如 RP 清洗后的正文）：以它为准继续追加
            _streamed.Append(Text);
        }

        var room = StreamLimit - _streamed.Length;
        if (room <= 0)
        {
            Truncate();
            Emit();
            return;
        }

        if (delta.Length <= room)
        {
            _streamed.Append(delta);
        }
        else
        {
            _streamed.Append(delta.AsSpan(0, room));
            Truncate();
        }

        Emit();
    }

    /// <summary>流式正文最多保留的字符数：思考行更小（完整思考在库里，桌面只留可读的一段）。</summary>
    private int StreamLimit => IsReasoning ? ConversationText.MaxReasoningChars : 400_000;

    /// <summary>
    /// 流式累计缓冲。直接在 <see cref="Text"/> 上 <c>+=</c> 是 O(n²) 拷贝：
    /// 一轮长思考能凭空分配十几 GB，桌面直接未响应。
    /// </summary>
    private readonly StringBuilder _streamed = new();

    private bool _streamTruncated;

    /// <summary>上一次由流式追加写出的完整串；用来识别「外部整体替换了 Text」。</summary>
    private string? _lastEmitted;

    private void Truncate()
    {
        _streamTruncated = true;
        _streamed.Append(IsReasoning
            ? "\n\n…（思考过长，后面的内容未展示）"
            : "\n\n…（内容过长，后面的部分未展示）");
    }

    private void Emit()
    {
        _lastEmitted = _streamed.ToString();
        Text = _lastEmitted;
    }

    /// <summary>解码图片气泡；失败只置 <see cref="ImageFailed"/>，不影响其他消息。</summary>
    public async Task LoadImageAsync()
    {
        if (ImageSource is not { Length: > 0 } source || Image is not null)
        {
            return;
        }

        IsImageLoading = true;
        var bitmap = await Task.Run(() => BubbleImageLoader.Load(source));
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

    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(ExpandGlyph));

    partial void OnTextChanged(string value)
    {
        // 外部整体替换（不是流式追加）：丢掉累计缓冲，下次 Append 从新文本继续
        if (_lastEmitted is not null && !ReferenceEquals(value, _lastEmitted))
        {
            _streamed.Clear();
            _streamTruncated = false;
            _lastEmitted = null;
        }

        OnPropertyChanged(nameof(HasText));
        OnPropertyChanged(nameof(Preview));
        OnPropertyChanged(nameof(CanSpeak));
        OnPropertyChanged(nameof(CanCopy));
        OnPropertyChanged(nameof(HasMessageActions));
        OnPropertyChanged(nameof(PlainText));
        OnPropertyChanged(nameof(MarkdownText));
    }

    partial void OnAttachmentsChanged(IReadOnlyList<AttachmentItem> value)
        => OnPropertyChanged(nameof(HasAttachments));

    partial void OnImageChanged(Bitmap? value)
    {
        OnPropertyChanged(nameof(HasImage));
        OnPropertyChanged(nameof(ImageMaxWidth));
    }

    partial void OnIsWideBubbleChanged(bool value)
    {
        OnPropertyChanged(nameof(BubbleMaxWidth));
        OnPropertyChanged(nameof(BubbleHorizontalAlignment));
        OnPropertyChanged(nameof(AssistantBubbleMargin));
        OnPropertyChanged(nameof(UserBubbleMargin));
        OnPropertyChanged(nameof(ToolRowMargin));
    }

    partial void OnResultChanged(string value)
    {
        OnPropertyChanged(nameof(ResultText));
        OnPropertyChanged(nameof(HasResult));
    }

    private static string Clip(string text, int max)
    {
        // 只取开头一段：思考可能有几万字，整串 Replace 会在每次增量时做一次全量拷贝
        var head = text.Length <= max * 4 ? text : text[..(max * 4)];
        var oneLine = head.Replace('\n', ' ').Trim();
        return oneLine.Length <= max ? oneLine : oneLine[..max] + "…";
    }
}
