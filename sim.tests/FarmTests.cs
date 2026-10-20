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

/// <summary>(E5c) 농사: 씨 · 싹 · 밭 · 거두기 · 모아 두기 · 짐승이 밭을 먹음 · 잠든 지방의 밭.</summary>
[Trait("Category", "Fast")]
public class FarmTests
{
    private const ulong Seed = 20261016;
    private static readonly CraftData C = CraftData.Default;
    private readonly ITestOutputHelper _out;
    public FarmTests(ITestOutputHelper o) { _out = o; }

    private static int I(string id) => C.ItemIndex(id);
    private static int Act(string id) => C.ActionIndex(id);
    private static int[] P(string id) => C.ItemProps[I(id)];

    /// <summary>뿌린 사람에게 심는 법(+ 불 · 볶기 · 일구는 법)과 낟알 · 땅 파는 막대를 준다.</summary>
    internal static void TeachFarming(GameWorld w, bool field = true)
    {
        var ps = w.People;
        var ids = new List<string> { "sparks", "campfire", "roast_grain", "sow" };
        if (field) ids.AddRange(new[] { "field", "spear" });
        foreach (var q in ps.All)
        {
            if (q.Dead) continue;
            foreach (var id in ids) ps.Learn(q, C.Rule(id)!, 0, -1, -1, -1);
            if (q.Stage(w.Now) >= LifeStage.Adult) { q.AddCarry(I("grain"), 8); if (field) q.AddCarry(I("spear"), 1); }
        }
    }

    /// <summary>따뜻하고 강이 있는 땅 몇 곳에 가족들 — 처음부터 심는 법을 안다.</summary>
    internal static GameWorld FarmFamilies(int provinces, int families, long days, int threads = 1)
    {
        var w = GameWorld.Create(Seed, new WorldConfig { DotCount = 0 });
        w.Threads = threads;
        foreach (int cell in PeopleSamples.Ranked(w.Planet, (pl, c) => pl.Biome[c] is Biome.Temperate or Biome.Grassland or Biome.Savanna && pl.RiverSize[c] > 0 ? pl.Temp10[c] : int.MinValue).Take(provinces))
            foreach (var (x, y) in PeopleSamples.SpawnSpots(w.People.Life(cell), families))
                w.Apply(new Intervention(0, Intervention.Kinds.SpawnFamily, new long[] { cell, x, y }));
        w.AdvanceTo(1);
        TeachFarming(w);
        w.AdvanceTo(days * GameTime.TicksPerDay + 777);
        return w;
    }

    [Fact]
    public void DataLoads()
    {
        foreach (var id in new[] { "grain", "beans", "tuber", "roasted_grain", "flour", "flatbread", "stone_hoe", "field", "sown_patch" }) Assert.True(I(id) >= 0, id);
        Assert.NotNull(C.Rule("sow")); Assert.NotNull(C.Rule("field"));
        Assert.Contains(C.Wishes, w => w.Id == "farming");
        Assert.True(C.Wishes.Count <= Person.WishSlots);
        var cat = ObjectCatalog.Default;
        foreach (var id in new[] { "wild_grain", "wild_beans", "wild_tuber" }) Assert.Contains(cat.Objects, o => o.Id == id);
        foreach (var id in new[] { "seed", "sprout", "field", "grain" }) Assert.True(LanguageData.Default.ConceptIndex(id) >= 0, id);
        Assert.True(PeopleData.Default.Skills[PeopleData.Farming].Id == "farming");
    }

