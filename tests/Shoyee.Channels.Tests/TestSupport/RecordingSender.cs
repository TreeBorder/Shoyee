using Shoyee.Contracts.Abstractions;

namespace Shoyee.Channels.Tests.TestSupport;

/// <summary>记录发送内容的假渠道。</summary>
internal sealed class RecordingSender : IChannelSender
{
    public RecordingSender(string laneId, Exception? failure = null)
    {
        LaneId = laneId;
        Failure = failure;
    }

    public string LaneId { get; }

    public Exception? Failure { get; }

    public List<(string Target, string Text)> Texts { get; } = [];

    public List<(string Target, string FilePath, string? Caption)> Files { get; } = [];

    public Task SendTextAsync(string target, string text, CancellationToken cancellationToken = default)
    {
        if (Failure is not null)
        {
            return Task.FromException(Failure);
        }

        Texts.Add((target, text));
        return Task.CompletedTask;
    }

    public Task SendFileAsync(
        string target,
        string filePath,
        string? caption,
        CancellationToken cancellationToken = default)
    {
        if (Failure is not null)
        {
            return Task.FromException(Failure);
        }

        Files.Add((target, filePath, caption));
        return Task.CompletedTask;
    }
}
