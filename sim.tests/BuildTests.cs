using Wtw.Sim.Data;
using Wtw.Sim.People;
using Wtw.Sim.Persistence;
using Wtw.Sim.Planet;
using Wtw.Sim.Provinces;
using Wtw.Sim.Time;
using Wtw.Sim.World;
using Xunit;
using Xunit.Abstractions;

namespace Wtw.Sim.Tests;

/// <summary>(E6) 건물: 짓는 법은 속성 판정 · 재료가 없으면 못 짓는다 · 움집 안은 덜 춥다 · 저장 구덩이는 덜 잃는다 · 잠든 지방의 건물은 깨어나면 흐른 날만큼 낡는다 · 결정론.</summary>
[Trait("Category", "Fast")]
public class BuildTests
{
    private const ulong Seed = 20261016;
    private static readonly CraftData C = CraftData.Default;
    private static readonly BuildingData B = BuildingData.Default;
    private readonly ITestOutputHelper _out;
    public BuildTests(ITestOutputHelper o) { _out = o; }

    private static int I(string id) => C.ItemIndex(id);
    private static int[] P(string id) => C.ItemProps[I(id)];

    /// <summary>따뜻한 강가 땅 몇 곳에 가족들 — 처음부터 불 · 바람막이 · 움집 · 구덩이 짓는 법을 알고 머물 곳이 아쉽다.</summary>
    internal static GameWorld BuildFamilies(int provinces, int families, long days, int threads = 1)
    {
        var w = GameWorld.Create(Seed, new WorldConfig { DotCount = 0 });
        w.Threads = threads;
        foreach (int cell in PeopleSamples.Ranked(w.Planet, (pl, c) => pl.Biome[c] is Biome.Temperate or Biome.Grassland && pl.RiverSize[c] > 0 ? pl.Temp10[c] : int.MinValue).Take(provinces))
            foreach (var (x, y) in PeopleSamples.SpawnSpots(w.People.Life(cell), families))
                w.Apply(new Intervention(0, Intervention.Kinds.SpawnFamily, new long[] { cell, x, y }));
        w.AdvanceTo(1);
        var ps = w.People;
        foreach (var q in ps.All)
        {
            foreach (var id in new[] { "sparks", "campfire", "windbreak", "hut", "storage_pit", "flake" }) ps.Learn(q, C.Rule(id)!, 0, -1, -1, -1);
            if (q.Stage(w.Now) >= LifeStage.Adult) { q.AddCarry(I("flake"), 1); q.AddCarry(I("spear"), 1); q.Wish[12] = 40; }
        }
        w.AdvanceTo(days * GameTime.TicksPerDay + 777);
        return w;
    }

    [Fact]
    public void DataLoads()
    {
        foreach (var id in new[] { "windbreak", "hut", "storage_pit", "rack", "knap_spot", "hide_frame", "kiln", "fire_pit", "grave", "cairn" }) Assert.True(B.Index(id) >= 0, id);
        foreach (var id in new[] { "windbreak", "hut", "storage_pit", "rack", "knap_spot", "hide_frame", "kiln" }) Assert.NotNull(C.Rule(id));
        Assert.Contains(C.Wishes, w => w.Id == "shelter");
        Assert.Contains(C.Wishes, w => w.Id == "storage");
        Assert.True(C.Wishes.Count <= Person.WishSlots);
        foreach (var id in new[] { "house", "windbreak", "pit", "grave", "kiln" }) Assert.True(LanguageData.Default.ConceptIndex(id) >= 0, id);
        Assert.Equal("building", PeopleData.Default.Skills[PeopleData.Building].Id);
        Assert.Equal(2, B.Buildings[B.Index("hut")].W);
    }

