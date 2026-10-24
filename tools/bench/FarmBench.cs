using System.Diagnostics;
using System.Text;
using Wtw.Sim.People;
using Wtw.Sim.Time;
using Wtw.Sim.World;

/// <summary>
/// (E5c) 농사 측정. dotnet run -c Release --project tools/bench -- people farm …
///   [--seeds 20] [--people 9] [--years 200] [--cell home|cool|cold|칸] [--know sow|field] [--cap 300] [--nocap] [--land N] [--parallel N] [--threads auto] [--out 파일.md]
///   시드마다 그 땅에 가족(사람 수 ÷ 3)을 뿌리고 Y 년 → 처음 심음 · 처음 밭 · 밭 · 거둔 양 · 흉년 · 씨까지 먹음 · 짐승이 먹은 밭 · 머문 날 ·
///   죽음 원인 · 발견 해 · 인구(10년마다) · 사람 사는 지방 · 지방마다 먹을 것(들 · 짐승 · 밭) 처음 → 끝 · 속도.
///   --know sow: 처음부터 심는 법(+ 불 · 볶기)을 알고 낟알을 든다. --know field: 거기에 일구는 법 · 땅 파는 막대.
///   --nocap: 상한 없이(측정용 — 굶주린 세계, 상한 5000).
/// </summary>
public static class FarmBench
{
    public sealed class Row
    {
        public ulong Seed; public int Cell; public string Biome = ""; public double Temp;
        public int Alive, Ever, Peak, PeakYear; public long[] Deaths = new long[12];
        public long PersonDays; public double Seconds;
        public int ExtinctAt = -1;
        public List<int> PopByYear = new(), CellsByYear = new();
        public readonly SortedDictionary<string, int> FirstYear = new();
        public PeopleStats St = new();
        public int FarmGroups, Tilled, Patches, Wild, StashFood, StashSeeds, Inhabited;
        public int FirstSowYear = -1, FirstFieldYear = -1;
        public double FoodStart, FoodEnd, GameStart, GameEnd; // 사람 사는 지방마다 평균(들의 하루 몫 · 짐승 마릿수)
        public double PeoplePerCellEnd, FieldPerCellEnd;
        public List<long> FarmStays = new();
        /// <summary>(T1) 끝에 사람 사는 지방마다 들이 먹여 살리는 사람(장부) 평균 · 가뭄 든 해(그해 · 지난해)와 아닌 해의 사람 하루 · 굶어 죽은 사람.</summary>
        public double LedgerPeopleEnd; public long DroughtPD, NormalPD, DroughtStarve, NormalStarve;
        public List<double> PerCellByYear = new();
        public List<int> StarveByYear = new();
        /// <summary>(E6b) 해마다: 사람 하루 · 가뭄(그해 날씨 50% 아래) 땅의 사람 하루 · 굶어 죽음 · 태어남 · 배 속의 아기를 잃음 · 먹을 것이 모자라 떠남 · 짐승 마릿수(사는 지방 평균).</summary>
        public List<long> YPD = new(), YDroughtPD = new(), YStarve = new(), YBirth = new(), YMisc = new(), YLeave = new();
        public List<double> YGame = new();
        /// <summary>(E6b) 굶어 죽은 사람: [0 젖먹이 · 1 아이 · 2 어른 · 3 늙은이] · 그중 젖 먹이던 어머니 · 아이 밴 사람 · 병든 사람. 뿌린 가족 수.</summary>
        public long[] StarveStage = new long[4]; public long StarveNursing, StarvePregnant, StarveIll; public int Families;
    }

    /// <summary>(E6b) --wide N: 처음에 작은 행성의 사람이 살 만한 칸(마실 물 · 딸 것이 있고 연평균 0°C 이상) N 칸마다 한 가족을 뿌린다(땅이 일찍 차게).</summary>
    public static int Wide;

