using System.Text;
using Wtw.Sim.People;
using Wtw.Sim.Planet;
using Wtw.Sim.Provinces;
using Wtw.Sim.World;

/// <summary>
/// 작은 물(E3b) 측정: 시드마다 땅 칸을 무작위로 골라 지방 지도를 만들고, 생물군계별로 "마실 민물이 있는 지방" 비율을 잰다.
///   dotnet run -c Release --project tools/bench -- people water [--seeds 20] [--cells 30] [--seed 1] [--out 파일.md]
/// "있다" = 지방에서 가장 큰 이어진 땅(걸을 수 있는 곳)에 민물을 마실 자리가 하나라도 있다.
/// </summary>
public static class WaterBench
{
    public static void Run(int seeds, int cells, ulong first, int parallel, string? outPath)
    {
        int nb = BiomeIds.Count;
        var has = new int[seeds, nb]; var all = new int[seeds, nb];
        var tiles = new long[seeds, nb]; var precip = new List<int>[nb];
        for (int b = 0; b < nb; b++) precip[b] = new List<int>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Parallel.For(0, seeds, new ParallelOptions { MaxDegreeOfParallelism = parallel }, k =>
        {
            ulong seed = first + (ulong)k;
            var w = GameWorld.Create(seed, new WorldConfig { DotCount = 0 });
            var p = w.Planet;
            var land = Enumerable.Range(0, p.CellCount).Where(p.IsLand).ToList();
            var rng = new Wtw.Sim.Core.Pcg32(seed, 77);
            for (int i = 0; i < cells; i++)
            {
                int cell = land[rng.NextInt(land.Count)];
                var map = ProvinceGenerator.Generate(p, cell);
                var life = new ProvinceLife(p, map, PeopleData.Default);
                int b = (int)p.Biome[cell];
                // 가장 큰 이어진 땅
                var count = new Dictionary<int, int>();
                foreach (int r in life.Region) if (r >= 0) count[r] = count.GetValueOrDefault(r) + 1;
                int big = count.Count == 0 ? -1 : count.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).First().Key;
                bool ok = big >= 0 && life.RegionHasDrink[big];
                lock (precip) { all[k, b]++; if (ok) has[k, b]++; tiles[k, b] += life.DrinkTiles; precip[b].Add(p.PrecipMm[cell]); }
            }
        });
        var sb = new StringBuilder();
        sb.AppendLine($"## 마실 민물이 있는 지방: 시드 {seeds}개 × 땅 칸 {cells}곳(무작위) — {sw.Elapsed.TotalSeconds:0}초");
        sb.AppendLine();
        sb.AppendLine("| 생물군계 | 지방 수 | 민물 있음 | 비율 | 마실 자리 타일(평균) | 강수 중앙값(mm) |");
        sb.AppendLine("|---|---|---|---|---|---|");
        for (int b = 1; b < nb; b++)
        {
            int a = 0, h = 0; long t = 0;
            for (int k = 0; k < seeds; k++) { a += all[k, b]; h += has[k, b]; t += tiles[k, b]; }
            if (a == 0) continue;
            var pr = precip[b].OrderBy(x => x).ToList();
            sb.AppendLine($"| {PlanetNames.Of((Biome)b)} | {a} | {h} | **{h * 100.0 / a:0}%** | {t / a} | {pr[pr.Count / 2]} |");
        }
        int A = 0, H = 0; for (int k = 0; k < seeds; k++) for (int b = 0; b < nb; b++) { A += all[k, b]; H += has[k, b]; }
        sb.AppendLine();
        sb.AppendLine($"- 모두: {A}곳 중 {H}곳({H * 100.0 / Math.Max(1, A):0}%)");
        Console.Write(sb.ToString());
        if (outPath is not null) File.WriteAllText(outPath, sb.ToString());
    }
}
