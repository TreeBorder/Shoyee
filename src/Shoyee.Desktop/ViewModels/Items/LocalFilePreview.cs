using System.Diagnostics;
using System.Text;

namespace Shoyee.Desktop.ViewModels.Items;

/// <summary>
/// 本地文件预览：默认按平台走（macOS 用 Quick Look 面板，其它平台交给系统默认程序），
/// 也可以在桌面设置里配一条命令覆盖——把「预览」当成「用某个命令行程序打开这个文件」。
///
/// 配置里的 <c>{path}</c> 会替换成文件绝对路径；没写占位符时自动把路径追加到最后。
/// 命令不经过 shell，按引号拆分参数，避免路径里的空格 / 引号被二次解释。
/// </summary>
public static class LocalFilePreview
{
    /// <summary>
    /// 预览命令模板（如 <c>open {path}</c>、<c>explorer "{path}"</c>、<c>xdg-open {path}</c>）。
    /// 空 = 用平台默认。
    /// </summary>
    public static string CommandTemplate { get; set; } = "";

    /// <summary>该路径能否预览（文件或目录存在）。</summary>
    /// <param name="path">本地路径。</param>
    public static bool CanPreview(string? path)
        => !string.IsNullOrWhiteSpace(path)
           && (File.Exists(path) || Directory.Exists(path));

