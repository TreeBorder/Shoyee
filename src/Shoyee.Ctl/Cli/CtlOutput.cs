using System.Text.Json;

namespace Shoyee.Ctl.Cli;

/// <summary>退出码（Swift 菜单栏按它区分「没运行」与「调用失败」）。</summary>
public static class CtlExitCodes
{
    /// <summary>成功。</summary>
    public const int Ok = 0;

    /// <summary>调用失败（RPC 错误、供应商失败、启动超时等）。</summary>
    public const int Failed = 1;

    /// <summary>daemon 没在跑（socket 连不上）。</summary>
    public const int NotRunning = 2;

    /// <summary>参数不认识。</summary>
    public const int Usage = 64;
}

/// <summary>输出：<c>--json</c> 给菜单栏，纯文本给终端；失败一律走 stderr。</summary>
public static class CtlOutput
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>打印 RPC 原样结果（<c>--json</c>）或调用方给的文本。</summary>
    /// <param name="element">RPC 结果。</param>
    /// <param name="json">是否输出 JSON。</param>
    /// <param name="text">JSON 模式下不用；文本模式下打印。</param>
    public static void Result(JsonElement element, bool json, string text)
        => Console.Out.WriteLine(json ? element.GetRawText() : text);

    /// <summary>打印一条成功消息。</summary>
    /// <param name="message">消息。</param>
    /// <param name="json">是否输出 JSON。</param>
    public static void Success(string message, bool json)
        => Console.Out.WriteLine(json ? Serialize(new { ok = true, message }) : message);

    /// <summary>打印失败并返回退出码。</summary>
    /// <param name="message">给人和菜单栏看的消息。</param>
    /// <param name="code">机器可读的类别：<c>not_running</c> / <c>failed</c> / <c>usage</c>。</param>
    /// <param name="json">是否输出 JSON。</param>
    /// <param name="exitCode">退出码。</param>
    public static int Fail(string message, string code, bool json, int exitCode)
    {
        Console.Error.WriteLine(json ? Serialize(new { ok = false, error = message, code }) : message);
        return exitCode;
    }

    private static string Serialize(object value) => JsonSerializer.Serialize(value, Json);
}
