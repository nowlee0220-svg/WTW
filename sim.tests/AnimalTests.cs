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

/// <summary>(E5b) 짐승 · 사냥 · 털옷.</summary>
[Trait("Category", "Fast")]
public class AnimalTests
{
    private const ulong Seed = 20261016;
    private static readonly CraftData C = CraftData.Default;
    private static readonly AnimalData A = AnimalData.Default;
    private readonly ITestOutputHelper _out;
    public AnimalTests(ITestOutputHelper o) { _out = o; }

    private static int I(string id) => C.ItemIndex(id);
    private static int Act(string id) => C.ActionIndex(id);
    private static int[] P(string id) => C.ItemProps[I(id)];

    private static (GameWorld W, int Cell) World(Func<Planet.Planet, int> where)
    {
        var w = GameWorld.Create(Seed, new WorldConfig { DotCount = 0 });
        return (w, where(w.Planet));
    }

    private static Person Spawn(GameWorld w, int cell, int k = 0, bool family = false)
    {
        var spot = PeopleSamples.SpawnSpots(w.People.Life(cell), k + 1)[k];
        int before = w.People.All.Count;
        w.Apply(new Intervention(w.Now, family ? Intervention.Kinds.SpawnFamily : Intervention.Kinds.SpawnPerson, new long[] { cell, spot.X, spot.Y }));
        return w.People.All[before];
    }

    [Fact]
    public void DataLoads()
    {
        Assert.InRange(A.Animals.Count, 10, 15);
        Assert.Contains(A.Animals, a => a.Predator);
        Assert.Contains(A.Animals, a => a.Size == 1);
        Assert.Contains(A.Animals, a => a.Size == 3);
        foreach (var a in A.Animals)
        {
            Assert.NotEmpty(a.Yields);
            Assert.True(a.MeatYield > 0, a.Id);
            Assert.Contains(a.Biome1000, v => v > 0);
        }
        Assert.True(A.FishItem >= 0);
        // 짐승의 이름 낱말이 말 데이터에 있다
        foreach (var a in A.Animals) Assert.True(LanguageData.Default.ConceptIndex(a.Id) >= 0, a.Id);
    }

    /// <summary>처음 열린 지방: 먹이(생물군계 · 사냥감 자원)만큼 짐승 — 땅마다 다른 짐승.</summary>
    [Fact]
    public void FirstProvinceHasAnimalsByFood()
    {
        var (w, warm) = World(p => PeopleSamples.Home(p));
        int cold = PeopleSamples.Cold(w.Planet);
        var lw = w.People.Life(warm); var lc = w.People.Life(cold);
        var cw = w.People.AnimalCounts(lw); var cc = w.People.AnimalCounts(lc);
        _out.WriteLine($"따뜻한 땅({PlanetNames.Of(w.Planet.Biome[warm])}): " + string.Join(", ", A.Animals.Where(a => cw[a.Index] > 0).Select(a => $"{a.Name} {cw[a.Index]}/{lw.AnimalCap[a.Index]}")));
        _out.WriteLine($"툰드라: " + string.Join(", ", A.Animals.Where(a => cc[a.Index] > 0).Select(a => $"{a.Name} {cc[a.Index]}/{lc.AnimalCap[a.Index]}")));
        Assert.True(lw.Herds.Count >= 5 && lc.Herds.Count >= 3);
        for (int s = 0; s < A.Animals.Count; s++) { Assert.True(cw[s] <= lw.AnimalCap[s]); Assert.True(cc[s] <= lc.AnimalCap[s]); }
        Assert.True(cc[A.Index("reindeer")] > 0, "툰드라에 순록");
        Assert.Equal(0, cw[A.Index("reindeer")]);
        Assert.Equal(0, cc[A.Index("lion")]);
        Assert.True(lc.FishCap >= 0);
    }

