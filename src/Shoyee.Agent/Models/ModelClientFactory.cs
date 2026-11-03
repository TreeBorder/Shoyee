using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;
using Shoyee.Contracts.Options;

namespace Shoyee.Agent.Models;

/// <summary>
/// 模型客户端工厂：三档（主 / 小 / 视觉）都走 OpenAI 兼容端点，不引任何厂商专用 provider 包。
///
/// API key 只从环境变量读（<see cref="ModelEndpoint.ApiKeyEnv"/> 存变量名），不存在配置文件里。
/// 每次调用都新建客户端；生命周期跟着持有它的运行时组件走。
/// </summary>
public sealed class ModelClientFactory
{
    private readonly ModelProfiles _profiles;
    private readonly Func<string, string?> _readEnvironment;

    /// <summary>从工作区配置创建。</summary>
    /// <param name="config">工作区配置。</param>
    public ModelClientFactory(WorkspaceConfig config)
        : this(config.Models)
    {
    }

    /// <summary>从模型档位创建。</summary>
    /// <param name="profiles">模型档位。</param>
    /// <param name="readEnvironment">环境变量读取（测试可注入；默认 <see cref="Environment.GetEnvironmentVariable(string)"/>）。</param>
    public ModelClientFactory(ModelProfiles profiles, Func<string, string?>? readEnvironment = null)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        _profiles = profiles;
        _readEnvironment = readEnvironment ?? Environment.GetEnvironmentVariable;
    }

    /// <summary>主模型（对话）。</summary>
    public IChatClient CreateMain() => Create(_profiles.Main);

    /// <summary>小模型（摘要/标题/压缩/智能审批）。</summary>
    public IChatClient CreateSmall() => Create(_profiles.Small);

    /// <summary>视觉模型；没配置时返回 null（不启用图片分析）。</summary>
    public IChatClient? CreateVision() => _profiles.Vision is null ? null : Create(_profiles.Vision);

    /// <summary>按端点创建客户端。</summary>
    /// <param name="endpoint">端点配置。</param>
    public IChatClient Create(ModelEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (!Uri.TryCreate(endpoint.BaseUrl, UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException($"模型端点 BaseUrl 不是合法的 http(s) 地址：{endpoint.BaseUrl}", nameof(endpoint));
        }

        var apiKey = _readEnvironment(endpoint.ApiKeyEnv);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                $"环境变量 {endpoint.ApiKeyEnv} 未设置或为空：模型 {endpoint.ModelId} 无法鉴权。"
                + "把密钥放进启动环境（或工作区 .env），配置文件里只写变量名。");
        }

        var options = new OpenAIClientOptions { Endpoint = baseUri };
        return new OpenAIClient(new ApiKeyCredential(apiKey), options)
            .GetChatClient(endpoint.ModelId)
            .AsIChatClient();
    }
}
