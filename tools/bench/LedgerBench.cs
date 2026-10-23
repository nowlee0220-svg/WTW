using System.Text;
using Wtw.Sim.Core;
using Wtw.Sim.People;
using Wtw.Sim.Planet;
using Wtw.Sim.World;

/// <summary>
/// (T1) 먹을 것 장부. dotnet run -c Release --project tools/bench -- people ledger [--seeds 4] [--per 6] [--out 파일.md]
///   시드(행성)마다 생물군계마다 지방 per 곳을 골라(안정 해시 순) 하루에 다시 나는 먹을 것을 갈래마다 셈하고,
///   사람 하나가 하루에 먹는 양(PeopleSim.DailyNeed)으로 나눠 "그 땅이 이론상 먹여 살리는 사람 수"를 낸다(시드 × 지방 중앙값).
/// </summary>
public static class LedgerBench
{
    static readonly Biome[] Biomes = { Biome.Temperate, Biome.Grassland, Biome.Rainforest, Biome.Savanna, Biome.Boreal, Biome.Wetland, Biome.Tundra, Biome.Desert, Biome.Alpine };

    public static void Run(int seeds, int per, string? outFile)
    {
        var rows = new Dictionary<Biome, List<FoodLedger>>();
        foreach (var b in Biomes) rows[b] = new();
        var cool = new List<FoodLedger>();
        var byObj = new Dictionary<Biome, SortedDictionary<string, (double Sum, int Tiles)>>();
        foreach (var b in Biomes) byObj[b] = new();
        for (int s = 1; s <= seeds; s++)
        {
            var w = GameWorld.Create((ulong)s, new WorldConfig { DotCount = 0 });
            var p = w.Planet;
            foreach (var b in Biomes)
            {
                var cells = Enumerable.Range(0, p.CellCount).Where(c => p.Biome[c] == b)
                    .OrderBy(c => StableHash.Combine((ulong)s, (ulong)c)).Take(per);
                foreach (int c in cells)
                {
                    var life = w.People.Life(c);
                    rows[b].Add(w.People.Ledger(life));
                    foreach (var (t, h) in life.HarvestTiles())
                    {
                        if (h.RegrowTicks <= 0) continue;
                        double v = (h.Food ? h.Nutrition : w.People.CarriedNutrition(h.ItemIndex)) * (double)Wtw.Sim.Time.GameTime.TicksPerDay / h.RegrowTicks / 1000;
                        if (v <= 0) continue;
                        var d = byObj[b]; d.TryGetValue(h.ObjectName, out var x); d[h.ObjectName] = (x.Sum + v, x.Tiles + 1);
                    }
                }
            }
            int cc = PeopleSamples.Cool(p);
            if (cc >= 0) cool.Add(w.People.Ledger(w.People.Life(cc)));
            Console.WriteLine($"시드 {s} 끝");
        }
        double need = PeopleSim.DailyNeed / 1000.0;
        var sb = new StringBuilder();
        sb.AppendLine($"# 먹을 것 장부 — 행성 {seeds}개 × 생물군계마다 지방 {per}곳 (지방 하나가 하루에 다시 내는 배부름, 중앙값)");
        sb.AppendLine();
        sb.AppendLine($"사람 하나가 하루에 먹는 양: **{need:0}** (깨어 16시간 × {PeopleSim.FoodAwake / 1000.0} + 자는 8시간 × {PeopleSim.FoodAsleep / 1000.0}). 한 끼 ≈ 33.");
        sb.AppendLine();
        sb.AppendLine("| 땅 | 지방 수 | 따 먹는 열매 · 버섯 | 주워 드는 낟알 · 콩 · 뿌리 · 도토리(날로) | 짐승(늘 수 있는 몫) | 물고기 | 들 합 | **들이 먹여 살리는 사람** | 밭 한 칸(볶은 낟알) | 밭이 될 타일 | 한 사람에 드는 밭 칸 |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
        void Line(string name, List<FoodLedger> l)
        {
            if (l.Count == 0) { sb.AppendLine($"| {name} | 0 | | | | | | | | | |"); return; }
            double M(Func<FoodLedger, double> f) { var x = l.Select(f).OrderBy(v => v).ToList(); return x.Count % 2 == 1 ? x[x.Count / 2] : (x[x.Count / 2 - 1] + x[x.Count / 2]) / 2; }
            double fpt = M(x => x.FieldPerTile);
            sb.AppendLine($"| {name} | {l.Count} | {M(x => x.Fruit):0} | {M(x => x.Wild):0} | {M(x => x.Game):0.0} | {M(x => x.Fish):0.0} | {M(x => x.WildTotal):0} | **{M(x => x.WildTotal) / need:0.0}명** | {fpt:0.00} | {M(x => x.FieldTiles):0} | {(fpt > 0 ? need / fpt : 0):0} |");
        }
        foreach (var b in Biomes) Line(PlanetNames.Of(b), rows[b]);
        Line("쌀쌀한 땅(측정에 쓰는 칸)", cool);
        sb.AppendLine();
        sb.AppendLine("## 갈래마다 몫 (지방 하나 평균 — 하루 몫 · 타일 수)");
        foreach (var b in Biomes)
        {
            int n = Math.Max(1, rows[b].Count);
            sb.AppendLine($"- {PlanetNames.Of(b)}: " + string.Join(" · ", byObj[b].OrderByDescending(kv => kv.Value.Sum).Select(kv => $"{kv.Key} {kv.Value.Sum / n:0}({kv.Value.Tiles / n}칸)")));
        }
        File.WriteAllText(outFile ?? "ledger.md", sb.ToString());
        Console.WriteLine(sb.ToString());
    }
}
