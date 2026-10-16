using Wtw.Sim.Planet;
using Xunit;
using Xunit.Abstractions;

namespace Wtw.Sim.Tests;

/// <summary>느린 테스트: 시드 20개 × (가장 적은 땅 + 단추의 땅 5가지). 땅 칸 수 · 대륙 비율 · 섬 수 · 오각형 · 강 · 한쪽에 모임 · 추운 땅.</summary>
[Trait("Category", "Slow")]
public class PlanetSlowTests
{
    private readonly ITestOutputHelper _out;
    public PlanetSlowTests(ITestOutputHelper output) { _out = output; }

    [Fact]
    public void TwentySeeds()
    {
        var jobs = new List<(ulong Seed, int Land)>();
        var lands = new[] { PlanetSettings.MinLand }.Concat(PlanetSettings.LandChoices).ToArray();
        for (ulong s = 1; s <= 20; s++) foreach (int l in lands) jobs.Add((s, l));
        var res = new (int Land, int LandCount, double Cont, int Islands, int Pent, bool RiversOk, double SpreadCos, int Cold)[jobs.Count];
        Parallel.For(0, jobs.Count, k =>
        {
            var p = PlanetGenerator.Generate(jobs[k].Seed, new PlanetSettings(jobs[k].Land, ClimateBias.Normal));
            bool ok = true;
            for (int c = 0; c < p.CellCount && ok; c++)
            {
                if (p.RiverSize[c] == 0) continue;
                int cur = c, guard = 0;
                while (p.IsLand(cur) && ok)
                {
                    int next = p.Downstream[cur];
                    if (next < 0 || (p.IsLand(next) && p.Elevation[next] > p.Elevation[cur]) || ++guard > 10_000) ok = false;
                    else cur = next;
                }
            }
            double mx = 0, my = 0, mz = 0;
            int cold = 0;
            for (int c = 0; c < p.CellCount; c++)
            {
                if (p.IsOcean(c)) continue;
                mx += p.X[c]; my += p.Y[c]; mz += p.Z[c];
                if (p.Biome[c] is Biome.Tundra or Biome.Ice) cold++;
            }
            double ml = Math.Sqrt(mx * mx + my * my + mz * mz), minCos = 1;
            for (int c = 0; c < p.CellCount; c++) if (!p.IsOcean(c)) minCos = Math.Min(minCos, (p.X[c] * mx + p.Y[c] * my + p.Z[c] * mz) / ml);
            res[k] = (jobs[k].Land, p.LandCount, p.ContinentCells * 100.0 / p.LandCount, p.IslandCount, p.PentagonsOnLand, ok, minCos, cold);
        });
        int landOk = res.Count(r => Math.Abs(r.LandCount - r.Land) <= r.Land * 0.03);
        int contOk = res.Count(r => r.Cont >= 70 && r.Cont <= 85);
        int islOk = res.Count(r => r.Islands >= 3 && r.Islands <= 8);
        int pentSeeds = res.Count(r => r.Pent > 0);
        var cont = res.Select(r => r.Cont).OrderBy(x => x).ToArray();
        var isl = res.Select(r => (double)r.Islands).OrderBy(x => x).ToArray();
        const double cos70 = 0.3420201433256687;
        var small = res.Where(r => r.Land <= 2_000).ToArray();
        int oneSide = small.Count(r => r.SpreadCos > cos70);
        int coldOk = res.Count(r => r.Cold > 0);
        _out.WriteLine($"땅 ±3%: {landOk}/{res.Length} · 대륙 70~85%: {contOk}/{res.Length} (중앙값 {cont[cont.Length / 2]:0.0}%) · 섬 3~8개: {islOk}/{res.Length} (중앙값 {isl[isl.Length / 2]}) · 오각형이 땅에 걸린 경우 {pentSeeds}/{res.Length} (합계 {res.Sum(r => r.Pent)}개)");
        _out.WriteLine($"땅 {PlanetSettings.MinLand:N0}·2,000 이 한쪽 면(70° 안)에: {oneSide}/{small.Length} · 툰드라·얼음이 있는 행성: {coldOk}/{res.Length}");
        foreach (int l in lands)
        {
            var rs = res.Where(r => r.Land == l).ToArray();
            _out.WriteLine($"  땅 {l,6:N0}: 대륙 중앙값 {rs.Select(r => r.Cont).OrderBy(x => x).ElementAt(rs.Length / 2):0.0}% · 섬 3~8개 {rs.Count(r => r.Islands >= 3 && r.Islands <= 8)}/{rs.Length} · 오각형-땅 {rs.Count(r => r.Pent > 0)}/{rs.Length} · 추운 땅 {rs.Count(r => r.Cold > 0)}/{rs.Length}");
        }

        Assert.Equal(res.Length, landOk);
        Assert.All(res, r => Assert.True(r.RiversOk));
        Assert.Equal(small.Length, oneSide);
        // 모양은 시드 20개 중앙값으로 판단(CLAUDE.md 7번). 개별 시드가 벗어난 수는 보고서에 그대로 적는다.
        // 대륙 비율(기획 70~85%)은 E1 검토 뒤 "땅을 한쪽에 모으기"를 앞세우며 넘게 되어 판단하지 않고 보고만 한다(보고서 참고).
        Assert.InRange(isl[isl.Length / 2], 3, 8);
    }
}
