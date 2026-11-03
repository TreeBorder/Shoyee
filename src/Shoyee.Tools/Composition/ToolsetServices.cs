using Shoyee.Contracts.Abstractions;
using Shoyee.Memory.Index;
using Shoyee.Memory.History;
using Shoyee.Notes;
using Shoyee.Tools.Mcp;
using Shoyee.Tools.Process;
using Shoyee.Voice;
using Shoyee.Web;

namespace Shoyee.Tools.Composition;

/// <summary>工具面的可选依赖（DI 装配；测试可以只传一部分）。</summary>
/// <param name="SessionSearch">会话检索（<c>session_search</c>/<c>session_read</c>）。</param>
/// <param name="Index">INDEX 地址簿（<c>index_*</c>）。</param>
/// <param name="Notes">笔记库（<c>notes_search</c>；未配置为 null）。</param>
/// <param name="Processes">后台进程表（<c>process_*</c>）。</param>
/// <param name="Mcp">MCP 工具（启动时由 <see cref="McpConnector.ConnectAsync"/> 收罗；未配置为 null）。</param>
/// <param name="Web">网页搜索/抓取（DeepSeek）。</param>
/// <param name="States">通用状态仓库（RP 状态工具）。</param>
/// <param name="Synthesizer">语音合成（<c>voice_speak</c> / <c>voice_help</c>；未配置为 null）。</param>
/// <param name="Transcriber">语音转写（<c>voice_transcribe</c>；未配置为 null）。</param>
/// <param name="Photo">出图服务（<c>image_generate</c>；未配置为 null）。</param>
/// <param name="PhotoVocabulary">角色图词表（RP 图片工具；未配置为 null）。</param>
/// <param name="PhotoTasks">异步出图队列（RP 图片工具；未配置为 null）。</param>
/// <param name="Cron">cron 任务仓库（<c>cron_*</c>；未配置为 null）。</param>
/// <param name="CronScheduler">cron 调度面（改完任务后刷新触发器）。</param>
/// <param name="Messages">消息投影仓库（<c>get_cron_detail</c> 读任务会话）。</param>
/// <param name="Events">事件出口（状态变更推 <c>state.updated</c>；null = 不推）。</param>
public sealed record ToolsetServices(
    ISessionSearch? SessionSearch = null,
    IIndexStore? Index = null,
    INotesProvider? Notes = null,
    ProcessRegistry? Processes = null,
    McpToolset? Mcp = null,
    IWebProvider? Web = null,
    Shoyee.Storage.Abstractions.IStateRepository? States = null,
    ISpeechSynthesizer? Synthesizer = null,
    ISpeechTranscriber? Transcriber = null,
    Shoyee.Photo.PhotoService? Photo = null,
    Shoyee.Photo.PhotoPrompts? PhotoVocabulary = null,
    Shoyee.Photo.PhotoTaskRunner? PhotoTasks = null,
    Shoyee.Storage.Abstractions.ICronRepository? Cron = null,
    Shoyee.Contracts.Abstractions.ICronScheduler? CronScheduler = null,
    Shoyee.Storage.Abstractions.IMessageRepository? Messages = null,
    ITurnEventSink? Events = null);
