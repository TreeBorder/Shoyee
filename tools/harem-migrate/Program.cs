using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Shoyee.Contracts.Domain;
using Shoyee.Contracts.Options;
using Shoyee.Storage.Abstractions;
using Shoyee.Storage.Sqlite;

// 一次性迁移：Harem(border) 的 rounds.json + summary.json → Shoyee state.db。
// 用法：dotnet run --project tools/harem-migrate -- <rounds.json> <summary.json> <state.db> <laneId>

if (args.Length >= 2 && args[0] == "verify")
{
    var verifyConfig = BuildConfig(args[1]);
    var verifyServices = new ServiceCollection();
    verifyServices.AddShoyeeSqliteStorage(verifyConfig);
    await using var verifyProvider = verifyServices.BuildServiceProvider();
    var verifyRounds = verifyProvider.GetRequiredService<IRoundRepository>();
    var recent = await verifyRounds.GetRecentAsync(3);
    var latestSummary = await verifyProvider.GetRequiredService<ISummaryRepository>().GetLatestAsync();
    Console.WriteLine($"轮次 {await verifyRounds.CountAsync()}，最大 {await verifyRounds.GetMaxRoundNoAsync()}，最近 {recent.Count} 轮可反序列化");
    foreach (var round in recent)
    {
        Console.WriteLine($"  #{round.RoundNo} {round.CreatedAt:yyyy-MM-dd HH:mm} 请求 {round.Request.Count} / 响应 {round.Response.Count}，文本片段：{FirstText(round.Request)}");
    }

    Console.WriteLine(latestSummary is null
        ? "没有摘要"
        : $"摘要覆盖到第 {latestSummary.ThroughRound} 轮，{latestSummary.Content.Length} 字");
    return 0;
}

if (args.Length < 4)
{
    Console.Error.WriteLine("用法：harem-migrate <rounds.json> <summary.json> <state.db> <laneId>");
    Console.Error.WriteLine("      harem-migrate verify <state.db>");
    return 2;
}

var roundsPath = args[0];
var summaryPath = args[1];
var databasePath = args[2];
var laneId = args[3];

WorkspaceConfig config = BuildConfig(databasePath);

var services = new ServiceCollection();
services.AddShoyeeSqliteStorage(config);
await using var provider = services.BuildServiceProvider();
var rounds = provider.GetRequiredService<IRoundRepository>();
var summaries = provider.GetRequiredService<ISummaryRepository>();

if (await rounds.CountAsync() > 0)
{
    Console.Error.WriteLine("目标库已有 RP 轮次，拒绝重复导入。");
    return 3;
}

using var document = JsonDocument.Parse(await File.ReadAllTextAsync(roundsPath));
var items = document.RootElement;
var total = items.GetArrayLength();
var index = 0;

foreach (var roundElement in items.EnumerateArray())
{
    index++;
    var request = MapMessages(roundElement.GetProperty("Request"));
    var response = MapMessages(roundElement.GetProperty("Response"));
    var at = ParseRoundTime(request) ?? DateTimeOffset.Now.AddMinutes(-(total - index));
    await rounds.AppendAsync(laneId, request, response, at);
    Console.WriteLine($"#{index} roundId={roundElement.GetProperty("RoundId").GetInt32()} 请求 {request.Count} 条 / 响应 {response.Count} 条 {at:yyyy-MM-dd HH:mm}");
}

using var summaryDocument = JsonDocument.Parse(await File.ReadAllTextAsync(summaryPath));
var summaryText = summaryDocument.RootElement.GetProperty("CurrentSummary").GetString();
if (!string.IsNullOrWhiteSpace(summaryText))
{
    var maxRound = await rounds.GetMaxRoundNoAsync();
    await summaries.AppendAsync(maxRound, maxRound, summaryText);
    Console.WriteLine($"摘要已写入：覆盖到第 {maxRound} 轮，{summaryText.Length} 字");
}

return 0;

