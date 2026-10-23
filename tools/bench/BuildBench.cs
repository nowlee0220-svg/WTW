using System.Diagnostics;
using System.Text;
using Wtw.Sim.People;
using Wtw.Sim.Time;
using Wtw.Sim.World;

/// <summary>
/// (E6) 건물 측정. dotnet run -c Release --project tools/bench -- people build …
///   [--seeds 20] [--people 9] [--years 200] [--cell home|cool|cold|칸] [--know coat|build] [--cap 300] [--passion on|off] [--parallel N] [--threads auto] [--out 파일.md]
///   시드마다 그 땅에 가족(사람 수 ÷ 3)을 뿌리고 Y 년 → 처음 바람막이 · 움집 · 구덩이 · 작업대 · 불자리 · 무덤 해 · 건물 수(지방마다) · 품질 · 머문 날 ·
///   모아 둔 것 · 죽음 원인 · 발견 해 · 잠든 시간 중 곁에 불 · 집 안 · 열정 · 되풀이 감시.
///   --know coat: 툰드라 (가)(털옷 · 불 · 가죽 일을 아는 가족 — E5b 장면과 같다). --know build: 불 · 바람막이 · 움집 · 구덩이 짓는 법을 안다.
/// </summary>
public static class BuildBench
{
    public sealed class Row
    {
        public ulong Seed; public int Cell; public string Biome = ""; public double Temp;
        public int Alive, Ever, Peak; public long[] Deaths = new long[12];
        public long PersonDays; public double Seconds; public int ExtinctAt = -1;
        public readonly SortedDictionary<string, int> FirstYear = new();
        public readonly SortedDictionary<string, int> FirstBuilt = new();
        public PeopleStats St = new();
        public int[] DoneByKind = Array.Empty<int>(); public int[] Quality = new int[4]; public int Sites, Worn, Inhabited, WithBuildings, Graves;
        public long SleepHours, SleepByFire, SleepInHouse, SleepSheltered;
        public int Named;
        public double SkillPassion = double.NaN, SkillPlain = double.NaN, MoodPassion = double.NaN, MoodPlain = double.NaN;
        public int PassionPeople, PassionEver;
        public double SkillAll = double.NaN, MoodAll = double.NaN;
        public Repeat Rep = new();
    }

    /// <summary>되풀이 감시: 사람 · 날마다 (1) 경계를 넘은 수 (2) 같은 법을 실패하고 곧바로(두 시간 안) 다시 한 수 (3) 같은 물건을 버리고 그날 다시 주운 수 (4) 같은 두 자리(10타일 넘게 떨어진)를 오간 수 (5) (E6) 땔감을 구하러 가거나 불에 넣은 수(불을 지키는 일).</summary>
    public sealed class Repeat
    {
        public static readonly string[] Names = { "하루에 지방 경계를 넘은 수", "같은 법을 실패하고 곧바로 다시 한 수", "같은 물건을 버리고 그날 다시 주운 수", "같은 두 자리를 오간 수", "땔감을 구하러 가거나 불에 넣은 수" };
        public static readonly int[] Thresholds = { 20, 10, 10, 20, 10 };
        public readonly long[] Sum = new long[5];
        public readonly int[] Max = new int[5];
        public readonly int[] MaxPerson = { -1, -1, -1, -1, -1 };
        public readonly long[] Over = new long[5];
        public long PersonDays;
        // 사람마다 오늘의 셈 · 기억
        internal readonly Dictionary<int, int[]> Today = new();
        internal readonly Dictionary<int, (int Rule, long Tick)> LastFail = new();
        internal readonly Dictionary<int, HashSet<int>> Dropped = new();
        internal readonly Dictionary<int, (int A, int B)> Goals = new();

        internal int[] T(int id) { if (!Today.TryGetValue(id, out var a)) Today[id] = a = new int[5]; return a; }

        internal void EndDay(GameWorld w)
        {
            foreach (var q in w.People.All) if (!q.Dead) PersonDays++;
            foreach (var (id, a) in Today)
                for (int k = 0; k < 5; k++)
                {
                    Sum[k] += a[k];
                    if (a[k] > Max[k]) { Max[k] = a[k]; MaxPerson[k] = id; }
                    if (a[k] >= Thresholds[k]) Over[k]++;
                }
            Today.Clear(); Dropped.Clear();
        }
    }

