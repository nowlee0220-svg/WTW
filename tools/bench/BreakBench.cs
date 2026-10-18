using System.Diagnostics;
using System.Text;
using Wtw.Sim.People;
using Wtw.Sim.Time;
using Wtw.Sim.World;

/// <summary>(E4) 정신 붕괴 측정: 한 사람이 1년에 몇 번(어른 · 아이 · 노인 따로), 무너질 때 기분을 끌어내린 생각 상위 다섯.</summary>
public static class BreakBench
{
    public static void Run(int seeds, int people, int years, ulong first, int parallel, string? outPath, string where)
    {
        var sw = Stopwatch.StartNew();
        var res = new (long[] Breaks, double[] Years, long[] Drag, string[] Names)[seeds];
        Parallel.For(0, seeds, new ParallelOptions { MaxDegreeOfParallelism = parallel }, k =>
        {
            var w = GameWorld.Create(first + (ulong)k, new WorldConfig { DotCount = 0, PeopleCap = 300 });
            int cell = where == "cold" ? PeopleSamples.Cold(w.Planet) : where == "cool" ? PeopleSamples.Cool(w.Planet) : PeopleSamples.Home(w.Planet);
            var life = w.People.Life(cell);
            foreach (var (x, y) in PeopleSamples.SpawnSpots(life, Math.Max(1, people / 3)))
                w.Apply(new Intervention(0, Intervention.Kinds.SpawnFamily, new long[] { cell, x, y }));
            var py = new double[4];
            long end = years * GameTime.TicksPerYear;
            for (long t = 0; t < end; t += GameTime.TicksPerDay)
            {
                w.AdvanceTo(Math.Min(end, t + GameTime.TicksPerDay));
                foreach (var q in w.People.All) if (!q.Dead) py[(int)q.Stage(w.Now)] += 1.0 / GameTime.DaysPerYear;
                if (w.People.AliveCount == 0) break;
            }
            res[k] = ((long[])w.People.Stats.BreaksByStage.Clone(), py, (long[])w.People.Stats.BreakDrag.Clone(), w.People.Data.Thoughts.Select(t => t.Name).ToArray());
        });
        var sb = new StringBuilder();
        sb.AppendLine($"## 정신 붕괴: 시드 {seeds} × {(where == "home" ? "따뜻한 땅" : where)} 가족 {people}명 × {years}년 (걸린 시간 {sw.Elapsed.TotalSeconds:0}초)");
        sb.AppendLine();
        string[] st = { "아기", "아이", "어른", "노인" };
        sb.AppendLine("| | 사람·년 | 무너짐 | 한 사람당 1년에 |");
        sb.AppendLine("|---|---|---|---|");
        long tb = 0; double ty = 0;
        for (int s = 1; s < 4; s++)
        {
            long b = res.Sum(r => r.Breaks[s]); double y = res.Sum(r => r.Years[s]);
            tb += b; ty += y;
            sb.AppendLine($"| {st[s]} | {y:0} | {b} | {(y > 0 ? b / y : 0):0.00}번 |");
        }
        sb.AppendLine($"| 모두 | {ty:0} | {tb} | {(ty > 0 ? tb / ty : 0):0.00}번 |");
        sb.AppendLine();
        var names = res[0].Names;
        var drag = new long[256];
        foreach (var r in res) for (int i = 0; i < 256; i++) drag[i] += r.Drag[i];
        long dsum = Math.Max(1, drag.Sum());
        sb.AppendLine("무너질 때 기분을 끌어내린 생각(기분 합 · 비율) 상위 다섯:");
        foreach (var (v, i) in drag.Select((v, i) => (v, i)).Where(x => x.v > 0 && x.i < names.Length).OrderByDescending(x => x.v).Take(5))
            sb.AppendLine($"- {names[i]}: {v} ({v * 100 / dsum}%)");
        Console.Write(sb.ToString());
        if (outPath is not null) File.WriteAllText(outPath, sb.ToString());
    }
}
