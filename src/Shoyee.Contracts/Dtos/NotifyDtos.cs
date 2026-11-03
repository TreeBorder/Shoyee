namespace Shoyee.Contracts.Dtos;

/// <summary><c>notify.test</c> 的请求。不传标题/正文时用默认测试文案。</summary>
/// <param name="Title">标题（可空）。</param>
/// <param name="Body">正文（可空）。</param>
public sealed record NotifyTestRequest(string? Title = null, string? Body = null);

/// <summary><c>notify.test</c> 的返回。</summary>
/// <param name="Provider">当前选中的通知供应商（system / serverchan / email / none）。</param>
/// <param name="Success">是否发送成功。</param>
/// <param name="Error">失败原因（成功时为 null）。</param>
public sealed record NotifyTestResult(string Provider, bool Success, string? Error);
