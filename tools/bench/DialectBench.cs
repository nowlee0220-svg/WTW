using System.Diagnostics;
using System.Text;
using Wtw.Sim.People;
using Wtw.Sim.Time;
using Wtw.Sim.World;

/// <summary>
/// (E3b) 떨어져 살면 말이 얼마나 달라지나: 시드마다 같은 말을 쓰는 두 무리(가족 N 집씩)를 멀리 떨어진 따뜻한 땅 두 곳(12칸 이상)에 뿌리고 Y 년.
/// 50년마다 두 무리(그 자손) 사이의 알아듣기 % 와 무리 안의 알아듣기 % 를 잰다. 처음에 같은 말로 맞추는 것만 실험 준비이고, 그 뒤는 그대로 돌린다.
///   dotnet run -c Release --project tools/bench -- people dialect [--seeds 20] [--people 8] [--years 500] [--cap 60] [--seed 1] [--out 파일.md]
/// </summary>
public static class DialectBench
{
    public static void Run(int seeds, int people, int years, int cap, ulong first, int parallel, string? outPath)
    {
        int steps = years / 50;
        var between = new double[seeds, steps + 1]; var within = new double[seeds, steps + 1];
        var alive = new int[seeds, 2]; var splits = new long[seeds]; var langs = new int[seeds]; var hops = new int[seeds];
        var secs = new double[seeds];
        for (int k = 0; k < seeds; k++) for (int s = 0; s <= steps; s++) { between[k, s] = double.NaN; within[k, s] = double.NaN; }
        Parallel.For(0, seeds, new ParallelOptions { MaxDegreeOfParallelism = parallel }, k =>
        {
            var sw = Stopwatch.StartNew();
            ulong seed = first + (ulong)k;
            // 같은 시드의 세계 두 벌 = 같은 행성 · 같은 첫 말. 한 벌에는 A 땅에만, 다른 벌에는 B 땅에만 뿌린다 → 서로 절대 만나지 않는 두 무리.
            var w = new[] { GameWorld.Create(seed, new WorldConfig { DotCount = 0, PeopleCap = cap }), GameWorld.Create(seed, new WorldConfig { DotCount = 0, PeopleCap = cap }) };
            var pl = w[0].Planet;
            int a = PeopleSamples.Home(pl), b = -1;
            foreach (int c in PeopleSamples.Ranked(pl, (p, c) => p.Biome[c] is Wtw.Sim.Planet.Biome.Temperate or Wtw.Sim.Planet.Biome.Rainforest or Wtw.Sim.Planet.Biome.Grassland or Wtw.Sim.Planet.Biome.Savanna ? p.Temp10[c] : int.MinValue))
                if (SpreadBench.Hops(w[0], a, new List<int> { c }) is int h && h >= 12) { b = c; hops[k] = h; break; }
            int[] home = { a, b };
            for (int g = 0; g < 2; g++)
            {
                foreach (var (x, y) in PeopleSamples.SpawnSpots(w[g].People.Life(home[g]), people))
                    w[g].Apply(new Intervention(0, Intervention.Kinds.SpawnFamily, new long[] { home[g], x, y }));
                // 실험 준비: 모두 같은 말, 말씨 0. 두 번째 세계는 번호만 다른(내용은 같은) 말 — 번호가 같으면 낱말이 바뀌는 순서까지 같아진다
                int lang = g == 0 ? w[g].People.All[0].LangId : w[g].People.All[^1].LangId;
                foreach (var p in w[g].People.All) { p.LangId = lang; Array.Clear(p.Accent); }
            }
            {
                var l0 = w[0].People.Languages[w[0].People.All[0].LangId];
                var l1 = w[1].People.Languages[w[1].People.All[^1].LangId];
                if (l1.Id == l0.Id) throw new InvalidOperationException("두 번째 세계의 말 번호가 같다(가족이 하나뿐?)");
                l1.Seed = l0.Seed; l1.Length = l0.Length; Array.Copy(l0.Sounds, l1.Sounds, l0.Sounds.Length); l1.Version++;
            }
            void Measure(int idx)
            {
                var g0 = Sample(w[0]); var g1 = Sample(w[1]);
                between[k, idx] = Avg(w[0], g0, w[1], g1);
                within[k, idx] = (Avg(w[0], g0, w[0], g0) + Avg(w[1], g1, w[1], g1)) / 2;
            }
            Measure(0);
            for (int s = 1; s <= steps; s++)
            {
                long end = s * 50L * GameTime.TicksPerYear;
                foreach (var ww in w)
                    for (long t = ww.Now; t < end; t += GameTime.TicksPerDay) ww.AdvanceTo(Math.Min(end, t + GameTime.TicksPerDay));
                Measure(s);
            }
            alive[k, 0] = w[0].People.AliveCount; alive[k, 1] = w[1].People.AliveCount;
            splits[k] = w[0].People.Stats.LanguageSplits + w[1].People.Stats.LanguageSplits;
            langs[k] = SpreadBench.LiveLanguages(w[0]).Count + SpreadBench.LiveLanguages(w[1]).Count;
            secs[k] = sw.Elapsed.TotalSeconds;
            if (Environment.GetEnvironmentVariable("WTW_DIALECT_DEBUG") == "1")
                for (int g = 0; g < 2; g++)
                    foreach (var id in SpreadBench.LiveLanguages(w[g]))
                    {
                        var l = w[g].People.Languages[id];
                        Console.WriteLine($"[seed {seed} 세계 {g}] 말 {id} 부모 {l.Parent} 태어남 {l.Born / GameTime.TicksPerYear}년 바뀐 낱말 {l.Changes} 덮어쓴 뜻 {l.Overrides.Count} 쓰는 사람 {w[g].People.Speakers(id)}");
                    }
        });
        var sb = new StringBuilder();
        sb.AppendLine($"## 떨어져 산 두 무리의 말: 시드 {seeds}개 × 무리마다 가족 {people}집 × {years}년 (무리마다 상한 {cap} — 서로 만나지 않는 두 세계)");
        sb.AppendLine();
        sb.AppendLine("| 시드 | 떨어진 거리(칸) | " + string.Join(" | ", Enumerable.Range(1, steps).Select(s => $"{s * 50}년")) + " | 무리 안(끝) | 끝 인구 | 갈라진 횟수 | 쓰는 말(두 무리 합) | 초 |");
        sb.AppendLine("|---|---|" + string.Concat(Enumerable.Repeat("---|", steps)) + "---|---|---|---|---|");
        for (int k = 0; k < seeds; k++)
            sb.AppendLine($"| {first + (ulong)k} | {hops[k]} | " + string.Join(" | ", Enumerable.Range(1, steps).Select(s => double.IsNaN(between[k, s]) ? "-" : $"{between[k, s]:0}%")) + $" | {within[k, steps]:0}% | {alive[k, 0]}+{alive[k, 1]} | {splits[k]} | {langs[k]} | {secs[k]:0} |");
        sb.AppendLine();
        sb.AppendLine("- **두 무리 사이 알아듣기 중앙값**: " + string.Join(" · ", Enumerable.Range(1, steps).Select(s => $"{s * 50}년 {PeopleBench.Median(Enumerable.Range(0, seeds).Select(k => between[k, s]).Where(v => !double.IsNaN(v))):0}%")));
        sb.AppendLine("- 무리 안 알아듣기 중앙값: " + string.Join(" · ", Enumerable.Range(1, steps).Select(s => $"{s * 50}년 {PeopleBench.Median(Enumerable.Range(0, seeds).Select(k => within[k, s]).Where(v => !double.IsNaN(v))):0}%")));
        sb.AppendLine($"- 목표(기획서 14-1): 200년 60~85%(사투리), 500년 40% 아래(다른 언어)");
        Console.Write(sb.ToString());
        if (outPath is not null) File.WriteAllText(outPath, sb.ToString());
    }

