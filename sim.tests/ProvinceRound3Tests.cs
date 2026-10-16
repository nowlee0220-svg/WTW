using Wtw.Sim.Data;
using Wtw.Sim.Planet;
using Wtw.Sim.Provinces;
using Wtw.Sim.Time;
using Xunit;
using Xunit.Abstractions;

namespace Wtw.Sim.Tests;

/// <summary>E2 검토 2차: 화산 · 돌 종류 · 돌 성질 · 지방 정보 칸.</summary>
[Trait("Category", "Fast")]
public class ProvinceRound3Tests : IClassFixture<ProvinceFixture>
{
    private readonly Planet.Planet _p;
    private readonly ITestOutputHelper _out;
    public ProvinceRound3Tests(ProvinceFixture f, ITestOutputHelper output) { _p = f.P; _out = output; }

    private int Find(string kind)
    {
        int c = ProvinceSamples.Find(_p, kind);
        Assert.True(c >= 0, $"{kind} 칸이 없음");
        return c;
    }

    [Fact]
    public void VolcanoesAreFew_NearMountains_AndShowInCellInfo()
    {
        var v = Volcanoes.All(_p);
        _out.WriteLine($"화산 {v.Count}개: {string.Join(", ", v)}");
        Assert.InRange(v.Count, 2, 6);
        foreach (int c in v)
        {
            Assert.True(_p.IsLand(c));
            bool mtn = _p.Terrain[c] is Terrain.Mountain or Terrain.HighMountain
                || _p.Grid.Neighbors(c).ToArray().Any(nb => _p.Terrain[nb] is Terrain.Mountain or Terrain.HighMountain);
            Assert.True(mtn, $"화산 {c} 이 산맥 둘레가 아님");
            Assert.Equal(0, Volcanoes.Distance(_p, c));
            Assert.Equal("basalt", RockCatalog.Default.Rocks[Geology.RocksOf(_p, c).Primary].Id);
            var rep = CellDescriber.Describe(_p, c);
            Assert.StartsWith("화산", rep.Title);
            Assert.Contains(rep.Facts, f => f.Label == "화산");
        }
        // 같은 시드 → 같은 화산
        var again = PlanetGenerator.Generate(3, new PlanetSettings(24_000, ClimateBias.Normal));
        Assert.Equal(v, Volcanoes.All(again));
    }

