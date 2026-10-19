using Wtw.Sim.People;
using Wtw.Sim.Persistence;
using Wtw.Sim.Time;
using Wtw.Sim.World;
using Xunit;
using Xunit.Abstractions;

namespace Wtw.Sim.Tests;

/// <summary>(S1) 느린 테스트: 지방마다 CPU — 사람 300명 · 지방 20곳을 오래(경계를 수천 번 넘고 지방이 잠들고 깨어남) CPU 1개 = 4개, 중간 저장 → 불러오기 → 계속.</summary>
[Trait("Category", "Slow")]
public class SpeedSlowTests
{
    private const ulong Seed = 20261016;
    private readonly ITestOutputHelper _out;
    public SpeedSlowTests(ITestOutputHelper o) { _out = o; }

    private static GameWorld Cap300(int threads)
    {
        var w = GameWorld.Create(Seed, new WorldConfig { DotCount = 0, PeopleCap = 300 });
        w.Threads = threads;
        var homes = PeopleSamples.Ranked(w.Planet, (pl, c) => pl.Biome[c] is Planet.Biome.Temperate or Planet.Biome.Rainforest or Planet.Biome.Grassland or Planet.Biome.Savanna && pl.RiverSize[c] > 0 ? pl.Temp10[c] : int.MinValue).Take(20).ToList();
        foreach (int cell in homes)
            foreach (var (x, y) in PeopleSamples.SpawnSpots(w.People.Life(cell), 15))
                w.Apply(new Intervention(0, Intervention.Kinds.SpawnPerson, new long[] { cell, x, y }));
        return w;
    }

    [Fact]
    public void ManyProvincesYearsOneEqualsFour()
    {
        var one = Cap300(1);
        var four = Cap300(4);
        long mid = 3 * GameTime.TicksPerYear + 1234, end = 6 * GameTime.TicksPerYear;
        one.AdvanceTo(mid); four.AdvanceTo(mid);
        Assert.Equal(SaveGame.Fingerprint(one), SaveGame.Fingerprint(four));
        // 중간에 저장 → 불러오기(다른 CPU 수) → 계속
        var loaded = SaveGame.FromBytes(SaveGame.ToBytes(four));
        loaded.Threads = 2;
        one.AdvanceTo(end); four.AdvanceTo(end); loaded.AdvanceTo(end);
        _out.WriteLine($"넘음 {one.People.Stats.Crossings} · 깨어남 {one.People.Stats.Wakes} · 사람 {one.People.AliveCount} · 예약 {one.EventsProcessed}");
        Assert.True(one.People.Stats.Crossings > 1000);
        Assert.Equal(SaveGame.Fingerprint(one), SaveGame.Fingerprint(four));
        Assert.Equal(SaveGame.Fingerprint(one), SaveGame.Fingerprint(loaded));
    }
}
