using Wtw.Sim.Time;
using Xunit;

namespace Wtw.Sim.Tests;

[Trait("Category", "Fast")]
public class TimeTests
{
    [Fact]
    public void Constants()
    {
        Assert.Equal(2_500, GameTime.TicksPerHour);
        Assert.Equal(60_000, GameTime.TicksPerDay);
        Assert.Equal(40, GameTime.DaysPerYear);
        Assert.Equal(2_400_000, GameTime.TicksPerYear);
    }

    [Theory]
    [InlineData(0L, 1, 1, 0, 0)]
    [InlineData(2_499L, 1, 1, 0, 59)]
    [InlineData(2_500L, 1, 1, 1, 0)]
    [InlineData(59_999L, 1, 1, 23, 59)]
    [InlineData(60_000L, 1, 2, 0, 0)]
    [InlineData(2_399_999L, 1, 40, 23, 59)]
    [InlineData(2_400_000L, 2, 1, 0, 0)]
    [InlineData(2_400_000L * 99 + 60_000 * 9 + 2_500 * 7 + 1_250, 100, 10, 7, 30)]
    public void TicksToDate(long tick, int y, int d, int h, int m)
    {
        Assert.Equal(new GameDate(y, d, h, m), GameTime.ToDate(tick));
    }

    [Fact]
    public void EveryMinuteRoundTrips()
    {
        for (int y = 1; y <= 3; y++)
        for (int d = 1; d <= GameTime.DaysPerYear; d += 13)
        for (int h = 0; h < 24; h++)
        for (int m = 0; m < 60; m++)
        {
            var date = new GameDate(y, d, h, m);
            long t = GameTime.ToTicks(date);
            Assert.Equal(date, GameTime.ToDate(t));
            if (t > 0) Assert.NotEqual(date, GameTime.ToDate(t - 1)); // 그 분의 첫 틱
        }
    }

    [Fact]
    public void Formatting()
    {
        Assert.Equal("1년 1일 00시", GameTime.Format(0));
        Assert.Equal("3년 12일 07시", GameTime.Format(GameTime.ToTicks(new GameDate(3, 12, 7, 45))));
        Assert.Equal("3년 12일 07시 45분", GameTime.FormatWithMinutes(GameTime.ToTicks(new GameDate(3, 12, 7, 45))));
    }
}
