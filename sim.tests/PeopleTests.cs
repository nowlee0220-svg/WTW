using Wtw.Sim.Data;
using Wtw.Sim.People;
using Wtw.Sim.Persistence;
using Wtw.Sim.Planet;
using Wtw.Sim.Provinces;
using Wtw.Sim.Time;
using Wtw.Sim.World;
using Xunit;

namespace Wtw.Sim.Tests;

/// <summary>E3 사람. 행성은 시드 + 설정이 같으면 한 벌을 나눠 쓴다(PlanetCache).</summary>
[Trait("Category", "Fast")]
public class PeopleTests
{
    private const ulong Seed = 20261016;
    private static readonly WorldConfig Cfg = new() { DotCount = 0 };

    private static (GameWorld W, int Cell, List<(int X, int Y)> Spots) NewWorld(int spots = 8)
    {
        var w = GameWorld.Create(Seed, Cfg);
        int cell = PeopleSamples.Home(w.Planet);
        var life = w.People.Life(cell);
        return (w, cell, PeopleSamples.SpawnSpots(life, spots));
    }

    private static void SpawnSome(GameWorld w, int cell, List<(int X, int Y)> spots)
    {
        w.Apply(new Intervention(w.Now, Intervention.Kinds.SpawnFamily, new long[] { cell, spots[0].X, spots[0].Y }));
        for (int k = 1; k < spots.Count; k++)
            w.Apply(new Intervention(w.Now, Intervention.Kinds.SpawnPerson, new long[] { cell, spots[k].X, spots[k].Y }));
    }

    [Fact]
    public void DataLoads()
    {
        var d = PeopleData.Default;
        Assert.Equal(22, d.Traits.Count);
        Assert.Equal(5, d.Abilities.Count);
        Assert.Equal(10, d.Skills.Count);
        Assert.InRange(d.Thoughts.Count, 20, 50);
        Assert.Contains(d.Harvest, h => h is { Food: true, Item: "apple" });
        Assert.Contains(d.Harvest, h => h is { Food: false, Item: "flint" });
    }

    [Fact]
    public void SameSeedSameSpawnsSameResult()
    {
        var (a, cell, spots) = NewWorld();
        var (b, _, _) = NewWorld();
        SpawnSome(a, cell, spots); SpawnSome(b, cell, spots);
        Assert.True(a.People.AliveCount >= spots.Count + 1);
        Assert.Equal(a.People.All.Select(p => (p.Name, p.Sex, p.BirthTick, p.Look)), b.People.All.Select(p => (p.Name, p.Sex, p.BirthTick, p.Look)));
        a.AdvanceTo(GameTime.TicksPerDay * 4 + 1234);
        b.AdvanceTo(GameTime.TicksPerDay * 4 + 1234);
        Assert.Equal(SaveGame.Fingerprint(a), SaveGame.Fingerprint(b));

        // 다른 자리에 뿌리면 다른 결과
        var (c, _, _) = NewWorld();
        SpawnSome(c, cell, spots.Skip(1).Append(spots[0]).ToList());
        c.AdvanceTo(GameTime.TicksPerDay * 4 + 1234);
        Assert.NotEqual(SaveGame.Fingerprint(a), SaveGame.Fingerprint(c));
    }

    [Fact]
    public void ReplayFromStartRecordGivesSamePeople()
    {
        var (w, cell, spots) = NewWorld();
        SpawnSome(w, cell, spots.Take(4).ToList());
        w.AdvanceTo(GameTime.TicksPerDay + 500);
        w.Apply(new Intervention(w.Now, Intervention.Kinds.SpawnPerson, new long[] { cell, spots[5].X, spots[5].Y }));
        w.AdvanceTo(GameTime.TicksPerDay * 3);
        var r = GameWorld.Replay(w.ToStartRecord(), GameTime.TicksPerDay * 3);
        Assert.Equal(SaveGame.ToBytes(w), SaveGame.ToBytes(r));
    }

    [Fact]
    public void SaveLoadContinueEqualsUninterrupted()
    {
        var (w, cell, spots) = NewWorld();
        SpawnSome(w, cell, spots);
        long t1 = GameTime.TicksPerDay * 3 + 777, t2 = GameTime.TicksPerDay * 7 + 31;
        w.AdvanceTo(t1);
        var life = w.People.Life(cell);
        Assert.True(life.Stocks.Count > 0, "딴 열매(바뀐 타일)가 저장에 들어가야 한다");
        Assert.Contains(w.People.All, p => p.Thoughts.Count > 0);
        Assert.Contains(w.People.All, p => p.Relations.Count > 0);
        var bytes = SaveGame.ToBytes(w);
        w.AdvanceTo(t2);

        var loaded = SaveGame.FromBytes(bytes);
        Assert.Equal(bytes, SaveGame.ToBytes(loaded));
        loaded.AdvanceTo(t2);
        Assert.Equal(SaveGame.ToBytes(w), SaveGame.ToBytes(loaded));
    }

