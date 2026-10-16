using System.Text.Json;
using Wtw.Sim.Core;
using Wtw.Sim.Persistence;
using Wtw.Sim.Planet;
using Wtw.Sim.World;
using Xunit;

namespace Wtw.Sim.Tests;

[Trait("Category", "Fast")]
public class GridTests
{
    private static GeoGrid G => GeoGrid.Shared;

    [Fact]
    public void CountsMatchGoldberg72()
    {
        Assert.Equal(51_842, G.CellCount);
        Assert.Equal(20 * 72 * 72, G.TriangleCount);
        int pent = 0, hex = 0;
        for (int c = 0; c < G.CellCount; c++)
        {
            int k = G.NeighborCount(c);
            if (k == 5) pent++;
            else if (k == 6) hex++;
            else Assert.Fail($"칸 {c} 의 이웃 {k}개");
            Assert.Equal(k == 5, G.IsPentagon(c));
        }
        Assert.Equal(12, pent);
        Assert.Equal(51_830, hex);
    }

    [Fact]
    public void NeighborsAreMutual()
    {
        for (int c = 0; c < G.CellCount; c++)
            foreach (int nb in G.Neighbors(c))
            {
                Assert.NotEqual(c, nb);
                Assert.True(G.Neighbors(nb).Contains(c), $"{c} 의 이웃 {nb} 의 이웃에 {c} 가 없음");
            }
    }

    [Fact]
    public void NeighborsAreClockwiseAndCornersMatch()
    {
        for (int c = 0; c < G.CellCount; c++)
        {
            var nbs = G.Neighbors(c);
            var corners = G.Corners(c);
            int k = nbs.Length;
            // 가장 작은 번호에서 시작
            int min = int.MaxValue;
            foreach (int nb in nbs) min = Math.Min(min, nb);
            Assert.Equal(min, nbs[0]);
            for (int i = 0; i < k; i++)
            {
                int a = nbs[i], b = nbs[(i + 1) % k];
                // 바깥에서 볼 때 시계 방향: (a − c) × (b − c) 가 바깥(c 방향)과 반대
                double ax = G.Cx[a] - G.Cx[c], ay = G.Cy[a] - G.Cy[c], az = G.Cz[a] - G.Cz[c];
                double bx = G.Cx[b] - G.Cx[c], by = G.Cy[b] - G.Cy[c], bz = G.Cz[b] - G.Cz[c];
                double nx = ay * bz - az * by, ny = az * bx - ax * bz, nz = ax * by - ay * bx;
                Assert.True(nx * G.Cx[c] + ny * G.Cy[c] + nz * G.Cz[c] < 0, $"칸 {c} 이웃 순서가 시계 방향이 아님");
                // 꼭짓점 i 는 c, a, b 세 칸이 만나는 삼각형
                int t = corners[i];
                var tri = new[] { G.TriA[t], G.TriB[t], G.TriC[t] };
                Assert.Contains(c, tri); Assert.Contains(a, tri); Assert.Contains(b, tri);
            }
        }
    }

    [Fact]
    public void IcosahedronFacesFaceOutward()
    {
        var v = GeoGrid.IcoVertices();
        for (int f = 0; f < 20; f++)
        {
            var a = v[GeoGrid.IcoFaces[f, 0]]; var b = v[GeoGrid.IcoFaces[f, 1]]; var c = v[GeoGrid.IcoFaces[f, 2]];
            double ux = b.X - a.X, uy = b.Y - a.Y, uz = b.Z - a.Z, wx = c.X - a.X, wy = c.Y - a.Y, wz = c.Z - a.Z;
            double nx = uy * wz - uz * wy, ny = uz * wx - ux * wz, nz = ux * wy - uy * wx;
            Assert.True(nx * (a.X + b.X + c.X) + ny * (a.Y + b.Y + c.Y) + nz * (a.Z + b.Z + c.Z) > 0);
        }
    }

    /// <summary>칸 번호 · 이웃 순서는 정수 규칙으로만 정해진다 → 어느 기계에서나 이 값.</summary>
    [Fact]
    public void TopologyFingerprintIsPinned()
    {
        ulong h = 0;
        for (int i = 0; i < G.Nbr.Length; i++) h = StableHash.Combine(h, (ulong)G.Nbr[i], (ulong)G.Corner[i]);
        Assert.Equal(TopologyPin, h);
    }

