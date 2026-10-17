using Wtw.Sim.People;
using Wtw.Sim.Persistence;
using Wtw.Sim.Planet;
using Wtw.Sim.Provinces;
using Wtw.Sim.Time;
using Wtw.Sim.World;
using Xunit;

namespace Wtw.Sim.Tests;

/// <summary>E3b 지방 넘기 · 작은 물 · 말의 갈라짐 · 지명.</summary>
[Trait("Category", "Fast")]
public class BorderTests
{
    private const ulong Seed = 20261016;
    private static readonly WorldConfig Cfg = new() { DotCount = 0 };

    /// <summary>가운데 땅에 마실 물이 없고 이웃 땅에는 많은 따뜻한 칸(사람이 물을 찾아 경계를 넘는다).</summary>
    private static int DryCell(Planet.Planet p)
    {
        foreach (int c in PeopleSamples.Ranked(p, (pl, c) => pl.Biome[c] is Biome.Grassland or Biome.Savanna or Biome.Temperate && pl.RiverSize[c] == 0 && pl.Temp10[c] > 120 ? 100 - Math.Abs(pl.Temp10[c] - 200) / 5 : int.MinValue).Take(400))
        {
            var life = new ProvinceLife(p, ProvinceCache.Get(p, c), PeopleData.Default);
            if (life.DrinkTiles > 0) continue;
            foreach (int nb in p.Grid.Neighbors(c))
                if (p.IsLand(nb) && new ProvinceLife(p, ProvinceCache.Get(p, nb), PeopleData.Default).DrinkTiles > 50) return c;
        }
        return -1;
    }

    private static GameWorld DryWorld(out int cell)
    {
        var w = GameWorld.Create(Seed, Cfg);
        cell = DryCell(w.Planet);
        Assert.True(cell >= 0, "물 없는 땅을 못 찾음");
        foreach (var (x, y) in PeopleSamples.SpawnSpots(w.People.Life(cell), 4))
            w.Apply(new Intervention(0, Intervention.Kinds.SpawnPerson, new long[] { cell, x, y }));
        return w;
    }

    [Fact]
    public void CrossingsAreDeterministic()
    {
        var a = DryWorld(out int cell);
        var b = DryWorld(out _);
        long end = GameTime.TicksPerDay * 3 + 777;
        a.AdvanceTo(end); b.AdvanceTo(end);
        Assert.True(a.People.Stats.Crossings >= 2, $"경계를 넘은 횟수 {a.People.Stats.Crossings}");
        Assert.Contains(a.People.All, p => p.Cell != cell);
        Assert.Equal(SaveGame.Fingerprint(a), SaveGame.Fingerprint(b));
        Assert.Equal(a.People.All.Select(p => (p.Cell, p.Tile(end))), b.People.All.Select(p => (p.Cell, p.Tile(end))));
    }

    [Fact]
    public void SaveWhileCrossingThenContinue()
    {
        var w = DryWorld(out _);
        // 누군가 경계로 걸어가는 중일 때까지
        long t = 0;
        while (!w.People.All.Any(p => p.Act == Act.Cross && p.Walking) && t < GameTime.TicksPerDay * 3) { t += 50; w.AdvanceTo(t); }
        Assert.Contains(w.People.All, p => p.Act == Act.Cross && p.Walking);
        var loaded = SaveGame.FromBytes(SaveGame.ToBytes(w));
        Assert.Equal(SaveGame.Fingerprint(w), SaveGame.Fingerprint(loaded));
        long end = t + GameTime.TicksPerDay;
        w.AdvanceTo(end); loaded.AdvanceTo(end);
        Assert.True(w.People.Stats.Crossings > 0);
        Assert.Equal(SaveGame.Fingerprint(w), SaveGame.Fingerprint(loaded));
    }

    [Fact]
    public void SeaCliffAndBlockedEdgesCannotBeCrossed()
    {
        var w = GameWorld.Create(Seed, Cfg);
        var p = w.Planet;
        int seaEdges = 0, landEdges = 0, blocked = 0;
        foreach (int c in PeopleSamples.Ranked(p, (pl, c) => (pl.Flags[c] & CellFlags.Coast) != 0 ? 1 : int.MinValue).Take(6))
        {
            var m = w.Province(c);
            for (int s = 0; s < 6; s++)
            {
                var b = w.People.BorderOf(c, s);
                int nb = m.SlotCell[s];
                if (nb < 0 || p.IsWater(nb)) { Assert.Null(b); seaEdges++; continue; }
                Assert.NotNull(b);
                landEdges++;
                // 넘어가는 자리는 양쪽 다 걸을 수 있는 땅, 이웃 쪽은 그 변 가까이
                var bm = w.Province(nb);
                for (int k = 0; k < b!.Gates.Length; k++)
                {
                    Assert.True(GroundInfo.IsWalkable(m.Ground[b.Gates[k]]));
                    Assert.True(GroundInfo.IsWalkable(bm.Ground[b.Entries[k]]));
                    Assert.True(HexFrame.EdgeDistance(b.Entries[k], b.NbSlot) < 3);
                }
            }
        }
        // 오각형의 막힌 변(절벽)
        for (int c = 0; c < p.CellCount && blocked == 0; c++)
        {
            if (!p.IsLand(c) || p.Grid.Neighbors(c).Length != 5) continue;
            var m = w.Province(c);
            Assert.Null(w.People.BorderOf(c, m.BlockedSlot));
            blocked++;
        }
        Assert.True(seaEdges > 0 && landEdges > 0, $"바다 변 {seaEdges} · 땅 변 {landEdges}");
    }

