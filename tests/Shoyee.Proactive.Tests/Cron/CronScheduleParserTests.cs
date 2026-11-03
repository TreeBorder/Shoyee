using Shoyee.Proactive.Cron;
using Xunit;

namespace Shoyee.Proactive.Tests.Cron;

public sealed class CronScheduleParserTests
{
    [Fact]
    public void Once_ParsesOffsetAndComputesNext()
    {
        var spec = CronScheduleParser.Parse("once:2026-10-30T09:00:00+08:00");

        Assert.Equal(CronScheduleKind.Once, spec.Kind);
        Assert.Equal(TimeSpan.FromHours(8), spec.RunAt!.Value.Offset);
        Assert.Equal(spec.RunAt, CronScheduleParser.GetNext(spec, new DateTimeOffset(2026, 10, 30, 0, 0, 0, TimeSpan.Zero)));
        Assert.Null(CronScheduleParser.GetNext(spec, spec.RunAt.Value));
    }

    [Theory]
    [InlineData("every:30m", 30)]
    [InlineData("every:2h", 120)]
    [InlineData("every:1d", 1440)]
    [InlineData("every:15", 15)]
    public void Every_ParsesInterval(string schedule, int minutes)
    {
        var spec = CronScheduleParser.Parse(schedule);

        Assert.Equal(CronScheduleKind.Interval, spec.Kind);
        Assert.Equal(TimeSpan.FromMinutes(minutes), spec.Interval);
    }

    [Fact]
    public void FiveFieldCron_GetsZeroSeconds()
    {
        var spec = CronScheduleParser.Parse("0 9 * * ?");

        Assert.Equal(CronScheduleKind.Cron, spec.Kind);
        Assert.Equal("0 0 9 * * ?", spec.Cron);
        var next = CronScheduleParser.GetNext(spec, new DateTimeOffset(2026, 10, 29, 0, 0, 0, TimeSpan.FromHours(8)));
        Assert.Equal(new DateTimeOffset(2026, 10, 29, 9, 0, 0, TimeSpan.FromHours(8)), next);
    }

    [Theory]
    [InlineData("")]
    [InlineData("every:0m")]
    [InlineData("once:not-a-time")]
    [InlineData("1 2 3")]
    public void Invalid_ReturnsMessage(string schedule)
    {
        Assert.NotNull(CronScheduleParser.Validate(schedule));
    }
}
