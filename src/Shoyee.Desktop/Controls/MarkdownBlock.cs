using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Shoyee.Desktop.Controls;

/// <summary>
/// 轻量 Markdown 渲染块：按块生成 <see cref="SelectableTextBlock"/> / 代码框 / 引用条，
/// 行内支持加粗、斜体、删除线、行内代码与链接文字。
///
/// 只覆盖聊天里实际会出现的子集（标题、列表、代码块、引用、分隔线），
/// 不引第三方 Markdown 控件——气泡里要的是稳定、可选中、跟主题一致。
/// </summary>
public sealed class MarkdownBlock : StackPanel
{
    /// <summary>要渲染的 Markdown 文本。</summary>
    public static readonly StyledProperty<string?> MarkdownProperty =
        AvaloniaProperty.Register<MarkdownBlock, string?>(nameof(Markdown));

    /// <summary>正文基准字号（标题在此基础上放大）。</summary>
    public static readonly StyledProperty<double> FontSizeProperty =
        AvaloniaProperty.Register<MarkdownBlock, double>(nameof(FontSize), 13.5);

    /// <summary>正文行高（标题按字号比例缩放）。</summary>
    public static readonly StyledProperty<double> LineHeightProperty =
        AvaloniaProperty.Register<MarkdownBlock, double>(nameof(LineHeight), 22);

    /// <summary>
    /// 是否仍在流式输出。流式期间不解析 Markdown，只维护一个纯文本块（更新 Text，不重建控件），
    /// 避免每 100ms 清空重画整段内容造成跳动；轮末切成 false 再做一次完整 Markdown 渲染。
    /// </summary>
    public static readonly StyledProperty<bool> IsStreamingProperty =
        AvaloniaProperty.Register<MarkdownBlock, bool>(nameof(IsStreaming));

    private const double BaseFontSize = 13.5;

    /// <summary>两次整块重建的最小间隔（流式正文按帧刷新时的节流）。</summary>
    private static readonly TimeSpan RebuildInterval = TimeSpan.FromMilliseconds(120);

    private long _lastRebuild;
    private bool _rebuildScheduled;
    private SelectableTextBlock? _streamingBlock;

    private static readonly FontFamily MonoFont =
        new("SF Mono, Menlo, Consolas, monospace");

    private static readonly IBrush InlineCodeBackground = new SolidColorBrush(Color.Parse("#1F808080"));
    private static readonly IBrush CodeBlockBackground = new SolidColorBrush(Color.Parse("#12808080"));
    private static readonly IBrush CodeBlockBorder = new SolidColorBrush(Color.Parse("#26808080"));
    private static readonly IBrush TableHeaderBackground = new SolidColorBrush(Color.Parse("#1A808080"));
    private static readonly IBrush RuleBrush = new SolidColorBrush(Color.Parse("#26808080"));
    private static readonly IBrush QuoteBrush = new SolidColorBrush(Color.Parse("#33808080"));

    static MarkdownBlock()
    {
        MarkdownProperty.Changed.AddClassHandler<MarkdownBlock>((block, _) => block.Rebuild());
        FontSizeProperty.Changed.AddClassHandler<MarkdownBlock>((block, _) => block.Rebuild());
        LineHeightProperty.Changed.AddClassHandler<MarkdownBlock>((block, _) => block.Rebuild());
        IsStreamingProperty.Changed.AddClassHandler<MarkdownBlock>((block, _) => block.Rebuild());
    }

    /// <summary>创建渲染块。</summary>
    public MarkdownBlock()
    {
        Orientation = Orientation.Vertical;
        Spacing = 7;
    }

    /// <summary>要渲染的 Markdown 文本。</summary>
    public string? Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    /// <summary>正文字号。</summary>
    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    /// <summary>正文行高。</summary>
    public double LineHeight
    {
        get => GetValue(LineHeightProperty);
        set => SetValue(LineHeightProperty, value);
    }

