using Wtw.Sim.People;
using Wtw.Sim.Persistence;
using Wtw.Sim.Time;
using Wtw.Sim.World;
using Xunit;
using Xunit.Abstractions;

namespace Wtw.Sim.Tests;

/// <summary>(E6b) 굶주림과 흉년: 몸에 쌓인 것(야윔) · 야위면 아이가 덜 생김 · 약한 사람부터 굶어 죽음 · 들에도 그해 날씨(넓은 가뭄) · 짐승이 열매를 먹음 · 저장.</summary>
[Trait("Category", "Fast")]
public class HungerTests
{
    private readonly ITestOutputHelper _out;
    public HungerTests(ITestOutputHelper o) { _out = o; }

    private static List<Person> Alive(GameWorld w) => w.People.All.Where(q => !q.Dead).ToList();

    /// <summary>한 시간씩 나아가며 산 사람마다 set 을 부른다(배부름 · 몸을 시험이 정해 둔다).</summary>
    private static void Hold(GameWorld w, long days, Action<Person> set)
    {
        long end = w.Now + days * GameTime.TicksPerDay;
        while (w.Now < end)
        {
            foreach (var q in Alive(w)) set(q);
            w.AdvanceTo(Math.Min(end, w.Now + GameTime.TicksPerHour));
        }
    }

    [Fact]
    public void UnderfedBodyShrinksWellFedRefills()
    {
        var w = SpeedTests.Families(1, 2, 1);
        var adults = Alive(w).Where(q => q.Stage(w.Now) == LifeStage.Adult).ToList();
        Assert.NotEmpty(adults);
        int before = adults.Min(q => q.Body);
        Hold(w, 5, q => q.Food = 0); // 배부름이 바닥난 채 닷새
        int after = adults.Where(q => !q.Dead).Max(q => q.Body);
        _out.WriteLine($"굶음: 몸 {before / 1e6:0.0}% → {after / 1e6:0.0}%");
        Assert.True(after < before - 3_000_000, "닷새 굶었는데 몸이 별로 줄지 않았다");
        Assert.All(adults, q => Assert.Equal(PeopleSim.MalOf(q.Body), q.Malnutrition));
        foreach (var q in adults) q.Body = 35_000_000;
        Hold(w, 5, q => { if (q.Food < 80_000) q.Food = Person.Full; }); // 넉넉히 닷새
        int refilled = adults.Where(q => !q.Dead).Min(q => q.Body);
        _out.WriteLine($"넉넉함: 35% → {refilled / 1e6:0.0}%");
        Assert.True(refilled > 36_000_000, "넉넉히 먹었는데 몸이 다시 차지 않았다");
        Assert.True(w.People.Stats.BodyRefilled > 0 && w.People.Stats.BodyDrawn > 0);
    }

    [Fact]
    public void ThinWomenConceiveLess()
    {
        Assert.Equal(1000, PeopleSim.Fertility(PeopleSim.BodyNormal));
        Assert.Equal(0, PeopleSim.Fertility(PeopleSim.BodyThin2));
        Assert.True(PeopleSim.Fertility(36_000_000) < PeopleSim.Fertility(42_000_000));
        long Conceptions(int body)
        {
            var w = SpeedTests.Families(4, 3, 1);
            Hold(w, 40, q => { q.Food = Math.Max(q.Food, 60_000); if (q.Stage(w.Now) >= LifeStage.Adult) q.Body = body; });
            _out.WriteLine($"몸 {body / 1e6:0}%: 아이 가짐 {w.People.Stats.Conceptions} · 배를 곯아 갖지 않음 {w.People.Stats.ConceiveHungry}");
            return w.People.Stats.Conceptions;
        }
        long fed = Conceptions(PeopleSim.BodyNormal), thin = Conceptions(31_000_000);
        Assert.True(fed >= 3, "넉넉한 세계에서 아이가 거의 생기지 않았다(시험이 약하다)");
        Assert.True(thin * 2 < fed, $"야위었는데 아이가 덜 생기지 않았다: {thin} vs {fed}");
    }