    /// <summary>打开预览；失败时退回默认程序，不抛异常。</summary>
    /// <param name="path">本地路径。</param>
    public static void QuickLook(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var template = CommandTemplate.Trim();
        if (template.Length > 0)
        {
            RunTemplate(template, path);
            return;
        }

        if (!OperatingSystem.IsMacOS())
        {
            LocalFileOpener.Open(path);
            return;
        }

        // 默认实现：qlmanage 是 Apple 的「调试」工具，现在只会弹一个带 [DEBUG] 标题、
        // 正文空白的调试壳（实测 macOS 27），不能当用户界面用。这里走 Quick Look 的
        // 正规入口 QLPreviewPanel（和访达空格预览同一个面板），用 JXA 调是为了不额外
        // 发布原生二进制。想要别的行为直接在设置里填预览命令即可。
        try
        {
            var fullPath = Path.GetFullPath(path);
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("osascript") { UseShellExecute = false },
            };
            process.StartInfo.ArgumentList.Add("-l");
            process.StartInfo.ArgumentList.Add("JavaScript");
            process.StartInfo.ArgumentList.Add("-e");
            process.StartInfo.ArgumentList.Add(PreviewScript);
            process.StartInfo.ArgumentList.Add(fullPath);
            process.Start();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            LocalFileOpener.Open(path);
        }
    }

    /// <summary>打开图片地址：本地路径走预览，http(s) 交给浏览器。</summary>
    /// <param name="source">本地路径 / <c>file://</c> / http(s) 地址。</param>
    public static void Open(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return;
        }

        if (Uri.TryCreate(source, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme is "http" or "https")
            {
                OpenUrl(uri.AbsoluteUri);
                return;
            }

            if (uri.Scheme == "file")
            {
                QuickLook(uri.LocalPath);
                return;
            }
        }

        QuickLook(source);
    }

    /// <summary>按模板启动命令；路径替换成绝对路径。</summary>
    private static void RunTemplate(string template, string path)
    {
        var parts = BuildArguments(template, path);
        if (parts.Count == 0)
        {
            LocalFileOpener.Open(path);
            return;
        }

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(parts[0]) { UseShellExecute = false },
            };

            for (var i = 1; i < parts.Count; i++)
            {
                process.StartInfo.ArgumentList.Add(parts[i]);
            }

            process.Start();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            LocalFileOpener.Open(path);
        }
    }

    /// <summary>
    /// 把预览命令拆成「程序 + 参数」：<c>{path}</c> 替换成绝对路径，没有占位符时把路径追加到最后。
    /// 拆不出任何 token 时返回空列表，由调用方退回默认程序。
    /// </summary>
    internal static IReadOnlyList<string> BuildArguments(string template, string path)
    {
        var parts = SplitArguments(template);
        if (parts.Count == 0)
        {
            return [];
        }

        var fullPath = Path.GetFullPath(path);
        var arguments = new List<string>(parts.Count + 1) { parts[0] };
        var replaced = false;
        for (var i = 1; i < parts.Count; i++)
        {
            var argument = parts[i];
            if (argument.Contains("{path}", StringComparison.Ordinal))
            {
                argument = argument.Replace("{path}", fullPath, StringComparison.Ordinal);
                replaced = true;
            }

            arguments.Add(argument);
        }

        if (!replaced)
        {
            arguments.Add(fullPath);
        }

        return arguments;
    }

    /// <summary>按空白拆参数，支持单/双引号与反斜杠转义（够配置命令用，不实现 shell 语法）。</summary>
    private static List<string> SplitArguments(string command)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        var quote = '\0';
        var escaped = false;
        var hasToken = false;

        foreach (var c in command)
        {
            if (escaped)
            {
                current.Append(c);
                escaped = false;
                hasToken = true;
                continue;
            }

            if (c == '\\' && quote != '\'')
            {
                escaped = true;
                continue;
            }

            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }
                else
                {
                    current.Append(c);
                }

                hasToken = true;
                continue;
            }

            if (c is '"' or '\'')
            {
                quote = c;
                hasToken = true;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (hasToken)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                    hasToken = false;
                }

                continue;
            }

            current.Append(c);
            hasToken = true;
        }

        if (hasToken)
        {
            parts.Add(current.ToString());
        }

        return parts;
    }

    private static void OpenUrl(string url)
    {
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo("open") { UseShellExecute = false },
                };
                process.StartInfo.ArgumentList.Add(url);
                process.Start();
                return;
            }

            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // 打不开就让用户自己复制地址
        }
    }

    /// <summary>
    /// JXA 版 Quick Look：注册一个单文件数据源，显示系统 <c>QLPreviewPanel</c>，面板关闭后退出。
    /// 第一个命令行参数是要预览的绝对路径（由 <see cref="QuickLook"/> 传进来，不做字符串拼接）。
    /// </summary>
    private const string PreviewScript = """
        ObjC.import('AppKit');
        ObjC.import('Quartz');
        ObjC.import('Foundation');

        function run(argv) {
          if (!argv || argv.length === 0) return 'no path';

          ObjC.registerSubclass({
            name: 'ShoyeeQLSource',
            superclass: 'NSObject',
            protocols: ['QLPreviewPanelDataSource'],
            methods: {
              'numberOfPreviewItemsInPreviewPanel:': {
                types: ['long', ['id']],
                implementation: function (panel) { return 1; }
              },
              'previewPanel:previewItemAtIndex:': {
                types: ['id', ['id', 'long']],
                implementation: function (panel, index) {
                  return $.NSURL.fileURLWithPath($(argv[0]));
                }
              }
            }
          });

          ObjC.registerSubclass({
            name: 'ShoyeeQLDelegate',
            superclass: 'NSObject',
            protocols: ['QLPreviewPanelDelegate'],
            methods: {
              'windowWillClose:': {
                types: ['void', ['id']],
                implementation: function (note) {
                  $.NSApplication.sharedApplication.terminate(null);
                }
              }
            }
          });

          const app = $.NSApplication.sharedApplication;
          app.setActivationPolicy(1);
          const panel = $.QLPreviewPanel.sharedPreviewPanel;
          globalThis.__shoyeeSource = $.ShoyeeQLSource.alloc.init;
          globalThis.__shoyeeDelegate = $.ShoyeeQLDelegate.alloc.init;
          panel.dataSource = globalThis.__shoyeeSource;
          panel.delegate = globalThis.__shoyeeDelegate;
          panel.makeKeyAndOrderFront(null);
          app.activateIgnoringOtherApps(true);

          // 关窗时不一定走 windowWillClose:（例如面板被系统收起），再兜一个可见性轮询，
          // 避免每预览一次就留一个 osascript 常驻进程。
          $.NSTimer.scheduledTimerWithTimeIntervalRepeatsBlock(0.4, true, function (timer) {
            if (!panel.isVisible) { app.terminate(null); }
          });

          app.run;
          return 'closed';
        }
        """;
}