    /// <summary>같은 시드 → 같은 결과 · CPU 1 = 4 (짐승이 있는 세계 — 사냥하고 경계를 넘는다).</summary>
    [Fact]
    public void AnimalsSameWithThreads()
    {
        var one = SpeedTests.Families(5, 3, 40, threads: 1);
        var four = SpeedTests.Families(5, 3, 40, threads: 4);
        var st = one.People.Stats;
        _out.WriteLine($"무리 판단 {st.HerdEvents} · 사냥 {st.HuntSuccess}/{st.HuntsTried} · 물고기 {st.FishCaught} · 덮침 {st.BeastAttacks}");
        Assert.True(st.HerdEvents > 1000);
        Assert.True(st.HuntsTried > 0);
        Assert.Equal(SaveGame.Fingerprint(one), SaveGame.Fingerprint(four));
        Assert.Equal(one.People.Stats.HerdEvents, four.People.Stats.HerdEvents);
    }

    /// <summary>저장 → 불러오기 → 계속 = 끊김 없이(무리가 움직이는 중에, 맞추는 시각 사이).</summary>
    [Fact]
    public void SaveLoadContinueWithAnimals()
    {
        var w = SpeedTests.Families(3, 3, 12);
        w.AdvanceTo(w.Now + 1_234);
        bool Walking() => w.People.Provinces.Values.Any(l => l.Herds.Values.Any(h => h.Motion.Arrive > w.Now));
        for (int k = 0; k < 200 && !Walking(); k++) w.AdvanceTo(w.Now + 1_111);
        Assert.True(Walking()); // 걷는 무리가 있다(맞추는 시각 사이)
        var loaded = SaveGame.FromBytes(SaveGame.ToBytes(w));
        Assert.Equal(SaveGame.Fingerprint(w), SaveGame.Fingerprint(loaded));
        long end = w.Now + 15 * GameTime.TicksPerDay + 17;
        w.AdvanceTo(end); loaded.AdvanceTo(end);
        Assert.Equal(SaveGame.Fingerprint(w), SaveGame.Fingerprint(loaded));
    }

    /// <summary>사람이 없는 지방의 짐승은 멈춰 있고(무리 판단 0), 깨어나면 흐른 날만큼 한 번에 늘어 있다 — 저장 → 불러오기를 거쳐도 같다.</summary>
    [Fact]
    public void DormantAnimalsStopThenCatchUp()
    {
        var (w, cell) = World(p => PeopleSamples.Home(p));
        var p0 = Spawn(w, cell);
        var life = w.People.Life(cell);
        // 짐승을 크게 줄여 둔다(마구 사냥한 것처럼)
        foreach (var h in life.Herds.Values) h.Count = 1;
        int start = w.People.AnimalCounts(life).Sum();
        w.People.Kill(p0, DeathCause.OldAge);
        w.AdvanceTo(w.Now + PeopleSim.SleepAfter + 2 * GameTime.TicksPerDay);
        Assert.False(w.People.Provinces.ContainsKey(cell)); // 잠들었다
        var d = w.People.Dormant[cell];
        int asleep = d.Herds.Sum(h => h.Count);
        long events = w.People.Stats.HerdEvents;
        w.AdvanceTo(w.Now + 200 * GameTime.TicksPerDay);
        Assert.Equal(events, w.People.Stats.HerdEvents); // 멈춰 있다
        Assert.Equal(asleep, w.People.Dormant[cell].Herds.Sum(h => h.Count));
        Assert.All(w.People.Dormant[cell].Herds, h => Assert.Equal(0, h.PendingHandle));
        // 저장 → 불러오기 → 깨우기 = 그대로 깨우기
        var copy = SaveGame.FromBytes(SaveGame.ToBytes(w));
        var q = Spawn(w, cell); var q2 = Spawn(copy, cell);
        int after = w.People.AnimalCounts(w.People.Life(cell)).Sum();
        _out.WriteLine($"줄여 둔 뒤 {start} · 잠들 때 {asleep} · 200일 뒤 깨어나서 {after}");
        Assert.True(after > asleep + 10, $"{asleep} → {after}");
        w.AdvanceTo(w.Now + 3 * GameTime.TicksPerDay); copy.AdvanceTo(w.Now);
        Assert.Equal(SaveGame.Fingerprint(w), SaveGame.Fingerprint(copy));
        _ = q; _ = q2;
    }

