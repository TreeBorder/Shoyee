using System.Text;

namespace Shoyee.Tools.Shell;

/// <summary>
/// 终端会话隔离：启动时生成 <c>setsid</c> 包装脚本给框架 <c>LocalShellExecutor</c> 用。
/// </summary>
/// <remarks>
/// 为什么要这个（Secretary 2026-10-19 live 事故，本项沿用）：macOS 不自带 <c>setsid</c>，框架
/// <c>ShellSession</c> 找不到它就把持久 shell 挂在宿主进程组上；命令超时后
/// <c>Kill(entireProcessTree: true)</c> 会触发内核给整个进程组补发 SIGHUP，宿主一起被打死。
/// 包装脚本先 <c>setsid()</c> 再 exec 真正的 shell，让组信号只落在独立会话里。
/// 脚本放 <c>{工作区}/.runtime/</c>，每次启动按内容幂等重写，不安装系统工具。
/// </remarks>
public static class ShellIsolation
{
    /// <summary>包装脚本文件名（稳定名：框架按文件名判 shell 方言）。</summary>
    public const string WrapperFileName = "setsid-shell";

    /// <summary>生成/刷新隔离包装脚本并返回路径；不可用时返回 null（回退框架默认 shell）。</summary>
    /// <param name="workspaceRoot">工作区根目录（脚本落在其 <c>.runtime/</c> 下）。</param>
    public static string? TryPrepare(string? workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot) || !Directory.Exists(workspaceRoot))
        {
            return null;
        }

        var interpreter = File.Exists("/usr/bin/python3") ? "/usr/bin/python3"
            : File.Exists("/usr/bin/perl") ? "/usr/bin/perl"
            : null;
        if (interpreter is null)
        {
            return null;
        }

        try
        {
            var directory = Path.Combine(workspaceRoot, ".runtime");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, WrapperFileName);
            var script = BuildScript(interpreter);

            if (!File.Exists(path) || File.ReadAllText(path) != script)
            {
                File.WriteAllText(path, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    path,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>包装脚本内容：setsid → exec bash（参数透传，兼容持久/非持久两种启动形态）。</summary>
    private static string BuildScript(string interpreter)
    {
        var launcher = interpreter.EndsWith("python3", StringComparison.Ordinal)
            ? $$"""
              exec {{interpreter}} -c 'import os,sys
              try:
                  os.setsid()
              except OSError:
                  pass
              os.execvp(sys.argv[1], sys.argv[1:])' /bin/bash --noprofile --norc "$@"
              """
            : $$"""
              exec {{interpreter}} -MPOSIX -e 'POSIX::setsid(); exec @ARGV or die "exec failed: $!\n";' /bin/bash --noprofile --norc "$@"
              """;

        return $"""
            #!/bin/sh
            # Shoyee terminal 会话隔离（自动生成，别手改；下次启动会被覆盖）。
            # setsid 让 shell 拥有独立会话/进程组：超时清理的组信号只落在本会话，不会打到 daemon。
            {launcher}
            """;
    }
}
