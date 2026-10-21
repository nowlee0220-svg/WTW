using System.Text;
using Wtw.Sim.People;
using Wtw.Sim.Time;
using Wtw.Sim.World;

/// <summary>
/// (E5c 고침) 굶주림 죽음 조사: dotnet run -c Release --project tools/bench -- people starve [--seeds 4] [--years 130] [--cell home] [--nocap] [--land 칸수] [--out 파일.md]
/// 사람마다 마지막 할 일 기록(시각 · 할 일 · 까닭 · 지방 · 타일 · 배부름)을 모아 두었다가, 굶어 죽은 사람의 마지막 사흘을 본다:
/// 누가 · 어디서(그 지방의 사람 수 · 들의 먹을 것 · 보이는/닿는 먹을 것 · 모아 둔 자리 · 밭) · 무엇을 하다(사흘 동안 할 일의 몫 · 까닭) · 경계를 몇 번 넘었나.
/// 세계 전체의 "지방 넘기"(Cross)를 까닭별로도 센다.
/// </summary>
public static class StarveBench
{
    private sealed class Ring { public readonly (long T, Act A, Why W, int Cell, int Tile, int Food)[] E = new (long, Act, Why, int, int, int)[96]; public int N; }

    private sealed class Death
    {
        public ulong Seed; public int Id, Age; public string Stage = "", Land = ""; public long Tick;
        public int CellPeople, RegionPeople; public long Supply; public int NearFood = -1, NearFoodAny = -1; public bool FoodReach;
        public int StashFood = -1, StashSeeds = -1, StashDist = -1, Tilled, Patches, Ripe, Growing, CarryFood, CarrySeeds;
        public int Crossed, Cells, EatTries, FarmActs; public bool FarmGroup, Knows;
        public Dictionary<string, long> Time = new();
        public Dictionary<string, int> Whys = new();
        public string Trace = "";
    }

