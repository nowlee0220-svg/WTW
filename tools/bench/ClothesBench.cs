using System.Text;
using Wtw.Sim.People;
using Wtw.Sim.Time;
using Wtw.Sim.World;

/// <summary>
/// (T1) 툰드라 (가) 떼죽음 조사. dotnet run -c Release --project tools/bench -- people clothes [--seeds 40] [--years 14] [--out 파일.md]
///   털옷 · 불 · 가죽 일을 아는 가족 셋(BuildBench --know coat 와 같다)을 툰드라에 뿌리고, 날마다 사람마다 입은 옷을 본다:
///   날마다 해진 옷 · 새로 입은 옷 · 얼어 죽은 사람(모든 시드 합) · 얼어 죽은 사람이 옷을 잃은 날 · 그 옷을 입기 시작한 날 · 그날의 바깥 기온.
/// </summary>
public static class ClothesBench
{
    sealed class Track { public int Item = -1; public long Since = -1, LostAt = -1, LostSince = -1; }

    public static void Run(int seeds, int years, string? outFile)
    {
        int days = years * GameTime.DaysPerYear;
        var wornOut = new long[days + 1]; var putOn = new long[days + 1]; var frozen = new long[days + 1];
        var deaths = new List<string>();
        var minTemp = new List<int>[days + 1];
        for (int d = 0; d <= days; d++) minTemp[d] = new();
        Parallel.For(1, seeds + 1, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, s =>
        {
            var w = GameWorld.Create((ulong)s, new WorldConfig { DotCount = 0, PeopleCap = 300 });
            int cell = PeopleSamples.Cold(w.Planet);
            var life = w.People.Life(cell);
            foreach (var (x, y) in PeopleSamples.SpawnSpots(life, 3))
                w.Apply(new Intervention(0, Intervention.Kinds.SpawnFamily, new long[] { cell, x, y }));
            w.AdvanceTo(1); BuildBench.Teach(w, "coat");
            var tr = new Dictionary<int, Track>();
            var lw = new long[days + 1]; var lp = new long[days + 1]; var lf = new long[days + 1];
            var ld = new List<string>(); var lt = new int[days + 1];
            for (int d = 0; d < days; d++)
            {
                w.AdvanceTo((long)(d + 1) * GameTime.TicksPerDay);
                lt[d] = int.MaxValue;
                for (int h = 0; h < 24; h++) lt[d] = Math.Min(lt[d], w.People.Life(cell).OutsideTemp10((long)d * GameTime.TicksPerDay + h * GameTime.TicksPerHour));
                foreach (var q in w.People.All)
                {
                    if (!tr.TryGetValue(q.Id, out var t)) tr[q.Id] = t = new Track();
                    if (q.Dead)
                    {
                        if (t.Item != -2)
                        {
                            if (q.Cause == DeathCause.Cold && q.DeathTick / GameTime.TicksPerDay == d)
                            {
                                lf[d]++;
                                ld.Add($"| {s} | {Day(d)} | {q.AgeTicks(w.Now) / GameTime.TicksPerYear} | {(t.LostAt >= 0 ? Day(t.LostAt) : "-")} | {(t.LostSince >= 0 ? Day(t.LostSince) : "-")} | {(t.Item >= 0 ? "입음" : "없음")} |");
                            }
                            t.Item = -2;
                        }
                        continue;
                    }
                    if (q.Worn != t.Item || (q.Worn >= 0 && q.WornWear >= 990 && t.Since < d - 1))
                    {
                        if (t.Item >= 0 && q.Worn < 0) { lw[d]++; t.LostAt = d; t.LostSince = t.Since; }
                        if (q.Worn >= 0) { lp[d]++; t.Since = d; }
                        t.Item = q.Worn;
                    }
                }
                if (w.People.AliveCount == 0) break;
            }
            lock (deaths)
            {
                for (int d = 0; d <= days; d++) { wornOut[d] += lw[d]; putOn[d] += lp[d]; frozen[d] += lf[d]; if (lt[d] != 0) minTemp[d].Add(lt[d]); }
                deaths.AddRange(ld);
            }
            Console.WriteLine($"시드 {s} 끝");
        });
        var sb = new StringBuilder();
        sb.AppendLine($"# 툰드라 (가) 옷 · 얼어 죽음 — 시드 {seeds} × {years}년 (모든 시드 합)");
        sb.AppendLine();
        sb.AppendLine("## 얼어 죽은 사람이 많은 날 (5명 이상)");
        sb.AppendLine("| 날 | 얼어 죽음 | 그날 해진 옷 | 그날 새로 입은 옷 | 그날 가장 낮은 바깥 기온(시드 중앙값) |");
        sb.AppendLine("|---|---|---|---|---|");
        for (int d = 0; d < days; d++)
            if (frozen[d] >= 5) sb.AppendLine($"| {Day(d)} | {frozen[d]} | {wornOut[d]} | {putOn[d]} | {Med(minTemp[d]) / 10.0:0.0}°C |");
        sb.AppendLine();
        sb.AppendLine("## 옷이 많이 해진 날 (5벌 이상)");
        sb.AppendLine("| 날 | 해진 옷 | 새로 입은 옷 | 얼어 죽음(그날부터 열흘) |");
        sb.AppendLine("|---|---|---|---|");
        for (int d = 0; d < days; d++)
            if (wornOut[d] >= 5) sb.AppendLine($"| {Day(d)} | {wornOut[d]} | {putOn[d]} | {frozen.Skip(d).Take(10).Sum()} |");
        sb.AppendLine();
        sb.AppendLine("## 해마다 · 기온(그날 가장 낮은 바깥 기온 · 모든 날 중앙값)");
        sb.AppendLine("| 해 | 얼어 죽음 | 해진 옷 | 새로 입은 옷 | 가장 낮은 기온 중앙값 |");
        sb.AppendLine("|---|---|---|---|---|");
        for (int y = 0; y < years; y++)
        {
            var r = Enumerable.Range(y * GameTime.DaysPerYear, GameTime.DaysPerYear);
            sb.AppendLine($"| {y} | {r.Sum(d => frozen[d])} | {r.Sum(d => wornOut[d])} | {r.Sum(d => putOn[d])} | {Med(r.SelectMany(d => minTemp[d]).ToList()) / 10.0:0.0}°C |");
        }
        sb.AppendLine();
        sb.AppendLine("## 얼어 죽은 사람마다");
        sb.AppendLine("| 시드 | 죽은 날 | 나이 | 마지막 옷이 해진 날 | 그 옷을 입기 시작한 날 | 죽을 때 옷 |");
        sb.AppendLine("|---|---|---|---|---|---|");
        foreach (var l in deaths.OrderBy(x => x)) sb.AppendLine(l);
        File.WriteAllText(outFile ?? "clothes.md", sb.ToString());
        Console.WriteLine(sb.ToString().Split("## 얼어 죽은 사람마다")[0]);
    }

    static string Day(long d) => $"{d / GameTime.DaysPerYear}년 {d % GameTime.DaysPerYear}일";
    static double Med(List<int> l) { if (l.Count == 0) return double.NaN; var x = l.OrderBy(v => v).ToList(); return x[x.Count / 2]; }
}
