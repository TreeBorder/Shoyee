namespace Shoyee.Notify.ServerChan;

/// <summary>
/// Server酱³ 配置。凭据不在 Shoyee 的配置文件里：默认复用推送脚本的
/// <c>~/Tools/push/config.sh</c>（读其中的 <c>SCT_UID</c> / <c>SCT_SENDKEY</c>）。
/// </summary>
/// <param name="ConfigFile">凭据文件路径（支持 <c>~/</c>）。</param>
/// <param name="TimeoutSeconds">HTTP 请求超时。</param>
/// <param name="Endpoint">
/// 自定义发送地址；null = <c>https://{uid}.push.ft07.com/send/{sendkey}.send</c>。
/// 只给测试/自建网关用，正常不用配。
/// </param>
public sealed record ServerChanOptions(
    string ConfigFile = "~/Tools/push/config.sh",
    int TimeoutSeconds = 15,
    string? Endpoint = null);