    public static void Run(int seeds, int people, int years, ulong first, int parallel, string? outPath, string where, bool noCap, int land)
    {
        var rings = new Dictionary<(PeopleSim, int), Ring>();
        var deaths = new List<Death>();
        var crossWhy = new Dictionary<string, long>();
        var all = new long[seeds]; var starve = new long[seeds]; var alive = new int[seeds];
        PeopleSim.ActHook = (sim, p) =>
        {
            int tile = -1;
            try { tile = p.Tile(sim.NowTick); } catch { }
            lock (rings)
            {
                if (!rings.TryGetValue((sim, p.Id), out var r)) rings[(sim, p.Id)] = r = new Ring();
                r.E[r.N % r.E.Length] = (sim.NowTick, p.Act, p.Why, p.Cell, tile, p.Food);
                r.N++;
                if (p.Act == Act.Cross) { string k = $"{p.Why}{(p.Food < PeopleSim.UrgentFood ? "(배고픔)" : "")}"; crossWhy[k] = crossWhy.GetValueOrDefault(k) + 1; }
            }
        };
        var seedOf = new Dictionary<PeopleSim, ulong>();
        PeopleSim.DeathHook = (sim, p, cause) =>
        {
            if (cause != DeathCause.Starvation) return;
            long now = sim.NowTick;
            var life = sim.Life(p.Cell);
            p.Position(now, out int x, out int y);
            int S = Wtw.Sim.Provinces.HexFrame.Size;
            int here = (y / 1000) * S + x / 1000;
            var d = new Death { Id = p.Id, Age = p.AgeYears(now), Stage = p.Stage(now).ToString(), Tick = now, Land = Wtw.Sim.Planet.PlanetNames.Of(sim.PlanetForBench.Biome[p.Cell]) };
            d.Supply = life.DailyFoodSupply() / 1000;
            int reg = (uint)here < (uint)life.Region.Length ? life.Region[here] : -1;
            foreach (var o in life.People) if (!o.Dead) { d.CellPeople++; int ot = o.Tile(now); if (reg >= 0 && (uint)ot < (uint)life.Region.Length && life.Region[ot] == reg) d.RegionPeople++; }
            var saved = life.Claims; life.Claims = null;
            int ft = life.NearestFood(x / 1000, y / 1000, now, 300);
            life.Claims = saved;
            if (ft >= 0) { d.NearFoodAny = Math.Max(Math.Abs(ft % S - x / 1000), Math.Abs(ft / S - y / 1000)); d.FoodReach = life.Paths.Reachable(here, ft); }
            if (ft >= 0 && d.FoodReach) d.NearFood = d.NearFoodAny;
            var fc = sim.FarmCounts(life);
            d.Tilled = fc.Tilled; d.Patches = fc.Patches; d.Ripe = fc.Ripe; d.Growing = fc.Growing;
            if (life.Stash >= 0) { d.StashFood = fc.StashFood; d.StashSeeds = fc.StashSeeds; d.StashDist = Math.Max(Math.Abs(life.Stash % S - x / 1000), Math.Abs(life.Stash / S - y / 1000)); }
            d.FarmGroup = fc.Tilled + fc.Patches >= 3; d.Knows = sim.KnowsFarming(p);
            foreach (var (it, c) in p.Carry) { if (sim.Craft.Prop(it, sim.Craft.PropIndex("sprouts")) > 0) d.CarrySeeds += c; else if (sim.Craft.Prop(it, sim.Craft.P_edible) > 0) d.CarryFood += c; }
            Ring? r;
            lock (rings) rings.TryGetValue((sim, p.Id), out r);
            lock (seedOf) d.Seed = seedOf.GetValueOrDefault(sim);
            if (r is not null)
            {
                int n = Math.Min(r.N, r.E.Length);
                var list = new List<(long T, Act A, Why W, int Cell, int Tile, int Food)>();
                for (int k = r.N - n; k < r.N; k++) list.Add(r.E[k % r.E.Length]);
                long from = now - 3 * GameTime.TicksPerDay;
                var sb = new StringBuilder();
                var cells = new HashSet<int>();
                for (int k = 0; k < list.Count; k++)
                {
                    var e = list[k];
                    long end = k + 1 < list.Count ? list[k + 1].T : now;
                    long s0 = Math.Max(e.T, from);
                    if (end > s0) { string key = e.A.ToString(); d.Time[key] = d.Time.GetValueOrDefault(key) + (end - s0); }
                    if (e.T >= from)
                    {
                        cells.Add(e.Cell);
                        if (e.A == Act.Cross) d.Crossed++;
                        if (e.A is Act.Eat or Act.EatCarried or Act.Gather) d.EatTries++;
                        if (e.A == Act.Farm) d.FarmActs++;
                        string wk = $"{e.A}/{e.W}"; d.Whys[wk] = d.Whys.GetValueOrDefault(wk) + 1;
                        sb.Append($"{GameTime.Format(e.T)} {e.A}/{e.W} 칸{e.Cell} t{e.Tile} 밥{e.Food / 1000}; ");
                    }
                }
                d.Cells = cells.Count;
                d.Trace = sb.ToString();
            }
            lock (deaths) deaths.Add(d);
        };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Parallel.For(0, seeds, new ParallelOptions { MaxDegreeOfParallelism = parallel }, k =>
        {
            ulong seed = first + (ulong)k;
            var cfg = new WorldConfig { DotCount = 0, PeopleCap = noCap ? 5000 : 300 };
            if (land > 0) { Wtw.Sim.Planet.PlanetGenerator.AllowTinyForBench = true; cfg = cfg with { LandCells = land }; }
            var w = GameWorld.Create(seed, cfg);
            lock (seedOf) seedOf[w.People] = seed;
            var p = w.Planet;
            int cell = where == "home" ? PeopleSamples.Home(p) : where == "cool" ? PeopleSamples.Cool(p) : int.Parse(where);
            foreach (var (x, y) in PeopleSamples.SpawnSpots(w.People.Life(cell), Math.Max(1, people / 3)))
                w.Apply(new Intervention(0, Intervention.Kinds.SpawnFamily, new long[] { cell, x, y }));
            w.AdvanceTo(years * GameTime.TicksPerYear);
            all[k] = w.People.Stats.Deaths.Sum(); starve[k] = w.People.Stats.Deaths[(int)DeathCause.Starvation]; alive[k] = w.People.AliveCount;
            Console.WriteLine($"시드 {seed}: 산 사람 {alive[k]} · 죽음 {all[k]} · 굶주림 {starve[k]} ({starve[k] * 100.0 / Math.Max(1, all[k]):0.0}%)");
        });
        PeopleSim.ActHook = null; PeopleSim.DeathHook = null;

        var o = new StringBuilder();
        o.AppendLine($"# 굶주림 죽음 조사 — 시드 {seeds} · {where} · 가족 {people / 3} · {years}년 · 상한 {(noCap ? "없음" : "300")}{(land > 0 ? $" · 땅 {land}칸" : "")} · {sw.Elapsed.TotalMinutes:0.0}분");
        o.AppendLine();
        int n0 = deaths.Count;
        o.AppendLine($"- 굶어 죽은 사람 {n0} / 모든 죽음 {all.Sum()} ({n0 * 100.0 / Math.Max(1, all.Sum()):0.0}%) · 끝에 산 사람 {string.Join(" · ", alive)}");
        string Pc(int c) => $"{c} ({c * 100.0 / Math.Max(1, n0):0}%)";
        o.AppendLine($"- 누가: 아이 {Pc(deaths.Count(d => d.Stage == "Child"))} · 어른 {Pc(deaths.Count(d => d.Stage == "Adult"))} · 늙은이 {Pc(deaths.Count(d => d.Stage == "Elder"))} · 심는 법을 앎 {Pc(deaths.Count(d => d.Knows))} · 밭이 있는 지방에서 {Pc(deaths.Count(d => d.FarmGroup))}");
        o.AppendLine($"- 땅: {string.Join(" · ", deaths.GroupBy(d => d.Land).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}"))}");
        o.AppendLine($"- 그 지방 사람 수 중앙값 {Med(deaths.Select(d => d.CellPeople))} · 같은 이어진 땅 {Med(deaths.Select(d => d.RegionPeople))} · 들의 먹을 것(하루 몫) 중앙값 {Med(deaths.Select(d => (int)d.Supply))} · 사람 하나당 {MedD(deaths.Select(d => d.Supply / (double)Math.Max(1, d.CellPeople))):0.0}");
        var near = deaths.Where(d => d.NearFood >= 0).Select(d => d.NearFood).OrderBy(v => v).ToList();
        o.AppendLine($"- 죽은 자리에서 가장 가까운 딸 것(걸어서 닿는, 몫이 남은): ≤14(보이는 거리) {Pc(near.Count(v => v <= 14))} · 15~40 {Pc(near.Count(v => v is > 14 and <= 40))} · 41~80 {Pc(near.Count(v => v is > 40 and <= 80))} · 80 넘음 {Pc(near.Count(v => v > 80))} · 지방에 없음/닿지 않음 {Pc(deaths.Count(d => d.NearFood < 0))} (닿지 않는 곳에만 있음 {Pc(deaths.Count(d => d.NearFood < 0 && d.NearFoodAny >= 0))})");
        o.AppendLine($"- 모아 둔 자리: 없음 {Pc(deaths.Count(d => d.StashFood < 0))} · 있고 먹을 것(씨 말고)이 있음 {Pc(deaths.Count(d => d.StashFood > 0))} · 씨만 있음 {Pc(deaths.Count(d => d.StashFood == 0 && d.StashSeeds > 0))} · 거리 중앙값 {Med(deaths.Where(d => d.StashDist >= 0).Select(d => d.StashDist))}");
        o.AppendLine($"- 밭: 익은 칸이 있음 {Pc(deaths.Count(d => d.Ripe > 0))} · 자라는 칸이 있음 {Pc(deaths.Count(d => d.Growing > 0))} · 들고 있던 먹을 것 있음 {Pc(deaths.Count(d => d.CarryFood > 0))} · 씨를 들고 있음 {Pc(deaths.Count(d => d.CarrySeeds > 0))}");
        o.AppendLine($"- 마지막 사흘: 지방 넘음 중앙값 {Med(deaths.Select(d => d.Crossed))}번(0번 {Pc(deaths.Count(d => d.Crossed == 0))} · 5번 넘게 {Pc(deaths.Count(d => d.Crossed > 5))}) · 거친 지방 수 중앙값 {Med(deaths.Select(d => d.Cells))} · 먹으러 나섬 중앙값 {Med(deaths.Select(d => d.EatTries))} · 밭일 중앙값 {Med(deaths.Select(d => d.FarmActs))}");
        var tot = new Dictionary<string, long>();
        foreach (var d in deaths) foreach (var (k, v) in d.Time) tot[k] = tot.GetValueOrDefault(k) + v;
        long ts = Math.Max(1, tot.Values.Sum());
        o.AppendLine($"- 마지막 사흘에 무엇을 하며 보냈나(시간 몫): {string.Join(" · ", tot.OrderByDescending(kv => kv.Value).Take(14).Select(kv => $"{kv.Key} {kv.Value * 100.0 / ts:0.0}%"))}");
        var wt = new Dictionary<string, long>();
        foreach (var d in deaths) foreach (var (k, v) in d.Whys) wt[k] = wt.GetValueOrDefault(k) + v;
        o.AppendLine($"- 마지막 사흘에 고른 일 · 까닭(번): {string.Join(" · ", wt.OrderByDescending(kv => kv.Value).Take(18).Select(kv => $"{kv.Key} {kv.Value}"))}");
        o.AppendLine($"- 세계 전체의 지방 넘기(까닭별): {string.Join(" · ", crossWhy.OrderByDescending(kv => kv.Value).Take(14).Select(kv => $"{kv.Key} {kv.Value}"))}");
        o.AppendLine();
        o.AppendLine("## 보기(앞 12명의 마지막 사흘)");
        foreach (var d in deaths.OrderBy(d => d.Seed).ThenBy(d => d.Tick).Take(12))
            o.AppendLine($"- 시드 {d.Seed} · {d.Stage} {d.Age}살 · {d.Land} · 지방 사람 {d.CellPeople} · 들 {d.Supply} · 딸 것까지 {d.NearFood}({d.NearFoodAny}) · 모아 둔 것 {d.StashFood}/{d.StashSeeds}(거리 {d.StashDist}) · 밭 {d.Tilled}+{d.Patches}(익음 {d.Ripe}) · 든 것 {d.CarryFood}/{d.CarrySeeds} · 넘음 {d.Crossed} · {GameTime.Format(d.Tick)}: {d.Trace}");
        Console.WriteLine(o.ToString().Split("## 보기")[0]);
        if (outPath is not null) File.WriteAllText(outPath, o.ToString());
    }

    private static double Med(IEnumerable<int> v) { var l = v.OrderBy(x => x).ToList(); return l.Count == 0 ? 0 : l.Count % 2 == 1 ? l[l.Count / 2] : (l[l.Count / 2 - 1] + l[l.Count / 2]) / 2.0; }
    private static double MedD(IEnumerable<double> v) { var l = v.OrderBy(x => x).ToList(); return l.Count == 0 ? 0 : l[l.Count / 2]; }
}

