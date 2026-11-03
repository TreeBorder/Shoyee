using Microsoft.Extensions.AI;

namespace Shoyee.Agent.Tests.TestSupport;

/// <summary>记录每次请求的消息与选项，固定回一句；不连网络。</summary>
internal sealed class RecordingChatClient : IChatClient
{
    public List<IReadOnlyList<ChatMessage>> Calls { get; } = [];

    public List<ChatOptions?> OptionCalls { get; } = [];

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Calls.Add(messages.ToList());
        OptionCalls.Add(options);
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "应答")));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Calls.Add(messages.ToList());
        OptionCalls.Add(options);
        yield return new ChatResponseUpdate(ChatRole.Assistant, "应答");
        await Task.CompletedTask;
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
        => serviceType == typeof(IChatClient) ? this : null;

    public void Dispose()
    {
    }
}