    public static string Know = "";
    public static bool Debug = Environment.GetEnvironmentVariable("WTW_REPEAT_DEBUG") == "1";
    private static readonly Dictionary<string, long> ActCount = new();

    public static Row Simulate(ulong seed, int people, int years, string where, int cap, bool passion)
    {
        var w = GameWorld.Create(seed, new WorldConfig { DotCount = 0, PeopleCap = cap, Passion = passion });
        var p = w.Planet;
        int cell = where == "home" ? PeopleSamples.Home(p) : where == "cold" ? PeopleSamples.Cold(p) : where == "cool" ? PeopleSamples.Cool(p) : int.Parse(where);
        var row = new Row { Seed = seed, Cell = cell, Biome = Wtw.Sim.Planet.PlanetNames.Of(p.Biome[cell]), Temp = p.Temp10[cell] / 10.0 };
        lock (Rows) Rows[w.People] = row;
        var life = w.People.Life(cell);
        foreach (var (x, y) in PeopleSamples.SpawnSpots(life, Math.Max(1, people / 3)))
            w.Apply(new Intervention(0, Intervention.Kinds.SpawnFamily, new long[] { cell, x, y }));
        if (Know.Length > 0) { w.AdvanceTo(1); Teach(w, Know); }
        var sw = Stopwatch.StartNew();
        long end = years * GameTime.TicksPerYear;
        var cr = w.People.Craft; var bd = w.People.Builds;
        SpeedBench.WorldStarted();
        try
        {
            long step = where == "cold" ? GameTime.TicksPerHour : GameTime.TicksPerDay;
            for (long t = w.Now / step * step; t < end; t += step)
            {
                SpeedBench.Pace(w);
                w.AdvanceTo(Math.Min(end, t + step));
                // 밤의 잠자리(추운 땅만 — 한 시간마다)
                if (step == GameTime.TicksPerHour)
                    foreach (var (c, l) in w.People.Provinces)
                    {
                        int h = l.LocalHour(w.Now);
                        if (Debug && seed == 1 && h == 22 && w.Now < 80 * GameTime.TicksPerDay && l.People.Any(q => !q.Dead))
                        {
                            var lit = l.Fires.Items.Where(f => f.Value.Lit(w.Now)).ToList();
                            Console.WriteLine($"[밤] {w.Now / GameTime.TicksPerDay}일 칸 {c}: 산 {l.People.Count(q => !q.Dead)} · 켜진 불 {lit.Count}(남은 {string.Join(",", lit.Select(f => f.Value.Remaining(w.Now) / GameTime.TicksPerHour))}h) · 불 자리 {l.Fires.Count} · 버릇 {string.Join(",", l.People.Where(q => !q.Dead).Select(q => q.FireHabit))} · 하는 일 {string.Join(",", l.People.Where(q => !q.Dead).Select(q => q.Act + "/" + q.Why))} · 땔감 든 수 {string.Join(",", l.People.Where(q => !q.Dead).Select(q => q.Carry.Where(c2 => w.People.Craft.Prop(c2.Item, w.People.Craft.P_burns) >= 3).Sum(c2 => c2.Count)))}");
                        }
                        if (Debug && h >= 7 && h < 19)
                            foreach (var q in l.People) if (!q.Dead && q.Stage(w.Now) >= LifeStage.Adult) lock (ActCount) { string k = q.Act + "/" + q.Why; ActCount[k] = ActCount.GetValueOrDefault(k) + 1; }
                        if (h < 20 && h >= 5) continue;
                        foreach (var q in l.People)
                        {
                            if (q.Dead || q.Carrier >= 0 || !q.Asleep) continue;
                            int tile = q.Tile(w.Now);
                            row.SleepHours++;
                            if (l.NearestFire(tile % 150, tile / 150, w.Now, 6) >= 0) row.SleepByFire++;
                            if (l.BuildingOn(tile) is { Done: true } b && (b.Kind == bd.Index("hut") || b.Kind == bd.Index("tent"))) row.SleepInHouse++;
                            if (l.Warm is { } wm && wm[tile] > 0) row.SleepSheltered++;
                        }
                    }
                if (w.Now % GameTime.TicksPerDay != 0 && t + step < end) continue;
                int alive = w.People.AliveCount;
                long day = w.Now / GameTime.TicksPerDay;
                row.PersonDays += alive;
                row.Peak = Math.Max(row.Peak, alive);
                lock (row.Rep) row.Rep.EndDay(w);
                foreach (var (_, l) in w.People.Provinces)
                    foreach (var (_, b) in l.Buildings)
                        if (b.Done && !row.FirstBuilt.ContainsKey(bd.Buildings[b.Kind].Id)) row.FirstBuilt[bd.Buildings[b.Kind].Id] = (int)(day / GameTime.DaysPerYear);
                if (alive == 0) { row.ExtinctAt = (int)(day / GameTime.DaysPerYear); break; }
            }
        }
        finally { SpeedBench.WorldDone(); }
        row.Seconds = sw.Elapsed.TotalSeconds;
        Fill(row, w);
        lock (Rows) Rows.Remove(w.People);
        return row;
    }

