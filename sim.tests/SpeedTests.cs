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
    internal static GameWorld Families(int provinces, int families, long days)
    {
        var w = GameWorld.Create(Seed, new WorldConfig { DotCount = 0 });
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

    // S1 (가) 정비 전 main 의 값
    private const ulong PinFamilies = 0x491270119F364ED4UL;
    private const ulong PinVillage = 0xA403FD1B3D62CD60UL;
}