    [Fact]
    public void LongHungerStarvesWeakFirst()
    {
        var w = SpeedTests.Families(3, 3, 1);
        Assert.Contains(Alive(w), q => q.Stage(w.Now) <= LifeStage.Child);
        foreach (var q in Alive(w)) q.Body = 12_000_000;
        Hold(w, 40, q => q.Food = 0);
        var dead = w.People.All.Where(q => q.Dead).ToList();
        var first = dead.OrderBy(q => q.DeathTick).ThenBy(q => q.Id).Take(1).ToList();
        _out.WriteLine($"죽음 {dead.Count}: " + string.Join(", ", dead.OrderBy(q => q.DeathTick).Take(12).Select(q => $"{q.Stage(q.DeathTick)} {(q.DeathTick - GameTime.TicksPerDay) / (double)GameTime.TicksPerDay:0.0}일")));
        Assert.NotEmpty(first);
        Assert.True(first[0].Stage(first[0].DeathTick) <= LifeStage.Child, "처음 굶어 죽은 사람이 아이 · 젖먹이가 아니다");
        Assert.All(dead, q => Assert.Equal(DeathCause.Starvation, q.Cause));
        // 같은 몸에서 어른은 아이보다 오래 버틴다
        var kid = dead.Where(q => q.Stage(q.DeathTick) == LifeStage.Child).Select(q => q.DeathTick).DefaultIfEmpty(long.MaxValue).Min();
        var adult = dead.Where(q => q.Stage(q.DeathTick) == LifeStage.Adult).Select(q => q.DeathTick).DefaultIfEmpty(long.MaxValue).Min();
        Assert.True(kid < adult, "어른이 아이보다 먼저 굶어 죽었다");
    }

    [Fact]
    public void DroughtYearWildFruitAndNeighborsShare()
    {
        var w = GameWorld.Create(20261016, new WorldConfig { DotCount = 0 });
        var ps = w.People;
        var pl = w.Planet;
        int cell = PeopleSamples.Home(pl);
        long dry = -1, good = -1;
        for (long y = 0; y < 200 && (dry < 0 || good < 0); y++)
        {
            int l = ps.YearLuck(cell, y);
            if (l < 50 && dry < 0) dry = y;
            if (l >= 100 && good < 0) good = y;
        }
        Assert.True(dry >= 0 && good >= 0);
        // 들의 열매 · 덩이뿌리는 밭과 같은 그해 날씨 · 도토리는 거기에 해거리
        Assert.Equal(ps.YearLuck(cell, dry), ps.GrowPct(cell, 1, dry));
        Assert.Equal(ps.YearLuck(cell, dry) * ps.MastPct(cell, dry) / 100, ps.GrowPct(cell, 2, dry));
        var life = ps.Life(cell);
        int tile = life.ForageTiles().First(t => life.HarvestAt(t)!.GrowClass == 1 && life.HarvestAt(t)!.Stock >= 2);
        var h = life.HarvestAt(tile)!;
        // 그해 다 따고 스무 날 뒤에 다시 열린 몫: 가뭄 해 < 좋은 해 · 한 그루에 열리는 몫도 적다
        int Regrown(long year)
        {
            long t0 = year * GameTime.TicksPerYear + GameTime.TicksPerDay;
            int got = life.Take(tile, 100, t0);
            life.Stocks.TryGetValue(tile, out var st);
            _out.WriteLine($"해 {year}: 딴 {got} · 남은 {st.Stock} · 시계 {st.Stamp} → {ps.GrowClock(life, h.GrowClass, t0 + 20 * GameTime.TicksPerDay)} · 다시 {h.RegrowTicks} · {h.ObjectName}");
            return life.StockAt(tile, t0 + 38 * GameTime.TicksPerDay);
        }
        // (시각이 앞으로만 흐르게 이른 해부터)
        int rd, rg;
        if (dry < good) { rd = Regrown(dry); rg = Regrown(good); } else { rg = Regrown(good); rd = Regrown(dry); }
        _out.WriteLine($"가뭄 {ps.YearLuck(cell, dry)}%: {rd} · 좋은 해 {ps.YearLuck(cell, good)}%: {rg} · 한 그루 몫 {life.CapAt(h, dry * GameTime.TicksPerYear)} vs {life.CapAt(h, good * GameTime.TicksPerYear)}");
        Assert.True(rd < rg);
        Assert.True(life.CapAt(h, dry * GameTime.TicksPerYear) < life.CapAt(h, good * GameTime.TicksPerYear));
        // 자라는 시계: 한 해에 그해 날씨만큼 간다
        long g0 = ps.GrowClock(life, 1, dry * GameTime.TicksPerYear), g1 = ps.GrowClock(life, 1, (dry + 1) * GameTime.TicksPerYear);
        Assert.Equal(ps.YearLuck(cell, dry) * GameTime.TicksPerYear / 100, g1 - g0);
        // 거꾸로: 시계가 g 에 닿는 가장 이른 시각
        long gq = ps.GrowClock(life, 1, dry * GameTime.TicksPerYear + 5000), ti = ps.GrowClockInverse(life, 1, gq, dry * GameTime.TicksPerYear);
        Assert.True(ps.GrowClock(life, 1, ti) >= gq && ps.GrowClock(life, 1, ti - 1) < gq);
        long gn = ps.GrowClock(life, 1, (dry + 3) * GameTime.TicksPerYear + 777), tn = ps.GrowClockInverse(life, 1, gn, dry * GameTime.TicksPerYear);
        Assert.True(ps.GrowClock(life, 1, tn) >= gn && ps.GrowClock(life, 1, tn - 1) < gn);
        // 넓은 가뭄: 이웃 땅은 대개 같은 날씨 조각 · 그러나 행성 전체가 한 조각은 아니다
        int same = 0, pairs = 0; var regions = new HashSet<ulong>();
        for (int c = 0; c < pl.CellCount; c += 37)
        {
            if (!pl.IsLand(c)) continue;
            foreach (int nb in pl.Grid.Neighbors(c))
                for (long y = 0; y < 5; y++) { pairs++; if (ps.WeatherRegionId(c, y) == ps.WeatherRegionId(nb, y)) same++; }
            regions.Add(ps.WeatherRegionId(c, 0));
        }
        _out.WriteLine($"이웃이 같은 조각: {same}/{pairs} · 조각 수(해 0, 표본) {regions.Count}");
        Assert.True(same * 100 > pairs * 70, "이웃 땅이 대개 같은 날씨를 겪지 않는다");
        Assert.True(regions.Count > 20, "날씨 조각이 너무 크다");
    }