    public static string Know = "";
    public static bool NoCap;
    /// <summary>(E5c 고침) --land N: 땅 N 칸의 작은 행성(측정 도구만 1,000 칸 아래를 만든다 — 땅이 다 차는 굶주린 세계).</summary>
    public static int Land;

    public static Row Simulate(ulong seed, int people, int years, string where, int cap)
    {
        if (Land > 0) Wtw.Sim.Planet.PlanetGenerator.AllowTinyForBench = true;
        var w = GameWorld.Create(seed, new WorldConfig { DotCount = 0, PeopleCap = NoCap ? 5000 : cap, LandCells = Land > 0 ? Land : 16_000 });
        var p = w.Planet;
        int cell = where == "home" ? PeopleSamples.Home(p) : where == "cold" ? PeopleSamples.Cold(p) : where == "cool" ? PeopleSamples.Cool(p) : int.Parse(where);
        var row = new Row { Seed = seed, Cell = cell, Biome = Wtw.Sim.Planet.PlanetNames.Of(p.Biome[cell]), Temp = p.Temp10[cell] / 10.0 };
        var life = w.People.Life(cell);
        row.FoodStart = life.DailyFoodSupply() / 1000.0; row.GameStart = w.People.AnimalCounts(life).Sum();
        foreach (var (x, y) in PeopleSamples.SpawnSpots(life, Math.Max(1, people / 3)))
            w.Apply(new Intervention(0, Intervention.Kinds.SpawnFamily, new long[] { cell, x, y }));
        row.Families = Math.Max(1, people / 3);
        if (Wide > 0)
        {
            int k = 0;
            for (int c = 0; c < p.CellCount; c++)
            {
                if (c == cell || !p.IsLand(c) || p.Temp10[c] < 0) continue;
                var cl = w.People.Life(c);
                if (cl.DrinkTiles == 0 || cl.FoodTiles < 20) continue;
                if (k++ % Wide != 0) continue;
                foreach (var (x, y) in PeopleSamples.SpawnSpots(cl, 1))
                    w.Apply(new Intervention(0, Intervention.Kinds.SpawnFamily, new long[] { c, x, y }));
                row.Families++;
            }
        }
        if (Know is "sow" or "field") { w.AdvanceTo(1); Teach(w, Know); }
        var sw = Stopwatch.StartNew();
        long end = years * GameTime.TicksPerYear;
        SpeedBench.WorldStarted();
        try
        {
            for (long t = w.Now / GameTime.TicksPerDay * GameTime.TicksPerDay; t < end; t += GameTime.TicksPerDay)
            {
                SpeedBench.Pace(w);
                w.AdvanceTo(Math.Min(end, t + GameTime.TicksPerDay));
                int alive = w.People.AliveCount;
                long day = w.Now / GameTime.TicksPerDay;
                row.PersonDays += alive;
                long yr = day / GameTime.DaysPerYear;
                foreach (var (pc, pl) in w.People.Provinces)
                {
                    int n = pl.People.Count(q => !q.Dead);
                    if (n == 0) continue;
                    if (w.People.YearLuck(pc, yr) < 50 || (yr > 0 && w.People.YearLuck(pc, yr - 1) < 50)) row.DroughtPD += n; else row.NormalPD += n;
                }
                if (alive > row.Peak) { row.Peak = alive; row.PeakYear = (int)(day / GameTime.DaysPerYear); }
                {
                    // (E6b) 해마다 셈
                    while (row.YPD.Count <= yr) { row.YPD.Add(0); row.YDroughtPD.Add(0); }
                    row.YPD[(int)yr] += alive;
                    foreach (var (pc, pl) in w.People.Provinces) { if (w.People.YearLuck(pc, yr) >= 50) continue; foreach (var q in pl.People) if (!q.Dead) row.YDroughtPD[(int)yr]++; }
                    if (day % GameTime.DaysPerYear == GameTime.DaysPerYear - 1)
                    {
                        var st = w.People.Stats;
                        row.YStarve.Add(st.Deaths[1] - row.YStarve.Sum()); row.YBirth.Add(st.Births - row.YBirth.Sum()); row.YMisc.Add(st.Miscarriages - row.YMisc.Sum());
                        row.YLeave.Add(st.MoveWhy[(int)Why.Scarce] - row.YLeave.Sum());
                        int gc = 0; double gs = 0; foreach (var (_, pl) in w.People.Provinces) if (pl.People.Any(q => !q.Dead)) { gc++; gs += w.People.AnimalCounts(pl).Sum(); }
                        row.YGame.Add(gc == 0 ? 0 : gs / gc);
                    }
                }
                if (day % GameTime.DaysPerYear == 0)
                {
                    row.PopByYear.Add(alive);
                    row.CellsByYear.Add(w.People.Provinces.Values.Count(l => l.People.Any(q => !q.Dead)));
                    row.PerCellByYear.Add(row.CellsByYear[^1] == 0 ? 0 : alive / (double)row.CellsByYear[^1]);
                }
                if (alive == 0) { row.ExtinctAt = (int)(day / GameTime.DaysPerYear); break; }
            }
        }
        finally { SpeedBench.WorldDone(); }
        row.Seconds = sw.Elapsed.TotalSeconds;
        Fill(row, w);
        return row;
    }

