using System.Text.Json.Nodes;

namespace Shoyee.Contracts.Dtos;

/// <summary><c>config.reload</c> 的结果。</summary>
/// <param name="Changed">已经热更新生效的配置段。</param>
/// <param name="RestartRequired">文件里改了、但这一版只能重启 daemon 才生效的配置段。</param>
public sealed record ConfigReloadResult(
    IReadOnlyList<string> Changed,
    IReadOnlyList<string> RestartRequired);

/// <summary>
/// <c>config.set</c> 的请求：<see cref="Patch"/> 与 <see cref="Raw"/> 二选一。
///
/// <para><see cref="Patch"/> 是**局部配置**，深合并进现有文件：对象逐层合并、数组与标量整体替换、
/// <c>null</c> 表示删掉该键（可选段恢复默认）。设置页每页只发自己那一段。</para>
/// <para><see cref="Raw"/> 是**整份 config.json 原文**（配置文件页），直接替换文件内容；
/// 仍然要过同一套校验，<c>root</c> 由 daemon 覆盖。</para>
/// </summary>
/// <param name="Patch">要合并的局部配置（与 <paramref name="Raw"/> 互斥）。</param>
/// <param name="Raw">整份 config.json 文本（与 <paramref name="Patch"/> 互斥）。</param>
public sealed record ConfigSetRequest(JsonObject? Patch = null, string? Raw = null);

/// <summary><c>config.set</c> 的结果：写盘后立即按 <c>config.reload</c> 的规则应用。</summary>
/// <param name="Changed">已经热更新生效的配置段。</param>
/// <param name="RestartRequired">已写入文件、但要重启 daemon 才生效的配置段。</param>
/// <param name="ConfigPath">写入的文件路径（桌面可直接打开）。</param>
/// <param name="Warnings">不拦保存的提醒（如子智能体开了免审批终端、引用的环境变量未设置）。</param>
public sealed record ConfigSetResult(
    IReadOnlyList<string> Changed,
    IReadOnlyList<string> RestartRequired,
    string ConfigPath,
    IReadOnlyList<string> Warnings);