    /// <summary>심는 법 · 밭 · 볶기 · 괭이는 정답표가 아니라 속성 판정: 씨(sprouts)가 아니면 묻어도 안 나고, 땅 파는 것(digs)이 없으면 밭이 안 된다.</summary>
    [Fact]
    public void FarmingComesFromProperties()
    {
        int bury = Act("bury"), dig = Act("dig"), roast = Act("roast"), twist = Act("twist");
        var r1 = C.Judge(bury, P("grain"), null, null, 0, 0, 0);
        Assert.True(r1.Success); Assert.Equal("sow", r1.Rule!.Id);
        Assert.True(C.Judge(bury, P("beans"), null, null, 0, 0, 0).Success);
        Assert.False(C.Judge(bury, P("stone"), null, null, 0, 0, 0).Success); // 돌은 묻어도 나지 않는다
        Assert.False(C.Judge(bury, P("berries"), null, null, 0, 0, 0).Success);
        // 씨의 속성만 바꾸면 결과도 바뀐다
        var fake = (int[])P("grain").Clone(); fake[C.PropIndex("sprouts")] = 0;
        Assert.False(C.Judge(bury, fake, null, null, 0, 0, 0).Success);
        var f1 = C.Judge(dig, P("grain"), null, P("spear"), 0, 0, 0);
        Assert.True(f1.Success); Assert.Equal("field", f1.Rule!.Id);
        var f2 = C.Judge(dig, P("grain"), null, P("stick"), 0, 0, 0); // 나무 막대(파는 힘 1)로는 얕게만
        Assert.True(f2.IsPartial); Assert.Equal("tool", f2.FailedKey);
        Assert.Equal("roast_grain", C.Judge(roast, P("grain"), null, null, 1, 0, 0).Rule!.Id);
        Assert.Equal("cook_beans", C.Judge(roast, P("beans"), null, null, 1, 0, 0).Rule!.Id);
        Assert.Equal("roasted_acorn", C.Judge(roast, P("acorn"), null, null, 1, 0, 0).Rule!.Id); // 도토리는 그대로
        Assert.Equal("flatbread", C.Judge(roast, P("flour"), null, null, 1, 0, 0).Rule!.Id);
        Assert.Equal("stone_hoe", C.Judge(twist, P("basalt"), P("stick"), P("cord"), 0, 0, 0).Rule!.Id);
        var hc = C.Judge(twist, P("chopper"), P("stick"), P("cord"), 0, 0, 0); // 날 선 찍개는 괭이 돌이 아니다(찍개를 괭이로 바꿔 날붙이를 잃던 것)
        Assert.False(hc.Success && hc.Rule?.Id == "stone_hoe");
        Assert.Equal("hafted_axe", C.Judge(twist, P("ground_axe"), P("cord"), null, 0, 0, 0).Rule!.Id);
    }

    /// <summary>자라기는 기온 · 비 · 흙으로: 따뜻한 땅은 잘 자라고, 툰드라 · 사막은 거의 안 자란다.</summary>
    [Fact]
    public void GrowthByClimate()
    {
        var w = GameWorld.Create(Seed, new WorldConfig { DotCount = 0 });
        var ps = w.People;
        int Best(int cell) { var g = ps.GrowMap(ps.Life(cell)); int b = 0; foreach (byte v in g) b = Math.Max(b, v); return b; }
        int warm = PeopleSamples.Home(w.Planet), cold = PeopleSamples.Cold(w.Planet);
        int desert = PeopleSamples.Ranked(w.Planet, (pl, c) => pl.Biome[c] == Biome.Desert ? -pl.PrecipMm[c] : int.MinValue).First();
        int bw = Best(warm), bc = Best(cold);
        var gd = ps.GrowMap(ps.Life(desert));
        int dryShare = gd.Count(v => v >= 15) * 1000 / Math.Max(1, ps.Life(desert).Walk.Count(x => x));
        _out.WriteLine($"따뜻한 땅 {bw}({PeopleSim.GrowDays(bw)}일) · 툰드라 {bc} · 사막(강수 {w.Planet.PrecipMm[desert]}mm) 자라는 타일 {dryShare}‰");
        Assert.True(bw >= 70 && PeopleSim.GrowDays(bw) <= 24);
        Assert.True(bc < 15 && PeopleSim.GrowDays(bc) == 0);
        Assert.True(dryShare < 150, "사막은 물가 말고는 거의 안 자란다");
    }

    /// <summary>씨를 묻으면 자라고 거둘 수 있다(심는 법을 아는 가족 — 40일).</summary>
    [Fact]
    public void SowGrowHarvest()
    {
        var w = FarmFamilies(1, 2, 40);
        var st = w.People.Stats;
        var life = w.People.Provinces.Values.First(l => l.Plots.Count > 0);
        _out.WriteLine($"일군 {st.Tilled} · 심음 {st.Sown} · 거둔 타일 {st.HarvestTiles} · 거둔 수 {st.HarvestItems} · 모아 둠 {st.Stored} · 밭 {life.Plots.Count} · 모아 둔 자리 {life.Stash}");
        Assert.True(st.Tilled > 0 && st.Sown > 0);
        Assert.True(st.HarvestItems > 0);
        Assert.True(life.Stash >= 0);
        Assert.Contains(w.People.All, q => q.Harvested > 0);
    }