    internal const ulong TopologyPin = 11854566979066830839UL;

    [Fact]
    public void SmallGridWorks()
    {
        var g = GeoGrid.Build(4);
        Assert.Equal(162, g.CellCount);
        for (int c = 0; c < g.CellCount; c++)
            foreach (int nb in g.Neighbors(c)) Assert.True(g.Neighbors(nb).Contains(c));
    }
}

/// <summary>행성 몇 벌을 한 번만 만들어 여러 테스트가 나눠 쓴다(캐시를 거치지 않고 직접 만든다).</summary>
public sealed class PlanetFixture
{
    public readonly Planet.Planet A16 = PlanetGenerator.Generate(1, new PlanetSettings(16_000, ClimateBias.Normal));
    public readonly Planet.Planet A24 = PlanetGenerator.Generate(1, new PlanetSettings(24_000, ClimateBias.Normal));
    public readonly Planet.Planet B16 = PlanetGenerator.Generate(2, new PlanetSettings(16_000, ClimateBias.Normal));
}

[Trait("Category", "Fast")]
public class PlanetTests : IClassFixture<PlanetFixture>
{
    private readonly PlanetFixture _f;
    public PlanetTests(PlanetFixture f) { _f = f; }

    [Fact]
    public void SameSeedSamePlanet_DifferentSeedDifferent()
    {
        var again = PlanetGenerator.Generate(1, new PlanetSettings(16_000, ClimateBias.Normal));
        Assert.Equal(_f.A16.Fingerprint, again.Fingerprint);
        Assert.Equal(_f.A16.Elevation, again.Elevation);
        Assert.NotEqual(_f.A16.Fingerprint, _f.B16.Fingerprint);
        var cold = PlanetGenerator.Generate(1, new PlanetSettings(16_000, ClimateBias.Cold));
        Assert.NotEqual(_f.A16.Fingerprint, cold.Fingerprint);
        Assert.Equal(_f.A16.Elevation, cold.Elevation); // 기후 경향은 땅 모양을 바꾸지 않는다
    }

    /// <summary>실수 계산이 기계마다 같다는 확인: 이 값이 CI(다른 컴퓨터)에서도 같아야 한다.</summary>
    [Fact]
    public void FingerprintIsPinned()
    {
        Assert.Equal(PlanetPin, _f.A16.Fingerprint);
    }

    internal const ulong PlanetPin = 16886073291844412963UL; // 생성 규칙을 바꾸면 이 값도 바뀐다(일부러 바꾼 것인지 확인)

    [Fact]
    public void LandCountWithin3Percent()
    {
        foreach (var p in new[] { _f.A16, _f.A24, _f.B16 })
            Assert.InRange(p.LandCount, p.Settings.LandCells * 0.97, p.Settings.LandCells * 1.03);
    }

    [Fact]
    public void MoreLandIsSamePlanetTurned_TheSameWay()
    {
        // 같은 시드면 땅의 양과 무관하게 같은 방향 · 같은 땅 값 → 24,000 의 땅은 16,000 의 땅을 거의 다 품는다
        Assert.Equal(_f.A16.X, _f.A24.X);
        int inside = 0, land16 = 0;
        for (int c = 0; c < _f.A16.CellCount; c++)
            if (!_f.A16.IsOcean(c)) { land16++; if (!_f.A24.IsOcean(c)) inside++; }
        Assert.True(inside >= land16 * 0.99, $"{inside}/{land16}");
    }

    [Fact]
    public void RiversFlowDownhillAndEndInSeaOrLake()
    {
        foreach (var p in new[] { _f.A16, _f.B16 })
        {
            int rivers = 0;
            for (int c = 0; c < p.CellCount; c++)
            {
                if (p.RiverSize[c] == 0) continue;
                rivers++;
                Assert.True(p.IsLand(c));
                int cur = c, guard = 0;
                while (p.IsLand(cur))
                {
                    int next = p.Downstream[cur];
                    Assert.True(next >= 0);
                    Assert.True(p.Grid.Neighbors(cur).Contains(next), "물은 이웃 칸으로만 흐른다");
                    Assert.True(p.Elevation[next] <= p.Elevation[cur] || !p.IsLand(next), $"강이 오르막으로: {cur}({p.Elevation[cur]}) → {next}({p.Elevation[next]})");
                    if (p.IsLand(next)) Assert.True(p.RiverSize[next] >= p.RiverSize[cur], "강은 내려갈수록 작아지지 않는다");
                    cur = next;
                    Assert.True(++guard < 10_000, "강이 끝나지 않음");
                }
                Assert.True(p.IsOcean(cur) || p.Terrain[cur] == Terrain.Lake);
            }
            Assert.True(rivers > 300, $"강 칸 {rivers}");
        }
    }