    public static void Teach(GameWorld w, string know)
    {
        var ps = w.People; var cr = ps.Craft;
        var rules = new List<string> { "sparks", "campfire", "roast_grain", "sow" };
        if (know == "field") rules.AddRange(new[] { "field", "spear" });
        foreach (var q in ps.All)
        {
            foreach (var id in rules) { var r = cr.Rules.First(x => x.Id == id); ps.Learn(q, r, 0, -1, -1, -1); }
            if (q.Stage(w.Now) >= LifeStage.Adult) { q.AddCarry(cr.ItemIndex("grain"), 6); if (know == "field") q.AddCarry(cr.ItemIndex("spear"), 1); }
        }
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
        row.FirstSowYear = row.FirstYear.TryGetValue("sown_patch", out int s1) ? s1 : -1;
        row.FirstFieldYear = row.FirstYear.TryGetValue("field", out int f1) ? f1 : -1;
        int cells = 0; double food = 0, game = 0, ppl = 0, fields = 0;
        foreach (var (c, life) in ps.Provinces)
        {
            int n = life.People.Count(q => !q.Dead);
            if (n == 0) continue;
            cells++; ppl += n;
            food += life.DailyFoodSupply() / 1000.0;
            game += ps.AnimalCounts(life).Sum();
            var fc = ps.FarmCounts(life);
            row.Tilled += fc.Tilled; row.Patches += fc.Patches; row.Wild += fc.Wild; row.StashFood += fc.StashFood; row.StashSeeds += fc.StashSeeds;
            fields += fc.Tilled + fc.Patches;
            if (fc.Tilled + fc.Patches >= 3) { row.FarmGroups++; if (life.FarmSince >= 0) row.FarmStays.Add((w.Now - life.FarmSince) / GameTime.TicksPerDay); }
        }
        row.Inhabited = cells;
        double led = 0;
        foreach (var (c, life) in ps.Provinces) if (life.People.Any(q => !q.Dead)) led += ps.Ledger(life).WildTotal * 1000 / PeopleSim.DailyNeed;
        if (cells > 0) row.LedgerPeopleEnd = led / cells;
        int ys = (int)(w.Now / GameTime.TicksPerYear) + 1;
        for (int y = 0; y < ys; y++) row.StarveByYear.Add(0);
        foreach (var q in ps.All)
        {
            if (!q.Dead || q.Cause != DeathCause.Starvation) continue;
            row.StarveStage[(int)q.Stage(q.DeathTick)]++;
            if (q.Female && q.Children.Any(c => ps.Get(c) is { } k && k.BirthTick <= q.DeathTick && q.DeathTick - k.BirthTick < 3 * GameTime.TicksPerYear && (!k.Dead || k.DeathTick >= q.DeathTick))) row.StarveNursing++;
            if (q.Bio.Count > 0 && q.Bio.Any(b => b.Kind == BioKind.Ill && q.DeathTick - b.Tick < 10 * GameTime.TicksPerDay)) row.StarveIll++;
            long y = q.DeathTick / GameTime.TicksPerYear;
            if (y < ys) row.StarveByYear[(int)y]++;
            if (ps.YearLuck(q.Cell, y) < 50 || (y > 0 && ps.YearLuck(q.Cell, y - 1) < 50)) row.DroughtStarve++; else row.NormalStarve++;
        }
        if (cells > 0) { row.FoodEnd = food / cells; row.GameEnd = game / cells; row.PeoplePerCellEnd = ppl / cells; row.FieldPerCellEnd = fields / cells; }
    }