/// <summary>(E5c 고침) 작은 행성 고르기: people landinfo [--land 1000] [--seeds 4] — 시작 칸이 있는 대륙(이어진 땅)의 칸 수.</summary>
public static class LandInfo
{
    public static void Run(int land, int seeds, ulong first)
    {
        Wtw.Sim.Planet.PlanetGenerator.AllowTinyForBench = true;
        for (int k = 0; k < seeds; k++)
        {
            ulong seed = first + (ulong)k;
            var w = GameWorld.Create(seed, new WorldConfig { DotCount = 0, LandCells = land });
            var p = w.Planet;
            int home = PeopleSamples.Home(p);
            var grid = Wtw.Sim.Planet.GeoGrid.Shared;
            var seen = new HashSet<int> { home }; var q = new Queue<int>(); q.Enqueue(home);
            while (q.Count > 0) { int c = q.Dequeue(); foreach (int n in grid.Neighbors(c)) if (p.IsLand(n) && seen.Add(n)) q.Enqueue(n); }
            int landCount = 0; for (int c = 0; c < p.CellCount; c++) if (p.IsLand(c)) landCount++;
            Console.WriteLine($"시드 {seed}: 땅 {landCount}칸 · 시작 칸 {home}({Wtw.Sim.Planet.PlanetNames.Of(p.Biome[home])}) · 그 대륙 {seen.Count}칸");
        }
    }
}
