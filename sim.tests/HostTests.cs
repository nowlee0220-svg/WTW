using Wtw.Sim.Runtime;
using Wtw.Sim.Time;
using Wtw.Sim.World;
using Xunit;

namespace Wtw.Sim.Tests;

[Trait("Category", "Fast")]
public class HostTests
{
    [Fact]
    public void RenderClockAdvancesSmoothlyAndNeverPassesSim()
    {
        using var host = new SimHost(GameWorld.Create(1, new WorldConfig { DotCount = 50 }));
        host.SetSpeed(4); // 하루 3초
        double last = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < 1.0)
        {
            var snap = host.Frame(1 / 60.0);
            Assert.True(host.RenderTick >= last);
            Assert.True(host.RenderTick <= snap.Now);
            last = host.RenderTick;
            Thread.Sleep(16);
        }
        Assert.True(last > GameTime.TicksPerDay * 0.1, $"render tick {last}");
    }

    [Fact]
    public void PauseStopsAtSimFrontier()
    {
        using var host = new SimHost(GameWorld.Create(1, new WorldConfig { DotCount = 10 }));
        host.SetSpeed(3);
        for (int i = 0; i < 20; i++) { host.Frame(1 / 60.0); Thread.Sleep(5); }
        host.SetPaused(true);
        WorldSnapshot snap = host.Frame(0);
        for (int i = 0; i < 30; i++) { snap = host.Frame(1 / 60.0); Thread.Sleep(5); }
        double t = host.RenderTick;
        for (int i = 0; i < 10; i++) { snap = host.Frame(1 / 60.0); Thread.Sleep(5); }
        Assert.Equal(t, host.RenderTick);
        Assert.Equal(snap.Now, (long)host.RenderTick);
    }

    [Fact]
    public void SaveThenLoadReturnsToSavedState()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wtw-host-{Guid.NewGuid():N}.wtw");
        try
        {
            using var host = new SimHost(GameWorld.Create(9, new WorldConfig { DotCount = 20 }));
            host.SetSpeed(4);
            for (int i = 0; i < 10; i++) { host.Frame(1 / 30.0); Thread.Sleep(10); }
            host.SetPaused(true);
            for (int i = 0; i < 10; i++) { host.Frame(1 / 30.0); Thread.Sleep(10); }
            long savedAt = host.Frame(0).Now;
            host.Save(path);
            WaitNotice(host, "저장");
            host.SetPaused(false);
            for (int i = 0; i < 10; i++) { host.Frame(1 / 30.0); Thread.Sleep(10); }
            Assert.True(host.Frame(0).Now > savedAt);
            host.SetPaused(true);
            host.Load(path);
            WaitNotice(host, "불러왔");
            var snap = host.Frame(0);
            Assert.Equal(savedAt, snap.Now);
            Assert.Equal(savedAt, (long)host.RenderTick);
        }
        finally { File.Delete(path); }
    }

    private static void WaitNotice(SimHost host, string contains)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < 5)
        {
            host.Frame(0);
            if (host.TryGetNotice(out var n)) { Assert.Contains(contains, n); return; }
            Thread.Sleep(5);
        }
        Assert.Fail("알림이 오지 않았다: " + contains);
    }
}
