using Shoyee.Contracts.Domain;

namespace Shoyee.Contracts.Dtos;

/// <summary><c>rpc.hello</c> 的请求：连接建立后的第一帧。</summary>
/// <param name="Client">客户端类型：desktop / menu / ctl / test。</param>
/// <param name="Version">客户端协议版本（<c>major.minor.patch</c>）。</param>
/// <param name="Role">连接角色，决定方法权限与事件可见性。</param>
/// <param name="LaneId">桌面 RP 连接必填：只订阅这个 Lane 的事件。</param>
public sealed record HelloRequest(string Client, string Version, ClientRole Role, string? LaneId = null);

/// <summary>握手时回报的一条 Lane。</summary>
/// <param name="Id">Lane id。</param>
/// <param name="Channel">渠道类型。</param>
/// <param name="Mode">绑定模式。</param>
/// <param name="Connected">渠道连接是否正常。</param>
public sealed record HelloLane(string Id, LaneChannel Channel, Mode Mode, bool Connected);

/// <summary><c>rpc.hello</c> 的返回。</summary>
/// <param name="ServerVersion">服务端协议版本。</param>
/// <param name="Workspace">工作区根目录（绝对路径）。</param>
/// <param name="StartedAt">daemon 启动时间（UTC）。</param>
/// <param name="Capabilities">服务端能力列表（客户端据此探测，不靠版本号猜）。</param>
/// <param name="Lanes">当前已知的 Lane。</param>
public sealed record HelloResult(
    string ServerVersion,
    string Workspace,
    DateTimeOffset StartedAt,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<HelloLane> Lanes);
