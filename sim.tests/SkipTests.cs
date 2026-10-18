using Wtw.Sim.People;
using Wtw.Sim.Persistence;
using Wtw.Sim.Runtime;
using Wtw.Sim.Time;
using Wtw.Sim.World;
using Xunit;

namespace Wtw.Sim.Tests;

/// <summary>E4 넘기기 · 큰 일 팝업.</summary>
[Trait("Category", "Fast")]
public class SkipTests
{
    private static GameWorld World()
    {
        var w = GameWorld.Create(20261016, new WorldConfig { DotCount = 0 });
        int cell = PeopleSamples.Home(w.Planet);
        foreach (var (x, y) in PeopleSamples.SpawnSpots(w.People.Life(cell), 3))
            w.Apply(new Intervention(0, Intervention.Kinds.SpawnFamily, new long[] { cell, x, y }));
        return w;
    }

    [Fact]
    public void SkippingGivesTheSameWorldAsRunning()
    {
        var a = World();
        var run = new SkipRun(a, SkipGoals.Years(2));
        int guard = 0;
        while (!run.Step(a) && guard++ < 1000) { }
        Assert.True(run.Done);
        Assert.Contains("목표", run.EndReason);
        var b = World();
        b.AdvanceTo(a.Now);
        Assert.Equal(SaveGame.ToBytes(b), SaveGame.ToBytes(a));
        var lines = run.Summary(a);
        Assert.Contains("인구", lines[1]);
    }

    [Fact]
    public void SkipStopsAtNextDiscoveryAndWhenCancelled()
    {
        var w = World();
        var run = new SkipRun(w, SkipGoals.Discovery(null));
        int guard = 0;
        while (!run.Step(w) && guard++ < 40 * 20) { }
        Assert.True(run.Done);
        Assert.Contains(run.Events, e => e.Kind == WorldEventKind.Discovery);
        var run2 = new SkipRun(w, SkipGoals.Years(50));
        run2.Step(w);
        run2.Cancel();
        Assert.True(run2.Step(w));
        Assert.Contains("그만", run2.EndReason);
    }

    [Fact]
    public void PopupOnlyForWorldEventsAndNotTooOften()
    {
        var gate = new PopupGate();
        WorldEvent E(WorldEventKind k, long t) => new(1, t, k, 0, -1, 0, "");
        Assert.False(gate.ShouldPause(E(WorldEventKind.Birth, 0), 0, -1, 1));          // ★ 는 멈추지 않는다
        Assert.False(gate.ShouldPause(E(WorldEventKind.DiscoveryGroup, 0), 0, -1, 1)); // ★★ 도
        Assert.True(gate.ShouldPause(E(WorldEventKind.Discovery, 0), 0, -1, 1));      // ★★★ 는 멈춘다
        Assert.False(gate.ShouldPause(E(WorldEventKind.Discovery, 10), 10, -1, 1));   // 같은 종류는 1년 간격
        Assert.True(gate.ShouldPause(E(WorldEventKind.Language, 10), 10, -1, 1));     // 다른 종류는 따로
        long y = GameTime.TicksPerYear;
        Assert.True(gate.ShouldPause(E(WorldEventKind.Discovery, y), y, -1, 1));
        Assert.False(gate.ShouldPause(E(WorldEventKind.Discovery, 3 * y), 3 * y, -1, 5)); // 5배속은 5년 간격
        gate.Muted[(int)WorldEventKind.DiscoveryLost] = true;
        Assert.False(gate.ShouldPause(E(WorldEventKind.DiscoveryLost, 9 * y), 9 * y, -1, 1)); // 꺼 둔 종류
        Assert.True(gate.ShouldPause(E(WorldEventKind.Discovery, 9 * y), 9 * y, 100, 1));
        Assert.False(gate.ShouldPause(E(WorldEventKind.Discovery, 20 * y), 20 * y, 110, 1)); // 실제 시간 30초 간격
    }
}