    /// <summary>너무 많이 잡으면 줄고(살 수 있는 수 아래), 다시 는다 — 한 종이 다 사라져도 가끔 이웃에서 들어온다.</summary>
    [Fact]
    public void DepletedAnimalsRecover()
    {
        var (w, cell) = World(p => PeopleSamples.Home(p));
        var life = w.People.Life(cell);
        var ps = w.People;
        int deer = A.Index("deer");
        int cap = life.AnimalCap[deer];
        Assert.True(cap > 4);
        // 사슴을 거의 다 잡은 것처럼: 한 무리 2마리만 남긴다
        var herds = life.Herds.Values.Where(h => h.Species == deer).ToList();
        foreach (var h in herds.Skip(1)) ps.RemoveHerd(life, h);
        herds[0].Count = 2;
        var rng = Core.RngStreams.Create(Seed, "test", 0);
        int[] byYear = new int[6];
        for (int day = 0; day < 6 * GameTime.DaysPerYear; day++)
        {
            ps.PopulationDay(life, rng, catchUp: true);
            if (day % GameTime.DaysPerYear == 0) byYear[day / GameTime.DaysPerYear] = ps.AnimalCounts(life)[deer];
        }
        int end = ps.AnimalCounts(life)[deer];
        _out.WriteLine($"사슴(살 수 있는 수 {cap}): " + string.Join(" → ", byYear) + $" → {end}");
        Assert.True(end > 2 && end <= cap);
        // 다 사라진 종도 이웃에서 들어온다
        foreach (var h in life.Herds.Values.Where(h => h.Species == deer).ToList()) ps.RemoveHerd(life, h);
        for (int day = 0; day < 400 && ps.AnimalCounts(life)[deer] == 0; day++) ps.PopulationDay(life, rng, catchUp: true);
        Assert.True(ps.AnimalCounts(life)[deer] > 0);
    }

    /// <summary>사냥하면 실제로 짐승이 준다(잡은 수만큼) · 잡은 것은 손질해서 들고 간다.</summary>
    [Fact]
    public void HuntingKillsAndButchers()
    {
        var w = SpeedTests.Families(2, 3, 30);
        var st = w.People.Stats;
        _out.WriteLine($"나섬 {st.HuntsStarted} · 몰아 봄 {st.HuntsTried} · 잡음 {st.HuntSuccess} · 손질 {st.Butchered} · 물고기 {st.FishCaught}");
        Assert.True(st.HuntSuccess > 0);
        Assert.True(st.Butchered > 0);
        Assert.Equal(st.HuntSuccess, st.KillsBySpecies.Sum());
        // 손질해서 나온 것(날고기 · 뼈 · 가죽)을 누군가 들었거나 먹었다 — 사람의 전기에 "잡았다"
        Assert.Contains(w.People.All, p => p.Bio.Any(b => b.Kind == BioKind.Hunted));
    }

    /// <summary>털옷을 입으면 툰드라의 추운 밤을 견딘다 — 입지 않은 사람은 얼어 간다(불도 동굴도 없이).</summary>
    [Fact]
    public void FurCoatSurvivesTundraNight()
    {
        var (w, cell) = World(p => PeopleSamples.Cold(p));
        var bare = Spawn(w, cell, 0);
        var coat = Spawn(w, cell, 1);
        coat.Worn = I("fur_coat"); coat.WornWear = 1000; coat.WornFeet = I("fur_boots"); coat.FeetWear = 1000;
        var life = w.People.Life(cell);
        _out.WriteLine($"툰드라 밤 기온 {(life.Mean10 - life.Range10 / 2) / 10.0:0.0}°C · 털옷의 따뜻함 +{w.People.ClothWarm(coat) / 10.0:0}°C");
        w.AdvanceTo(w.Now + GameTime.TicksPerDay + 100);
        _out.WriteLine($"하루 뒤: 맨몸 — 죽음 {bare.Dead}({bare.Cause}) 저체온 {bare.Hypothermia / 1000}% · 털옷 — 죽음 {coat.Dead} 저체온 {coat.Hypothermia / 1000}%");
        Assert.False(coat.Dead);
        Assert.True(bare.Dead || bare.Hypothermia > coat.Hypothermia + 20_000);
        Assert.True(coat.Hypothermia < 60_000);
    }