    public static void Teach(GameWorld w, string know)
    {
        if (know == "coat") { HuntBench.Teach(w, "coat"); return; }
        var ps = w.People; var cr = ps.Craft;
        var rules = new List<string> { "sparks", "campfire", "windbreak", "hut", "storage_pit", "flake", "spear" };
        foreach (var q in ps.All)
        {
            foreach (var id in rules) { var r = cr.Rules.First(x => x.Id == id); ps.Learn(q, r, 0, -1, -1, -1); }
            if (q.Stage(w.Now) >= LifeStage.Adult) { q.AddCarry(cr.ItemIndex("flake"), 1); q.AddCarry(cr.ItemIndex("spear"), 1); q.AddCarry(cr.ItemIndex("flint"), 2); }
        }
    }

    private static readonly Dictionary<PeopleSim, Row> Rows = new();
    private static Row? RowOf(PeopleSim s) { lock (Rows) return Rows.TryGetValue(s, out var r) ? r : null; }

    private static void Hook()
    {
        PeopleSim.CrossHook = (s, p, _) => { if (RowOf(s) is { } r) lock (r.Rep) r.Rep.T(p.Id)[0]++; };
        // (E6) 땔감 나르기 · 불에 넣기(쌀쌀한 땅에서 땔감 하나씩 끝없이 나르던 되풀이를 이 표가 놓쳤다)
        PeopleSim.ActHook = (s, p) =>
        {
            if (!(p.Act == Act.TendFire || p.Act == Act.Material && (p.Why == Why.Fuel || p.Why == Why.EveningFire || p.Why == Why.FireKit))) return;
            if (RowOf(s) is { } r) lock (r.Rep) { int c4 = ++r.Rep.T(p.Id)[4]; if (Debug && c4 >= 20 && c4 <= 26) Console.WriteLine($"[땔감] 사람 {p.Id} {c4} {p.Act}/{p.Why} 찾는 것 {(p.PlanItem >= 0 ? s.Craft.ItemName(p.PlanItem) : "-")} 시각 {s.NowTick % 60000 / 2500}시 버릇 {p.FireHabit} 든 것 {string.Join(",", p.Carry.Select(c2 => s.Craft.ItemName(c2.Item) + c2.Count))}"); }
        };
        PeopleSim.WorkHook = (s, p, rule, ok) =>
        {
            if (RowOf(s) is not { } r) return;
            lock (r.Rep)
            {
                if (r.Rep.LastFail.TryGetValue(p.Id, out var lf) && lf.Rule == rule && rule >= 0 && s.NowTick - lf.Tick < 2 * GameTime.TicksPerHour + 2 * GameTime.TicksPerHour) { int c1 = ++r.Rep.T(p.Id)[1]; if (Debug && c1 == 10) Console.WriteLine($"[실패] 사람 {p.Id} 법 {s.Craft.ItemName(s.Craft.Rules[rule].Result)} {p.Act}/{p.Why} 바람 {p.PlanWish}"); }
                if (!ok) r.Rep.LastFail[p.Id] = (rule, s.NowTick); else r.Rep.LastFail.Remove(p.Id);
            }
        };
        PeopleSim.ItemHook = (s, p, item, picked) =>
        {
            if (RowOf(s) is not { } r) return;
            lock (r.Rep)
            {
                if (!r.Rep.Dropped.TryGetValue(p.Id, out var set)) r.Rep.Dropped[p.Id] = set = new HashSet<int>();
                if (!picked) { set.Add(item); if (Debug && r.Rep.T(p.Id)[2] >= 5) Console.WriteLine($"[버림] 사람 {p.Id} {s.Craft.ItemName(item)} 찾는 것 {(p.PlanItem >= 0 ? s.Craft.ItemName(p.PlanItem) : "-")} 법 {(p.PlanRule >= 0 ? s.Craft.ItemName(s.Craft.Rules[p.PlanRule].Result) : "-")} 짐 {s.CarryWeight(p)}/{s.CarryCapOf(p)} 든 것 {string.Join(",", p.Carry.Select(c2 => s.Craft.ItemName(c2.Item) + c2.Count))}"); }
                else if (set.Remove(item)) { int c = ++r.Rep.T(p.Id)[2]; if (Debug && c == 10) Console.WriteLine($"[되풀이] 사람 {p.Id} 물건 {s.Craft.ItemName(item)} 하는 일 {p.Act}/{p.Why} 바람 {p.PlanWish} 법 {(p.PlanRule >= 0 ? s.Craft.ItemName(s.Craft.Rules[p.PlanRule].Result) : "-")} 찾는 것 {(p.PlanItem >= 0 ? s.Craft.ItemName(p.PlanItem) : "-")} 짐 {s.CarryWeight(p)}/{s.CarryCapOf(p)} 든 것 {string.Join(",", p.Carry.Select(c2 => s.Craft.ItemName(c2.Item) + c2.Count))}"); }
            }
        };
        PeopleSim.WalkHook = (s, p, goal) =>
        {
            if (RowOf(s) is not { } r) return;
            int g = p.Cell * 22_500 + goal;
            lock (r.Rep)
            {
                var (a, b) = r.Rep.Goals.TryGetValue(p.Id, out var x) ? x : (-1, -1);
                // A → B → A: 두 걸음 전 목표로 되돌아간다(10타일 넘게 떨어진 두 자리)
                if (a == g && b >= 0 && b / 22_500 == g / 22_500 && Math.Max(Math.Abs(b % 22_500 % 150 - goal % 150), Math.Abs(b % 22_500 / 150 - goal / 150)) > 10) { int c3 = ++r.Rep.T(p.Id)[3]; if (Debug && c3 >= 18 && c3 <= 21) Console.WriteLine($"[오감] 사람 {p.Id} {c3} {p.Act}/{p.Why} 목표 {goal} 이전 {b % 22_500} 시각 {s.NowTick % 60000 / 2500}시 배 {p.Food / 1000} 물 {p.Water / 1000} 저체온 {p.Hypothermia / 1000}"); }
                r.Rep.Goals[p.Id] = (b, g);
            }
        };
    }