    [Fact]
    public void ShapeAndContent()
    {
        foreach (var p in new[] { _f.A16, _f.B16 })
        {
            var terrains = new HashSet<Terrain>();
            var biomes = new HashSet<Biome>();
            for (int c = 0; c < p.CellCount; c++) { terrains.Add(p.Terrain[c]); biomes.Add(p.Biome[c]); }
            foreach (var t in new[] { Terrain.Plain, Terrain.Hill, Terrain.Plateau, Terrain.Mountain, Terrain.Lake, Terrain.ShallowSea, Terrain.DeepSea })
                Assert.Contains(t, terrains);
            Assert.True(biomes.Count >= 8, $"생물군계 {biomes.Count}종");
            Assert.True(p.IslandCount >= 1);
            for (int r = 0; r < ResourceKinds.Count; r++)
            {
                int n = 0;
                for (int c = 0; c < p.CellCount; c++) if (p.IsLand(c) && p.Resource(c, r) > 0) n++;
                Assert.True(n > 20, $"자원 {ResourceKinds.Ids[r]} 칸 {n}");
            }
        }
    }

    [Fact]
    public void ResourceIdsMatchData()
    {
        var path = Path.Combine(DataTests.FindDataDir(), "resources.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var ids = doc.RootElement.GetProperty("resources").EnumerateArray().Select(e => e.GetProperty("id").GetString()).ToArray();
        Assert.Equal(ResourceKinds.Ids, ids);
        var names = doc.RootElement.GetProperty("resources").EnumerateArray().Select(e => e.GetProperty("name").GetString()).ToArray();
        Assert.Equal(ResourceKinds.Names, names);
    }

    [Fact]
    public void ExplanationsReadWell()
    {
        var p = _f.A16;
        bool sawShadow = false;
        for (int c = 0; c < p.CellCount; c += 7)
        {
            var r = CellDescriber.Describe(p, c);
            Assert.False(string.IsNullOrWhiteSpace(r.Title));
            Assert.NotEmpty(r.Facts);
            Assert.NotEmpty(r.Why);
            if (p.ShadowPct[c] >= 30 && p.PrecipMm[c] < 800) { Assert.Contains(r.Why, w => w.Contains("비그늘")); sawShadow = true; }
        }
        Assert.True(sawShadow);
    }

    [Fact]
    public void ProvinceSeedsAreStableAndDistinct()
    {
        var p = _f.A16;
        Assert.Equal(p.ProvinceSeed(100), _f.A24.ProvinceSeed(100));
        Assert.NotEqual(p.ProvinceSeed(100), p.ProvinceSeed(101));
        Assert.NotEqual(p.ProvinceSeed(100), _f.B16.ProvinceSeed(100));
    }

    [Fact]
    public void SaveLoadGivesSamePlanet()
    {
        var w = GameWorld.Create(2, new WorldConfig { DotCount = 0, LandCells = 16_000, Climate = ClimateBias.Normal });
        var bytes = SaveGame.ToBytes(w);
        var loaded = SaveGame.FromBytes(bytes);
        Assert.Equal(w.Config, loaded.Config);
        Assert.Equal(bytes, SaveGame.ToBytes(loaded));
        // 불러온 설정으로 새로 만든 행성 = 원래 행성 (캐시를 거치지 않음)
        var remade = PlanetGenerator.Generate(loaded.Seed, loaded.Config.PlanetSettings);
        Assert.Equal(_f.B16.Fingerprint, remade.Fingerprint);
        Assert.Equal(_f.B16.Fingerprint, loaded.Planet.Fingerprint);
    }

    [Fact]
    public void SaveKeepsPlanetSettings()
    {
        var w = GameWorld.Create(9, new WorldConfig { DotCount = 3, LandCells = 24_000, Climate = ClimateBias.Warm });
        var loaded = SaveGame.FromBytes(SaveGame.ToBytes(w));
        Assert.Equal(24_000, loaded.Config.LandCells);
        Assert.Equal(ClimateBias.Warm, loaded.Config.Climate);
    }
}