    [Fact]
    public void EmptyProvinceSleepsAndCatchesUpOnWake()
    {
        var w = GameWorld.Create(Seed, Cfg);
        int cell = PeopleSamples.Home(w.Planet);
        var life = w.People.Life(cell);
        int tile = -1;
        for (int t = 0; t < HexFrame.TileCount && tile < 0; t++) if (life.HarvestAt(t) is { Food: true, RegrowTicks: > 0 }) tile = t;
        var h = life.HarvestAt(tile)!;
        life.Take(tile, h.Stock, 0);
        Assert.Equal(0, life.StockAt(tile, 0));
        // 아무도 없으니 며칠 뒤 잠든다
        w.AdvanceTo(PeopleSim.SleepAfter + 2 * GameTime.TicksPerDay);
        Assert.False(w.People.Provinces.ContainsKey(cell));
        Assert.True(w.People.Dormant.ContainsKey(cell));
        // 오래 지나 다시 들어가면(깨어나면) 흐른 시간만큼 다시 열려 있다
        long later = w.Now + h.RegrowTicks * h.Stock + GameTime.TicksPerDay;
        w.AdvanceTo(later);
        var woke = w.People.Life(cell);
        Assert.Equal(h.Stock, woke.StockAt(tile, later));
        Assert.True(w.People.Stats.Wakes >= 1);
        // 깨어 있든 잠들었든 저장 내용은 같다
        var loaded = SaveGame.FromBytes(SaveGame.ToBytes(w));
        Assert.Equal(SaveGame.Fingerprint(w), SaveGame.Fingerprint(loaded));
    }

    [Fact]
    public void StreamsMeetAtBordersAndWetLandHasWater()
    {
        var w = GameWorld.Create(Seed, Cfg);
        var p = w.Planet;
        int checkedEdges = 0;
        for (int c = 0; c < p.CellCount && checkedEdges < 6; c++)
        {
            if (!p.IsLand(c)) continue;
            var m = w.Province(c);
            for (int s = 0; s < 6 && checkedEdges < 6; s++)
            {
                int nb = m.SlotCell[s];
                if (nb < 0 || nb < c || !p.IsLand(nb) || s == m.BlockedSlot || ProvinceGenerator.StreamAcross(p, c, nb) is not double u) continue;
                var bm = w.Province(nb);
                if (bm.SlotOf(c) == bm.BlockedSlot) continue;
                int ta = m.SlotCornerTri[(s + 5) % 6], tb = m.SlotCornerTri[s];
                var (qx, qy) = HexFrame.EdgePoint(s, ta < tb ? u : 1 - u);
                // 이쪽: 건너는 자리 2타일 안에 물
                Assert.True(WaterNear(m, qx, qy, 2.5), $"{c}→{nb} 이쪽에 개울이 없음");
                // 저쪽: 같은 점을 이웃 지도 좌표로
                var place = ProvinceLayout.Neighbor(p, c, s)!.Value;
                var (bx, by) = place.ToLocal(qx - HexFrame.Center, qy - HexFrame.Center);
                Assert.True(WaterNear(bm, bx, by, 2.5), $"{c}→{nb} 저쪽에 개울이 없음");
                checkedEdges++;
            }
        }
        Assert.True(checkedEdges >= 6);
        // 습한 땅(열대우림 · 온대림 · 침엽수림)은 대부분 마실 민물이 있다
        int wet = 0, withWater = 0;
        foreach (int c in PeopleSamples.Ranked(p, (pl, c) => pl.Biome[c] is Biome.Rainforest or Biome.Temperate or Biome.Boreal ? (int)(StableHashOf(c) % 1000) : int.MinValue).Take(20))
        {
            wet++;
            if (new ProvinceLife(p, w.Province(c), PeopleData.Default).DrinkTiles > 0) withWater++;
        }
        Assert.True(withWater >= wet * 85 / 100, $"습한 땅 {wet}곳 중 민물 있는 곳 {withWater}");
    }