    public static void Fill(Row row, GameWorld w)
    {
        var ps = w.People;
        row.St = ps.Stats;
        row.Alive = ps.AliveCount; row.Ever = ps.All.Count;
        Array.Copy(row.St.Deaths, row.Deaths, 12);
        foreach (var (rule, dd) in ps.Discoveries)
        {
            string item = ps.Craft.Catalog.Items[ps.Craft.Rules[rule].Result].Id;
            int year = (int)(dd.Tick / GameTime.TicksPerYear);
            if (!row.FirstYear.TryGetValue(item, out int y0) || year < y0) row.FirstYear[item] = year;
        }
        row.DoneByKind = new int[ps.Builds.Buildings.Count];
        foreach (var (_, life) in ps.Provinces)
        {
            bool alive = life.People.Any(q => !q.Dead);
            if (alive) row.Inhabited++;
            bool any = false;
            foreach (var (_, b) in life.Buildings)
            {
                if (!b.Done) { row.Sites++; continue; }
                row.DoneByKind[b.Kind]++;
                if (ps.Builds.Buildings[b.Kind].Grave > 0) { row.Graves++; continue; }
                any = true;
                row.Quality[Math.Clamp(b.Quality, 0, 3)]++;
                if (ps.Builds.Buildings[b.Kind].DecayDays > 0 && ps.Condition(b, w.Now) < PeopleSim.WornCond) row.Worn++;
            }
            if (any && alive) row.WithBuildings++;
        }
        row.Named = ps.Named.Count;
        if (Debug)
            foreach (var q in ps.All.Where(q => q.Dead && q.Cause == DeathCause.Cold))
                Console.WriteLine($"[얼어죽음] 시드 {row.Seed} {q.DeathTick / GameTime.TicksPerYear}년 {q.DeathTick % GameTime.TicksPerYear / GameTime.TicksPerDay}일 {q.DeathTick % GameTime.TicksPerDay / GameTime.TicksPerHour}시 나이 {q.AgeYears(q.DeathTick)} 옷 {(q.Worn >= 0 ? ps.Craft.ItemName(q.Worn) + "/" + q.WornWear : "없음")} 신 {(q.WornFeet >= 0 ? ps.Craft.ItemName(q.WornFeet) : "없음")} 버릇 {q.FireHabit} 짝 {(q.Partner >= 0 ? "있음" : "-")} 하던 일 {q.Act}/{q.Why} 든 것 {string.Join(",", q.Carry.Select(c2 => ps.Craft.ItemName(c2.Item) + c2.Count))}");
        if (Debug)
        {
            var ad = ps.All.Where(q => !q.Dead && q.Stage(w.Now) >= LifeStage.Adult).ToList();
            var cr = ps.Craft; int sp = cr.Rule("sparks")!.Index, cf = cr.Rule("campfire")!.Index;
            Console.WriteLine($"[불] 시드 {row.Seed}: 불 피움 {row.St.FiresLit} · 불씨 얻음(불 곁) {row.St.FiresTaken} · 부싯돌 불씨 성공 {row.St.RuleDone[sp]} 실패 {row.St.RuleFail[sp]} · 모닥불 성공 {row.St.RuleDone[cf]} · 계획 막힘 " + string.Join(",", row.St.PlanWhy.Take(20)) + $" · 저녁 불 {row.St.EveningFires} · 버릇 평균 {ps.All.Where(q => q.Stage(w.Now) >= LifeStage.Child).DefaultIfEmpty().Average(q => q?.FireHabit ?? 0):0}");
            if (ad.Count > 0) Console.WriteLine($"[건물] 시드 {row.Seed}: 어른 {ad.Count} · 머물 곳 바람 평균 {ad.Average(q => q.Wish[12]):0.0} · 최대 {ad.Max(q => q.Wish[12])} · 모아 둘 곳 {ad.Average(q => q.Wish[13]):0.0} · 잠자리 그늘 " + string.Join(",", ad.Select(q => q.SleepShelter)) + " · 껴안음 " + ad.Count(q => q.Huddle));
        }
        // 열정: 열정이 있는 사람과 없는 사람(어른)의 가장 높은 기술 · 기분
        var withP = new List<double>(); var noP = new List<double>(); var mP = new List<double>(); var mN = new List<double>();
        foreach (var q in ps.All)
        {
            if (q.Passion.Any(x => x > 0)) row.PassionEver++;
            if (q.Dead || q.Stage(w.Now) < LifeStage.Adult) continue;
            double sk = Enumerable.Range(0, PeopleData.SkillCount).Max(s => q.SkillLevel(s));
            if (q.Passion.Any(x => x > 0)) { withP.Add(sk); mP.Add(ps.Mood(q)); row.PassionPeople++; } else { noP.Add(sk); mN.Add(ps.Mood(q)); }
        }
        if (withP.Count + noP.Count > 0) { row.SkillAll = withP.Concat(noP).Average(); row.MoodAll = mP.Concat(mN).Average(); }
        if (withP.Count > 0) { row.SkillPassion = withP.Average(); row.MoodPassion = mP.Average(); }
        if (noP.Count > 0) { row.SkillPlain = noP.Average(); row.MoodPlain = mN.Average(); }
    }

