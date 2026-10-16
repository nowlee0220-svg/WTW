using Wtw.Sim.Data;
using Wtw.Sim.Persistence;
using Wtw.Sim.Planet;
using Wtw.Sim.Provinces;
using Wtw.Sim.World;
using Xunit;
using Xunit.Abstractions;

namespace Wtw.Sim.Tests;

public sealed class ProvinceFixture
{
    /// <summary>땅 24,000 — 오각형이 땅에 걸리고 강 · 해안 · 산 · 사막 · 툰드라가 다 있다.</summary>
    public readonly Planet.Planet P = PlanetGenerator.Generate(3, new PlanetSettings(24_000, ClimateBias.Normal));
}

/// <summary>지방 지도 테스트에서 함께 쓰는 검사.</summary>
public static class ProvinceChecks
{
    public sealed record EdgeResult(int Samples, int WaterMismatch, int RiverMismatch, double MeanHeightDiff, double InteriorHeightDiff, int GroundMismatch);

    /// <summary>
    /// 맞댄 변 하나를 따라 양쪽 지도의 "변 바로 안쪽" 타일을 비교한다. 같은 행성 위의 점을 c 쪽은 변 s 의 t, 이웃 쪽은 변 m 의 1 − t 로 본다.
    /// </summary>
    public static EdgeResult CompareEdge(ProvinceMap a, ProvinceMap b)
    {
        int s = a.SlotOf(b.Cell), m = b.SlotOf(a.Cell);
        if (s < 0 || m < 0) throw new ArgumentException("이웃이 아님");
        int n = 0, water = 0, river = 0, ground = 0;
        double dh = 0;
        for (double t = 0.06; t <= 0.94; t += 0.004)
        {
            var (ax, ay) = HexFrame.EdgePoint(s, t);
            var (bx, by) = HexFrame.EdgePoint(m, 1 - t);
            int ia = Tile(ax - HexFrame.NX[s] * 0.6, ay - HexFrame.NY[s] * 0.6);
            int ib = Tile(bx - HexFrame.NX[m] * 0.6, by - HexFrame.NY[m] * 0.6);
            if (ia < 0 || ib < 0 || !HexFrame.Inside[ia] || !HexFrame.Inside[ib]) continue;
            n++;
            if (GroundInfo.IsWater(a.Ground[ia]) != GroundInfo.IsWater(b.Ground[ib])) water++;
            if (((a.Flags[ia] ^ b.Flags[ib]) & TileFlags.River) != 0) river++;
            if (a.Ground[ia] != b.Ground[ib]) ground++;
            dh += Math.Abs(a.Height[ia] - b.Height[ib]);
        }
        // 비교 기준: 지도 안에서 1.4 타일 떨어진 두 타일의 높이 차 평균
        double inner = 0; int ni = 0;
        for (int i = 0; i < HexFrame.TileCount; i += 7)
        {
            int j = i + HexFrame.Size + 1;
            if (j >= HexFrame.TileCount || !HexFrame.Inside[i] || !HexFrame.Inside[j]) continue;
            inner += Math.Abs(a.Height[i] - a.Height[j]); ni++;
        }
        return new EdgeResult(n, water, river, n > 0 ? dh / n : 0, ni > 0 ? inner / ni : 0, ground);
    }

    private static int Tile(double x, double y)
    {
        int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
        return ix < 0 || iy < 0 || ix >= HexFrame.Size || iy >= HexFrame.Size ? -1 : iy * HexFrame.Size + ix;
    }

    /// <summary>변 s 근처(2타일 안)에 강물 타일이 있나.</summary>
    public static bool RiverAtEdge(ProvinceMap m, int s)
    {
        for (int i = 0; i < HexFrame.TileCount; i++)
            if (HexFrame.Inside[i] && (m.Flags[i] & TileFlags.River) != 0 && HexFrame.EdgeDistance(i, s) < 2) return true;
        return false;
    }

    public static bool RiverExpected(Planet.Planet p, int c, int nb) =>
        nb >= 0 && ((p.RiverSize[c] > 0 && p.Downstream[c] == nb) || (p.IsLand(nb) && p.RiverSize[nb] > 0 && p.Downstream[nb] == c));
}

[Trait("Category", "Fast")]
public class ProvinceTests : IClassFixture<ProvinceFixture>
{
    private readonly Planet.Planet _p;
    private readonly ITestOutputHelper _out;
    public ProvinceTests(ProvinceFixture f, ITestOutputHelper output) { _p = f.P; _out = output; }

