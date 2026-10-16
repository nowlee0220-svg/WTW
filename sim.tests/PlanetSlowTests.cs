using Wtw.Sim.Planet;
using Xunit;
using Xunit.Abstractions;

namespace Wtw.Sim.Tests;

/// <summary>느린 테스트: 시드 20개 × 땅 3가지. 땅 칸 수 · 대륙 비율 · 섬 수 · 오각형 · 강을 본다.</summary>
[Trait("Category", "Slow")]
public class PlanetSlowTests
{
    private readonly ITestOutputHelper _out;
    public PlanetSlowTests(ITestOutputHelper output) { _out = output; }

    [Fact]
    public void TwentySeeds()
    {
        var jobs = new List<(ulong Seed, int Land)>();
        for (ulong s = 1; s <= 20; s++) foreach (int l in PlanetSettings.LandChoices) jobs.Add((s, l));
        var res = new (int Land, int LandCount, double Cont, int Islands, int Pent, bool RiversOk)[jobs.Count];
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
            res[k] = (jobs[k].Land, p.LandCount, p.ContinentCells * 100.0 / p.LandCount, p.IslandCount, p.PentagonsOnLand, ok);
        });
        int landOk = res.Count(r => Math.Abs(r.LandCount - r.Land) <= r.Land * 0.03);
        int contOk = res.Count(r => r.Cont >= 70 && r.Cont <= 85);
        int islOk = res.Count(r => r.Islands >= 3 && r.Islands <= 8);
        int pentSeeds = res.Count(r => r.Pent > 0);
        var cont = res.Select(r => r.Cont).OrderBy(x => x).ToArray();
        var isl = res.Select(r => (double)r.Islands).OrderBy(x => x).ToArray();
        _out.WriteLine($"땅 ±3%: {landOk}/{res.Length} · 대륙 70~85%: {contOk}/{res.Length} (중앙값 {cont[cont.Length / 2]:0.0}%) · 섬 3~8개: {islOk}/{res.Length} (중앙값 {isl[isl.Length / 2]}) · 오각형이 땅에 걸린 경우 {pentSeeds}/{res.Length} (합계 {res.Sum(r => r.Pent)}개)");

        Assert.Equal(res.Length, landOk);
        Assert.All(res, r => Assert.True(r.RiversOk));
        // 모양은 시드 20개 중앙값으로 판단(CLAUDE.md 7번). 개별 시드가 벗어난 수는 보고서에 그대로 적는다.
        Assert.InRange(cont[cont.Length / 2], 70, 85);
        Assert.InRange(isl[isl.Length / 2], 3, 8);
    }
}