    private static ulong StableHashOf(int c) => Core.StableHash.Mix((ulong)c * 2654435761UL);

    private static bool WaterNear(ProvinceMap m, double x, double y, double r)
    {
        for (int dy = -3; dy <= 3; dy++)
            for (int dx = -3; dx <= 3; dx++)
            {
                int tx = (int)Math.Floor(x) + dx, ty = (int)Math.Floor(y) + dy;
                if ((uint)tx >= HexFrame.Size || (uint)ty >= HexFrame.Size) continue;
                int i = ty * HexFrame.Size + tx;
                if (!HexFrame.Inside[i]) continue;
                double ddx = tx + 0.5 - x, ddy = ty + 0.5 - y;
                if (ddx * ddx + ddy * ddy <= r * r && GroundInfo.IsWater(m.Ground[i])) return true;
            }
        return false;
    }

    [Fact]
    public void SeparatedGroupsDriftApartAndReconvergeWhenTogether()
    {
        var w = GameWorld.Create(Seed, Cfg);
        var p = w.Planet;
        int a = PeopleSamples.Home(p), b = PeopleSamples.Home(p, 1);
        foreach (int cell in new[] { a, b })
            foreach (var (x, y) in PeopleSamples.SpawnSpots(w.People.Life(cell), 5))
                w.Apply(new Intervention(0, Intervention.Kinds.SpawnPerson, new long[] { cell, x, y }));
        var ps = w.People;
        int lang = ps.All[0].LangId;
        foreach (var q in ps.All) { q.LangId = lang; Array.Clear(q.Accent); }
        var ga = ps.All.Where(q => q.Cell == a).ToList(); var gb = ps.All.Where(q => q.Cell == b).ToList();
        int before = ps.Understanding(ga[0], gb[0]);
        Assert.Equal(100, before);
        for (int y = 0; y < 400; y++) ps.LanguageYear(); // 400년 동안 서로 이야기하지 않음(시간은 멈춘 채 말만)
        Assert.NotEqual(ga[0].LangId, gb[0].LangId);
        Assert.True(ps.Stats.LanguageSplits >= 1);
        int apart = ps.Understanding(ga[0], gb[0]);
        Assert.True(apart < 75, $"400년 떨어져 산 뒤 알아듣기 {apart}%");
        // 무리 안에서는 같은 말
        Assert.All(ga, q => Assert.Equal(ga[0].LangId, q.LangId));
        // 다시 함께 살며 여러 번 이야기하면 한 말이 된다
        var rng = new Core.Pcg32(1, 2);
        for (int round = 0; round < 10; round++)
            foreach (var x in ga)
                foreach (var z in gb)
                {
                    var rx = x.GetRelation(z.Id); rx.Talks++; x.SetRelation(rx);
                    var rz = z.GetRelation(x.Id); rz.Talks++; z.SetRelation(rz);
                    ps.Converge(x, z, rng);
                }
        Assert.Single(ga.Concat(gb).Select(q => q.LangId).Distinct());
        Assert.True(ps.Understanding(ga[0], gb[0]) > 90);
    }

    [Fact]
    public void LandIsNamedInTheGroupsLanguageWithCorrectJosa()
    {
        var w = GameWorld.Create(Seed, Cfg);
        int cell = PeopleSamples.Home(w.Planet); // 큰 강이 흐르는 땅
        w.Apply(new Intervention(0, Intervention.Kinds.SpawnFamily, new long[] { cell, PeopleSamples.SpawnSpots(w.People.Life(cell), 1)[0].X, PeopleSamples.SpawnSpots(w.People.Life(cell), 1)[0].Y }));
        w.AdvanceTo(3 * GameTime.TicksPerDay);
        var ps = w.People;
        var n = ps.LandNameOf(cell);
        Assert.NotNull(n);
        Assert.Equal(ps.All[0].LangId, n!.LangId);
        Assert.False(ps.Lang.IsBanned(n.Word));
        Assert.InRange(n.Word.Length, 2, 4);
        Assert.False(string.IsNullOrEmpty(ps.LandMeaning(n)));
        var ev = ps.Events.Last(e => e.Kind == WorldEventKind.LandNamed);
        Assert.Contains($"'{n.Word}'" + (Hangul.HasFinal(n.Word) ? "이라고" : "라고"), ev.Text);
        Assert.Contains(n.Word, ps.LandLabel(cell));
        // 그 사람의 이야기에도
        var namer = ps.Get(n.Namer)!;
        Assert.Contains(namer.Bio, b => b.Kind == BioKind.NamedLand && b.Param == cell);
    }
}