    /// <summary>是否仍在流式输出（见 <see cref="IsStreamingProperty"/>）。</summary>
    public bool IsStreaming
    {
        get => GetValue(IsStreamingProperty);
        set => SetValue(IsStreamingProperty, value);
    }

    private void Rebuild()
    {
        // 流式期间只更新一个纯文本块：不走「整块清空重建」，否则每帧重新排版、画面会跳。
        if (IsStreaming)
        {
            RebuildNow();
            return;
        }

        // 非流式（轮末/历史消息）才走整块重建，并用最小间隔节流。
        if (Stopwatch.GetElapsedTime(_lastRebuild) >= RebuildInterval)
        {
            RebuildNow();
            return;
        }

        ScheduleRebuild();
    }

    private void ScheduleRebuild()
    {
        if (_rebuildScheduled)
        {
            return;
        }

        _rebuildScheduled = true;
        DispatcherTimer.RunOnce(
            () =>
            {
                _rebuildScheduled = false;
                RebuildNow();
            },
            RebuildInterval);
    }

    private void RebuildNow()
    {
        _lastRebuild = Stopwatch.GetTimestamp();
        var markdown = Markdown;

        if (IsStreaming)
        {
            // 流式期间：一个文本块原地改 Text，不碰 Children 结构。
            if (_streamingBlock is null)
            {
                Children.Clear();
                _streamingBlock = new SelectableTextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = FontSize,
                    LineHeight = LineHeight,
                };
                Children.Add(_streamingBlock);
            }
            else
            {
                _streamingBlock.FontSize = FontSize;
                _streamingBlock.LineHeight = LineHeight;
            }

            if (!string.Equals(_streamingBlock.Text, markdown, StringComparison.Ordinal))
            {
                _streamingBlock.Text = markdown;
            }

            return;
        }

