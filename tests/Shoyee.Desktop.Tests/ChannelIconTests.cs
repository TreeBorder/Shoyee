using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Shoyee.Desktop;
using Shoyee.Desktop.Controls;
using Xunit;

namespace Shoyee.Desktop.Tests;

/// <summary>会话列表渠道图标：三种来源都能把 SVG 路径画出来。</summary>
[Collection("Avalonia")]
public sealed class ChannelIconTests
{
    public ChannelIconTests()
    {
        if (Application.Current is null)
        {
            AppBuilder.Configure<App>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .UseSkia()
                .SetupWithoutStarting();
        }
    }

    [Fact]
    public void RendersAllChannelIcons()
    {
        var panel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new ChannelIcon { Channel = "桌面", Width = 16, Height = 16 },
                new ChannelIcon { Channel = "Mattermost", Width = 16, Height = 16 },
                new ChannelIcon { Channel = "微信", Width = 16, Height = 16 },
            },
        };

        panel.Measure(new Size(120, 40));
        panel.Arrange(new Rect(0, 0, 120, 40));

        using var bitmap = new RenderTargetBitmap(new PixelSize(120, 40));
        bitmap.Render(panel);

        var directory = Environment.GetEnvironmentVariable("SHOYEE_DESKTOP_SNAPSHOT");
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
            using var stream = File.Create(Path.Combine(directory, "channel-icons.png"));
            bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        }
    }
}