    /// <summary>짓는 법은 속성 판정: 잔가지(질기고 가벼운 것) + 풀(덮을 것) → 바람막이, 돌을 세우면 아니다. 움집은 땅 파는 것이 없으면 부분 성공(단서), 작업대도 속성으로.</summary>
    [Fact]
    public void BuildingRulesComeFromProperties()
    {
        int raise = C.ActionIndex("raise"), dig = C.ActionIndex("dig"), set = C.ActionIndex("set");
        var w1 = C.Judge(raise, P("twigs"), P("grass"), null, 0, 0, 0);
        Assert.True(w1.Success); Assert.Equal("windbreak", w1.Rule!.Id);
        Assert.False(C.Judge(raise, P("stone"), P("grass"), null, 0, 0, 0).Success); // 돌은 기둥이 아니다(단단하고 무겁다)
        Assert.False(C.Judge(raise, P("twigs"), P("stone"), null, 0, 0, 0).Success); // 덮을 것이 아니다
        var r = C.Judge(raise, P("stick"), P("cord"), null, 0, 0, 0);
        Assert.True(r.Success); Assert.Equal("rack", r.Rule!.Id); // 막대를 끈으로 묶으면 시렁
        var h1 = C.Judge(dig, P("wood"), P("grass"), P("spear"), 0, 0, 0);
        Assert.True(h1.Success); Assert.Equal("hut", h1.Rule!.Id);
        var h2 = C.Judge(dig, P("wood"), P("grass"), null, 0, 0, 0);
        Assert.False(h2.Success); Assert.True(h2.IsPartial); Assert.Equal("tool", h2.Partial!.Key);
        Assert.False(C.Judge(dig, P("twigs"), P("grass"), P("spear"), 0, 0, 0).Success); // 잔가지는 지붕을 받치지 못한다(무게)
        var pit = C.Judge(dig, P("grass"), null, P("spear"), 0, 0, 0);
        Assert.True(pit.Success); Assert.Equal("storage_pit", pit.Rule!.Id);
        // 속성을 바꾸면 결과도 바뀐다
        var fake = (int[])P("twigs").Clone(); fake[C.P_toughness] = 0;
        Assert.False(C.Judge(raise, fake, P("grass"), null, 0, 0, 0).Success);
        Assert.Equal("knap_spot", C.Judge(set, P("granite"), null, null, 0, 0, 0).Rule?.Id);
        Assert.False(C.Judge(set, P("flint"), null, null, 0, 0, 0).Success); // 가벼운 부싯돌은 받침이 못 된다
        var k = C.Judge(set, P("clay"), P("stone"), null, 1, 0, 0);
        Assert.True(k.Success); Assert.Equal("kiln", k.Rule!.Id);
        Assert.False(C.Judge(set, P("clay"), P("stone"), null, 0, 0, 0).Success); // 불 없이는 가마가 굳지 않는다
        // (E6 툰드라) 뼈 기둥 + 가죽 덮개 → 가죽 천막(나무가 없는 땅에서도). 뼈에 풀을 덮으면 아무것도 아니다(뼈는 단단해 바람막이 기둥이 아니다)
        var tent = C.Judge(raise, P("bone"), P("dried_hide"), null, 0, 0, 0);
        Assert.True(tent.Success); Assert.Equal("tent", tent.Rule!.Id);
        Assert.Equal("tent", C.Judge(raise, P("stick"), P("raw_hide"), null, 0, 0, 0).Rule?.Id);
        Assert.False(C.Judge(raise, P("bone"), P("grass"), null, 0, 0, 0).Success);
    }

    /// <summary>"알아냈다"와 "지었다"는 다르다: 재료가 없는 자리는 쌓지 못하고(다 짓지 못한다), 재료 · 일을 채우면 선다.</summary>
    [Fact]
    public void NoMaterialsNoBuilding()
    {
        var w = GameWorld.Create(Seed, new WorldConfig { DotCount = 0 });
        int cell = PeopleSamples.Home(w.Planet);
        var life = w.People.Life(cell);
        var (x, y) = PeopleSamples.SpawnSpots(life, 1)[0];
        w.Apply(new Intervention(0, Intervention.Kinds.SpawnPerson, new long[] { cell, x, y }));
        w.AdvanceTo(1);
        var ps = w.People; var p = ps.All[0];
        ps.Learn(p, C.Rule("hut")!, 0, -1, -1, -1);
        int anchor = ps.TestFindSite(life, B.Index("hut"), p.Tile(w.Now));
        Assert.True(anchor >= 0);
        var site = ps.TestSite(p, B.Index("hut"), anchor);
        Assert.False(site.Done);
        // 재료가 하나도 없으면 쌓는 일은 한 분도 쌓이지 않는다(재료부터 날라야)
        Assert.True(site.Have.All(h => h == 0));
        ps.TestComplete(p, site);
        Assert.True(site.Done);
        Assert.Equal(site.Id, life.BuildingOn(anchor)!.Id);
        Assert.Equal(site.Id, life.BuildingOn(anchor + HexFrame.Size + 1)!.Id); // 2 × 2 타일
        Assert.False(ps.TestSiteOk(life, B.Index("hut"), anchor)); // 이미 선 자리에는 또 못 짓는다
    }

    /// <summary>움집 안에서 자면 추운 밤을 더 잘 견딘다(밤의 느끼는 기온이 높다) · 품질이 낮으면 덜 막는다.</summary>
    [Fact]
    public void HutKeepsWarm()
    {
        var w = GameWorld.Create(Seed, new WorldConfig { DotCount = 0 });
        int cell = PeopleSamples.Cold(w.Planet);
        var life = w.People.Life(cell);
        var (x, y) = PeopleSamples.SpawnSpots(life, 1)[0];
        w.Apply(new Intervention(0, Intervention.Kinds.SpawnPerson, new long[] { cell, x, y }));
        w.AdvanceTo(1);
        var ps = w.People; var p = ps.All[0];
        int anchor = ps.TestFindSite(life, B.Index("hut"), p.Tile(w.Now));
        Assert.True(anchor >= 0);
        long night = GameTime.TicksPerDay + 3 * GameTime.TicksPerHour - life.OffsetTicks;
        while (!life.IsNight(night)) night += GameTime.TicksPerHour;
        int before = life.FeltTemp10(night, anchor, false);
        var hut = ps.TestSite(p, B.Index("hut"), anchor);
        ps.TestComplete(p, hut);
        int inside = life.FeltTemp10(night, anchor, false);
        _out.WriteLine($"밖 {before / 10.0}°C → 움집 안 {inside / 10.0}°C (품질 {hut.Quality})");
        Assert.True(inside >= before + 40, $"움집 안 {inside} · 밖 {before}");
    }