    static List<Person> Sample(GameWorld w)
    {
        var all = w.People.All.Where(p => !p.Dead && p.Stage(w.Now) != LifeStage.Baby).ToList();
        if (all.Count <= 12) return all;
        var pick = new List<Person>();
        for (int i = 0; i < 12; i++) pick.Add(all[i * all.Count / 12]);
        return pick;
    }

    /// <summary>두 묶음(서로 다른 세계일 수 있음) 사이 알아듣기 평균: 기본 뜻 낱말의 비슷함(PeopleSim.Understanding 과 같은 셈).</summary>
    static double Avg(GameWorld wa, List<Person> a, GameWorld wb, List<Person> b)
    {
        if (a.Count == 0 || b.Count == 0) return double.NaN;
        var d = LanguageData.Default;
        var concepts = d.Concepts.Where(c => c.Base).ToList();
        long s = 0; int n = 0;
        foreach (var p in a)
            foreach (var q in b)
            {
                if (ReferenceEquals(p, q)) continue;
                var lp = wa.People.Languages[p.LangId]; var lq = wb.People.Languages[q.LangId];
                int sum = 0;
                foreach (var c in concepts) sum += Words.Similarity(Words.Word(lp, p.Accent, c, d), Words.Word(lq, q.Accent, c, d));
                s += sum / concepts.Count; n++;
            }
        return n == 0 ? 100 : s / (double)n;
    }
}
