using System.Diagnostics;
using System.Text;
using Wtw.Sim.People;
using Wtw.Sim.Time;
using Wtw.Sim.World;

/// <summary>
/// (E3b) 퍼져 나가기: 시드마다 따뜻한 땅에 N 명을 뿌리고 Y 년 → 사람 사는 지방 수 · 가장 멀리 간 거리(칸) · 인구 · 죽음 원인 ·
/// 경계를 넘은 횟수와 까닭 · 말(사투리) 수와 알아듣기.
///   dotnet run -c Release --project tools/bench -- people spread [--seeds 20] [--people 10] [--years 100] [--seed 1] [--out 파일.md]
/// </summary>
public static class SpreadBench
{
    public sealed class Row
    {
        public ulong Seed; public int Home; public string Biome = "";
        public int Alive, Peak, Births, Inhabited, Opened, MaxHops;
        public long Crossings, Wakes;
        public long[] Deaths = new long[12], MoveWhy = new long[64];
        public long NoWater, NoFood;
        public int Languages, LandNames;
        public double Seconds; public long PersonDays;
        public List<(int Year, int Inhabited, int Alive, int Langs, int MinUnderstand)> ByDecade = new();
        public string Note = "";
    }

    public static void Run(int seeds, int people, int years, ulong first, int parallel, string? outPath)
    {
        var rows = new Row[seeds];
        var sw = Stopwatch.StartNew();
        Parallel.For(0, seeds, new ParallelOptions { MaxDegreeOfParallelism = parallel }, k =>
        {
            var row = new Row { Seed = first + (ulong)k };
            GameWorld? world = null;
            var r = PeopleBench.Simulate(row.Seed, people, years, passion: false, perDay: (w, day) =>
            {
                world = w;
                if (day % (GameTime.DaysPerYear * 10) == 0) row.ByDecade.Add(((int)(day / GameTime.DaysPerYear), w.People.Provinces.Count(kv => kv.Value.People.Exists(q => !q.Dead)), w.People.AliveCount, LiveLanguages(w).Count, MinUnderstanding(w)));
            });
            var wd = world!;
            row.Home = r.Cell; row.Biome = r.Biome; row.Alive = r.Alive; row.Peak = r.Peak; row.Births = r.Births; row.Seconds = r.Seconds; row.PersonDays = r.PersonDays;
            Array.Copy(r.Deaths, row.Deaths, 12);
            var st = wd.People.Stats;
            row.Crossings = st.Crossings; row.Wakes = st.Wakes; Array.Copy(st.MoveWhy, row.MoveWhy, 64);
            row.NoWater = st.NoWaterDeaths; row.NoFood = st.NoFoodDeaths;
            row.Inhabited = wd.People.Provinces.Count(kv => kv.Value.People.Exists(q => !q.Dead));
            var opened = wd.People.OpenedProvinces().ToList();
            row.Opened = opened.Count;
            row.MaxHops = Hops(wd, r.Cell, opened);
            row.Languages = LiveLanguages(wd).Count;
            row.LandNames = wd.People.LandNames.Count;
            rows[k] = row;
        });
        var sb = new StringBuilder();
        sb.AppendLine($"## 퍼져 나가기: 시드 {seeds}개 × 따뜻한 땅에 {people}명 × {years}년 ({sw.Elapsed.TotalSeconds:0}초, 코어 {parallel})");
        sb.AppendLine();
        sb.AppendLine("| 시드 | 처음 칸 | 끝 인구 | 가장 많을 때 | 태어남 | 사람 사는 지방 | 한 번이라도 산 지방 | 가장 멀리(칸) | 경계 넘음 | 굶주림 | 목마름 | 추위 | 늙음 · 병 | 쓰는 말 | 이름 붙은 땅 | 10년마다(사는 지방/인구/말/가장 낮은 알아듣기%) | 초 |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var r in rows)
            sb.AppendLine($"| {r.Seed} | {r.Home}({r.Biome}) | {r.Alive} | {r.Peak} | {r.Births} | {r.Inhabited} | {r.Opened} | {r.MaxHops} | {r.Crossings} | {r.Deaths[1]} | {r.Deaths[2]} | {r.Deaths[3]} | {r.Deaths[5] + r.Deaths[6]} | {r.Languages} | {r.LandNames} | {string.Join(" · ", r.ByDecade.Select(d => $"{d.Inhabited}/{d.Alive}/{d.Langs}/{d.MinUnderstand}"))} | {r.Seconds:0} |");
        double M(Func<Row, double> f) => PeopleBench.Median(rows.Select(f));
        sb.AppendLine();
        sb.AppendLine($"- **중앙값**: 사람 사는 지방 {M(r => r.Inhabited)} · 한 번이라도 산 지방 {M(r => r.Opened)} · 가장 멀리 {M(r => r.MaxHops)}칸 · 끝 인구 {M(r => r.Alive)} · 경계 넘음 {M(r => r.Crossings)} · 쓰는 말 {M(r => r.Languages)} · 이름 붙은 땅 {M(r => r.LandNames)}");
        sb.AppendLine($"- 멸종: {rows.Count(r => r.Alive == 0)}/{seeds}");
        long deaths = rows.Sum(r => r.Deaths.Sum());
        string[] cn = { "-", "굶주림", "목마름", "추위", "더위", "늙음", "병" };
        sb.AppendLine($"- 죽음 원인(합 {deaths}): " + string.Join(" · ", Enumerable.Range(1, 6).Select(c => $"{cn[c]} {rows.Sum(r => r.Deaths[c]) * 100.0 / Math.Max(1, deaths):0}%")) + $" · 지방에 물이 없어 {rows.Sum(r => r.NoWater)} · 먹을 것이 없어 {rows.Sum(r => r.NoFood)}");
        var whys = new (Why W, string N)[] { (Why.Scarce, "먹을 것이 모자라"), (Why.SeekMate, "짝을 찾아"), (Why.Explore, "호기심"), (Why.GoBack, "돌아감"), (Why.FollowFamily, "가족 따라"), (Why.Break, "정신 붕괴") };
        sb.AppendLine($"- 떠나기로 한 까닭(모든 시드 합, 물 · 먹을 것 찾아 넘은 것은 빼고): " + string.Join(" · ", whys.Select(x => $"{x.N} {rows.Sum(r => r.MoveWhy[(int)x.W])}")));
        sb.AppendLine($"- 잠든 지방이 다시 깨어난 횟수 합 {rows.Sum(r => r.Wakes)} · 사람 하루당 {rows.Sum(r => r.Seconds) * 1e6 / Math.Max(1, rows.Sum(r => r.PersonDays)):0}µs");
        Console.Write(sb.ToString());
        if (outPath is not null) File.WriteAllText(outPath, sb.ToString());
    }

    public static List<int> LiveLanguages(GameWorld w) => w.People.All.Where(q => !q.Dead).Select(q => q.LangId).Distinct().OrderBy(x => x).ToList();

    /// <summary>사람 사는 지방마다 대표(가장 많이 쓰는 말의 가장 번호가 작은 사람)끼리 알아듣기의 가장 낮은 값(지방이 하나면 100).</summary>
    public static int MinUnderstanding(GameWorld w)
    {
        var reps = new List<Person>();
        foreach (var (_, life) in w.People.Provinces)
        {
            var alive = life.People.Where(q => !q.Dead && q.Stage(w.Now) != LifeStage.Baby).ToList();
            if (alive.Count == 0) continue;
            int lang = alive.GroupBy(q => q.LangId).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;
            reps.Add(alive.First(q => q.LangId == lang));
        }
        int min = 100;
        for (int i = 0; i < reps.Count; i++)
            for (int j = i + 1; j < reps.Count; j++) min = Math.Min(min, w.People.Understanding(reps[i], reps[j]));
        return min;
    }

    public static int Hops(GameWorld w, int from, List<int> cells)
    {
        var want = new HashSet<int>(cells);
        var dist = new Dictionary<int, int> { [from] = 0 };
        var q = new Queue<int>(); q.Enqueue(from);
        int max = 0, found = want.Contains(from) ? 1 : 0;
        while (q.Count > 0 && found < want.Count)
        {
            int c = q.Dequeue();
            if (dist[c] > 60) break;
            foreach (int nb in w.Planet.Grid.Neighbors(c))
            {
                if (dist.ContainsKey(nb)) continue;
                dist[nb] = dist[c] + 1;
                if (want.Contains(nb)) { found++; max = Math.Max(max, dist[nb]); }
                q.Enqueue(nb);
            }
        }
        return max;
    }
}