    private int Find(string kind)
    {
        int c = ProvinceSamples.Find(_p, kind);
        Assert.True(c >= 0, $"{kind} 칸이 없음");
        return c;
    }

    [Fact]
    public void SameCellSameMap_DifferentCellDifferent()
    {
        int c = Find("forest");
        var a = ProvinceGenerator.Generate(_p, c);
        var b = ProvinceGenerator.Generate(_p, c);
        Assert.Equal(a.Fingerprint, b.Fingerprint);
        Assert.Equal(a.Ground, b.Ground);
        Assert.Equal(a.Object, b.Object);
        Assert.Equal(a.Height, b.Height);
        int nb = _p.Grid.Neighbors(c)[0];
        Assert.NotEqual(a.Fingerprint, ProvinceGenerator.Generate(_p, nb).Fingerprint);
        // 다른 행성의 같은 칸 번호도 다르다
        var other = PlanetGenerator.Generate(4, new PlanetSettings(24_000, ClimateBias.Normal));
        int oc = ProvinceSamples.Find(other, "forest");
        Assert.NotEqual(ProvinceGenerator.Generate(other, oc).Fingerprint, ProvinceGenerator.Generate(_p, oc).Fingerprint);
    }

    [Fact]
    public void OutsideHexIsUnused_InsideIsGround()
    {
        var m = ProvinceGenerator.Generate(_p, Find("river"));
        int inside = 0;
        for (int i = 0; i < HexFrame.TileCount; i++)
        {
            if (!HexFrame.Inside[i]) { Assert.Equal(Ground.None, m.Ground[i]); Assert.Equal(0, m.Object[i]); continue; }
            inside++;
            Assert.NotEqual(Ground.None, m.Ground[i]);
        }
        Assert.Equal(HexFrame.InsideCount, inside);
        // 정육각형: 150×150 의 약 65%
        Assert.InRange(inside, 14_400, 14_800);
    }

    [Fact]
    public void SlotsMatchNeighborsAndCornersAreShared()
    {
        for (int c = 0; c < _p.CellCount; c += 97)
        {
            var (rot, blocked, slots, tris) = ProvinceGenerator.Slots(_p, c);
            var nbs = _p.Grid.Neighbors(c).ToArray();
            Assert.Equal(nbs.OrderBy(x => x), slots.Where(x => x >= 0).OrderBy(x => x));
            Assert.Equal(_p.Grid.IsPentagon(c) ? 1 : 0, slots.Count(x => x < 0));
            if (blocked >= 0) Assert.Equal(-1, slots[blocked]);
            // 가장 북쪽 이웃이 위(자리 0)
            Assert.Equal(nbs.OrderByDescending(x => _p.Z[x]).ThenBy(x => Array.IndexOf(nbs, x)).First(), slots[0] >= 0 ? slots[0] : slots[1]);
            for (int s = 0; s < 6; s++)
            {
                int nb = slots[s];
                if (nb < 0) continue;
                var (_, _, ns, nt) = ProvinceGenerator.Slots(_p, nb);
                int m = Array.IndexOf(ns, c);
                Assert.True(m >= 0);
                // 맞댄 변의 두 끝 = 행성의 같은 꼭짓점 (방향은 반대)
                Assert.Equal(tris[s], nt[(m + 5) % 6]);
                Assert.Equal(tris[(s + 5) % 6], nt[m]);
            }
        }
    }

    [Fact]
    public void PentagonBlocksOneEdge()
    {
        int c = Find("pentagon");
        Assert.True(_p.Grid.IsPentagon(c));
        var m = ProvinceGenerator.Generate(_p, c);
        Assert.InRange(m.BlockedSlot, 0, 5);
        Assert.Equal(-1, m.SlotCell[m.BlockedSlot]);
        for (int i = 0; i < HexFrame.TileCount; i++)
        {
            if (!HexFrame.Inside[i]) continue;
            for (int s = 0; s < 6; s++)
            {
                if (HexFrame.EdgeDistance(i, s) >= 1.5) continue;
                if (s == m.BlockedSlot) Assert.Equal(Ground.Cliff, m.Ground[i]);
                else if (HexFrame.EdgeDistance(i, m.BlockedSlot) > 9) Assert.NotEqual(Ground.Cliff, m.Ground[i]);
            }
        }
        Assert.False(GroundInfo.IsWalkable(Ground.Cliff));
        // 육각형 칸은 막힌 변이 없다
        var h = ProvinceGenerator.Generate(_p, Find("forest"));
        Assert.Equal(-1, h.BlockedSlot);
        Assert.DoesNotContain(Ground.Cliff, h.Ground);
    }

