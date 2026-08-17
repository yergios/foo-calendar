using Api.Services;

using Shouldly;

namespace UnitTests.Services;

public sealed class WeeklyDigestBackgroundServiceTests
{
    [Fact]
    public void DelayUntilNext_WhenTargetIsLaterTodayOnTheTargetDay_DelaysToLaterToday()
    {
        // Monday 06:00, target Monday 07:00: three hours away, same day.
        var now = new DateTime(2026, 3, 2, 6, 0, 0);

        var delay = WeeklyDigestBackgroundService.DelayUntilNext(DayOfWeek.Monday, TimeSpan.FromHours(7), now);

        delay.ShouldBe(TimeSpan.FromHours(1));
    }

    [Fact]
    public void DelayUntilNext_WhenTargetTimeOnTheTargetDayHasAlreadyPassed_RollsToNextWeek()
    {
        // Monday 08:00, target Monday 07:00: already gone for today, so the next Monday.
        var now = new DateTime(2026, 3, 2, 8, 0, 0);

        var delay = WeeklyDigestBackgroundService.DelayUntilNext(DayOfWeek.Monday, TimeSpan.FromHours(7), now);

        delay.ShouldBe(TimeSpan.FromDays(7) - TimeSpan.FromHours(1));
    }

    [Fact]
    public void DelayUntilNext_WhenNowIsExactlyTheTargetInstant_RollsToNextWeekRatherThanFiringImmediately()
    {
        var now = new DateTime(2026, 3, 2, 7, 0, 0);

        var delay = WeeklyDigestBackgroundService.DelayUntilNext(DayOfWeek.Monday, TimeSpan.FromHours(7), now);

        delay.ShouldBe(TimeSpan.FromDays(7));
    }

    [Fact]
    public void DelayUntilNext_WhenTargetIsALaterDayInTheSameWeek_CountsTheDaysBetween()
    {
        // Monday 07:00, target Thursday 07:00: three full days away.
        var now = new DateTime(2026, 3, 2, 7, 0, 0);

        var delay = WeeklyDigestBackgroundService.DelayUntilNext(DayOfWeek.Thursday, TimeSpan.FromHours(7), now);

        delay.ShouldBe(TimeSpan.FromDays(3));
    }

    [Fact]
    public void DelayUntilNext_WhenTargetDayAlreadyPassedThisWeek_WrapsAroundToNextWeek()
    {
        // Thursday 07:00, target Monday 07:00: Monday already happened this week.
        var now = new DateTime(2026, 3, 5, 7, 0, 0);

        var delay = WeeklyDigestBackgroundService.DelayUntilNext(DayOfWeek.Monday, TimeSpan.FromHours(7), now);

        delay.ShouldBe(TimeSpan.FromDays(4));
    }
}
