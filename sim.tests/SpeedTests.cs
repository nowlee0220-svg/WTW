using Wtw.Sim.People;
using Wtw.Sim.Persistence;
using Wtw.Sim.Time;
using Wtw.Sim.World;
using Xunit;
using Xunit.Abstractions;

namespace Wtw.Sim.Tests;

/// <summary>
/// (S1) 속도 정비. 고정된 지문: 결과가 똑같은 정비((가) 종류)는 이 값을 바꾸면 안 된다.
/// 값이 바뀌면 그 까닭((나) 종류 — 결과가 조금 바뀌는 정비)을 docs/reports/S1.md 에 적고 새 값으로 고친다.
/// </summary>
[Trait("Category", "Fast")]
public class SpeedTests
{
    private const ulong Seed = 20261016;
    private readonly ITestOutputHelper _out;
    public SpeedTests(ITestOutputHelper o) { _out = o; }

    /// <summary>따뜻한 땅 여러 곳에 가족을 뿌려 며칠(경계를 넘고 · 이야기하고 · 실험하는 세계).</summary>
    internal static GameWorld Families(int provinces, int families, long days, int threads = 1)
    {
        var w = GameWorld.Create(Seed, new WorldConfig { DotCount = 0 });
        w.Threads = threads;
        foreach (int cell in PeopleSamples.Ranked(w.Planet, (pl, c) => pl.Biome[c] is Planet.Biome.Temperate or Planet.Biome.Rainforest or Planet.Biome.Grassland or Planet.Biome.Savanna && pl.RiverSize[c] > 0 ? pl.Temp10[c] : int.MinValue).Take(provinces))
            foreach (var (x, y) in PeopleSamples.SpawnSpots(w.People.Life(cell), families))
                w.Apply(new Intervention(0, Intervention.Kinds.SpawnFamily, new long[] { cell, x, y }));
        w.AdvanceTo(days * GameTime.TicksPerDay + 777);
        return w;
    }

    internal static string ScenePath(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Wtw.sln"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "game", "scenes", file);
    }

    [Fact]
    public void PinnedFamilies()
    {
        var w = Families(4, 3, 60);
        ulong fp = SaveGame.Fingerprint(w);
        _out.WriteLine($"families 0x{fp:X16}UL · 사람 {w.People.AliveCount} · 넘음 {w.People.Stats.Crossings}");
        Assert.Equal(PinFamilies, fp);
    }

    [Fact]
    public void PinnedVillage()
    {
        var w = SaveGame.LoadFile(ScenePath("village-50y.wtw"));
        w.AdvanceTo(w.Now + 10 * GameTime.TicksPerDay + 777);
        ulong fp = SaveGame.Fingerprint(w);
        _out.WriteLine($"village 0x{fp:X16}UL · 사람 {w.People.AliveCount} · 수업 {w.People.Stats.Lessons}");
        Assert.Equal(PinVillage, fp);
    }

    /// <summary>(S1) CPU 1개 = CPU 4개: 같은 시드 · 같은 뿌리기 → 같은 지문(경계를 여러 번 넘는 세계).</summary>
    [Fact]
    public void FourThreadsSameAsOne()
    {
        var one = Families(6, 3, 45, threads: 1);
        var four = Families(6, 3, 45, threads: 4);
        _out.WriteLine($"넘음 {one.People.Stats.Crossings} · 예약 {one.EventsProcessed} · 사람 {one.People.AliveCount}");
        Assert.True(one.People.Stats.Crossings >= 3, $"경계를 넘은 횟수 {one.People.Stats.Crossings}");
        Assert.True(one.People.Provinces.Count >= 6);
        Assert.Equal(SaveGame.Fingerprint(one), SaveGame.Fingerprint(four));
        Assert.Equal(one.EventsProcessed, four.EventsProcessed);
        Assert.Equal(one.People.Stats.Decisions, four.People.Stats.Decisions);
    }

    /// <summary>(S1) 큰 마을(지식 · 가르치기 · 그림)도 CPU 수와 상관없이 같다.</summary>
    [Fact]
    public void VillageThreadsSame()
    {
        ulong Run(int threads)
        {
            var w = SaveGame.LoadFile(ScenePath("village-50y.wtw"));
            w.Threads = threads;
            w.AdvanceTo(w.Now + 6 * GameTime.TicksPerDay + 333);
            return SaveGame.Fingerprint(w);
        }
        Assert.Equal(Run(1), Run(3));
    }

    /// <summary>(S1) 맞추는 시각 사이(미뤄 둔 일 · 넘겨주기 대기가 있을 때)에 저장 → 불러오기 → 계속(여러 CPU) = 끊김 없이 계속한 것.</summary>
    [Fact]
    public void SaveMidSyncThenContinueWithThreads()
    {
        var w = Families(6, 3, 20, threads: 4);
        // 맞추는 시각 사이로(미뤄 둔 일이 남아 있게)
        long t = w.Now;
        for (int k = 0; k < 20_000 && (w.People.PendingOpsForSave().Count == 0 || t % GameWorld.SyncInterval == 0); k++) { t += 37; w.AdvanceTo(t); }
        Assert.NotEqual(0, t % GameWorld.SyncInterval);
        Assert.NotEmpty(w.People.PendingOpsForSave());
        var bytes = SaveGame.ToBytes(w);
        var loaded = SaveGame.FromBytes(bytes);
        loaded.Threads = 2;
        Assert.Equal(SaveGame.Fingerprint(w), SaveGame.Fingerprint(loaded));
        long end = t + 25 * GameTime.TicksPerDay + 11;
        w.AdvanceTo(end); loaded.AdvanceTo(end);
        Assert.Equal(SaveGame.Fingerprint(w), SaveGame.Fingerprint(loaded));
    }

    // 고친 기록(바뀔 때마다 한 줄 — 까닭은 docs/reports/S1.md):
    //   S1 시작(main): families 0x491270119F364ED4 · village 0xA403FD1B3D62CD60 — (가) 정비 뒤에도 같았다
    //   S1 지방마다 CPU(맞추는 시각 · 지방마다 예약표 · 미룬 세계의 일 · 저장 버전 9): 순서가 바뀌어 새 값
    private const ulong PinFamilies = 0xAAA388FDBF7B7D7EUL;
    private const ulong PinVillage = 0x9D4C3ECFF227DFC7UL;
}
