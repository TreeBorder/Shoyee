using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Shoyee.Sessions.Tests.TestSupport;

/// <summary>测试用聊天客户端：不连网络，固定回一句；只用于拿一个可序列化的 <see cref="ChatClientAgent"/>。</summary>
internal sealed class StubChatClient : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield break;
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
        => serviceType == typeof(IChatClient) ? this : null;

    public void Dispose()
    {
    }

    /// <summary>创建一个不连网络的 agent。</summary>
    public static AIAgent CreateAgent() => new ChatClientAgent(new StubChatClient(), instructions: "test");
}