    /// <summary>날고기는 배탈이 나고(독), 익히면 괜찮다 — 익히기는 E4 규칙(굽기 · 맨불)에서.</summary>
    [Fact]
    public void RawMeatUpsetsCookedDoesNot()
    {
        Assert.True(C.Prop(I("raw_meat"), C.P_poison) > 0);
        var o = C.Judge(Act("roast"), P("raw_meat"), null, null, 1, 0, 0);
        Assert.True(o.Success);
        Assert.Equal(I("cooked_meat"), o.Rule!.Result);
        Assert.Equal(0, C.Prop(I("cooked_meat"), C.P_poison));
        Assert.True(C.Prop(I("cooked_meat"), C.P_edible) > C.Prop(I("berries"), C.P_edible)); // 열매보다 배가 찬다
        // 생선은 생선대로(규칙이 속성으로 가린다), 불이 없으면 익지 않는다
        Assert.Equal(I("cooked_fish"), C.Judge(Act("roast"), P("raw_fish"), null, null, 1, 0, 0).Rule!.Result);
        Assert.False(C.Judge(Act("roast"), P("raw_meat"), null, null, 0, 0, 0).Success);
        // 실제로 먹어 보기: 먹을 것이 없는 땅에서 날고기만 든 사람 · 익힌 고기만 든 사람
        int Upsets(string item)
        {
            var (w, cell) = World(p => PeopleSamples.Home(p));
            var life = w.People.Life(cell);
            for (int t = 0; t < HexFrame.TileCount; t++) if (life.HarvestAt(t) is { Food: true }) life.Stocks[t] = new TileStock { Stock = 0, Stamp = long.MaxValue / 4 };
            var p = Spawn(w, cell);
            long n = 0;
            for (int d = 0; d < 6; d++)
            {
                p.Carry.RemoveAll(c => c.Item == I(item)); p.AddCarry(I(item), 12); p.Food = Math.Min(p.Food, 20_000);
                w.AdvanceTo(w.Now + GameTime.TicksPerDay);
                if (p.Dead) break;
            }
            n = w.People.Stats.BadStomachs;
            return (int)n;
        }
        int raw = Upsets("raw_meat"), cooked = Upsets("cooked_meat");
        _out.WriteLine($"배탈: 날고기 {raw} · 익힌 고기 {cooked}");
        Assert.True(raw > 0);
        Assert.Equal(0, cooked);
    }