    /// <summary>심는 법은 흘린 씨에서 난 싹을 보고 알아낸다(정답표 · 시간이 아니라 — 본 사람만, 그 씨의 속성으로).</summary>
    [Fact]
    public void LearnSowingBySeeingSprouts()
    {
        var w = GameWorld.Create(Seed, new WorldConfig { DotCount = 0 });
        int cell = PeopleSamples.Home(w.Planet);
        var life = w.People.Life(cell);
        foreach (var (x, y) in PeopleSamples.SpawnSpots(life, 3)) w.Apply(new Intervention(0, Intervention.Kinds.SpawnFamily, new long[] { cell, x, y }));
        w.AdvanceTo(1);
        var sow = C.Rule("sow")!;
        Assert.DoesNotContain(w.People.All, q => q.Knows(sow.Index));
        // 사람들 곁에 저절로 난 싹(자라는 중)을 늘 둔다
        for (int day = 0; day < 160 && !w.People.All.Any(q => q.Knows(sow.Index)); day++)
        {
            foreach (var q in w.People.All.Where(q => !q.Dead).Take(4))
            {
                int t = q.Tile(w.Now) + 2;
                if (!life.Walk[t] || life.Plots.ContainsKey(t)) continue;
                life.AddPlot(new Plot { Tile = t, Stage = PlotStage.Growing, Wild = true, Crop = I("grain"), Stamp = w.Now, LastTend = w.Now, Due = w.Now + 30 * GameTime.TicksPerDay });
            }
            w.AdvanceTo(w.Now + GameTime.TicksPerDay);
        }
        var st = w.People.Stats;
        _out.WriteLine($"싹을 본 번 {st.SproutSights} · 알아냄 {st.SproutLearned} · {w.Now / GameTime.TicksPerDay}일");
        Assert.True(st.SproutSights > 0);
        var who = w.People.All.FirstOrDefault(q => q.Knows(sow.Index));
        Assert.NotNull(who);
        Assert.True(who!.SproutSeen > 0); // 싹을 본 사람이 알아냈다
        Assert.True(w.People.Discoveries.ContainsKey(sow.Index));
    }