        _streamingBlock = null;
        Children.Clear();
        if (string.IsNullOrEmpty(markdown))
        {
            return;
        }

        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var index = 0;
        while (index < lines.Length)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                index++;
                continue;
            }

            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                index = AddCodeBlock(lines, index);
                continue;
            }

            if (IsRule(trimmed))
            {
                AddRule();
                index++;
                continue;
            }

            if (StartsTable(lines, index))
            {
                index = AddTable(lines, index);
                continue;
            }

            if (TryHeading(trimmed, out var level, out var heading))
            {
                AddHeading(heading, level);
                index++;
                continue;
            }

            if (trimmed.StartsWith("> ", StringComparison.Ordinal) || trimmed == ">")
            {
                index = AddQuote(lines, index);
                continue;
            }

            if (TryListItem(trimmed, out var ordered, out var number, out var item))
            {
                AddListItem(item, ordered, number);
                index++;
                continue;
            }

            index = AddParagraph(lines, index);
        }
    }

    private int AddCodeBlock(string[] lines, int start)
    {
        var code = new StringBuilder();
        var index = start + 1;
        while (index < lines.Length)
        {
            if (lines[index].TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                index++;
                break;
            }

            if (code.Length > 0)
            {
                code.Append('\n');
            }

            code.Append(lines[index]);
            index++;
        }

        var text = new SelectableTextBlock
        {
            Text = code.ToString(),
            FontFamily = MonoFont,
            FontSize = Math.Max(10.5, FontSize - 1.5),
            LineHeight = Math.Max(15, LineHeight - 3),
            TextWrapping = TextWrapping.Wrap,
        };

        Children.Add(new Border
        {
            Background = CodeBlockBackground,
            BorderBrush = CodeBlockBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(11, 8),
            Child = text,
        });

        return index;
    }

    private void AddHeading(string text, int level)
    {
        var scale = level switch
        {
            1 => 1.45,
            2 => 1.26,
            3 => 1.12,
            _ => 1.0,
        };

        var block = CreateText(text, FontSize * scale, FontWeight.Bold);
        block.Margin = new Thickness(0, level <= 2 ? 4 : 2, 0, 0);
        Children.Add(block);
    }

    private void AddRule()
    {
        Children.Add(new Border
        {
            Height = 1,
            Background = RuleBrush,
            Margin = new Thickness(0, 3),
        });
    }

    private int AddTable(string[] lines, int start)
    {
        var headers = SplitCells(lines[start]);
        var aligns = SplitCells(lines[start + 1])
            .Select(ParseAlignment)
            .ToArray();

        var rows = new List<IReadOnlyList<string>>();
        var index = start + 2;
        while (index < lines.Length)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line) || !line.Contains('|'))
            {
                break;
            }

            var cells = SplitCells(line);
            if (cells.Count == 0)
            {
                break;
            }

            rows.Add(cells);
            index++;
        }

        var columns = headers.Count;
        var grid = new Grid();
        for (var column = 0; column < columns; column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        }

        for (var row = 0; row <= rows.Count; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        }

        for (var column = 0; column < columns; column++)
        {
            AddCell(
                grid,
                0,
                column,
                headers[column],
                bold: true,
                align: column < aligns.Length ? aligns[column] : TextAlignment.Left,
                isHeader: true,
                isLastRow: rows.Count == 0,
                isLastColumn: column == columns - 1);
        }

        for (var row = 0; row < rows.Count; row++)
        {
            var cells = rows[row];
            for (var column = 0; column < columns; column++)
            {
                AddCell(
                    grid,
                    row + 1,
                    column,
                    column < cells.Count ? cells[column] : string.Empty,
                    bold: false,
                    align: column < aligns.Length ? aligns[column] : TextAlignment.Left,
                    isHeader: false,
                    isLastRow: row == rows.Count - 1,
                    isLastColumn: column == columns - 1);
            }
        }

        Children.Add(new Border
        {
            Background = CodeBlockBackground,
            BorderBrush = CodeBlockBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            Child = grid,
        });

        return index;
    }

    private void AddCell(
        Grid grid,
        int row,
        int column,
        string text,
        bool bold,
        TextAlignment align,
        bool isHeader,
        bool isLastRow,
        bool isLastColumn)
    {
        var block = new SelectableTextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = Math.Max(11, FontSize - 0.5),
            FontWeight = bold ? FontWeight.Bold : FontWeight.Normal,
            LineHeight = Math.Max(16, LineHeight - 2),
            TextAlignment = align,
        };
        AppendInlines(block.Inlines!, text, block.FontSize, bold, false, null);

        var cell = new Border
        {
            Padding = new Thickness(9, 6),
            Background = isHeader ? TableHeaderBackground : null,
            BorderBrush = CodeBlockBorder,
            BorderThickness = new Thickness(
                0,
                0,
                isLastColumn ? 0 : 1,
                isLastRow ? 0 : 1),
            Child = block,
        };

        Grid.SetRow(cell, row);
        Grid.SetColumn(cell, column);
        grid.Children.Add(cell);
    }

    private static bool StartsTable(string[] lines, int index)
        => index + 1 < lines.Length
           && lines[index].Contains('|')
           && IsTableSeparator(lines[index + 1]);

    private static bool IsTableSeparator(string line)
    {
        var cells = SplitCells(line);
        if (cells.Count == 0)
        {
            return false;
        }

        return cells.All(static cell =>
        {
            var value = cell.Trim();
            if (value.StartsWith(':'))
            {
                value = value[1..];
            }

            if (value.EndsWith(':'))
            {
                value = value[..^1];
            }

            return value.Length >= 3 && value.All(static ch => ch == '-');
        });
    }

    private static IReadOnlyList<string> SplitCells(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith('|'))
        {
            trimmed = trimmed[1..];
        }

        if (trimmed.EndsWith('|'))
        {
            trimmed = trimmed[..^1];
        }

        return trimmed.Split('|').Select(static cell => cell.Trim()).ToArray();
    }

    private static TextAlignment ParseAlignment(string separator)
    {
        var value = separator.Trim();
        var left = value.StartsWith(':');
        var right = value.EndsWith(':');
        return (left, right) switch
        {
            (true, true) => TextAlignment.Center,
            (false, true) => TextAlignment.Right,
            _ => TextAlignment.Left,
        };
    }

    private int AddQuote(string[] lines, int start)
    {
        var content = new StringBuilder();
        var index = start;
        while (index < lines.Length)
        {
            var trimmed = lines[index].TrimStart();
            if (!trimmed.StartsWith(">", StringComparison.Ordinal))
            {
                break;
            }

            if (content.Length > 0)
            {
                content.Append('\n');
            }

            content.Append(trimmed[1..].TrimStart());
            index++;
        }

        Children.Add(new Border
        {
            BorderBrush = QuoteBrush,
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(10, 2, 0, 2),
            Child = CreateText(content.ToString(), FontSize, FontWeight.Normal),
        });

        return index;
    }

    private void AddListItem(string text, bool ordered, string marker)
    {
        var block = CreateText(
            (ordered ? $"{marker} " : "• ") + text,
            FontSize,
            FontWeight.Normal);
        block.Margin = new Thickness(6, 0, 0, 0);
        Children.Add(block);
    }

    private int AddParagraph(string[] lines, int start)
    {
        var content = new StringBuilder();
        var index = start;
        while (index < lines.Length)
        {
            var line = lines[index];
            var trimmed = line.TrimStart();
            if (string.IsNullOrWhiteSpace(line)
                || trimmed.StartsWith("```", StringComparison.Ordinal)
                || trimmed.StartsWith(">", StringComparison.Ordinal)
                || TryHeading(trimmed, out _, out _)
                || TryListItem(trimmed, out _, out _, out _)
                || StartsTable(lines, index)
                || IsRule(trimmed))
            {
                break;
            }

            if (content.Length > 0)
            {
                content.Append('\n');
            }

            content.Append(line.TrimEnd());
            index++;
        }

        Children.Add(CreateText(content.ToString(), FontSize, FontWeight.Normal));
        return index;
    }

    private SelectableTextBlock CreateText(string text, double size, FontWeight weight)
    {
        var block = new SelectableTextBlock
        {
            FontSize = size,
            FontWeight = weight,
            LineHeight = LineHeight * (size / BaseFontSize),
            TextWrapping = TextWrapping.Wrap,
        };

        AppendInlines(block.Inlines!, text, size, weight == FontWeight.Bold, false, null);
        return block;
    }

    private static void AppendInlines(
        InlineCollection sink,
        string text,
        double size,
        bool bold,
        bool italic,
        TextDecorationCollection? decorations)
    {
        var buffer = new StringBuilder();
        var index = 0;

        void Flush()
        {
            if (buffer.Length == 0)
            {
                return;
            }

            var run = new Run(buffer.ToString())
            {
                FontWeight = bold ? FontWeight.Bold : FontWeight.Normal,
                FontStyle = italic ? FontStyle.Italic : FontStyle.Normal,
            };
            if (decorations is not null)
            {
                run.TextDecorations = decorations;
            }

            sink.Add(run);
            buffer.Clear();
        }

        while (index < text.Length)
        {
            var current = text[index];

            if (current == '\\' && index + 1 < text.Length)
            {
                buffer.Append(text[index + 1]);
                index += 2;
                continue;
            }

            if (current == '`')
            {
                var ticks = CountRun(text, index, '`');
                var fence = new string('`', ticks);
                var close = text.IndexOf(fence, index + ticks, StringComparison.Ordinal);
                if (close > index + ticks)
                {
                    Flush();
                    sink.Add(new Run(text[(index + ticks)..close])
                    {
                        FontFamily = MonoFont,
                        FontSize = size - 0.5,
                        Background = InlineCodeBackground,
                    });
                    index = close + ticks;
                    continue;
                }
            }

            if (current is '*' or '_')
            {
                var marker = new string(current, 2);
                if (index + 1 < text.Length && text[index + 1] == current)
                {
                    var close = text.IndexOf(marker, index + 2, StringComparison.Ordinal);
                    if (close > index + 2)
                    {
                        Flush();
                        AppendInlines(sink, text[(index + 2)..close], size, true, italic, decorations);
                        index = close + 2;
                        continue;
                    }
                }

                var single = text.IndexOf(current, index + 1);
                if (single > index + 1)
                {
                    Flush();
                    AppendInlines(sink, text[(index + 1)..single], size, bold, true, decorations);
                    index = single + 1;
                    continue;
                }
            }

            if (current == '~' && index + 1 < text.Length && text[index + 1] == '~')
            {
                var close = text.IndexOf("~~", index + 2, StringComparison.Ordinal);
                if (close > index + 2)
                {
                    Flush();
                    AppendInlines(
                        sink,
                        text[(index + 2)..close],
                        size,
                        bold,
                        italic,
                        TextDecorations.Strikethrough);
                    index = close + 2;
                    continue;
                }
            }

            if (current == '!' && index + 1 < text.Length && text[index + 1] == '[')
            {
                if (TryReadLink(text, index + 1, out var imageLabel, out var imageEnd))
                {
                    Flush();
                    buffer.Append(imageLabel);
                    index = imageEnd;
                    continue;
                }
            }

            if (current == '[')
            {
                if (TryReadLink(text, index, out var label, out var linkEnd))
                {
                    Flush();
                    buffer.Append(label);
                    index = linkEnd;
                    continue;
                }
            }

            buffer.Append(current);
            index++;
        }

        Flush();
    }

    private static bool TryReadLink(string text, int start, out string label, out int end)
    {
        label = string.Empty;
        end = start;
        var close = text.IndexOf(']', start + 1);
        if (close < 0)
        {
            return false;
        }

        if (close + 1 >= text.Length || text[close + 1] != '(')
        {
            return false;
        }

        var paren = text.IndexOf(')', close + 2);
        if (paren < 0)
        {
            return false;
        }

        var candidate = text[(start + 1)..close];
        label = string.IsNullOrWhiteSpace(candidate) ? text[(close + 2)..paren] : candidate;
        end = paren + 1;
        return true;
    }

    private static int CountRun(string text, int start, char value)
    {
        var index = start;
        while (index < text.Length && text[index] == value)
        {
            index++;
        }

        return index - start;
    }

    private static bool TryHeading(string line, out int level, out string text)
    {
        level = 0;
        while (level < line.Length && line[level] == '#')
        {
            level++;
        }

        if (level is 0 or > 6 || level >= line.Length || line[level] != ' ')
        {
            level = 0;
            text = string.Empty;
            return false;
        }

        text = line[(level + 1)..].Trim();
        return true;
    }

    private static bool TryListItem(string line, out bool ordered, out string marker, out string text)
    {
        ordered = false;
        marker = string.Empty;
        text = string.Empty;

        if (line.Length >= 2 && line[0] is '-' or '*' or '+')
        {
            if (line[1] != ' ')
            {
                return false;
            }

            text = line[2..].Trim();
            return true;
        }

        var digits = 0;
        while (digits < line.Length && char.IsDigit(line[digits]))
        {
            digits++;
        }

        if (digits == 0 || digits + 1 >= line.Length || line[digits] != '.' || line[digits + 1] != ' ')
        {
            return false;
        }

        ordered = true;
        marker = line[..digits] + ".";
        text = line[(digits + 2)..].Trim();
        return true;
    }

    private static bool IsRule(string line)
    {
        if (line.Length < 3)
        {
            return false;
        }

        var body = line.Replace(" ", "");
        if (body.Length < 3 || body[0] is not ('-' or '*' or '_'))
        {
            return false;
        }

        return body.All(value => value == body[0]);
    }
}