    /// <summary>저장 구덩이에 둔 낟알은 맨땅에 쌓은 것보다 덜 잃는다(쥐 · 벌레).</summary>
    [Fact]
    public void StoragePitLosesLess()
    {
        int Lost(bool pit)
        {
            var w = GameWorld.Create(Seed, new WorldConfig { DotCount = 0 });
            int cell = PeopleSamples.Home(w.Planet);
            var life = w.People.Life(cell);
            var (x, y) = PeopleSamples.SpawnSpots(life, 1)[0];
            w.Apply(new Intervention(0, Intervention.Kinds.SpawnPerson, new long[] { cell, x, y }));
            w.AdvanceTo(1);
            var ps = w.People; var p = ps.All[0];
            int t = ps.TestFindSite(life, B.Index("storage_pit"), p.Tile(w.Now));
            life.Stash = t;
            if (pit) { var b = ps.TestSite(p, B.Index("storage_pit"), t); life.Stash = -1; ps.TestComplete(p, b); life.Stash = t; }
            life.Drop(t, I("grain"), 400);
            for (int d = 0; d < 40; d++) ps.TestStashDaily(life);
            return 400 - life.Items[t].Where(c => c.Item == I("grain")).Sum(c => c.Count);
        }
        int open = Lost(false), pitLost = Lost(true);
        _out.WriteLine($"맨땅 {open} · 구덩이 {pitLost} (40일, 400개 가운데)");
        Assert.True(pitLost * 3 < open, $"구덩이 {pitLost} · 맨땅 {open}");
    }

    /// <summary>잠든 지방의 건물은 멈추고, 깨어나면 흐른 날만큼 낡는다 — 오래 비면 무너져 있다(저장 → 불러오기를 거쳐도 같다).</summary>
    [Fact]
    public void DormantBuildingsDecayOnWake()
    {
        var w = GameWorld.Create(Seed, new WorldConfig { DotCount = 0 });
        int cell = PeopleSamples.Home(w.Planet);
        var life = w.People.Life(cell);
        var (x, y) = PeopleSamples.SpawnSpots(life, 1)[0];
        w.Apply(new Intervention(0, Intervention.Kinds.SpawnPerson, new long[] { cell, x, y }));
        w.AdvanceTo(1);
        var ps = w.People; var p = ps.All[0];
        int a1 = ps.TestFindSite(life, B.Index("windbreak"), p.Tile(w.Now));
        var wb = ps.TestSite(p, B.Index("windbreak"), a1);
        ps.TestComplete(p, wb);
        int id = wb.Id;
        ps.Kill(p, DeathCause.OldAge);
        w.AdvanceTo(w.Now + 20 * GameTime.TicksPerDay);
        Assert.False(ps.Provinces.ContainsKey(cell)); // 잠들었다
        Assert.Contains(ps.Dormant[cell].Buildings, b => b.Id == id);
        // 잠든 동안(바람막이는 보통 120일 — 나쁨이면 60일) 한 해 넘게 흘렀다 → 깨어나면 무너져 없다
        w.AdvanceTo(w.Now + 5 * GameTime.TicksPerYear);
        var bytes = SaveGame.ToBytes(w);
        var w2 = SaveGame.FromBytes(bytes);
        foreach (var ww in new[] { w, w2 })
        {
            var l2 = ww.People.Life(cell); // 깨운다
            Assert.False(l2.Buildings.ContainsKey(id), "다 낡은 바람막이는 깨어나면 무너져 있다");
        }
        Assert.Equal(SaveGame.Fingerprint(w), SaveGame.Fingerprint(w2));
    }

    /// <summary>건물이 있는 세계: CPU 1 = 4 · 저장 → 불러오기 → 계속(짓다 만 자리 포함)도 같은 결과.</summary>
    [Fact]
    public void BuildingWorldDeterministic()
    {
        var one = BuildFamilies(2, 3, 30, threads: 1);
        var four = BuildFamilies(2, 3, 30, threads: 4);
        int built = one.People.Provinces.Values.Sum(l => l.Buildings.Count);
        int sites = one.People.Provinces.Values.Sum(l => l.Buildings.Values.Count(b => !b.Done));
        _out.WriteLine($"건물 {built} (짓는 중 {sites}) · 다 지음 {one.People.Stats.BuildingsDone} · 날라 온 재료 {one.People.Stats.BuildDelivered}");
        Assert.True(built > 0, "건물이 하나도 없다");
        Assert.Equal(SaveGame.Fingerprint(one), SaveGame.Fingerprint(four));
        // 저장 → 불러오기 → 계속 = 끊김 없이 계속
        var a = BuildFamilies(2, 3, 30);
        var b = SaveGame.FromBytes(SaveGame.ToBytes(a));
        long end = a.Now + 6 * GameTime.TicksPerDay;
        a.AdvanceTo(end); b.AdvanceTo(end);
        Assert.Equal(SaveGame.Fingerprint(a), SaveGame.Fingerprint(b));
    }
}
