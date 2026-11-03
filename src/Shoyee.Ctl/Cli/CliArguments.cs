namespace Shoyee.Ctl.Cli;

/// <summary>
/// 极简参数解析：第一个非选项是命令，其余是子命令/位置参数；
/// <c>--key value</c> 与 <c>--flag</c>（无值 = <c>true</c>）都收。
///
/// 不引第三方解析库：这里只有六七个命令，参数形状稳定。
/// </summary>
public sealed class CliArguments
{
    private readonly Dictionary<string, string?> _options = new(StringComparer.Ordinal);

    private CliArguments(IReadOnlyList<string> positionals, Dictionary<string, string?> options)
    {
        Positionals = positionals;
        _options = options;
    }

    /// <summary>命令与子命令（按出现顺序）。</summary>
    public IReadOnlyList<string> Positionals { get; }

    /// <summary>命令名（可能为 null = 只给了选项）。</summary>
    public string? Command => Positionals.Count > 0 ? Positionals[0] : null;

    /// <summary>解析命令行。</summary>
    /// <param name="args">原始参数（不含可执行文件名）。</param>
    public static CliArguments Parse(string[] args)
    {
        var positionals = new List<string>(args.Length);
        var options = new Dictionary<string, string?>(StringComparer.Ordinal);

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--")
            {
                positionals.AddRange(args[(i + 1)..]);
                break;
            }

            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                positionals.Add(arg);
                continue;
            }

            var name = arg[2..];
            if (name.Length == 0)
            {
                continue;
            }

            // --key=value 与 --key value 两种写法都认
            var equals = name.IndexOf('=');
            if (equals > 0)
            {
                options[name[..equals]] = name[(equals + 1)..];
                continue;
            }

            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                options[name] = args[++i];
            }
            else
            {
                options[name] = "true";
            }
        }

        return new CliArguments(positionals, options);
    }

    /// <summary>取选项值；未给返回 null。</summary>
    /// <param name="name">选项名（不带 <c>--</c>）。</param>
    public string? Get(string name) => _options.TryGetValue(name, out var value) ? value : null;

    /// <summary>开关是否打开。</summary>
    /// <param name="name">选项名（不带 <c>--</c>）。</param>
    public bool Has(string name) => _options.ContainsKey(name);
}