static List<ChatMessage> MapMessages(JsonElement messages)
{
    var result = new List<ChatMessage>();
    foreach (var message in messages.EnumerateArray())
    {
        var role = message.GetProperty("Role").GetString() switch
        {
            "assistant" => ChatRole.Assistant,
            "tool" => ChatRole.Tool,
            "system" => ChatRole.System,
            _ => ChatRole.User,
        };

        var contents = new List<AIContent>();
        foreach (var content in message.GetProperty("Contents").EnumerateArray())
        {
            switch (content.GetProperty("$type").GetString())
            {
                case "text" when content.TryGetProperty("Text", out var text)
                                 && !string.IsNullOrEmpty(text.GetString()):
                    contents.Add(new TextContent(text.GetString()!));
                    break;

                case "reasoning" when content.TryGetProperty("Text", out var reasoning):
                    contents.Add(new TextReasoningContent(reasoning.GetString() ?? string.Empty));
                    break;

                case "functionCall":
                    contents.Add(new FunctionCallContent(
                        content.GetProperty("CallId").GetString()!,
                        content.GetProperty("Name").GetString()!,
                        content.TryGetProperty("Arguments", out var arguments)
                            ? JsonSerializer.Deserialize<Dictionary<string, object?>>(arguments.GetRawText())
                            : null)
                    {
                        InformationalOnly = content.TryGetProperty("InformationalOnly", out var info)
                                            && info.GetBoolean(),
                    });
                    break;

                case "functionResult":
                    contents.Add(new FunctionResultContent(
                        content.GetProperty("CallId").GetString()!,
                        content.GetProperty("Result").ValueKind == JsonValueKind.String
                            ? content.GetProperty("Result").GetString()
                            : content.GetProperty("Result").GetRawText()));
                    break;
            }
        }

        if (contents.Count == 0)
        {
            continue;
        }

        var chat = new ChatMessage(role, contents);
        if (message.TryGetProperty("AuthorName", out var author)
            && author.ValueKind == JsonValueKind.String)
        {
            chat.AuthorName = author.GetString();
        }

        result.Add(chat);
    }

    return result;
}

static DateTimeOffset? ParseRoundTime(IReadOnlyList<ChatMessage> request)
{
    var text = request.SelectMany(m => m.Contents.OfType<TextContent>())
        .Select(c => c.Text)
        .FirstOrDefault(t => t.Contains("[消息时间:", StringComparison.Ordinal));
    if (text is null)
    {
        return null;
    }

    var match = Regex.Match(text, @"(\d{4})-(\d{2})-(\d{2})\([^)]*\)\s*(\d{2}):(\d{2})");
    if (!match.Success)
    {
        return null;
    }

    return new DateTimeOffset(
        int.Parse(match.Groups[1].Value),
        int.Parse(match.Groups[2].Value),
        int.Parse(match.Groups[3].Value),
        int.Parse(match.Groups[4].Value),
        int.Parse(match.Groups[5].Value),
        0,
        TimeSpan.FromHours(8));
}

static WorkspaceConfig BuildConfig(string databasePath) => new(
    Root: Path.GetDirectoryName(databasePath)!,
    Models: new ModelProfiles(
        new ModelEndpoint("https://api.deepseek.com", "deepseek-flash", "DEEPSEEK_API_KEY"),
        new ModelEndpoint("https://api.deepseek.com", "deepseek-flash", "DEEPSEEK_API_KEY")),
    Channels: new ChannelAccounts(),
    Proactive: new ProactiveOptions(new HeartbeatOptions()),
    Tools: new ToolOptions(),
    DatabasePath: databasePath);

static string FirstText(IReadOnlyList<ChatMessage> messages)
    => messages.SelectMany(m => m.Contents.OfType<TextContent>())
        .Select(c => c.Text)
        .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)) is { } text
        ? text.Replace('\n', ' ')[..Math.Min(60, text.Length)]
        : "(无文本)";