    /// <summary>사냥 도구 · 털옷은 정답표가 아니라 속성에서 나온다: 같은 속성의 다른 것도 같고, 속성이 다르면 안 된다.</summary>
    [Fact]
    public void ToolsAndClothesComeFromProperties()
    {
        // 돌 날 + 막대 + 끈(꼬기) → 돌창
        var spear = C.Judge(Act("twist"), P("flake"), P("stick"), P("cord"), 0, 0, 0);
        Assert.True(spear.Success); Assert.Equal(I("stone_spear"), spear.Rule!.Result);
        // 힘줄도 끈처럼 묶는다(질기고 실이 된다)
        Assert.True(C.Judge(Act("twist"), P("flake"), P("stick"), P("sinew"), 0, 0, 0).Success);
        // 지어낸 "날 선 돌"(속성만 같게) — 같은 결과
        var fake = (int[])P("flake").Clone();
        Assert.Equal(I("stone_spear"), C.Judge(Act("twist"), fake, P("stick"), P("cord"), 0, 0, 0).Rule!.Result);
        // 통나무(무겁다)에는 못 묶는다 · 풀(질기지 않다)로는 못 묶는다
        Assert.False(C.Judge(Act("twist"), P("flake"), P("wood"), P("cord"), 0, 0, 0).Success);
        var grass = C.Judge(Act("twist"), P("flake"), P("stick"), P("grass"), 0, 0, 0);
        Assert.False(grass.Success); Assert.Equal("tool", grass.FailedKey);
        // 막대를 불에 그을리면 뾰족한 막대(찌른다)
        Assert.Equal(I("spear"), C.Judge(Act("roast"), P("stick"), null, null, 1, 0, 0).Rule!.Result);
        Assert.True(C.Prop(I("spear"), C.P_pierces) > 0);
        // 가죽 일: 날가죽 --긁기(날)--> 긁은 가죽 --말리기--> 마른 가죽 --그을리기(불) 또는 주무르기--> 무두질한 가죽 --꿰매기(바늘 · 힘줄)--> 털가죽 옷
        Assert.Equal(I("scraped_hide"), C.Judge(Act("scrape"), P("raw_hide"), null, P("scraper"), 0, 0, 0).Rule!.Result);
        var bare = C.Judge(Act("scrape"), P("raw_hide"), null, null, 0, 0, 0);
        Assert.False(bare.Success); Assert.Equal("tool", bare.FailedKey);
        var rotten = C.Judge(Act("dry"), P("raw_hide"), null, null, 0, 0, 0); // 긁지 않고 말리면 썩는다
        Assert.False(rotten.Success); Assert.Contains("썩", rotten.Partial!.Text);
        Assert.Equal(I("dried_hide"), C.Judge(Act("dry"), P("scraped_hide"), null, null, 0, 0, 0).Rule!.Result);
        Assert.Equal(I("tanned_hide"), C.Judge(Act("smoke"), P("dried_hide"), null, null, 1, 0, 0).Rule!.Result);
        Assert.Equal(I("tanned_hide"), C.Judge(Act("twist"), P("dried_hide"), null, null, 0, 0, 0).Rule!.Result);
        Assert.Equal(I("bone_needle"), C.Judge(Act("grind"), P("bone"), P("granite"), null, 0, 0, 0).Rule!.Result);
        var coat = C.Judge(Act("sew"), P("tanned_hide"), P("sinew"), P("bone_needle"), 0, 0, 0);
        Assert.True(coat.Success); Assert.Equal(I("fur_coat"), coat.Rule!.Result);
        var noNeedle = C.Judge(Act("sew"), P("tanned_hide"), P("sinew"), null, 0, 0, 0);
        Assert.False(noNeedle.Success); Assert.Equal("tool", noNeedle.FailedKey); Assert.Contains("바늘", noNeedle.Partial!.Hint);
        Assert.Equal(I("fur_boots"), C.Judge(Act("sew"), P("dried_hide"), P("sinew"), P("bone_needle"), 0, 0, 0).Rule!.Result);
        // 털옷은 입는 것 · 따뜻하다
        Assert.Equal(1, C.WearSlot(I("fur_coat"))); Assert.Equal(2, C.WearSlot(I("fur_boots")));
        Assert.True(C.Prop(I("fur_coat"), C.P_warm) > C.Prop(I("raw_hide"), C.P_warm));
    }

    /// <summary>불은 포식자를 쫓는다: 혼자 자는 사람을 노리다가도 곁에 불이 타면 노리지 않는다.</summary>
    [Fact]
    public void FireKeepsPredatorsAway()
    {
        var (w, cell) = World(p => PeopleSamples.Cold(p));
        var life = w.People.Life(cell);
        var q = Spawn(w, cell);
        q.Asleep = true;
        int t = q.Tile(w.Now);
        q.Position(w.Now, out int qx, out int qy);
        Assert.Same(q, w.People.Vulnerable(life, qx + 3000, qy, 30, t, night: true));
        life.LightFire(t, 1, 10 * GameTime.TicksPerHour, w.Now, q.Id);
        Assert.Null(w.People.Vulnerable(life, qx + 3000, qy, 30, t, night: true));
    }
}
