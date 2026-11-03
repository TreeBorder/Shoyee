using System.Text.Json;
using System.Text.Json.Nodes;
using Shoyee.Contracts.Rpc;

namespace Shoyee.Daemon.Configuration;

/// <summary>
/// 把配置文档写回 <c>{workspace}/config.json</c>：先写同目录临时文件再原子替换，
/// 旧文件留一份 <c>config.json.bak</c>。先写临时文件是为了避免写一半崩溃留下半份 JSON。
/// </summary>
public static class WorkspaceConfigWriter
{
    /// <summary>写入配置文件。</summary>
    /// <param name="workspaceRoot">工作区根目录。</param>
    /// <param name="document">要写入的文档。</param>
    /// <exception cref="RpcException">写盘失败（<c>internal</c>）。</exception>
    public static void Write(string workspaceRoot, JsonObject document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(document);

        var path = Path.Combine(workspaceRoot, "config.json");
        var temp = Path.Combine(workspaceRoot, $".config.json.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temp, document.ToJsonString(WriterOptions) + "\n");

            if (File.Exists(path))
            {
                File.Copy(path, path + ".bak", overwrite: true);
            }

            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new RpcException(RpcErrors.Internal, $"配置文件写入失败：{ex.Message}");
        }
        finally
        {
            try
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
            catch (IOException)
            {
                // 临时文件清不掉不影响结果
            }
        }
    }

    /// <summary>写文件用缩进格式，方便直接打开改；其余编码/命名与线上 JSON 一致。</summary>
    private static readonly JsonSerializerOptions WriterOptions =
        new(JsonWire.Options) { WriteIndented = true };
}