    [Fact]
    public void BordersMatch_EitherOrder()
    {
        var checkedPairs = 0;
        foreach (var kind in new[] { "river", "coast", "mountain", "forest", "desert" })
        {
            int c = Find(kind);
            foreach (int nb in _p.Grid.Neighbors(c))
            {
                if (!_p.IsLand(nb)) continue;
                ProvinceCache.Clear();
                var b1 = ProvinceCache.Get(_p, nb); // 이웃을 먼저
                var a1 = ProvinceCache.Get(_p, c);
                ProvinceCache.Clear();
                var a2 = ProvinceCache.Get(_p, c);  // 가운데를 먼저
                var b2 = ProvinceCache.Get(_p, nb);
                Assert.Equal(a1.Fingerprint, a2.Fingerprint);
                Assert.Equal(b1.Fingerprint, b2.Fingerprint);
                var r = ProvinceChecks.CompareEdge(a1, b1);
                _out.WriteLine($"{kind} {c}↔{nb}: 표본 {r.Samples} · 물 다름 {r.WaterMismatch} · 강 다름 {r.RiverMismatch} · 땅 종류 다름 {r.GroundMismatch} · 높이 차 {r.MeanHeightDiff:0.0}m (지도 안 이웃 타일 {r.InteriorHeightDiff:0.0}m)");
                Assert.True(r.Samples > 150);
                Assert.True(r.WaterMismatch <= r.Samples * 0.03, $"물이 어긋남 {r.WaterMismatch}/{r.Samples}");
                Assert.True(r.RiverMismatch <= 6, $"강이 어긋남 {r.RiverMismatch}");
                Assert.True(r.MeanHeightDiff <= r.InteriorHeightDiff * 2.5 + 4, $"높이 차 {r.MeanHeightDiff:0.0} (안쪽 {r.InteriorHeightDiff:0.0})");
                checkedPairs++;
            }
        }
        Assert.True(checkedPairs >= 15);
    }

    [Fact]
    public void RiversEnterAndLeaveThroughPlanetRiverEdges()
    {
        int rivers = 0;
        for (int c = 0; c < _p.CellCount && rivers < 12; c += 3)
        {
            if (!_p.IsLand(c) || _p.RiverSize[c] == 0) continue;
            var m = ProvinceGenerator.Generate(_p, c);
            for (int s = 0; s < 6; s++)
            {
                int nb = m.SlotCell[s];
                bool expected = ProvinceChecks.RiverExpected(_p, c, nb);
                Assert.True(expected == ProvinceChecks.RiverAtEdge(m, s), $"칸 {c} 변 {s}(이웃 {nb}): 강이 {(expected ? "있어야" : "없어야")} 함");
            }
            rivers++;
        }
        Assert.True(rivers >= 10);
        // 강이 없는 칸에는 강물이 없다
        int dry = Enumerable.Range(0, _p.CellCount).First(c => _p.IsLand(c) && _p.RiverSize[c] == 0 &&
            !_p.Grid.Neighbors(c).ToArray().Any(nb => _p.IsLand(nb) && _p.RiverSize[nb] > 0 && _p.Downstream[nb] == c));
        var d = ProvinceGenerator.Generate(_p, dry);
        Assert.DoesNotContain(d.Flags, f => (f & TileFlags.River) != 0);
    }

    [Fact]
    public void CoastHasSeaOnSeaSide()
    {
        int c = Find("coast");
        var m = ProvinceGenerator.Generate(_p, c);
        for (int s = 0; s < 6; s++)
        {
            int nb = m.SlotCell[s];
            if (nb < 0) continue;
            int water = 0, n = 0;
            for (int i = 0; i < HexFrame.TileCount; i++)
            {
                if (!HexFrame.Inside[i] || HexFrame.EdgeDistance(i, s) > 1.5) continue;
                double t = HexFrame.EdgeParam(s, i % HexFrame.Size + 0.5, i / HexFrame.Size + 0.5);
                if (t < 0.4 || t > 0.6) continue;
                n++;
                if (GroundInfo.IsWater(m.Ground[i])) water++;
            }
            if (_p.IsOcean(nb)) Assert.Equal(n, water);
            else if (!ProvinceChecks.RiverExpected(_p, c, nb) && !_p.IsWater(nb)) Assert.True(water <= n / 4, $"땅 쪽 변 {s} 에 물 {water}/{n}");
        }
        Assert.Contains(m.Flags, f => (f & TileFlags.Sea) != 0);
        Assert.Contains(Ground.Sand, m.Ground);
    }