    public static void Run(int seeds, int people, int years, ulong seed0, int parallel, string? outFile, string where, int cap, bool passion)
    {
        Hook();
        var rows = new Row[seeds];
        var sw = Stopwatch.StartNew();
        Parallel.For(0, seeds, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, parallel) }, i =>
        {
            rows[i] = Simulate(seed0 + (ulong)i, people, years, where, cap, passion);
            var r = rows[i];
            Console.WriteLine($"시드 {r.Seed}: {r.Biome}({r.Temp:0.0}°C) 산 사람 {r.Alive}/{r.Ever} · 지은 것 {r.DoneByKind.Sum()} · 처음 " + string.Join(" ", r.FirstBuilt.Select(kv => $"{kv.Key}={kv.Value}")) + $" · 추위 {r.Deaths[3]} · {r.Seconds:0}초");
        });
        PeopleSim.CrossHook = null; PeopleSim.ActHook = null; PeopleSim.WorkHook = null; PeopleSim.ItemHook = null; PeopleSim.WalkHook = null;
        Console.WriteLine(Report(rows, seeds, people, years, where, cap, passion, sw.Elapsed));
        if (Debug && ActCount.Count > 0) { long tot = ActCount.Values.Sum(); foreach (var (k, n) in ActCount.OrderByDescending(x => x.Value).Take(30)) Console.WriteLine($"[낮] {k} {n * 100.0 / tot:0.0}% ({n})"); }
        if (outFile is not null) { File.WriteAllText(outFile, Report(rows, seeds, people, years, where, cap, passion, sw.Elapsed)); Console.WriteLine($"→ {outFile}"); }
    }

    private static string Report(Row[] rows, int seeds, int people, int years, string where, int cap, bool passion, TimeSpan took)
    {
        double Med(IEnumerable<double> xs) { var l = xs.Where(x => !double.IsNaN(x)).OrderBy(x => x).ToList(); return l.Count == 0 ? double.NaN : l.Count % 2 == 1 ? l[l.Count / 2] : (l[l.Count / 2 - 1] + l[l.Count / 2]) / 2; }
        double M(Func<Row, double> f) => Med(rows.Select(f));
        var bd = Wtw.Sim.Data.BuildingData.Default;
        var sb = new StringBuilder();
        sb.AppendLine($"# 건물 측정 — 시드 {seeds}개 · {where} · 가족 {Math.Max(1, people / 3)} · {years}년 · 아는 것 '{(Know.Length == 0 ? "없음" : Know)}' · 상한 {cap} · 열정 {(passion ? "켬" : "끔")} · 걸린 시간 {took.TotalMinutes:0.0}분");
        sb.AppendLine();
        sb.AppendLine("| 시드 | 칸(땅, 연평균) | 끝 인구 | 멸종 | 사는 지방 | 건물 있는 지방 | " + string.Join(" | ", bd.Buildings.Select(b => b.Name)) + " | 짓는 중 | 낡음 | 품질(나쁨·보통·좋음·훌륭) | 추위 | 굶주림 | 초 |");
        sb.AppendLine("|---|---|---|---|---|---|" + string.Concat(bd.Buildings.Select(_ => "---|")) + "---|---|---|---|---|---|");
        foreach (var r in rows)
            sb.AppendLine($"| {r.Seed} | {r.Cell}({r.Biome}, {r.Temp:0.0}°C) | {r.Alive} | {(r.ExtinctAt >= 0 ? r.ExtinctAt.ToString() : "-")} | {r.Inhabited} | {r.WithBuildings} | " + string.Join(" | ", bd.Buildings.Select(b => r.DoneByKind.Length > b.Index ? r.DoneByKind[b.Index].ToString() : "0")) + $" | {r.Sites} | {r.Worn} | {string.Join("·", r.Quality)} | {r.Deaths[3]} | {r.Deaths[1]} | {r.Seconds:0} |");
        sb.AppendLine();
        sb.AppendLine("## 중앙값");
        sb.AppendLine($"- 끝 인구 {M(r => r.Alive)} · 가장 많을 때 {M(r => r.Peak)} · 멸종 {rows.Count(r => r.ExtinctAt >= 0)}/{seeds}" + (rows.Any(r => r.ExtinctAt >= 0) ? $"(해: {string.Join(", ", rows.Where(r => r.ExtinctAt >= 0).Select(r => r.ExtinctAt))})" : "") + $" · 사람 사는 지방 {M(r => r.Inhabited)} · 건물(무덤 빼고)이 있는 사는 지방 {M(r => r.WithBuildings)}");
        foreach (var b in bd.Buildings)
        {
            var ys = rows.Where(r => r.FirstBuilt.ContainsKey(b.Id)).Select(r => (double)r.FirstBuilt[b.Id]).ToList();
            sb.AppendLine($"  - **처음 {b.Name}**: {ys.Count}/{seeds} 시드" + (ys.Count > 0 ? $" · 중앙값 {Med(ys)}년" : "") + $" · 끝에 서 있는 수 중앙값 {M(r => r.DoneByKind.Length > b.Index ? r.DoneByKind[b.Index] : 0)}");
        }
        sb.AppendLine($"- 자리를 정함 {M(r => r.St.SitesStarted)} · 다 지음 {M(r => r.St.BuildingsDone)} · 버려진 자리 {M(r => r.St.SitesAbandoned)} · 무너짐 {M(r => r.St.BuildingsCollapsed)} · 고침 {M(r => r.St.Repairs)} · 날라 온 재료 {M(r => r.St.BuildDelivered)} · 쌓은 일 {M(r => r.St.BuildWorkMin / 60.0):0}시간 · 걷은 덤불 {M(r => r.St.ClearedBrush)}");
        long q0 = rows.Sum(r => r.Quality[0]), q1 = rows.Sum(r => r.Quality[1]), q2 = rows.Sum(r => r.Quality[2]), q3 = rows.Sum(r => r.Quality[3]);
        sb.AppendLine($"- 서 있는 건물의 품질(모든 시드 합): 나쁨 {q0} · 보통 {q1} · 좋음 {q2} · 훌륭함 {q3} · 낡음 중앙값 {M(r => r.Worn)}");
        sb.AppendLine($"- 만든 물건의 품질(모든 시드 합): 나쁨 {rows.Sum(r => r.St.Q0)} · 보통 {rows.Sum(r => r.St.Q1)} · 좋음 {rows.Sum(r => r.St.Q2)} · 훌륭함 {rows.Sum(r => r.St.Q3)} · 작업대에서 만든 번 중앙값 {M(r => r.St.BenchCrafts)} · 작업대 짓는 법을 알아냄 {M(r => r.St.BenchLearned)}");
        sb.AppendLine($"- 잠: 집(움집 · 천막) 안에서 잔 번 {M(r => r.St.SleptHome)} · 바람막이 곁 {M(r => r.St.SleptWindbreak)} · 저녁 불을 챙김 {M(r => r.St.EveningFires)} · 불쏘시개를 챙김 {M(r => r.St.FireKits)} · 불씨를 담아 들고 떠남 {M(r => r.St.EmbersKept)}");
        if (rows.Any(r => r.SleepHours > 0))
            sb.AppendLine($"- **잠든 시간 중 곁(6타일)에 켜진 불**: 중앙값 {M(r => r.SleepByFire * 100.0 / Math.Max(1, r.SleepHours)):0}% · 집(움집 · 천막) 안 {M(r => r.SleepInHouse * 100.0 / Math.Max(1, r.SleepHours)):0}% · 건물이 막아 준 자리(움집 · 천막 · 바람막이 곁) {M(r => r.SleepSheltered * 100.0 / Math.Max(1, r.SleepHours)):0}%");
        sb.AppendLine($"- 무덤: 흙무덤 {M(r => r.St.BuriedEarth)} · 돌무더기 {M(r => r.St.BuriedCairn)} · 그대로 둠 {M(r => r.St.BodiesLeft)} (모든 시드 합 {rows.Sum(r => r.St.BuriedEarth)} · {rows.Sum(r => r.St.BuriedCairn)} · {rows.Sum(r => r.St.BodiesLeft)}, 죽음 {rows.Sum(r => r.Deaths.Sum())})");
        sb.AppendLine($"- 사연 있는 물건: 생김 {M(r => r.St.NamedMade)} · 물려받음 {M(r => r.St.NamedInherited)} · 잃음 {M(r => r.St.NamedLost)} · 다시 찾음 {M(r => r.St.NamedFound)} · 없어짐 {M(r => r.St.NamedGone)} (모든 시드 합 생김 {rows.Sum(r => r.St.NamedMade)})");
        sb.AppendLine($"- 모아 두기: 모아 둠 {M(r => r.St.Stored)} · 꺼내 먹음 {M(r => r.St.StashTaken)} · 쥐 · 벌레 {M(r => r.St.StashLost)} · 쥐 · 벌레가 먹은 몫 {M(r => r.St.StashLost * 100.0 / Math.Max(1, r.St.Stored)):0}%");
        long fd = rows.Sum(r => r.St.StayFarmDays), fn = rows.Sum(r => r.St.StayFarmN), od = rows.Sum(r => r.St.StayOtherDays), on = rows.Sum(r => r.St.StayOtherN);
        sb.AppendLine($"- 머문 날(떠난 사람이 그 땅에 머문 날 평균): 밭이 있는 무리 {(fn > 0 ? fd / (double)fn : double.NaN):0}일 · 없는 무리 {(on > 0 ? od / (double)on : double.NaN):0}일");
        sb.AppendLine($"- 끝에 산 어른: 가장 높은 기술 평균 {M(r => r.SkillAll):0.00} · 기분 평균 {M(r => r.MoodAll):0.0} (열정 {(passion ? "켬" : "끔")} — 켠 세계와 끈 세계를 견준다)");
        if (passion)
            sb.AppendLine($"- **열정**: 열정이 생긴 사람(모든 시드 합) {rows.Sum(r => r.PassionEver)} · 끝에 산 어른 중 열정 있는 사람 {M(r => r.PassionPeople)} · 가장 높은 기술 평균 — 있음 {M(r => r.SkillPassion):0.00} · 없음 {M(r => r.SkillPlain):0.00} · 기분 — 있음 {M(r => r.MoodPassion):0.0} · 없음 {M(r => r.MoodPlain):0.0}");
        long deaths = rows.Sum(r => r.Deaths.Sum());
        var names = new[] { "-", "굶주림", "목마름", "추위", "더위", "늙음", "병", "짐승", "사냥 사고" };
        sb.AppendLine($"- 죽음 원인(모든 시드 합 {deaths}): " + string.Join(" · ", Enumerable.Range(1, 8).Select(c => $"{names[c]} {rows.Sum(r => r.Deaths[c]) * 100.0 / Math.Max(1, deaths):0.#}% ({rows.Sum(r => r.Deaths[c])})")));
        sb.AppendLine($"- 기분 · 정신 붕괴: 붕괴 {M(r => r.St.Breaks)}번(세계마다)");
        foreach (var item in new[] { "campfire", "hearth", "pot", "cooked_meat", "sown_patch", "field", "fur_coat", "windbreak", "hut", "storage_pit", "rack", "knap_spot", "hide_frame", "kiln", "copper_lump" })
        {
            var ys = rows.Where(r => r.FirstYear.ContainsKey(item)).Select(r => (double)r.FirstYear[item]).ToList();
            sb.AppendLine($"  - 발견(짓는 법 · 만드는 법) {Wtw.Sim.Data.CraftData.Default.ItemName(Wtw.Sim.Data.CraftData.Default.ItemIndex(item))}: {ys.Count}/{seeds} 시드" + (ys.Count > 0 ? $" · 처음 나온 해 중앙값 {Med(ys)}" : ""));
        }
        sb.AppendLine();
        sb.AppendLine("## 되풀이 감시");
        sb.AppendLine("| 무엇 | 사람 하루당 평균 | 가장 많은 사람(하루) | 문턱 | 문턱을 넘은 사람 · 날 |");
        sb.AppendLine("|---|---|---|---|---|");
        long pdays = rows.Sum(r => r.Rep.PersonDays);
        for (int k = 0; k < 5; k++)
            sb.AppendLine($"| {Repeat.Names[k]} | {rows.Sum(r => r.Rep.Sum[k]) / (double)Math.Max(1, pdays):0.000} | {rows.Max(r => r.Rep.Max[k])} | 하루 {Repeat.Thresholds[k]} | {rows.Sum(r => r.Rep.Over[k])} |");
        long pd = rows.Sum(r => r.PersonDays); double sec = rows.Sum(r => r.Seconds);
        sb.AppendLine();
        sb.AppendLine($"- 속도: 사람 하루당 {sec * 1e6 / Math.Max(1, pd):0}µs (시드 여럿을 함께 돌림)");
        return sb.ToString();
    }
}
