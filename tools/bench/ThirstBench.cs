using System.Text;
using Wtw.Sim.People;
using Wtw.Sim.Provinces;
using Wtw.Sim.Time;
using Wtw.Sim.World;

/// <summary>
/// (E5b 고침) 목마름 죽음 조사: dotnet run -c Release --project tools/bench -- people thirst [--seeds 4] [--years 60] [--cell home] [--nocap] [--out 파일.md]
/// 사람마다 마지막 할 일 기록(시각 · 할 일 · 까닭 · 지방 · 타일 · 물)을 모아 두었다가, 목말라 죽은 사람의 마지막 사흘을 본다:
/// 누가(나이) · 어디서(물가까지 거리 · 그 지방에 물이 있나 · 아는 물가) · 무엇을 하다(사흘 동안 할 일의 몫).
/// </summary>
public static class ThirstBench
{
    private sealed class Ring { public readonly (long T, Act A, Why W, int Cell, int Tile, int Water, byte How)[] E = new (long, Act, Why, int, int, int, byte)[64]; public int N; }

    private sealed class Death
    {
        public ulong Seed; public int Id, Age; public string Stage = "", Land = ""; public long Tick;
        public int Region, RegionSize, Walkable, Gates, Miss, From; public int NearDrink, DrinkTiles, Known, KnownHere, KnownReach, Crossed, DrinkTries, Search, Pot; public bool Injured, Container, Reach;
        public Dictionary<string, long> Time = new();
        public string Trace = "";
    }

