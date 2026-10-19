using System.Diagnostics;
using System.Text;
using Wtw.Sim.People;
using Wtw.Sim.Persistence;
using Wtw.Sim.Time;
using Wtw.Sim.World;

/// <summary>
/// 사람 측정(E3). dotnet run -c Release --project tools/bench -- people &lt;모드&gt; …
///   seeds  [--seeds 20] [--people 10] [--years 50] [--passion on|off|both] [--seed 1] [--parallel N] [--out 파일.md]
///          : 시드마다 "따뜻하고 열매가 많은 땅"에 혼자 사는 사람 N 명을 뿌리고 Y 년 → 인구 · 죽음 원인 · 기분 · 정신 붕괴 · 짝 · 출산 (시드 중앙값)
///   cap    [--people 500] [--years 100] [--seed 1] [--per 25]
///          : 사람 N 명(지방마다 per 명씩 여러 지방)을 Y 년 → 걸린 시간 · 사람 하루당 비용 · 메모리 (상한 결정)
///   one    [--people 10] [--years 5] [--seed 1] [--cell home|cold|칸번호] [--family]
///          : 한 지방을 돌리며 날마다 줄을 찍는다(개발용)
/// </summary>
public static class PeopleBench
{
    public static void Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "seeds";
        var o = new Dictionary<string, string>();
        for (int i = 1; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--")) continue;
            o[args[i][2..]] = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[++i] : "1";
        }
        int I(string k, int d) => o.TryGetValue(k, out var v) ? int.Parse(v) : d;
        // (S1) 세계 하나를 CPU 여러 개로(지방마다) — --parallel(시드를 나눠 돌리기)과 함께 쓸 수 있다
        SpeedBench.AutoThreads = Sx("threads", "1") == "auto";
        PeopleSim.UsePool = !o.ContainsKey("nopool"); // (S1) 비교용: 부를 때마다 Parallel.ForEach
        GameWorld.DefaultThreads = SpeedBench.AutoThreads ? 1 : Math.Max(1, I("threads", 1));
        if (SpeedBench.AutoThreads) Console.WriteLine("(S1) 세계마다 CPU: 자동(코어 수 ÷ 돌고 있는 세계 수, --threads auto)");
        else if (GameWorld.DefaultThreads > 1) Console.WriteLine($"(S1) 세계마다 CPU {GameWorld.DefaultThreads}개(--threads)");
        string Sx(string k, string d) => o.TryGetValue(k, out var v) ? v : d;
        switch (mode)
        {
            case "seeds": Seeds(I("seeds", 20), I("people", 10), I("years", 50), Sx("passion", "on"), (ulong)I("seed", 1), I("parallel", Environment.ProcessorCount), o.GetValueOrDefault("out")); break;
            case "cap": Cap(I("people", 500), I("years", 100), (ulong)I("seed", 1), I("per", 25), I("cap", 500)); if (o.GetValueOrDefault("save") is string sp && _lastWorld is { } lw) { SaveGame.SaveFile(lw, sp); Console.WriteLine($"저장: {sp}"); } break;
            case "nosource": NoSource(I("seeds", 20), I("cells", 5), I("people", 5), I("years", 3), (ulong)I("seed", 1), I("parallel", Environment.ProcessorCount), o.GetValueOrDefault("out")); break;
            case "water": WaterBench.Run(I("seeds", 20), I("cells", 30), (ulong)I("seed", 1), I("parallel", Environment.ProcessorCount), o.GetValueOrDefault("out")); break;
            case "spread": SpreadBench.Run(I("seeds", 20), I("people", 10), I("years", 100), (ulong)I("seed", 1), I("parallel", Environment.ProcessorCount), o.GetValueOrDefault("out")); break;
            case "drift": DriftBench.Run(I("langs", 200), Sx("k", "1,2,3,4")); break;
            case "driftmodel": DriftModel.Run(I("langs", 200), Sx("k", "0.05,0.1,0.2"), Sx("r", "0.02,0.03,0.04"), I("t", 3)); break;
            case "dialect": DialectBench.Run(I("seeds", 20), I("families", 3), I("years", 500), I("cap", 60), (ulong)I("seed", 1), I("parallel", Environment.ProcessorCount), o.GetValueOrDefault("out")); break;
            case "one": One(I("people", 10), I("years", 5), (ulong)I("seed", 1), Sx("cell", "home"), o.ContainsKey("family"), I("cap", 300)); break;
            case "breaks": BreakBench.Run(I("seeds", 4), I("people", 10), I("years", 30), (ulong)I("seed", 1), I("parallel", Environment.ProcessorCount), o.GetValueOrDefault("out"), Sx("cell", "home")); break;
            case "scenes": SceneBench.Run(o.GetValueOrDefault("out") ?? "game/scenes", (ulong)I("seed", 20261016), o.ContainsKey("artisan"), o.ContainsKey("copper")); break;
            case "copper": CopperBench.Run(I("seeds", 4), I("years", 200), (ulong)I("seed", 1), I("parallel", Environment.ProcessorCount), o.GetValueOrDefault("out"), I("cap", 300)); break;
            case "skip": SpeedBench.Skip(Sx("scene", "game/scenes/village-50y.wtw"), I("years", 50)); break;
            case "discover": DiscoverBench.Know = o.GetValueOrDefault("know") ?? ""; DiscoverBench.SaveTo = o.GetValueOrDefault("save"); DiscoverBench.Run(I("seeds", 20), I("people", 10), I("years", 200), (ulong)I("seed", 1), I("parallel", Environment.ProcessorCount), o.GetValueOrDefault("out"), Sx("cell", "home"), I("cap", 300)); break;
            default: Console.WriteLine("모드: seeds | cap | one"); break;
        }
    }

    public sealed class Run
    {
        public ulong Seed; public int Cell; public bool Passion;
        public double Seconds;
        public int Alive, Ever, Peak, Births, Pairs, Breaks, Talks, Arguments;
        public double PersonYears; // 아기 아닌 사람 · 해
        public long PersonDays;
        public readonly long[] Deaths = new long[8];
        public double MoodMedian, BreaksPerPersonYear;
        public List<int> MoodSamples = new();
        public List<int> PopByYear = new();
        public double GatherPassionLevel = double.NaN, GatherPlainLevel = double.NaN, MoodPassion = double.NaN, MoodPlain = double.NaN, GatherMedian, WithPassion;
        public long XpPassion, XpPlain, ActsPassion, ActsPlain;
        public long FoodSupply;
        public string Biome = "";
        public long Events, Decisions, Paths, PathFails;
    }

    public static Run Simulate(ulong seed, int people, int years, bool passion, int cap = 300, bool family = false, string where = "home", Action<GameWorld, long>? perDay = null, int perProvince = 0)
    {
        var w = GameWorld.Create(seed, new WorldConfig { DotCount = 0, Passion = passion, PeopleCap = cap });
        var p = w.Planet;
        var run = new Run { Seed = seed, Passion = passion };
        var homes = new List<int>();
        if (perProvince > 0) homes.AddRange(PeopleSamples.Ranked(p, (pl, c) => pl.Biome[c] is Wtw.Sim.Planet.Biome.Temperate or Wtw.Sim.Planet.Biome.Rainforest or Wtw.Sim.Planet.Biome.Grassland or Wtw.Sim.Planet.Biome.Savanna && pl.RiverSize[c] > 0 ? pl.Temp10[c] : int.MinValue)
            .Take((people + perProvince - 1) / perProvince));
        else homes.Add(where == "home" ? PeopleSamples.Home(p) : where == "cold" ? PeopleSamples.Cold(p) : where == "cool" ? PeopleSamples.Cool(p) : int.Parse(where));
        run.Cell = homes[0];
        run.Biome = Wtw.Sim.Planet.PlanetNames.Of(p.Biome[homes[0]]);
        int left = people;
        foreach (int cell in homes)
        {
            var life = w.People.Life(cell);
            run.FoodSupply += life.DailyFoodSupply();
            int n = perProvince > 0 ? Math.Min(perProvince, left) : people;
            left -= n;
            foreach (var (x, y) in PeopleSamples.SpawnSpots(life, family ? Math.Max(1, n / 3) : n))
                w.Apply(new Intervention(0, family ? Intervention.Kinds.SpawnFamily : Intervention.Kinds.SpawnPerson, new long[] { cell, x, y }));
        }
        run.Ever = w.People.All.Count;
        var sw = Stopwatch.StartNew();
        w.DayBoundary += (world, day) =>
        {
            int alive = 0;
            foreach (var q in world.People.All)
            {
                if (q.Dead) continue;
                alive++;
                if (q.Stage(world.Now) == LifeStage.Baby) continue;
                run.PersonYears += 1.0 / GameTime.DaysPerYear;
                if (day % 2 == 0) run.MoodSamples.Add(world.People.Mood(q));
            }
            run.PersonDays += alive;
            run.Peak = Math.Max(run.Peak, alive);
            if (day % GameTime.DaysPerYear == 0) run.PopByYear.Add(alive);
            perDay?.Invoke(world, day);
        };
        _lastWorld = w;
        long end = years * GameTime.TicksPerYear;
        SpeedBench.WorldStarted();
        try { for (long t = 0; t < end; t += GameTime.TicksPerDay) { SpeedBench.Pace(w); w.AdvanceTo(Math.Min(end, t + GameTime.TicksPerDay)); } }
        finally { SpeedBench.WorldDone(); }
        run.Seconds = sw.Elapsed.TotalSeconds;
        var st = w.People.Stats;
        run.Alive = w.People.AliveCount; run.Ever = w.People.All.Count;
        run.Births = (int)st.Births; run.Pairs = (int)st.Pairs; run.Breaks = (int)st.Breaks; run.Talks = (int)st.Talks; run.Arguments = (int)st.Arguments;
        Array.Copy(st.Deaths, run.Deaths, 8);
        run.Events = st.Events; run.Decisions = st.Decisions; run.Paths = st.Paths; run.PathFails = st.PathFails;
        run.MoodMedian = Median(run.MoodSamples);
        run.BreaksPerPersonYear = run.PersonYears > 0 ? run.Breaks / run.PersonYears : 0;
        run.XpPassion = st.GatherXpPassion; run.XpPlain = st.GatherXpPlain; run.ActsPassion = st.GatherActsPassion; run.ActsPlain = st.GatherActsPlain;
        var adults = w.People.All.Where(q => !q.Dead && q.Stage(w.Now) >= LifeStage.Adult).ToList();
        var withP = adults.Where(q => q.Passion[PeopleData.Gathering] > 0).ToList();
        var noP = adults.Where(q => q.Passion[PeopleData.Gathering] == 0).ToList();
        if (withP.Count > 0) { run.GatherPassionLevel = withP.Average(q => q.SkillXp[PeopleData.Gathering] / 1000.0); run.MoodPassion = withP.Average(q => w.People.Mood(q)); }
        if (noP.Count > 0) { run.GatherPlainLevel = noP.Average(q => q.SkillXp[PeopleData.Gathering] / 1000.0); run.MoodPlain = noP.Average(q => w.People.Mood(q)); }
        run.GatherMedian = adults.Count > 0 ? Median(adults.Select(q => q.SkillLevel(PeopleData.Gathering)).ToList()) : 0;
        run.WithPassion = adults.Count > 0 ? withP.Count / (double)adults.Count : 0;
        return run;
    }

    [ThreadStatic] private static GameWorld? _lastWorld;

    public static double Median(List<int> v) { if (v.Count == 0) return 0; var s = v.OrderBy(x => x).ToList(); return s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2.0; }
    public static double Median(IEnumerable<double> v) { var s = v.OrderBy(x => x).ToList(); if (s.Count == 0) return 0; return s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2.0; }

    private static readonly string[] CauseNames = { "-", "굶주림", "목마름", "추위", "더위", "늙음", "병" };

    static void Seeds(int seeds, int people, int years, string passion, ulong first, int parallel, string? outPath)
    {
        var modes = passion == "both" ? new[] { true, false } : new[] { passion != "off" };
        var sb = new StringBuilder();
        var results = new Dictionary<bool, Run[]>();
        foreach (bool pas in modes)
        {
            var runs = new Run[seeds];
            var sw = Stopwatch.StartNew();
            Parallel.For(0, seeds, new ParallelOptions { MaxDegreeOfParallelism = parallel }, k => runs[k] = Simulate(first + (ulong)k, people, years, pas));
            results[pas] = runs;
            sb.AppendLine($"## 시드 {seeds}개 × 처음 {people}명 × {years}년 — 열정 {(pas ? "켬" : "끔")} (걸린 시간 {sw.Elapsed.TotalSeconds:0}초, 코어 {parallel})");
            sb.AppendLine();
            sb.AppendLine("| 시드 | 칸(생물군계) | 먹을 것/일 | 끝 인구 | 가장 많을 때 | 모두(태어남 포함) | 태어남 | 짝 | 굶주림 | 목마름 | 추위 | 더위 | 늙음 | 병 | 기분 중앙값 | 붕괴/사람·년 | 인구(해마다 10년 간격) | 초 |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (var r in runs)
                sb.AppendLine($"| {r.Seed} | {r.Cell}({r.Biome}) | {r.FoodSupply / 100_000.0:0}명분 | {r.Alive} | {r.Peak} | {r.Ever} | {r.Births} | {r.Pairs} | {r.Deaths[1]} | {r.Deaths[2]} | {r.Deaths[3]} | {r.Deaths[4]} | {r.Deaths[5]} | {r.Deaths[6]} | {r.MoodMedian:0} | {r.BreaksPerPersonYear:0.00} | {string.Join("·", r.PopByYear.Where((_, i) => i % 10 == 0))} | {r.Seconds:0} |");
            long deaths = runs.Sum(r => r.Deaths.Sum());
            sb.AppendLine();
            sb.AppendLine($"- **중앙값**: 끝 인구 {Median(runs.Select(r => (double)r.Alive))} · 가장 많을 때 {Median(runs.Select(r => (double)r.Peak))} · 태어남 {Median(runs.Select(r => (double)r.Births))} · 짝 {Median(runs.Select(r => (double)r.Pairs))} · 기분 {Median(runs.Select(r => r.MoodMedian)):0.0} · 정신 붕괴 {Median(runs.Select(r => r.BreaksPerPersonYear)):0.00}/사람·년");
            sb.AppendLine($"- 멸종(끝 인구 0): {runs.Count(r => r.Alive == 0)}/{seeds}");
            sb.AppendLine($"- 죽음 원인(모든 시드 합 {deaths}): " + string.Join(" · ", Enumerable.Range(1, 6).Select(c => $"{CauseNames[c]} {runs.Sum(r => r.Deaths[c]) * 100.0 / Math.Max(1, deaths):0}%")));
            sb.AppendLine($"- 채집 기술 중앙값(살아 있는 어른) {Median(runs.Select(r => r.GatherMedian)):0.0} · 열정 가진 어른 비율 {Median(runs.Select(r => r.WithPassion)) * 100:0}%");
            if (pas)
            {
                var a = runs.Where(r => !double.IsNaN(r.GatherPassionLevel) && !double.IsNaN(r.GatherPlainLevel)).ToList();
                if (a.Count > 0)
                    sb.AppendLine($"- 같은 세계 안 비교(살아 있는 어른, 시드 {a.Count}개 중앙값): 채집 경험 열정 있음 {Median(a.Select(r => r.GatherPassionLevel)):0.0}천 vs 없음 {Median(a.Select(r => r.GatherPlainLevel)):0.0}천 · 기분 있음 {Median(a.Select(r => r.MoodPassion)):0.0} vs 없음 {Median(a.Select(r => r.MoodPlain)):0.0}");
            }
            sb.AppendLine($"- 계산: 예약 {runs.Sum(r => r.Events):N0} · 판단 {runs.Sum(r => r.Decisions):N0} · 길 찾기 {runs.Sum(r => r.Paths):N0} (실패 {runs.Sum(r => r.PathFails):N0}) · 사람 하루 {runs.Sum(r => r.PersonDays):N0} → 사람 하루당 {runs.Sum(r => r.Seconds) * 1e6 / Math.Max(1, runs.Sum(r => r.PersonDays)):0}µs");
            sb.AppendLine();
            Console.Write(sb.ToString());
            if (outPath is not null) File.WriteAllText(outPath, sb.ToString());
        }
        if (results.Count == 2)
        {
            var on = results[true]; var off = results[false];
            sb.AppendLine("## 열정 켬 vs 끔 (같은 시드)");
            sb.AppendLine($"- 채집 기술 중앙값: 켬 {Median(on.Select(r => r.GatherMedian)):0.0} · 끔 {Median(off.Select(r => r.GatherMedian)):0.0}");
            sb.AppendLine($"- 채집 한 번당 경험: 켬 {on.Sum(r => r.XpPassion + r.XpPlain) / (double)Math.Max(1, on.Sum(r => r.ActsPassion + r.ActsPlain)):0.0} · 끔 {off.Sum(r => r.XpPlain) / (double)Math.Max(1, off.Sum(r => r.ActsPlain)):0.0}");
            sb.AppendLine($"- 기분 중앙값: 켬 {Median(on.Select(r => r.MoodMedian)):0.0} · 끔 {Median(off.Select(r => r.MoodMedian)):0.0}");
            sb.AppendLine($"- 끝 인구 중앙값: 켬 {Median(on.Select(r => (double)r.Alive))} · 끔 {Median(off.Select(r => (double)r.Alive))}");
            sb.AppendLine($"- 계산 시간 합: 켬 {on.Sum(r => r.Seconds):0}초 · 끔 {off.Sum(r => r.Seconds):0}초");
            Console.Write(sb.ToString());
            if (outPath is not null) File.WriteAllText(outPath, sb.ToString());
        }
    }

    static void Cap(int people, int years, ulong seed, int per, int cap)
    {
        GC.Collect();
        long mem0 = GC.GetTotalMemory(true);
        long peakMem = 0;
        var gc = SpeedBench.GcMark.Now();
        var r = Simulate(seed, people, years, true, cap: cap, perProvince: per, perDay: (w, d) => { if (d % 200 == 0) peakMem = Math.Max(peakMem, GC.GetTotalMemory(false)); });
        double us = r.Seconds * 1e6 / Math.Max(1, r.PersonDays);
        double projected = us * people * years * GameTime.DaysPerYear / 1e6;
        Console.WriteLine($"사람 {people}명(지방마다 {per}명) × {years}년, 시드 {seed}, 상한 {cap}");
        Console.WriteLine($"  걸린 시간 {r.Seconds:0.0}초 · 사람 하루 {r.PersonDays:N0}개(평균 {r.PersonDays / (double)(years * GameTime.DaysPerYear):0}명이 살아 있음) · 사람 하루당 {us:0.0}µs");
        Console.WriteLine($"  {people}명이 {years}년 내내 살아 있다면(같은 비용으로 셈): {projected:0}초");
        Console.WriteLine($"  인구(10년마다): {string.Join(" · ", r.PopByYear.Where((_, i) => i % 10 == 0))} · 끝 {r.Alive} · 가장 많을 때 {r.Peak} · 태어남 {r.Births}");
        Console.WriteLine($"  죽음: " + string.Join(" · ", Enumerable.Range(1, 6).Select(c => $"{CauseNames[c]} {r.Deaths[c]}")));
        Console.WriteLine($"  메모리(관리 힙): 처음 {mem0 / 1048576.0:0} MB → 가장 클 때 {peakMem / 1048576.0:0} MB · 프로세스 {Process.GetCurrentProcess().PeakWorkingSet64 / 1048576.0:0} MB");
        Console.WriteLine("  " + SpeedBench.GcMark.Now().Since(gc, r.Seconds));
        Console.WriteLine($"  예약 {r.Events:N0} · 판단 {r.Decisions:N0} · 길 찾기 {r.Paths:N0} (실패 {r.PathFails:N0})");
        if (_lastWorld is { } w)
        {
            var st = w.People.Stats;
            double days = years * GameTime.DaysPerYear;
            int awake = w.People.Provinces.Count, dormant = w.People.Dormant.Count;
            Console.WriteLine($"  (E3b) 지방 넘기: 경계를 넘은 사람 {st.Crossings:N0}번(하루 {st.Crossings / days:0.0}번) · 넘겨주기가 일어난 시각 {st.CrossBatches:N0}번(하루 {st.CrossBatches / days:0.00}번) · 잠든 지방이 깨어남 {st.Wakes:N0}번");
            Console.WriteLine($"  지문 {SaveGame.Fingerprint(w):X16}");
            Console.WriteLine($"  (E3b) 끝에 깨어 있는 지방 {awake}곳 · 잠든 지방 {dormant}곳 · 사람 사는 지방 {w.People.Provinces.Count(kv => kv.Value.People.Exists(q => !q.Dead))}곳 · 지도 기억 {Wtw.Sim.Provinces.ProvinceCache.Count}장 · 말 {w.People.All.Where(q => !q.Dead).Select(q => q.LangId).Distinct().Count()}개 · 이름 붙은 땅 {w.People.LandNames.Count}곳");
        }
    }

    /// <summary>
    /// 아무 땅에나 뿌렸을 때: 시드마다 땅 칸 K 곳(무작위, 결정론)에 N 명씩 Y 년 → 죽음 원인 · "지방(닿을 수 있는 땅) 안에 물 · 먹을 것이 없어서 죽은 사람" 수.
    /// </summary>
    static void NoSource(int seeds, int cells, int people, int years, ulong first, int parallel, string? outPath)
    {
        var rows = new (ulong Seed, int Spawned, int NoDrinkCells, int NoFoodCells, long[] Deaths, long NoWater, long NoFood, int Alive, long[] How, string Biomes, int MovedAlive, int NoWaterSpawned, int NoWaterSurvived, long Crossings)[seeds];
        Parallel.For(0, seeds, new ParallelOptions { MaxDegreeOfParallelism = parallel }, k =>
        {
            ulong seed = first + (ulong)k;
            var w = GameWorld.Create(seed, new WorldConfig { DotCount = 0 });
            var p = w.Planet;
            var land = Enumerable.Range(0, p.CellCount).Where(p.IsLand).ToList();
            var rng = new Wtw.Sim.Core.Pcg32(seed, 99);
            int noDrink = 0, noFood = 0, spawned = 0;
            var biomes = new List<string>();
            for (int i = 0; i < cells; i++)
            {
                int cell = land[rng.NextInt(land.Count)];
                var life = w.People.Life(cell);
                var spots = PeopleSamples.SpawnSpots(life, people);
                if (spots.Count == 0) continue;
                int t0 = spots[0].Y * 150 + spots[0].X;
                if (!life.HasDrinkFrom(t0)) noDrink++;
                if (!life.HasFoodFrom(t0)) noFood++;
                biomes.Add(Wtw.Sim.Planet.PlanetNames.Of(p.Biome[cell]) + (life.HasDrinkFrom(t0) ? "" : "(물 없음)") + (life.HasFoodFrom(t0) ? "" : "(먹을 것 없음)"));
                foreach (var (x, y) in spots) w.Apply(new Intervention(0, Intervention.Kinds.SpawnPerson, new long[] { cell, x, y }));
                spawned += spots.Count;
            }
            var home = w.People.All.ToDictionary(q => q.Id, q => q.Cell);
            var dryStart = w.People.All.Where(q => !w.People.Life(q.Cell).HasDrinkFrom(q.Tile(0))).Select(q => q.Id).ToHashSet();
            long end = years * GameTime.TicksPerYear;
            for (long t = 0; t < end; t += GameTime.TicksPerDay) w.AdvanceTo(Math.Min(end, t + GameTime.TicksPerDay));
            var st = w.People.Stats;
            int movedAlive = w.People.All.Count(q => !q.Dead && home.TryGetValue(q.Id, out int hc) && q.Cell != hc);
            int drySurvived = w.People.All.Count(q => !q.Dead && dryStart.Contains(q.Id));
            rows[k] = (seed, spawned, noDrink, noFood, st.Deaths.ToArray(), st.NoWaterDeaths, st.NoFoodDeaths, w.People.AliveCount, st.FindHow.ToArray(), string.Join(", ", biomes), movedAlive, dryStart.Count, drySurvived, st.Crossings);
        });
        var sb = new StringBuilder();
        sb.AppendLine($"## 아무 땅에나 뿌림: 시드 {seeds}개 × 땅 칸 {cells}곳(무작위) × {people}명 × {years}년");
        sb.AppendLine();
        sb.AppendLine("| 시드 | 뿌림 | 물 없는 칸 | 먹을 것 없는 칸 | 끝에 살아 있음 | 굶주림 | 목마름 | 추위 | 더위 | 늙음 · 병 | 물이 지방에 없어 죽음 | 먹을 것이 지방에 없어 죽음 | 고른 칸 |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var r in rows)
            sb.AppendLine($"| {r.Seed} | {r.Spawned} | {r.NoDrinkCells} | {r.NoFoodCells} | {r.Alive} | {r.Deaths[1]} | {r.Deaths[2]} | {r.Deaths[3]} | {r.Deaths[4]} | {r.Deaths[5] + r.Deaths[6]} | {r.NoWater} | {r.NoFood} | {r.Biomes} |");
        long sp = rows.Sum(r => (long)r.Spawned), dead = rows.Sum(r => r.Deaths.Sum());
        sb.AppendLine();
        sb.AppendLine($"- 모두: 뿌린 사람 {sp}명 · 죽음 {dead}명 — 굶주림 {rows.Sum(r => r.Deaths[1])} · 목마름 {rows.Sum(r => r.Deaths[2])} · 추위 {rows.Sum(r => r.Deaths[3])} · 더위 {rows.Sum(r => r.Deaths[4])} · 늙음 · 병 {rows.Sum(r => r.Deaths[5] + r.Deaths[6])}");
        sb.AppendLine($"- **지방 안(닿을 수 있는 땅)에 물이 없어서 죽은 사람 {rows.Sum(r => r.NoWater)}명 · 먹을 것이 없어서 죽은 사람 {rows.Sum(r => r.NoFood)}명** (뿌린 사람의 {(rows.Sum(r => r.NoWater + r.NoFood)) * 100.0 / Math.Max(1, sp):0.0}%)");
        sb.AppendLine($"- 고른 칸 {seeds * cells}곳 중 물이 없는 칸 {rows.Sum(r => r.NoDrinkCells)} · 먹을 것이 없는 칸 {rows.Sum(r => r.NoFoodCells)}");
        sb.AppendLine($"- (E3b) **처음 뿌린 사람 중 끝에 다른 지방에서 살아 있는 사람 {rows.Sum(r => r.MovedAlive)}명** · 물 없는 땅에 뿌린 사람 {rows.Sum(r => r.NoWaterSpawned)}명 중 살아 남은 사람 {rows.Sum(r => r.NoWaterSurvived)}명 · 경계를 넘은 횟수 {rows.Sum(r => r.Crossings)}");
        long[] how = new long[4]; foreach (var r in rows) for (int i = 0; i < 4; i++) how[i] += r.How[i];
        long ht = Math.Max(1, how.Sum());
        sb.AppendLine($"- 물 · 먹을 것을 찾아간 방법: 보임 {how[0] * 100 / ht}% · 기억 {how[1] * 100 / ht}% · 남을 따라감 {how[2] * 100 / ht}% · 찾아다님 {how[3] * 100 / ht}%");
        Console.Write(sb.ToString());
        if (outPath is not null) File.WriteAllText(outPath, sb.ToString());
    }

    static void One(int people, int years, ulong seed, string where, bool family, int cap)
    {
        long seen = 0;
        var last = new Dictionary<int, string>();
        var r = Simulate(seed, people, years, true, cap: cap, family: family, where: where, perDay: (w, d) =>
        {
            foreach (var e in w.People.Events)
            {
                if (e.Seq <= seen) continue;
                seen = e.Seq;
                if (e.Kind != WorldEventKind.Death) continue;
                var q = w.People.Get(e.Person)!;
                var life = w.People.Life(q.Cell);
                int t = q.Tile(w.Now);
                int dr = life.NearestDrink(t % 150, t / 150, 220);
                Console.WriteLine($"  † {d}일 {e.Text} · {q.Stage(w.Now)} · 타일 {t % 150},{t / 150} 땅번호 {life.Region[t]} · 마실 곳 {(dr < 0 ? "없음" : $"{dr % 150},{dr / 150}")} · 안은 사람 {q.Carrier} · 배 {q.Food / 1000} 물 {q.Water / 1000} 탈수 {q.Dehydration / 1000} 영양 {q.Malnutrition / 1000} · 마지막 판단 {q.Why} · 그 전날 {last.GetValueOrDefault(q.Id, "?")}");
            }
            foreach (var q in w.People.All) if (!q.Dead) last[q.Id] = $"{q.Act}/{q.Why} 걷는중={q.Walking} 목표={q.Target} 잠={q.Asleep} 계획행동={q.PlanAction} 물그릇={q.PotWater}";
            if (d % 5 != 0) return;
            var alive = w.People.All.Where(q => !q.Dead).ToList();
            if (alive.Count == 0) { Console.WriteLine($"{d}일: 모두 죽음"); return; }
            var q0 = alive[0];
            Console.WriteLine($"{d}일 살아 있음 {alive.Count} · 기분 중앙값 {Median(alive.Select(q => w.People.Mood(q)).ToList())} · 배부름 {alive.Average(q => q.Food) / 1000:0} · 물 {alive.Average(q => q.Water) / 1000:0} · 잠 {alive.Average(q => q.Rest) / 1000:0} · 저체온 {alive.Average(q => q.Hypothermia) / 1000:0} · 영양실조 {alive.Average(q => q.Malnutrition) / 1000:0} | {q0.Name}: {w.People.ActivityText(q0)} — {w.People.WhyText(q0)}");
        });
        if (_lastWorld is { } lw)
        {
            var ps = lw.People;
            var langs = ps.All.Where(q => !q.Dead).GroupBy(q => q.LangId).Select(g => $"{ps.LanguageLabel(g.Key)}: {g.Count()}명").ToList();
            Console.WriteLine("말: " + string.Join(" · ", langs));
            foreach (var q in ps.All.Where(q => !q.Dead).Take(24))
                Console.WriteLine($"  {ps.DisplayName(q)} ({ps.NameMeaning(q)}) · {ps.LanguageLabel(q.LangId)} · 물={ps.WordOf(q, ps.Lang.ConceptIndex("water"))} 너={ps.WordOf(q, ps.Lang.ConceptIndex("you"))} 나={ps.WordOf(q, ps.Lang.ConceptIndex("me"))} 해={ps.WordOf(q, ps.Lang.ConceptIndex("sun"))}");
            var a = ps.All.Where(q => !q.Dead).ToList();
            if (a.Count >= 2) Console.WriteLine($"  알아듣기: {ps.DisplayName(a[0])}↔{ps.DisplayName(a[1])} {ps.Understanding(a[0], a[1])}% · {ps.DisplayName(a[0])}↔{ps.DisplayName(a[^1])} {ps.Understanding(a[0], a[^1])}%");
        }
        Console.WriteLine($"끝: 살아 있음 {r.Alive}/{r.Ever} · 태어남 {r.Births} · 짝 {r.Pairs} · 붕괴 {r.Breaks} · 이야기 {r.Talks} · 말다툼 {r.Arguments} · 기분 중앙값 {r.MoodMedian} · {r.Seconds:0.0}초 · 칸 {r.Cell}({r.Biome}) 먹을 것 {r.FoodSupply / 100_000.0:0}명분/일");
        Console.WriteLine("죽음: " + string.Join(" · ", Enumerable.Range(1, 6).Select(c => $"{CauseNames[c]} {r.Deaths[c]}")));
        if (_lastWorld is { } lw2) Console.WriteLine($"찾아간 방법: 보임 {lw2.People.Stats.FindHow[0]} · 기억 {lw2.People.Stats.FindHow[1]} · 따라감 {lw2.People.Stats.FindHow[2]} · 찾아다님 {lw2.People.Stats.FindHow[3]} · 물 없어 죽음 {lw2.People.Stats.NoWaterDeaths} · 먹을 것 없어 죽음 {lw2.People.Stats.NoFoodDeaths}");
        Console.WriteLine($"예약 {r.Events:N0} · 판단 {r.Decisions:N0} · 길 {r.Paths:N0} 실패 {r.PathFails:N0} · 사람 하루당 {r.Seconds * 1e6 / Math.Max(1, r.PersonDays):0}µs");
        if (_lastWorld is { } lw3) Console.WriteLine(DiscoverBench.Summary(lw3));
    }
}