    [Fact]
    public void AnimalsEatWildFruit()
    {
        var w = SpeedTests.Families(1, 2, 6);
        var ps = w.People;
        var life = ps.Provinces.Values.First(l => l.People.Any(q => !q.Dead));
        var here = life.People.Where(q => !q.Dead).Select(q => q.Tile(w.Now)).ToList();
        int far = life.Stocks.Count(kv => here.All(t => Math.Max(Math.Abs(t % 150 - kv.Key % 150), Math.Abs(t / 150 - kv.Key / 150)) > 50));
        _out.WriteLine($"짐승이 먹음 {ps.Stats.AnimalForage} / {ps.Stats.AnimalForageWant} · 사람에게서 먼 딴 자리 {far}");
        Assert.True(ps.Stats.AnimalForage > 0);
        Assert.True(far > 0, "사람이 없는 곳의 열매가 줄지 않았다");
        // 짐승이 없으면 먹지 않는다
        var w2 = SpeedTests.Families(1, 2, 1);
        foreach (var l in w2.People.Provinces.Values) foreach (var id in l.Herds.Items.Select(kv => kv.Key).ToList()) l.Herds.Remove(id);
        long a0 = w2.People.Stats.AnimalForage;
        w2.AdvanceTo(w2.Now + 3 * GameTime.TicksPerDay);
        Assert.Equal(a0, w2.People.Stats.AnimalForage);
    }

    [Fact]
    public void ThinWorldSaveLoadContinuesAcrossYear()
    {
        var a = SpeedTests.Families(2, 3, 1);
        // 야윈 사람 · 아이 밴 사람이 있는 채, 해가 바뀌기 사흘 전에 저장 → 해를 넘겨 견준다(들의 몫 · 자라는 시계 · 몸)
        a.AdvanceTo(GameTime.TicksPerYear - 3 * GameTime.TicksPerDay + 123);
        int k = 0;
        foreach (var q in Alive(a)) q.Body = (k++ % 3) switch { 0 => 25_000_000, 1 => 15_000_000, _ => q.Body };
        var b = SaveGame.FromBytes(SaveGame.ToBytes(a));
        Assert.Empty(SaveGame.DiffSections(a, b));
        for (int day = 1; day <= 6; day++)
        {
            long t = a.Now + GameTime.TicksPerDay;
            a.AdvanceTo(t); b.AdvanceTo(t);
            var diff = SaveGame.DiffSections(a, b);
            Assert.True(diff.Count == 0, $"불러온 뒤 {day}일째 갈라짐: {string.Join(", ", diff.Take(20))}");
        }
        Assert.Contains(Alive(a), q => PeopleSim.ThinStage(q) >= 2);
    }
}