    public static void Run(int seeds, int people, int years, ulong first, int parallel, string? outPath, string where, int cap, bool noCap)
    {
        var rings = new Dictionary<(PeopleSim, int), Ring>();
        var deaths = new List<Death>();
        var all = new long[seeds];
        PeopleSim.ActHook = (sim, p) =>
        {
            int tile = -1;
            try { tile = p.Tile(sim.NowTick); } catch { }
            lock (rings)
            {
                if (!rings.TryGetValue((sim, p.Id), out var r)) rings[(sim, p.Id)] = r = new Ring();
                r.E[r.N % r.E.Length] = (sim.NowTick, p.Act, p.Why, p.Cell, tile, p.Water, p.How);
                r.N++;
            }
        };
        var seedOf = new Dictionary<PeopleSim, ulong>();
        PeopleSim.DeathHook = (sim, p, cause) =>
        {
            if (cause != DeathCause.Thirst) return;
            long now = sim.NowTick;
            var life = sim.Life(p.Cell);
            p.Position(now, out int x, out int y);
            int here = (y / 1000) * HexFrame.Size + x / 1000;
            var d = new Death { Id = p.Id, Age = p.AgeYears(now), Stage = p.Stage(now).ToString(), Tick = now, Land = Wtw.Sim.Planet.PlanetNames.Of(sim.PlanetForBench.Biome[p.Cell]) };
            int dr = life.NearestDrink(x / 1000, y / 1000, 200);
            d.NearDrink = dr < 0 ? -1 : Math.Max(Math.Abs(dr % HexFrame.Size - x / 1000), Math.Abs(dr / HexFrame.Size - y / 1000));
            d.Reach = dr >= 0 && life.Paths.Reachable(here, dr);
            d.DrinkTiles = life.DrinkTiles;
            d.Region = life.Region[here]; d.Walkable = life.Walk[here] ? 1 : 0; d.Miss = p.SearchMiss; d.From = p.FromCell;
            if (d.Region >= 0) for (int t = 0; t < HexFrame.TileCount; t++) if (life.Region[t] == d.Region) d.RegionSize++;
            d.Known = p.KnownWater.Count;
            foreach (int place in p.KnownWater) if (Person.PlaceCell(place) == p.Cell) { d.KnownHere++; if (life.Paths.Reachable(here, Person.PlaceTile(place))) d.KnownReach++; }
            d.Injured = p.Injury > 0; d.Pot = p.PotWater;
            Ring? r;
            lock (rings) rings.TryGetValue((sim, p.Id), out r);
            lock (seedOf) d.Seed = seedOf.GetValueOrDefault(sim);
            if (r is not null)
            {
                int n = Math.Min(r.N, r.E.Length);
                var list = new List<(long T, Act A, Why W, int Cell, int Tile, int Water, byte How)>();
                for (int k = r.N - n; k < r.N; k++) list.Add(r.E[k % r.E.Length]);
                long from = now - 3 * GameTime.TicksPerDay;
                var sb = new StringBuilder();
                for (int k = 0; k < list.Count; k++)
                {
                    var e = list[k];
                    long end = k + 1 < list.Count ? list[k + 1].T : now;
                    long s0 = Math.Max(e.T, from);
                    if (end > s0) { string key = e.A.ToString(); d.Time[key] = d.Time.GetValueOrDefault(key) + (end - s0); }
                    if (e.T >= from)
                    {
                        if (e.A == Act.Cross) d.Crossed++;
                        if (e.A is Act.Drink or Act.DrinkCarried) d.DrinkTries++;
                        if (e.A == Act.Drink && e.W is Why.Thirsty or Why.VeryThirsty && p.How == 3) d.Search++;
                        sb.Append($"{GameTime.Format(e.T)} {e.A}/{e.W}/h{e.How} 칸{e.Cell} t{e.Tile} 물{e.Water / 1000}; ");
                    }
                }
                d.Trace = sb.ToString();
            }
            lock (deaths) deaths.Add(d);
        };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Parallel.For(0, seeds, new ParallelOptions { MaxDegreeOfParallelism = parallel }, k =>
        {
            ulong seed = first + (ulong)k;
            var w = GameWorld.Create(seed, new WorldConfig { DotCount = 0, PeopleCap = noCap ? 5000 : cap });
            lock (seedOf) seedOf[w.People] = seed;
            var p = w.Planet;
            int cell = where == "home" ? PeopleSamples.Home(p) : where == "cool" ? PeopleSamples.Cool(p) : int.Parse(where);
            foreach (var (x, y) in PeopleSamples.SpawnSpots(w.People.Life(cell), Math.Max(1, people / 3)))
                w.Apply(new Intervention(0, Intervention.Kinds.SpawnFamily, new long[] { cell, x, y }));
            w.AdvanceTo(years * GameTime.TicksPerYear);
            all[k] = w.People.Stats.Deaths.Sum();
            Console.WriteLine($"시드 {seed}: 죽음 {all[k]} · 목마름 {w.People.Stats.Deaths[(int)DeathCause.Thirst]}");
        });
        PeopleSim.ActHook = null; PeopleSim.DeathHook = null;

        var o = new StringBuilder();
        o.AppendLine($"# 목마름 죽음 조사 — 시드 {seeds} · {where} · 가족 {people / 3} · {years}년 · 상한 {(noCap ? "없음" : cap.ToString())} · {sw.Elapsed.TotalMinutes:0.0}분");
        o.AppendLine();
        int n0 = deaths.Count;
        o.AppendLine($"- 목말라 죽은 사람 {n0} / 모든 죽음 {all.Sum()} ({n0 * 100.0 / Math.Max(1, all.Sum()):0.0}%)");
        string Pc(int c) => $"{c} ({c * 100.0 / Math.Max(1, n0):0}%)";
        o.AppendLine($"- 누가: 아이 {Pc(deaths.Count(d => d.Stage == "Child"))} · 어른 {Pc(deaths.Count(d => d.Stage == "Adult"))} · 늙은이 {Pc(deaths.Count(d => d.Stage == "Elder"))} · 다친 사람 {Pc(deaths.Count(d => d.Injured))}");
        o.AppendLine($"- 땅: {string.Join(" · ", deaths.GroupBy(d => d.Land).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}"))}");
        o.AppendLine($"- 선 자리가 걸을 수 없는 타일(갇힘): {Pc(deaths.Count(d => d.Walkable == 0))} · 이어진 땅이 작음(300타일 아래): {Pc(deaths.Count(d => d.Walkable == 1 && d.RegionSize < 300))}");
        o.AppendLine($"- 그 지방에 마실 물이 없음: {Pc(deaths.Count(d => d.DrinkTiles == 0))} · 물이 있지만 걸어서 닿지 않음: {Pc(deaths.Count(d => d.DrinkTiles > 0 && !d.Reach))}");
        var near = deaths.Where(d => d.NearDrink >= 0).Select(d => d.NearDrink).OrderBy(v => v).ToList();
        o.AppendLine($"- 죽은 자리에서 가장 가까운 물(같은 지방, 타일): ≤14(보이는 거리) {Pc(near.Count(v => v <= 14))} · 15~40 {Pc(near.Count(v => v is > 14 and <= 40))} · 41~80 {Pc(near.Count(v => v is > 40 and <= 80))} · 80 넘음 {Pc(near.Count(v => v > 80))} · 중앙값 {(near.Count > 0 ? near[near.Count / 2] : -1)}");
        o.AppendLine($"- 기억하는 물가: 하나도 없음 {Pc(deaths.Count(d => d.Known == 0))} · 이 지방에 있음 {Pc(deaths.Count(d => d.KnownHere > 0))} · 그중 걸어서 닿음 {Pc(deaths.Count(d => d.KnownReach > 0))}");
        o.AppendLine($"- 마지막 사흘: 마시러 나섬 중앙값 {Med(deaths.Select(d => d.DrinkTries))}번 · 지방 넘음 중앙값 {Med(deaths.Select(d => d.Crossed))}번 · 그릇에 물 {Pc(deaths.Count(d => d.Pot > 0))}");
        var tot = new Dictionary<string, long>();
        foreach (var d in deaths) foreach (var (k, v) in d.Time) tot[k] = tot.GetValueOrDefault(k) + v;
        long ts = Math.Max(1, tot.Values.Sum());
        o.AppendLine($"- 마지막 사흘에 무엇을 하며 보냈나(시간 몫): {string.Join(" · ", tot.OrderByDescending(kv => kv.Value).Take(14).Select(kv => $"{kv.Key} {kv.Value * 100.0 / ts:0.0}%"))}");
        o.AppendLine();
        o.AppendLine("## 보기(앞 12명의 마지막 사흘)");
        foreach (var d in deaths.OrderBy(d => d.Seed).ThenBy(d => d.Tick).Take(12))
            o.AppendLine($"- 시드 {d.Seed} · {d.Stage} {d.Age}살 · {d.Land} · 물까지 {d.NearDrink} · 선 자리 걸음{d.Walkable} 땅{d.Region}({d.RegionSize}타일) 못찾음{d.Miss} 온곳{d.From} · 아는 물가 {d.Known}(이 지방 {d.KnownHere}, 닿음 {d.KnownReach}) · {GameTime.Format(d.Tick)}: {d.Trace}");
        Console.WriteLine(o.ToString().Split("## 보기")[0]);
        if (outPath is not null) File.WriteAllText(outPath, o.ToString());
    }

    private static double Med(IEnumerable<int> v) { var l = v.OrderBy(x => x).ToList(); return l.Count == 0 ? 0 : l.Count % 2 == 1 ? l[l.Count / 2] : (l[l.Count / 2 - 1] + l[l.Count / 2]) / 2.0; }
}