    [Fact]
    public void MountainsAreHighAndRocky_PlainsAreFlat()
    {
        var mt = ProvinceGenerator.Generate(_p, Find("mountain"));
        var pl = ProvinceGenerator.Generate(_p, Find("plain"));
        double Range(ProvinceMap m)
        {
            var hs = Enumerable.Range(0, HexFrame.TileCount).Where(i => HexFrame.Inside[i] && !GroundInfo.IsWater(m.Ground[i])).Select(i => (double)m.Height[i]).OrderBy(x => x).ToArray();
            return hs[(int)(hs.Length * 0.95)] - hs[(int)(hs.Length * 0.05)];
        }
        var gm = mt.GroundCounts(); var gp = pl.GroundCounts();
        _out.WriteLine($"산 높낮이 {Range(mt):0}m · 바위 {gm[(int)Ground.Rock]} · 눈 {gm[(int)Ground.Snow]} / 평야 높낮이 {Range(pl):0}m · 바위 {gp[(int)Ground.Rock]}");
        Assert.True(Range(mt) > 3 * Range(pl));
        Assert.True(gm[(int)Ground.Rock] + gm[(int)Ground.Snow] > HexFrame.InsideCount / 2);
        Assert.True(gp[(int)Ground.Rock] < HexFrame.InsideCount / 20);
        Assert.True(mt.Height.Max() > pl.Height.Max());
    }

    [Fact]
    public void ResourceLevelsDriveObjectCounts()
    {
        var cat = ObjectCatalog.Default;
        var byRes = new (string Res, int[] Objs)[]
        {
            ("copper", new[] { cat.IndexOf("copper_vein") }),
            ("stone", new[] { cat.IndexOf("boulder"), cat.IndexOf("loose_stones") }),
            ("wood", cat.Objects.Where(o => o.IsTree).Select(o => o.Index).ToArray()),
            ("clay", new[] { cat.IndexOf("clay_bed") }),
        };
        var sums = new double[byRes.Length, 3];
        var counts = new int[byRes.Length, 3];
        int made = 0;
        for (int c = 5; c < _p.CellCount && made < 70; c += 211)
        {
            if (!_p.IsLand(c)) continue;
            var m = ProvinceGenerator.Generate(_p, c);
            var oc = m.ObjectCounts(cat.Objects.Count);
            made++;
            for (int k = 0; k < byRes.Length; k++)
            {
                int lvl = _p.Resource(c, Array.IndexOf(ResourceKinds.Ids, byRes[k].Res));
                sums[k, lvl] += byRes[k].Objs.Sum(o => oc[o]);
                counts[k, lvl]++;
            }
        }
        for (int k = 0; k < byRes.Length; k++)
        {
            double Avg(int l) => counts[k, l] == 0 ? double.NaN : sums[k, l] / counts[k, l];
            _out.WriteLine($"{byRes[k].Res}: 없음 {Avg(0):0.0} ({counts[k, 0]}칸) · 있음 {Avg(1):0.0} ({counts[k, 1]}칸) · 많음 {Avg(2):0.0} ({counts[k, 2]}칸)");
            var present = Enumerable.Range(0, 3).Where(l => counts[k, l] > 0).Select(Avg).ToArray();
            for (int q = 1; q < present.Length; q++) Assert.True(present[q] > present[q - 1], $"{byRes[k].Res}: 단계가 높을수록 많아야 함");
        }
        // 구리가 "없음" 인 칸에는 구리 광맥이 없다
        Assert.Equal(0, sums[0, 0]);
    }

    [Fact]
    public void EveryTileHasAtMostOneObject_AndObjectsSitOnAllowedGround()
    {
        var cat = ObjectCatalog.Default;
        foreach (var kind in ProvinceSamples.Kinds)
        {
            int c = ProvinceSamples.Find(_p, kind);
            if (c < 0) continue;
            var m = ProvinceGenerator.Generate(_p, c);
            for (int i = 0; i < HexFrame.TileCount; i++)
            {
                int o = m.ObjectAt(i);
                if (o < 0) continue;
                Assert.True(o < cat.Objects.Count);
                Assert.True(cat.Objects[o].GroundWeight[(int)m.Ground[i]] > 0, $"{cat.Objects[o].Id} 이(가) {m.Ground[i]} 위에");
            }
        }
    }