    [Fact]
    public void LookingAtCardsDoesNotChangeTheWorld()
    {
        var (a, cell, spots) = NewWorld();
        var (b, _, _) = NewWorld();
        SpawnSome(a, cell, spots); SpawnSome(b, cell, spots);
        for (long t = 1000; t < GameTime.TicksPerDay * 3; t += 7_919)
        {
            a.AdvanceTo(t); b.AdvanceTo(t);
            foreach (var p in a.People.All) Assert.NotNull(a.People.Card(p.Id));
            a.People.Rows(cell);
        }
        Assert.Equal(SaveGame.ToBytes(b), SaveGame.ToBytes(a));
    }

    [Fact]
    public void PathsAvoidStoneAndDeepWaterAndGiveUpWhenUnreachable()
    {
        var w = GameWorld.Create(Seed, Cfg);
        var p = w.Planet;
        int cell = ProvinceSamples.Find(p, "mountain");
        var life = w.People.Life(cell);
        var path = new List<(int X, int Y)>();
        var rng = new Core.Pcg32(5, 7);
        int found = 0, refused = 0;
        var walk = Enumerable.Range(0, HexFrame.TileCount).Where(t => life.Walk[t]).ToList();
        int big = walk.GroupBy(t => life.Region[t]).OrderByDescending(g => g.Count()).First().Key;
        var main = walk.Where(t => life.Region[t] == big).ToList();
        for (int k = 0; k < 300; k++)
        {
            int a = main[rng.NextInt(main.Count)], b = k % 3 == 0 ? walk[rng.NextInt(walk.Count)] : main[rng.NextInt(main.Count)];
            bool ok = life.Paths.Find(a % 150 * 1000 + 500, a / 150 * 1000 + 500, b, path);
            if (life.Region[a] != life.Region[b]) { Assert.False(ok); refused++; continue; }
            if (!ok) continue;
            found++;
            for (int i = 1; i < path.Count; i++)
            {
                var (x0, y0) = path[i - 1]; var (x1, y1) = path[i];
                int steps = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0)) / 100 + 1;
                for (int s = 0; s <= steps; s++)
                {
                    int x = (x0 + (x1 - x0) * s / steps) / 1000, y = (y0 + (y1 - y0) * s / steps) / 1000;
                    var g = life.Map.Ground[y * 150 + x];
                    Assert.True(GroundInfo.IsWalkable(g), $"길이 {g} 위를 지난다 ({x},{y})");
                }
            }
        }
        Assert.True(found > 100, $"찾은 길 {found}");
        // 바위 타일로는 길이 없다
        int stone = Enumerable.Range(0, HexFrame.TileCount).First(t => life.Map.Ground[t] == Ground.Stone);
        Assert.False(life.Paths.Find(walk[0] % 150 * 1000 + 500, walk[0] / 150 * 1000 + 500, stone, path));
        _ = refused;
    }

    [Fact]
    public void StarveWithoutFoodLiveWithFood()
    {
        var (fed, cell, spots) = NewWorld(4);
        var (hungry, _, _) = NewWorld(4);
        foreach (var w in new[] { fed, hungry })
            for (int k = 0; k < 3; k++) w.Apply(new Intervention(0, Intervention.Kinds.SpawnPerson, new long[] { cell, spots[k].X, spots[k].Y }));
        // 한쪽 지방에서는 먹을 것을 모두 없앤다(다시 나지도 않게)
        var life = hungry.People.Life(cell);
        for (int t = 0; t < HexFrame.TileCount; t++)
            if (life.HarvestAt(t) is { Food: true }) life.Stocks[t] = new TileStock { Stock = 0, Stamp = long.MaxValue / 4 };
        foreach (var p in hungry.People.All) p.Food = 30_000;
        fed.AdvanceTo(GameTime.TicksPerDay * 14);
        hungry.AdvanceTo(GameTime.TicksPerDay * 14);
        // (E3b) 먹을 것이 없는 땅에서는 굶어 죽거나, 찾아다니다 이웃 땅으로 떠난다 — 그 땅에 살아 남은 사람은 없다
        Assert.All(hungry.People.All, p => Assert.True(p.Dead ? p.Cause == DeathCause.Starvation : p.Cell != cell, $"{p.Id}: 죽음 {p.Dead} · 칸 {p.Cell}"));
        Assert.Equal(3, fed.People.AliveCount);
        Assert.All(fed.People.All, p => Assert.Equal(0, p.Malnutrition));
    }

    [Fact]
    public void DrinkOnlyFreshWater()
    {
        var w = GameWorld.Create(Seed, Cfg);
        // 바닷가이면서 강이 흐르는 칸(짠물 · 민물이 다 있다)
        int cell = PeopleSamples.Ranked(w.Planet, (pl, c) => (pl.Flags[c] & CellFlags.Coast) != 0 && pl.RiverSize[c] > 0 && pl.Temp10[c] > 80 ? pl.RiverSize[c] : int.MinValue).First();
        var life = w.People.Life(cell);
        var m = life.Map;
        int seaOnly = 0;
        for (int t = 0; t < HexFrame.TileCount; t++)
        {
            if (!life.Walk[t]) continue;
            bool fresh = false, sea = false;
            int x = t % 150, y = t / 150;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if ((uint)nx >= 150 || (uint)ny >= 150) continue;
                    int j = ny * 150 + nx;
                    if (!GroundInfo.IsWater(m.Ground[j]) || !HexFrame.Inside[j]) continue;
                    if ((m.Flags[j] & TileFlags.Sea) != 0) sea = true; else fresh = true;
                }
            Assert.Equal(fresh, life.Drink[t]);
            if (sea && !fresh) seaOnly++;
        }
        Assert.True(seaOnly > 50, "바닷가 타일이 있어야 시험이 된다");
        // 사람이 물을 마실 때는 늘 민물 곁
        var spots = PeopleSamples.SpawnSpots(life, 4);
        foreach (var (x, y) in spots) w.Apply(new Intervention(0, Intervention.Kinds.SpawnPerson, new long[] { cell, x, y }));
        int drinks = 0;
        for (long t = 0; t < GameTime.TicksPerDay * 3; t += 50)
        {
            w.AdvanceTo(t);
            foreach (var p in w.People.All)
                if (!p.Dead && p.Act == Act.Drink && !p.Walking) { Assert.True(life.Drink[p.Tile(w.Now)]); drinks++; }
        }
        Assert.True(drinks > 0);
    }

    [Fact]
    public void SleepAtNightByLocalTime()
    {
        var (w, cell, spots) = NewWorld();
        SpawnSome(w, cell, spots);
        var life = w.People.Life(cell);
        int nightAsleep = 0, nightTotal = 0, dayAsleep = 0, dayTotal = 0;
        for (long t = GameTime.TicksPerDay; t < GameTime.TicksPerDay * 4; t += GameTime.TicksPerHour / 2)
        {
            w.AdvanceTo(t);
            int h = life.LocalHour(t);
            foreach (var p in w.People.All)
            {
                if (p.Dead || p.Carrier >= 0) continue;
                if (h is >= 1 and < 5) { nightTotal++; if (p.Asleep) nightAsleep++; }
                if (h is >= 10 and < 16) { dayTotal++; if (p.Asleep) dayAsleep++; }
            }
        }
        Assert.True(nightAsleep > nightTotal * 0.9, $"밤에 잔 비율 {nightAsleep}/{nightTotal}");
        Assert.True(dayAsleep < dayTotal * 0.15, $"낮에 잔 비율 {dayAsleep}/{dayTotal}");
    }

    [Fact]
    public void TraitsChangeAbilitiesAndOppositesNeverMeet()
    {
        var (w, cell, spots) = NewWorld();
        var ps = w.People;
        var d = ps.Data;
        var rng = ps.Rng(cell);
        int tile = spots[0].Y * 150 + spots[0].X;
        for (int k = 0; k < 120; k++) ps.Make(cell, tile, 25, k % 2 == 0 ? Sex.Male : Sex.Female, null, null, rng);
        foreach (var p in ps.All)
        {
            Assert.InRange(p.Traits.Count, 1, 3);
            foreach (int t in p.Traits)
                foreach (int o in p.Traits)
                    Assert.NotEqual(d.Traits[t].Opposite, d.Traits[o].Id);
            for (int a = 0; a < PeopleData.AbilityCount; a++)
            {
                int expect = p.BaseAbility[a] + p.Traits.Sum(t => d.Traits[t].Abilities[a]);
                Assert.Equal(Math.Clamp(expect, 1, 10), ps.Ability(p, a));
            }
        }
        int tough = d.TraitIndex("tough");
        var someone = ps.All[0];
        int before = ps.Ability(someone, PeopleData.Str);
        someone.Traits.Clear(); someone.Traits.Add(tough);
        someone.BaseAbility[PeopleData.Str] = 5;
        Assert.Equal(7, ps.Ability(someone, PeopleData.Str));
        _ = before;
        // 빠른 발: 걷는 빠르기 +20% 남짓
        int fast = d.TraitIndex("fast_walker");
        someone.Traits.Clear();
        int slow = ps.TicksPerTile(someone);
        someone.Traits.Add(fast);
        Assert.True(ps.TicksPerTile(someone) < slow);
    }

    [Fact]
    public void ChildrenResembleParents()
    {
        var (w, cell, spots) = NewWorld();
        var ps = w.People;
        var rng = ps.Rng(cell);
        int tile = spots[0].Y * 150 + spots[0].X;
        var mom = ps.Make(cell, tile, 25, Sex.Female, null, null, rng);
        var dad = ps.Make(cell, tile, 25, Sex.Male, null, null, rng);
        mom.Look = mom.Look with { Skin = 230, Hair = 20, Height = 220 };
        dad.Look = dad.Look with { Skin = 210, Hair = 40, Height = 240 };
        for (int a = 0; a < PeopleData.AbilityCount; a++) { mom.BaseAbility[a] = 9; dad.BaseAbility[a] = 9; }
        double skin = 0, height = 0, ability = 0;
        const int n = 60;
        for (int k = 0; k < n; k++)
        {
            var c = ps.Make(cell, tile, 0, Sex.Female, mom, dad, rng, newborn: true);
            skin += c.Look.Skin; height += c.Look.Height;
            ability += c.BaseAbility.Average();
            Assert.Equal(mom.Id, c.Mother); Assert.Equal(dad.Id, c.Father);
            Assert.Equal(mom.Id, c.Carrier); // 아기는 엄마가 안는다
        }
        Assert.InRange(skin / n, 190, 250);     // 부모(210 · 230) 쪽 — 아무렇게나면 128 근처
        Assert.InRange(height / n, 200, 255);
        Assert.True(ability / n > 6.5, $"능력 평균 {ability / n}"); // 부모 9 · 9 — 아무렇게나면 5.5 근처
    }

    [Fact]
    public void CaveIsRevealedWhenEntered()
    {
        var w = GameWorld.Create(Seed, Cfg);
        var p = w.Planet;
        foreach (int cell in PeopleSamples.Ranked(p, (pl, c) => pl.Terrain[c] == Terrain.Hill ? 1 : int.MinValue).Take(30).Concat(new[] { ProvinceSamples.Find(p, "mountain") }))
        {
            var life = w.People.Life(cell);
            int cave = Enumerable.Range(0, HexFrame.TileCount).FirstOrDefault(t => life.Shelter[t] == 3 && life.IsHidden(t), -1);
            if (cave < 0) continue;
            Assert.True(life.RevealAround(cave) > 0);
            Assert.False(life.IsHidden(cave));
            Assert.Contains(cave, life.Revealed);
            return;
        }
        Assert.Fail("숨은 동굴이 있는 지방을 찾지 못했다");
    }

    [Fact]
    public void SpawnRefusedOnUnwalkableGround()
    {
        var w = GameWorld.Create(Seed, Cfg);
        int cell = ProvinceSamples.Find(w.Planet, "mountain");
        var life = w.People.Life(cell);
        int stone = Enumerable.Range(0, HexFrame.TileCount).First(t => life.Map.Ground[t] == Ground.Stone);
        w.Apply(new Intervention(0, Intervention.Kinds.SpawnPerson, new long[] { cell, stone % 150, stone / 150 }));
        Assert.Empty(w.People.All);
        Assert.Single(w.Interventions); // 기록은 남는다(다시 만들면 똑같이 아무 일도 없다)
    }

    [Fact]
    public void CardsTellWhy()
    {
        var (w, cell, spots) = NewWorld();
        SpawnSome(w, cell, spots);
        w.AdvanceTo(GameTime.TicksPerDay * 2 + 3000);
        foreach (var p in w.People.All)
        {
            var c = w.People.Card(p.Id)!;
            Assert.False(string.IsNullOrEmpty(c.Activity));
            Assert.Equal(5, c.Abilities.Count);
            Assert.Equal(10, c.Skills.Count);
            Assert.NotEmpty(c.Story);
            if (p.Carrier < 0 && !p.Dead) Assert.False(string.IsNullOrEmpty(c.Why), $"{c.Activity} 의 까닭이 비었다 ({p.Why})");
        }
    }
}
