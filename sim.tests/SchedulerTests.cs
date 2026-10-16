using Wtw.Sim.Scheduling;
using Xunit;

namespace Wtw.Sim.Tests;

[Trait("Category", "Fast")]
public class SchedulerTests
{
    [Fact]
    public void PopsByTimeThenByScheduleOrder()
    {
        var s = new Scheduler();
        s.Schedule(50, 1, 0);
        s.Schedule(10, 2, 0);
        s.Schedule(50, 3, 0);
        s.Schedule(10, 4, 0);
        s.Schedule(30, 5, 0);
        var order = new List<int>();
        while (s.Count > 0) order.Add(s.Pop().Target);
        Assert.Equal(new[] { 2, 4, 5, 1, 3 }, order);
    }

    [Fact]
    public void ManySameTickKeepInsertionOrder()
    {
        var s = new Scheduler();
        var r = new Wtw.Sim.Core.Pcg32(9, 9);
        for (int i = 0; i < 2000; i++) s.Schedule(r.NextInt(20), i, 0);
        long lastTick = -1; int lastTarget = -1;
        while (s.Count > 0)
        {
            var e = s.Pop();
            if (e.Tick == lastTick) Assert.True(e.Target > lastTarget);
            Assert.True(e.Tick >= lastTick);
            lastTick = e.Tick; lastTarget = e.Target;
        }
    }

    [Fact]
    public void CancelRemovesOnlyThatEvent()
    {
        var s = new Scheduler();
        var a = s.Schedule(10, 1, 0);
        var b = s.Schedule(20, 2, 0);
        var c = s.Schedule(30, 3, 0);
        Assert.True(s.Cancel(b));
        Assert.False(s.Cancel(b));
        Assert.False(s.Contains(b));
        Assert.Equal(new[] { 1, 3 }, new[] { s.Pop().Target, s.Pop().Target });
        Assert.False(s.Cancel(a));
        Assert.False(s.Cancel(c));
    }

    [Fact]
    public void RescheduleEarlierInterrupts()
    {
        var s = new Scheduler();
        s.Schedule(10, 1, 0);
        var late = s.Schedule(100, 2, 0);
        s.Schedule(20, 3, 0);
        Assert.True(s.Reschedule(late, 5)); // 급한 일 → 당김
        Assert.Equal(2, s.Pop().Target);
        Assert.Equal(1, s.Pop().Target);
        Assert.Equal(3, s.Pop().Target);
    }

    [Fact]
    public void RescheduledToSameTickGoesAfterExisting()
    {
        var s = new Scheduler();
        var a = s.Schedule(10, 1, 0);
        s.Schedule(10, 2, 0);
        s.Reschedule(a, 10);
        Assert.Equal(2, s.Pop().Target);
        Assert.Equal(1, s.Pop().Target);
    }

    [Fact]
    public void ExportImportKeepsOrder()
    {
        var s = new Scheduler();
        var r = new Wtw.Sim.Core.Pcg32(3, 3);
        var handles = new List<long>();
        for (int i = 0; i < 300; i++) handles.Add(s.Schedule(r.NextInt(100), i, i % 3));
        for (int i = 0; i < 300; i += 7) s.Cancel(handles[i]);
        var copy = new Scheduler();
        copy.Import(s.ExportSorted(), s.NextHandle, s.NextOrder);
        while (s.Count > 0) Assert.Equal(s.Pop(), copy.Pop());
        Assert.Equal(0, copy.Count);
        Assert.Equal(s.Schedule(1, 1, 1), copy.Schedule(1, 1, 1));
    }
}