    [Fact]
    public void VolcanoProvince_HasCraterInsideImpassableCone_BasaltAndObsidianAround()
    {
        var m = ProvinceGenerator.Generate(_p, Find("volcano"));
        var cat = ObjectCatalog.Default;
        int obs = cat.IndexOf("obsidian_shards");
        int basalt = RockCatalog.Default.Rocks.ToList().FindIndex(r => r.Id == "basalt");
        int crater = 0, obsidian = 0, stone = 0, basaltTiles = 0;
        for (int i = 0; i < HexFrame.TileCount; i++)
        {
            if (!HexFrame.Inside[i]) continue;
            if (m.Ground[i] == Ground.Crater)
            {
                crater++;
                int x = i % HexFrame.Size - HexFrame.Size / 2, y = i / HexFrame.Size - HexFrame.Size / 2;
                Assert.True(x * x + y * y < 14 * 14, "분화구는 가운데에");
                Assert.False(GroundInfo.IsWalkable(m.Ground[i]));
            }
            if (m.Ground[i] == Ground.Stone && (m.Flags[i] & TileFlags.Volcano) != 0) stone++;
            if (m.ObjectAt(i) == obs) obsidian++;
            if ((m.Flags[i] & TileFlags.Volcano) != 0 && m.Rock[i] == basalt) basaltTiles++;
        }
        _out.WriteLine($"분화구 {crater} · 화산 바위 {stone} · 현무암 {basaltTiles} · 흑요석 {obsidian}");
        Assert.True(crater > 50);
        Assert.True(stone > crater * 3, "분화구를 둘러싼 지나갈 수 없는 산");
        Assert.True(basaltTiles > stone);
        Assert.True(obsidian > 10);
        // 분화구에서 바깥(지방 경계)까지 걸어서 나갈 수 없다(산이 둘러쌈) — 분화구 옆 걸을 수 있는 타일이 없다
        for (int i = 0; i < HexFrame.TileCount; i++)
        {
            if (m.Ground[i] != Ground.Crater) continue;
            int x = i % HexFrame.Size, y = i / HexFrame.Size;
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int j = (y + dy) * HexFrame.Size + x + dx;
                Assert.False(GroundInfo.IsWalkable(m.Ground[j]), "분화구 바로 옆이 걸을 수 있는 땅");
            }
        }
    }

    [Fact]
    public void StoneKinds_FollowRockAndVolcano()
    {
        var cat = ObjectCatalog.Default;
        var rocks = RockCatalog.Default.Rocks.Select(r => r.Id).ToList();
        int obs = cat.IndexOf("obsidian_shards"), flint = cat.IndexOf("flint_nodules"), quartz = cat.IndexOf("quartz_pebbles"), loose = cat.IndexOf("loose_stones");
        Assert.True(obs >= 0 && flint >= 0 && quartz >= 0 && loose >= 0);
        int flintAll = 0, flintLime = 0, quartzAll = 0, quartzGranite = 0, looseAll = 0, graniteTiles = 0, tiles = 0;
        var cells = ProvinceSamples.Kinds.Where(k => k != "pentagon").Select(Find).Distinct().ToList();
        foreach (int c in cells)
        {
            var m = ProvinceGenerator.Generate(_p, c);
            for (int i = 0; i < HexFrame.TileCount; i++)
            {
                if (!HexFrame.Inside[i] || !GroundInfo.IsWalkable(m.Ground[i])) continue;
                string rock = m.Rock[i] < rocks.Count ? rocks[m.Rock[i]] : "-";
                tiles++; if (rock == "granite") graniteTiles++;
                int o = m.ObjectAt(i);
                if (o < 0) continue;
                if (o == obs) Assert.Fail($"화산이 먼 칸 {c} 에 흑요석"); // 화산 둘레에만
                if (o == flint) { flintAll++; if (rock == "limestone") flintLime++; }
                if (o == quartz) { quartzAll++; if (rock == "granite") quartzGranite++; }
                if (o == loose) looseAll++;
            }
        }
        _out.WriteLine($"흔한 돌 {looseAll} · 부싯돌 {flintAll} (석회암 위 {flintLime}) · 석영 {quartzAll} (화강암 위 {quartzGranite}, 화강암 땅 {graniteTiles}/{tiles})");
        foreach (int c in cells) Assert.True(Volcanoes.Distance(_p, c) is < 0 or > 1, "표본 칸이 화산 가까이");
        Assert.True(looseAll > 100);
        Assert.True(flintAll > 0 && flintLime * 2 > flintAll, "부싯돌은 주로 석회암 땅에");
        // 석영: 화강암 땅에서 같은 넓이당 훨씬 자주(다른 바위 땅의 5배 넘게)
        if (quartzAll > 0 && graniteTiles > 0)
            Assert.True((double)quartzGranite / graniteTiles > 5.0 * (quartzAll - quartzGranite) / Math.Max(1, tiles - graniteTiles), "석영은 화강암 땅에서 더 자주");
    }

    [Fact]
    public void StoneItemsHaveTraits_WithKnownPropertyNames()
    {
        var props = ContentLoader.LoadDirectory(DataTests.FindDataDir()).Properties.Select(p => p.Id).ToHashSet();
        var cat = ObjectCatalog.Default;
        foreach (var id in new[] { "stone", "flint", "obsidian", "quartz", "granite", "limestone", "sandstone", "basalt", "slate" })
        {
            var it = cat.Items.FirstOrDefault(i => i.Id == id);
            Assert.True(it is not null, id);
            Assert.True(it!.Traits.Count >= 3, $"{id} 의 성질");
            foreach (var (k, v) in it.Traits)
            {
                Assert.Contains(k, props);
                Assert.Contains(v, ItemDef.TraitLevels);
            }
        }
        // 흑요석 · 부싯돌은 날이 잘 서고, 흔한 돌은 아니다
        Assert.Equal("아주 높음", cat.Items.First(i => i.Id == "obsidian").Traits["sharpens"]);
        Assert.NotEqual("낮음", cat.Items.First(i => i.Id == "flint").Traits["sharpens"]);
        Assert.Equal("낮음", cat.Items.First(i => i.Id == "stone").Traits["sharpens"]);
    }

    [Fact]
    public void ProvinceInfo_HasAllItems_LocalTimeAndDayNight()
    {
        int c = Find("plain");
        var m = ProvinceGenerator.Generate(_p, c);
        var facts = ProvinceReport.Describe(_p, m, 0);
        foreach (var label in new[] { "이 지방의 시각", "지금 기온", "연평균 기온", "해의 높이", "바람", "강수", "높이", "생물군계", "바위", "물", "땅의 비옥함", "나는 자원" })
        {
            var f = facts.FirstOrDefault(x => x.Label == label);
            Assert.True(f is not null, label);
            Assert.False(string.IsNullOrWhiteSpace(f!.Value), label);
            Assert.False(string.IsNullOrWhiteSpace(f.Why), label + " 자세히 보기");
        }
        // 경도가 다르면 그 지방의 시각이 다르다(15° = 1시간)
        var (_, lonA) = _p.LatLon(c);
        int far = Enumerable.Range(0, _p.CellCount).First(o => Math.Abs(_p.LatLon(o).Lon - lonA) is > 80 and < 100);
        double ha = ProvinceReport.LocalHour(_p, c, 0), hb = ProvinceReport.LocalHour(_p, far, 0);
        double diff = Math.Abs(ha - hb); diff = Math.Min(diff, 24 - diff);
        Assert.InRange(diff, 80 / 15.0 - 0.01, 100 / 15.0 + 0.01);
        // 한낮이 한밤보다 따뜻하고, 사막은 낮밤 차이가 더 크다
        Assert.True(ProvinceReport.TemperatureNow(_p, c, 15) > ProvinceReport.TemperatureNow(_p, c, 3));
        Assert.True(ProvinceReport.TemperatureNow(_p, c, 6) <= ProvinceReport.TemperatureNow(_p, c, 9));
        Assert.True(ProvinceReport.DailyRange(_p, Find("desert"), out _) > ProvinceReport.DailyRange(_p, Find("forest"), out _));
        // 해는 6시에 뜨고 18시에 진다(계절 없음): 정오에 가장 높고 한밤엔 지평선 아래
        Assert.True(ProvinceReport.SunAltitude(30, 12) > ProvinceReport.SunAltitude(30, 9));
        Assert.True(ProvinceReport.SunAltitude(30, 0) < 0);
        Assert.InRange(ProvinceReport.SunAltitude(30, 6), -0.01, 0.01);
        // 시간이 지나면 시각 줄이 바뀐다
        double hourTicks = GameTime.TicksPerHour;
        Assert.NotEqual(facts[0].Value, ProvinceReport.Describe(_p, m, hourTicks * 3)[0].Value);
    }
}