    /// <summary>풀 먹는 짐승은 지키는 사람이 없는 밭을 먹는다.</summary>
    [Fact]
    public void HerbivoresEatUnguardedField()
    {
        var w = GameWorld.Create(Seed, new WorldConfig { DotCount = 0 });
        int cell = PeopleSamples.Home(w.Planet);
        var life = w.People.Life(cell);
        var spot = PeopleSamples.SpawnSpots(life, 1)[0];
        w.Apply(new Intervention(0, Intervention.Kinds.SpawnPerson, new long[] { cell, spot.X, spot.Y }));
        w.AdvanceTo(1);
        var herd = life.Herds.Values.First(h => !w.People.Fauna.Animals[h.Species].Predator);
        // 사람에게서 먼 곳에 자라는 밭 여럿, 그 곁에 무리
        int here = spot.Y * HexFrame.Size + spot.X, center = -1;
        for (int t = 0; t < HexFrame.TileCount && center < 0; t += 37)
            if (life.Walk[t] && HexFrame.BorderDistance(t) > 20 && Math.Max(Math.Abs(t % 150 - here % 150), Math.Abs(t / 150 - here / 150)) > 40 && w.People.GrowAt(life, t) > 20) center = t;
        Assert.True(center >= 0);
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int t = center + dy * 150 + dx;
                if (!life.Walk[t]) continue;
                life.AddPlot(new Plot { Tile = t, Stage = PlotStage.Growing, Tilled = true, Crop = I("grain"), Stamp = w.Now, LastTend = w.Now, Due = w.Now + 30 * GameTime.TicksPerDay });
            }
        herd.Motion.SetStill(w.Now, (center % 150 + 3) * 1000 + 500, (center / 150) * 1000 + 500);
        herd.Prev.CopyFrom(herd.Motion);
        w.AdvanceTo(w.Now + 6 * GameTime.TicksPerDay);
        var st = w.People.Stats;
        _out.WriteLine($"짐승이 먹은 밭 {st.FieldEaten} · 망침 {st.FieldsRuined}");
        Assert.True(st.FieldEaten > 0);
        Assert.Contains(life.Plots.Values, q => q.Damage > 0 || q.Stage == PlotStage.Empty);
    }

    /// <summary>사람이 없는 지방의 밭은 멈추고(잠듦), 깨어나면 흐른 날만큼 맞춰진다 — 익어 떨어지고, 빈 밭은 잡풀이 덮는다.</summary>
    [Fact]
    public void DormantFieldsCatchUp()
    {
        var w = GameWorld.Create(Seed, new WorldConfig { DotCount = 0 });
        int cell = PeopleSamples.Home(w.Planet);
        var life = w.People.Life(cell);
        var spot = PeopleSamples.SpawnSpots(life, 1)[0];
        w.Apply(new Intervention(0, Intervention.Kinds.SpawnPerson, new long[] { cell, spot.X, spot.Y }));
        w.AdvanceTo(1);
        var p0 = w.People.All[0];
        int t0 = spot.Y * 150 + spot.X + 3;
        for (int k = 0; k < 4; k++)
            life.AddPlot(new Plot { Tile = t0 + k * 150, Stage = PlotStage.Sown, Tilled = true, Crop = I("grain"), Stamp = w.Now, LastTend = w.Now, Due = w.Now + 4 * GameTime.TicksPerDay });
        w.People.Kill(p0, DeathCause.OldAge);
        w.AdvanceTo(w.Now + PeopleSim.SleepAfter + 2 * GameTime.TicksPerDay);
        Assert.False(w.People.Provinces.ContainsKey(cell)); // 잠들었다
        var d = w.People.Dormant[cell];
        Assert.Equal(4, d.Plots.Count);
        var stages = d.Plots.Select(q => q.Stage).ToList();
        // 오래 둔다(저장 → 불러오기도 거쳐) — 잠든 동안은 아무것도 바뀌지 않는다
        w.AdvanceTo(w.Now + 120 * GameTime.TicksPerDay);
        Assert.Equal(stages, w.People.Dormant[cell].Plots.Select(q => q.Stage).ToList());
        var loaded = SaveGame.FromBytes(SaveGame.ToBytes(w));
        Assert.Equal(SaveGame.Fingerprint(w), SaveGame.Fingerprint(loaded));
        // 누가 들어오면 깨어나 흐른 날만큼: 익었다가 떨어지고 → 빈 밭 → 잡풀이 덮어 사라졌다
        foreach (var g in new[] { w, loaded })
        {
            var sp = PeopleSamples.SpawnSpots(g.People.Life(cell), 1)[0];
            g.Apply(new Intervention(g.Now, Intervention.Kinds.SpawnPerson, new long[] { cell, sp.X, sp.Y }));
            g.AdvanceTo(g.Now + 1);
        }
        Assert.True(w.People.Provinces.ContainsKey(cell));
        _out.WriteLine($"깨어난 뒤 밭 {w.People.Provinces[cell].Plots.Count} · 떨어짐 {w.People.Stats.FieldsFallen} · 싹이 안 틈 {w.People.Stats.SeedsFailed} · 잡풀이 덮음 {w.People.Stats.FieldsWeededOver}");
        Assert.Empty(w.People.Provinces[cell].Plots);
        var st = w.People.Stats;
        Assert.True(st.FieldsFallen + st.SeedsFailed >= 4 && st.FieldsFallen >= 1 && st.FieldsWeededOver >= 4); // (싹이 트지 않는 타일에 묻은 씨는 익지 않고 빈 밭이 된다)
        Assert.Equal(SaveGame.Fingerprint(w), SaveGame.Fingerprint(loaded));
    }

    /// <summary>같은 시드 → 같은 결과 · CPU 1 = 4 (밭이 있는 세계) · 저장 → 불러오기 → 계속(자라는 밭 포함).</summary>
    [Fact]
    public void FarmsSameWithThreadsAndSaveLoad()
    {
        var one = FarmFamilies(3, 2, 30, threads: 1);
        var four = FarmFamilies(3, 2, 30, threads: 4);
        Assert.Equal(SaveGame.Fingerprint(one), SaveGame.Fingerprint(four));
        Assert.True(one.People.Provinces.Values.Sum(l => l.Plots.Values.Count(q => q.Stage is PlotStage.Sprout or PlotStage.Growing)) > 0); // 자라는 밭이 있다
        one.AdvanceTo(one.Now + 1_111);
        var loaded = SaveGame.FromBytes(SaveGame.ToBytes(one));
        Assert.Equal(SaveGame.Fingerprint(one), SaveGame.Fingerprint(loaded));
        long end = one.Now + 12 * GameTime.TicksPerDay + 17;
        one.AdvanceTo(end); loaded.AdvanceTo(end);
        Assert.Equal(SaveGame.Fingerprint(one), SaveGame.Fingerprint(loaded));
        Assert.True(one.People.Stats.HarvestItems > 0 || loaded.People.Stats.HarvestItems > 0);
    }
}