    [Fact]
    public void LayoutEdgesMeet()
    {
        foreach (var kind in new[] { "forest", "pentagon", "coast" })
        {
            int c = Find(kind);
            var placed = ProvinceLayout.Around(_p, c, nb => true);
            Assert.Equal(_p.Grid.NeighborCount(c) + 1, placed.Count);
            var center = placed[0];
            foreach (var pl in placed.Skip(1))
            {
                var (_, _, ns, _) = ProvinceGenerator.Slots(_p, pl.Cell);
                int m = Array.IndexOf(ns, c), s = pl.CenterSlot;
                // 가운데의 H[s−1] = 이웃의 H[m], 가운데의 H[s] = 이웃의 H[m−1]
                var a0 = center.ToLayout(HexFrame.HX[(s + 5) % 6], HexFrame.HY[(s + 5) % 6]);
                var a1 = center.ToLayout(HexFrame.HX[s], HexFrame.HY[s]);
                var b0 = pl.ToLayout(HexFrame.HX[m], HexFrame.HY[m]);
                var b1 = pl.ToLayout(HexFrame.HX[(m + 5) % 6], HexFrame.HY[(m + 5) % 6]);
                Assert.True(Math.Abs(a0.X - b0.X) + Math.Abs(a0.Y - b0.Y) < 1e-9);
                Assert.True(Math.Abs(a1.X - b1.X) + Math.Abs(a1.Y - b1.Y) < 1e-9);
                var back = pl.ToLocal(b0.X, b0.Y);
                Assert.True(Math.Abs(back.X - HexFrame.HX[m]) + Math.Abs(back.Y - HexFrame.HY[m]) < 1e-9);
            }
        }
    }

    [Fact]
    public void SaveLoadGivesSameProvince()
    {
        var w = GameWorld.Create(3, new WorldConfig { DotCount = 0, LandCells = 24_000, Climate = ClimateBias.Normal });
        int c = Find("river");
        var before = ProvinceGenerator.Generate(w.Planet, c).Fingerprint;
        var bytes = SaveGame.ToBytes(w);
        Assert.Equal(SaveGame.Version, BitConverter.ToInt32(bytes, 4));
        var loaded = SaveGame.FromBytes(bytes);
        ProvinceCache.Clear();
        var remade = PlanetGenerator.Generate(loaded.Seed, loaded.Config.PlanetSettings);
        Assert.Equal(before, ProvinceGenerator.Generate(remade, c).Fingerprint);
        Assert.Equal(before, loaded.Province(c).Fingerprint);
        Assert.Empty(loaded.ProvinceChanges);
    }

    [Fact]
    public void CacheForgetsOldMaps()
    {
        ProvinceCache.Clear();
        int n = 0;
        for (int c = 0; c < _p.CellCount && n < ProvinceCache.Capacity + 5; c++)
            if (_p.IsLand(c)) { ProvinceCache.Get(_p, c); n++; }
        Assert.Equal(ProvinceCache.Capacity, ProvinceCache.Count);
        ProvinceCache.Clear();
    }
}

[Trait("Category", "Fast")]
public class ObjectDataTests
{
    private static readonly string Items = """{ "items": [ { "id": "wood", "name": "나무" }, { "id": "apple", "name": "사과" } ] }""";
    private static string Objects(string objs) => "{ \"actions\": [ { \"id\": \"cut\", \"name\": \"베기\" }, { \"id\": \"pick\", \"name\": \"따기\" } ], \"objects\": [ " + objs + " ] }";
    private const string Tree = """{ "id": "apple_tree", "name": "사과나무", "kind": "tree", "art": "tree_apple", "resource": "wood", "density": 0.1, "biomes": { "temperate": 1 }, "grounds": { "grass": 1 }, "uses": [ { "action": "pick", "yields": ["apple"] }, { "action": "cut", "yields": ["wood"] } ] }""";

    private static IReadOnlyList<string> Problems(string items, string objects) =>
        Assert.Throws<DataValidationException>(() => ObjectCatalog.Load(new Dictionary<string, string> { [ObjectCatalog.ItemsFile] = items, [ObjectCatalog.ObjectsFile] = objects })).Problems;

