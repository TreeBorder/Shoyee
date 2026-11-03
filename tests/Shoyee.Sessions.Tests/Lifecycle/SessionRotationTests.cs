using Shoyee.Sessions.Lifecycle;
using Xunit;

namespace Shoyee.Sessions.Tests.Lifecycle;

public sealed class SessionRotationTests
{
    private static DateTimeOffset Local(int dayOffset, int hour, int minute = 0)
        => new(DateTime.Today.AddDays(dayOffset).AddHours(hour).AddMinutes(minute));

    [Fact]
    public void SameLocalDay_NeverRotates()
    {
        Assert.False(SessionRotation.IsStale(Local(0, 6), Local(0, 9)));
    }

    [Fact]
    public void CrossDay_ButIdleLessThanTwoHours_DoesNotRotate()
    {
        Assert.False(SessionRotation.IsStale(Local(-1, 23, 30), Local(0, 0, 30)));
    }

    [Fact]
    public void CrossDay_AndIdleAtLeastTwoHours_Rotates()
    {
        Assert.True(SessionRotation.IsStale(Local(-1, 21), Local(0, 0, 30)));
    }

    [Fact]
    public void ClockGoingBackwards_DoesNotRotate()
    {
        // 跨本地日但 now 早于 last（时间倒流）：空闲为负，不切
        Assert.False(SessionRotation.IsStale(Local(1, 9), Local(0, 21)));
    }
}