    public static void Run(int seeds, int people, int years, ulong seed0, int parallel, string? outFile, string where, int cap)
    {
        var rows = new Row[seeds];
        var sw = Stopwatch.StartNew();
        Parallel.For(0, seeds, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, parallel) }, i =>
        {
            rows[i] = Simulate(seed0 + (ulong)i, people, years, where, cap);
            var r = rows[i];
            Console.WriteLine($"시드 {r.Seed}: {r.Biome}({r.Temp:0.0}°C) 산 사람 {r.Alive}/{r.Ever} · 가장 많을 때 {r.Peak}({r.PeakYear}년) · 처음 심음 {r.FirstSowYear}년 · 처음 밭 {r.FirstFieldYear}년 · 밭 {r.Tilled}+{r.Patches} · 거둠 {r.St.HarvestItems} · 굶주림 {r.Deaths[1]} · {r.Seconds:0}초");
        });
        double Med(IEnumerable<double> xs) { var l = xs.OrderBy(x => x).ToList(); return l.Count == 0 ? double.NaN : l.Count % 2 == 1 ? l[l.Count / 2] : (l[l.Count / 2 - 1] + l[l.Count / 2]) / 2; }
        double M(Func<Row, double> f) => Med(rows.Select(f));
        var sb = new StringBuilder();
        sb.AppendLine($"# 농사 측정 — 시드 {seeds}개 · {where} · 가족 {Math.Max(1, people / 3)} · {years}년 · 아는 것 '{(Know.Length == 0 ? "없음" : Know)}' · 상한 {(NoCap ? "없음(5000)" : cap.ToString())}{(Land > 0 ? $" · 땅 {Land}칸(작은 행성)" : "")} · 걸린 시간 {sw.Elapsed.TotalMinutes:0.0}분");
        sb.AppendLine();
        sb.AppendLine("| 시드 | 칸(땅, 연평균) | 끝 인구 | 가장 많을 때(해) | 멸종 | 사는 지방 | 처음 심음 | 처음 밭 | 밭 무리 | 일군 밭 · 묻은 자리 · 저절로 | 심은 타일 | 거둔 수 | 흉년 | 씨까지 먹음 | 짐승이 먹음 | 모아 둔 먹을 것 · 씨 | 굶주림 | 초 |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var r in rows)
            sb.AppendLine($"| {r.Seed} | {r.Cell}({r.Biome}, {r.Temp:0.0}°C) | {r.Alive} | {r.Peak}({r.PeakYear}) | {(r.ExtinctAt >= 0 ? r.ExtinctAt.ToString() : "-")} | {r.Inhabited} | {(r.FirstSowYear >= 0 ? r.FirstSowYear.ToString() : "-")} | {(r.FirstFieldYear >= 0 ? r.FirstFieldYear.ToString() : "-")} | {r.FarmGroups} | {r.Tilled} · {r.Patches} · {r.Wild} | {r.St.Sown} | {r.St.HarvestItems} | {r.St.BadHarvests} | {r.St.SeedEaten} | {r.St.FieldEaten} | {r.StashFood} · {r.StashSeeds} | {r.Deaths[1]} | {r.Seconds:0} |");
        sb.AppendLine();
        sb.AppendLine("## 중앙값");
        var sowY = rows.Where(r => r.FirstSowYear >= 0).Select(r => (double)r.FirstSowYear).ToList();
        var fieldY = rows.Where(r => r.FirstFieldYear >= 0).Select(r => (double)r.FirstFieldYear).ToList();
        sb.AppendLine($"- 끝 인구 {M(r => r.Alive)} · 가장 많을 때 {M(r => r.Peak)}(해 {M(r => r.PeakYear)}) · 멸종 {rows.Count(r => r.ExtinctAt >= 0)}/{seeds} · 사람 사는 지방 {M(r => r.Inhabited)}");
        sb.AppendLine($"- **처음 심음(보고 알아냄)**: {sowY.Count}/{seeds} 시드 · 중앙값 {Med(sowY)}년 · **처음 밭(일군 땅)**: {fieldY.Count}/{seeds} 시드 · 중앙값 {Med(fieldY)}년");
        sb.AppendLine($"- 끝에 밭을 가진 무리(지방) {M(r => r.FarmGroups)} · 일군 밭 {M(r => r.Tilled)} · 그냥 묻은 자리 {M(r => r.Patches)} · 저절로 난 싹 {M(r => r.Wild)} · 모아 둔 먹을 것 {M(r => r.StashFood)} · 모아 둔 씨 {M(r => r.StashSeeds)}");
        sb.AppendLine($"- 저절로 난 싹(흘린 씨) {M(r => r.St.WildSprouts)} · 싹을 본 번 {M(r => r.St.SproutSights)} · 보고 알아낸 사람 {M(r => r.St.SproutLearned)}");
        sb.AppendLine($"- 일군 타일 {M(r => r.St.Tilled)} · 심은 타일 {M(r => r.St.Sown)} · 거둔 타일 {M(r => r.St.HarvestTiles)} · **거둔 수 {M(r => r.St.HarvestItems)}** · 김맨 타일 {M(r => r.St.Weeded)} · 싹이 안 난 씨 {M(r => r.St.SeedsFailed)}");
        sb.AppendLine($"- 다 익고 오래 두어 떨어짐 {M(r => r.St.FieldsFallen)} · 빈 밭에 잡풀이 덮음 {M(r => r.St.FieldsWeededOver)} · **흉년 {M(r => r.St.BadHarvests)}** · **씨까지 먹어 버림 {M(r => r.St.SeedEaten)}**");
        sb.AppendLine($"- **짐승이 뜯어 먹은 밭(칸 · 번) {M(r => r.St.FieldEaten)}** · 다 먹혀 망침 {M(r => r.St.FieldsRuined)} · 지키러 남음 {M(r => r.St.Guarded)} · 쫓아냄 {M(r => r.St.ChasedOff)}");
        sb.AppendLine($"- 아이: 가짐 {M(r => r.St.Conceptions)} · 태어남 {M(r => r.St.Births)} · **짝 곁에서 아이를 가질 만했는데 배를 곯아(영양 부족) 갖지 않은 번 {M(r => r.St.ConceiveHungry)}**");
        sb.AppendLine($"- 모아 둠 {M(r => r.St.Stored)} · 모아 둔 데서 가져감 {M(r => r.St.StashTaken)} · 모아 둔 것을 쥐 · 벌레가 먹음 {M(r => r.St.StashLost)} · 씨를 가지러 {M(r => r.St.SeedFetch)} · 씨를 먹을 거리로 주움 {M(r => r.St.SeedGathered)}");
        sb.AppendLine($"- 썩어 버린 것(들고 있다): 열매 등 {M(r => r.St.RotOther)} · 날고기 · 날생선 {M(r => r.St.RotMeat)} · 날가죽 {M(r => r.St.RotHide)} · 땅의 짐승 것 {M(r => r.St.RotGround)}");
        {
            long Mv(Why w) => rows.Sum(r => r.St.MoveWhy[(int)w]);
            sb.AppendLine($"- 떠난 까닭(모든 시드 합): 먹을 것이 모자라서 {Mv(Why.Scarce):N0} · 가족 따라 {Mv(Why.FollowFamily):N0} · 돌아감 {Mv(Why.GoBack):N0} · 짝 찾기 {Mv(Why.SeekMate):N0} · 호기심 {Mv(Why.Explore):N0}");
        }
        long fd = rows.Sum(r => r.St.StayFarmDays), fn = rows.Sum(r => r.St.StayFarmN), od = rows.Sum(r => r.St.StayOtherDays), on = rows.Sum(r => r.St.StayOtherN);
        var stays = rows.SelectMany(r => r.FarmStays).Select(x => (double)x).ToList();
        sb.AppendLine($"- **머문 날**: 떠난 사람이 그 땅에 머문 날(평균) — 밭이 있는 무리 {(fn > 0 ? fd / (double)fn : double.NaN):0}일({fn}번) · 없는 무리 {(on > 0 ? od / (double)on : double.NaN):0}일({on}번) · 끝에 밭이 있는 지방이 밭을 가진 지 중앙값 {Med(stays):0}일({stays.Count}곳)");
        long deaths = rows.Sum(r => r.Deaths.Sum());
        var names = new[] { "-", "굶주림", "목마름", "추위", "더위", "늙음", "병", "짐승", "사냥 사고" };
        sb.AppendLine($"- 죽음 원인(모든 시드 합 {deaths}): " + string.Join(" · ", Enumerable.Range(1, 8).Select(c => $"{names[c]} {rows.Sum(r => r.Deaths[c]) * 100.0 / Math.Max(1, deaths):0.#}% ({rows.Sum(r => r.Deaths[c])})")));
        sb.AppendLine($"- 굶주림 사망 시드별 중앙값 {M(r => r.Deaths[1] * 100.0 / Math.Max(1, r.Deaths.Sum())):0.0}%");
        sb.AppendLine($"- 인구(10년마다 중앙값): " + string.Join(" · ", Enumerable.Range(0, years / 10).Select(k => Med(rows.Select(r => k * 10 < r.PopByYear.Count ? (double)r.PopByYear[k * 10] : 0)).ToString("0"))) + $" · 끝 {M(r => r.Alive)}");
        sb.AppendLine($"- 사람 사는 지방(10년마다 중앙값): " + string.Join(" · ", Enumerable.Range(0, years / 10).Select(k => Med(rows.Select(r => k * 10 < r.CellsByYear.Count ? (double)r.CellsByYear[k * 10] : 0)).ToString("0"))) + $" · 끝 {M(r => r.Inhabited)}");
        sb.AppendLine($"- 지방마다(사람 사는 곳 평균): 사람 {M(r => r.PeoplePerCellEnd):0.0} · 밭 {M(r => r.FieldPerCellEnd):0.0}칸 · 들의 먹을 것 하루 몫 처음(뿌린 지방) {M(r => r.FoodStart):0} → 끝(사는 지방 평균) {M(r => r.FoodEnd):0} · 짐승 마릿수 처음 {M(r => r.GameStart):0} → 끝 {M(r => r.GameEnd):0}");
        sb.AppendLine($"- (T1) 지방당 인구(10년마다 중앙값): " + string.Join(" · ", Enumerable.Range(0, years / 10).Select(k => Med(rows.Select(r => k * 10 < r.PerCellByYear.Count ? r.PerCellByYear[k * 10] : 0)).ToString("0.0"))) + $" · 끝 {M(r => r.PeoplePerCellEnd):0.0} · 끝에 사는 지방의 들이 먹여 살리는 사람(장부) {M(r => r.LedgerPeopleEnd):0.0}");
        sb.AppendLine($"- (T1) 굶어 죽은 사람(10년마다, 모든 시드 합): " + string.Join(" · ", Enumerable.Range(0, Math.Max(1, years / 10)).Select(k => rows.Sum(r => r.StarveByYear.Skip(k * 10).Take(10).Sum()).ToString())));
        {
            long dpd = rows.Sum(r => r.DroughtPD), npd = rows.Sum(r => r.NormalPD), ds = rows.Sum(r => r.DroughtStarve), ns = rows.Sum(r => r.NormalStarve);
            double rate(long d, long pd) => pd == 0 ? double.NaN : d * GameTime.DaysPerYear * 1000.0 / pd;
            sb.AppendLine($"- (T1) **가뭄 해(그해 · 지난해) vs 아닌 해**: 굶어 죽은 사람 {ds} vs {ns} · 사람 1,000명 · 1년당 {rate(ds, dpd):0.00} vs {rate(ns, npd):0.00} (사람 · 해 {dpd / GameTime.DaysPerYear:N0} vs {npd / GameTime.DaysPerYear:N0})");
            long[] e = new long[6]; foreach (var r in rows) for (int i = 0; i < 6; i++) e[i] += r.St.Eaten[i];
            long et = Math.Max(1, e.Sum());
            var en = new[] { "들에서 바로 딴 열매 · 버섯 · 이끼", "들고 먹은 열매 · 버섯 · 이끼", "낟알 · 콩 · 덩이뿌리 · 도토리", "고기 · 물고기", "그 밖(끓인 음식)", "시신" };
            sb.AppendLine($"- (T1) **먹은 것(배부름 몫)**: " + string.Join(" · ", Enumerable.Range(0, 6).Select(i => $"{en[i]} {e[i] * 100.0 / et:0.0}%")) + $" · 사람 하루당 먹은 양 {e.Sum() / 1000.0 / Math.Max(1, rows.Sum(r => r.PersonDays)):0.0}(필요 {PeopleSim.DailyNeed / 1000})");
        }
        {
            // (E6b) 몸 · 흉년
            long[] td = new long[4]; foreach (var r in rows) for (int i = 0; i < 4; i++) td[i] += r.St.ThinDays[i];
            long tt = Math.Max(1, td.Sum());
            sb.AppendLine($"- (E6b) **야윔 단계별 사람-날**: 보통 {td[0] * 100.0 / tt:0.0}% · 조금 야윔 {td[1] * 100.0 / tt:0.0}% · 야윔 {td[2] * 100.0 / tt:0.0}% · 몹시 야윔 {td[3] * 100.0 / tt:0.0}%");
            sb.AppendLine($"- (E6b) 몸에 쌓인 것에서 쓴 배부름 · 다시 쌓은 배부름(사람 하루당): {rows.Sum(r => r.St.BodyDrawn) / 1000.0 / Math.Max(1, rows.Sum(r => r.PersonDays)):0.00} · {rows.Sum(r => r.St.BodyRefilled) / 1000.0 / Math.Max(1, rows.Sum(r => r.PersonDays)):0.00} · **배 속의 아기를 잃음 {rows.Sum(r => r.St.Miscarriages)}**(합) · 너무 야위어 떠나지 못함(사람-날) {rows.Sum(r => r.St.TooWeakToLeave)}");
            long[] ss = new long[4]; foreach (var r in rows) for (int i = 0; i < 4; i++) ss[i] += r.StarveStage[i];
            sb.AppendLine($"- (E6b) **누가 굶어 죽나**(합 {ss.Sum()}): 젖먹이 {ss[0]} · 아이 {ss[1]} · 어른 {ss[2]} · 늙은이 {ss[3]} — 그중 젖 먹이던 어머니 {rows.Sum(r => r.StarveNursing)} · 죽기 열흘 안에 병든 사람 {rows.Sum(r => r.StarveIll)}");
            sb.AppendLine($"- (E6b) 짐승이 먹은 들의 먹을 것 / 먹으려던 것(합): {rows.Sum(r => r.St.AnimalForage):N0} / {rows.Sum(r => r.St.AnimalForageWant):N0} ({rows.Sum(r => r.St.AnimalForage) * 100.0 / Math.Max(1, rows.Sum(r => r.St.AnimalForageWant)):0}%)");
            // 흉년 해 vs 아닌 해: 해마다 그해 가뭄 땅에 산 사람 몫으로 나눈다(절반 넘게 = 흉년 해, 10% 아래 = 보통 해)
            long[] a = new long[10];
            foreach (var r in rows)
                for (int y = 0; y < r.YStarve.Count && y < r.YPD.Count; y++)
                {
                    double share = r.YPD[y] == 0 ? 0 : r.YDroughtPD[y] / (double)r.YPD[y];
                    int k = share >= 0.5 ? 0 : share < 0.1 ? 5 : -1;
                    if (k < 0) continue;
                    a[k] += r.YPD[y]; a[k + 1] += r.YStarve[y]; a[k + 2] += r.YBirth[y]; a[k + 3] += r.YMisc[y]; a[k + 4] += r.YLeave[y];
                }
            double per(long n, long pd) => pd == 0 ? double.NaN : n * GameTime.DaysPerYear * 1000.0 / pd;
            sb.AppendLine($"- (E6b) **흉년 해 vs 보통 해**(사람의 절반 넘게 가뭄 땅 · 10% 아래, 사람 1,000명 · 1년당): 굶어 죽음 {per(a[1], a[0]):0.0} vs {per(a[6], a[5]):0.0} · 태어남 {per(a[2], a[0]):0.0} vs {per(a[7], a[5]):0.0} · 아기를 잃음 {per(a[3], a[0]):0.0} vs {per(a[8], a[5]):0.0} · 먹을 것이 모자라 떠남 {per(a[4], a[0]):0.0} vs {per(a[9], a[5]):0.0} (사람 · 해 {a[0] / GameTime.DaysPerYear:N0} vs {a[5] / GameTime.DaysPerYear:N0})");
            sb.AppendLine($"- (E6b) 짐승 마릿수(사는 지방 평균, 10년마다 중앙값): " + string.Join(" · ", Enumerable.Range(0, years / 10).Select(k => Med(rows.Select(r => k * 10 < r.YGame.Count ? r.YGame[k * 10] : 0)).ToString("0"))));
            sb.AppendLine($"- (E6b) 굶주림 · 태어남(10년마다, 모든 시드 합): " + string.Join(" · ", Enumerable.Range(0, Math.Max(1, years / 10)).Select(k => $"{rows.Sum(r => r.YStarve.Skip(k * 10).Take(10).Sum())}/{rows.Sum(r => r.YBirth.Skip(k * 10).Take(10).Sum())}")));
        }
        foreach (var item in new[] { "campfire", "pot", "cooked_meat", "roasted_grain", "cooked_beans", "roasted_tuber", "flour", "flatbread", "sown_patch", "field", "stone_hoe", "spear", "fur_coat" })
        {
            var ys = rows.Where(r => r.FirstYear.ContainsKey(item)).Select(r => (double)r.FirstYear[item]).ToList();
            sb.AppendLine($"  - 발견 {Wtw.Sim.Data.CraftData.Default.ItemName(Wtw.Sim.Data.CraftData.Default.ItemIndex(item))}: {ys.Count}/{seeds} 시드" + (ys.Count > 0 ? $" · 처음 나온 해 중앙값 {Med(ys)}" : ""));
        }
        long pd = rows.Sum(r => r.PersonDays); double sec = rows.Sum(r => r.Seconds);
        sb.AppendLine($"- 속도: 사람 하루당 {sec * 1e6 / Math.Max(1, pd):0}µs (시드 {Math.Max(1, parallel)}개씩 함께)");
        Console.WriteLine(sb.ToString());
        if (outFile is not null) { File.WriteAllText(outFile, sb.ToString()); Console.WriteLine($"→ {outFile}"); }
    }
}