    [Fact]
    public void RepositoryDataIsValid_AndEmbeddedCopyIsCurrent()
    {
        var files = ObjectCatalog.LoadDirectory(DataTests.FindDataDir());
        var cat = ObjectCatalog.Default;
        Assert.Equal(files.Objects.Select(o => o.Id + o.UsesText + o.Density), cat.Objects.Select(o => o.Id + o.UsesText + o.Density));
        Assert.Equal(files.Items.Select(i => i.Id), cat.Items.Select(i => i.Id));
        Assert.InRange(cat.Objects.Count, 20, 40);
        // 프롬프트의 예
        Assert.Equal("따기 → 사과 · 베기 → 나무", cat.Find("apple_tree")!.UsesText);
        Assert.Equal("베기 → 나무, 송진", cat.Find("pine")!.UsesText);
        Assert.Equal("깨기 → 돌", cat.Find("boulder")!.UsesText);
        Assert.Equal("캐기 → 구리 광석, 돌", cat.Find("copper_vein")!.UsesText);
        // 행성 자원과 같은 이름 체계: 같은 물건은 같은 아이디
        foreach (var id in new[] { "stone", "flint", "clay", "salt", "wood" })
        {
            Assert.Contains(id, ResourceKinds.Ids);
            Assert.Contains(cat.Items, i => i.Id == id);
        }
        // 사냥감 · 물고기(동물)를 뺀 행성 자원마다 지도에 나오는 오브젝트가 있다
        for (int r = 0; r < ResourceKinds.Count; r++)
            if (r != ResourceKinds.Game && r != ResourceKinds.Fish)
                Assert.Contains(cat.Objects, o => o.Resource == r);
        // 생물군계마다 나무나 덤불이 하나 이상(얼음은 빼고)
        for (int b = 2; b < BiomeIds.Count; b++)
            Assert.Contains(cat.Objects, o => (o.IsTree || o.Kind == "bush" || o.Kind == "plant") && o.BiomeWeight[b] > 0);
    }

    [Fact]
    public void GoodDataLoads()
    {
        var cat = ObjectCatalog.Load(new Dictionary<string, string> { [ObjectCatalog.ItemsFile] = Items, [ObjectCatalog.ObjectsFile] = Objects(Tree) });
        Assert.Single(cat.Objects);
        Assert.Equal(ResourceKinds.Wood, cat.Objects[0].Resource);
        Assert.Equal(1.0, cat.Objects[0].BiomeWeight[(int)Biome.Temperate]);
    }

    [Fact]
    public void CatchesDuplicateAndUnknownIds()
    {
        var p = Problems(Items, Objects(Tree + ", " + Tree + ", " + Tree.Replace("apple_tree", "pear_tree").Replace("\"apple\"", "\"pear\"")));
        Assert.Contains(p, s => s.Contains("2번째 항목(apple_tree)") && s.Contains("겹칩니다"));
        Assert.Contains(p, s => s.Contains("'pear'") && s.Contains("items.json 에 없습니다"));

        var q = Problems(Items, Objects(Tree.Replace("\"pick\"", "\"lick\"").Replace("\"temperate\"", "\"jungle\"").Replace("\"grass\"", "\"lava\"").Replace("\"resource\": \"wood\"", "\"resource\": \"gold\"")));
        Assert.Contains(q, s => s.Contains("'lick'") && s.Contains("actions"));
        Assert.Contains(q, s => s.Contains("'jungle'") && s.Contains("생물군계"));
        Assert.Contains(q, s => s.Contains("'lava'") && s.Contains("땅 종류"));
        Assert.Contains(q, s => s.Contains("'gold'") && s.Contains("resources.json"));
    }

    [Fact]
    public void CatchesMissingFieldsAndBadValues()
    {
        var p = Problems(Items, Objects("""{ "id": "x", "kind": "tree", "art": "a", "density": 3, "biomes": {}, "grounds": {} }"""));
        Assert.Contains(p, s => s.Contains("'name'"));
        Assert.Contains(p, s => s.Contains("density") && s.Contains("0~1"));
        Assert.Contains(p, s => s.Contains("'uses'"));
        var q = Problems("""{ "items": [ { "id": "a", "name": "가" }, { "id": "a", "name": "나" } ] }""", "{ \"objects\": [] }");
        Assert.Contains(q, s => s.Contains("items.json") && s.Contains("겹칩니다"));
        Assert.Contains(q, s => s.Contains("actions"));
        var r = Problems(Items, Objects(Tree.Replace("\"kind\": \"tree\"", "\"kind\": \"robot\"")));
        Assert.Contains(r, s => s.Contains("robot"));
        var bad = Problems(Items, "{ \"objects\": [ ");
        Assert.Contains(bad, s => s.Contains("문법"));
    }
}
