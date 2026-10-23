using System.Diagnostics;
using System.Text;
using Wtw.Sim.People;
using Wtw.Sim.Time;
using Wtw.Sim.World;

/// <summary>
/// (E5b) 짐승 · 사냥 측정. dotnet run -c Release --project tools/bench -- people hunt …
///   [--seeds 20] [--people 9] [--years 100] [--cell home|cool|cold|칸] [--know fire|coat] [--cap 300] [--parallel N] [--threads auto] [--out 파일.md]
///   시드마다 그 땅에 가족(사람 수 ÷ 3)을 뿌리고 Y 년 → 짐승 수 · 사냥(나섬 · 잡음 · 성공률) · 다침 · 사냥/짐승에게 죽음 · 물고기 · 털옷 · 발견 · 죽음 원인 ·
///   인구 · 추운 땅으로 퍼짐 · 속도(사람 하루 · 짐승 계산 몫).
///   --know fire: 처음부터 불 피우는 법. --know coat: 불 + 고기 익히기 · 가죽 일 · 바늘 · 털옷 · 신 짓는 법을 알고 털가죽 옷 · 신을 입고 돌창을 든다.
///   --nocap: 상한 없이(측정용 — 굶주린 세계, 상한은 5000).
/// </summary>
public static class HuntBench
{
    public sealed class Row
    {
        public ulong Seed; public int Cell; public string Biome = ""; public double Temp;
        public int Alive, Ever, Peak; public long[] Deaths = new long[12];
        public long HuntsStarted, HuntsTried, HuntSuccess, HuntInjuries, HuntDeaths, BeastAttacks, BeastDeaths, FireRepelled, FishCaught, Tamed, Butchered, Injuries, Rotted, WornOut, LeftForGame, Born, Immigrant, PreyKilled, RotMeat, RotHide, RotOther, RotGround, MeatGot, Dropped, Shared, Dried, Stash;
        public long[] Kills = new long[16];
        public int[] AnimalsStart = Array.Empty<int>(), AnimalsEnd = Array.Empty<int>(), Caps = Array.Empty<int>();
        public readonly SortedDictionary<string, int> FirstYear = new();
        public List<int> PopByYear = new();
        public int WearingCoat, WearingAny, ColdCells, ColdPeople, CoatColdPeople, Inhabited;
        public double ColdestTemp = double.NaN;
        public bool SurvivedNight1; public int AliveDay3, AliveYear1;
        public double Seconds, HerdShare; public long PersonDays, HerdEvents, PersonEvents;
        public int ExtinctAt = -1;
    }

    public static string Know = "";
    public static bool NoCap, Detail;
    private static ulong Seed0;

    public static string HuntDetail(GameWorld w)
    {
        var st = w.People.Stats; var sb = new StringBuilder();
        sb.AppendLine($"(E5b) 썩음: 날고기 {st.RotMeat} · 날가죽 {st.RotHide} · 그 밖 {st.RotOther} · 땅의 짐승 것 {st.RotGround} · 손질로 얻은 고기 {st.MeatGot} · 못 들어 둔 것 {st.ButcherDropped}");
        sb.AppendLine($"(E5b) 배탈 {st.BadStomachs} · 익힌 끼니 {st.CookedMeals} · 썩음 {st.Rotted} · 다침 {st.Injuries} · 무리 판단 {st.HerdEvents} · 달아남 {st.HerdFlees} · 쫓음 {st.Stalks}");
        var acts = new SortedDictionary<string, int>();
        foreach (var q in w.People.All) if (!q.Dead) foreach (var (it, n) in q.Carry) acts[w.People.Craft.ItemName(it)] = acts.GetValueOrDefault(w.People.Craft.ItemName(it)) + n;
        sb.AppendLine("  들고 있는 것: " + string.Join(", ", acts.OrderByDescending(kv => kv.Value).Take(25).Select(kv => $"{kv.Key} {kv.Value}")));
        return sb.ToString();
    }

    public static Row Simulate(ulong seed, int people, int years, string where, int cap)
    {
        var w = GameWorld.Create(seed, new WorldConfig { DotCount = 0, PeopleCap = NoCap ? 5000 : cap });
        var p = w.Planet;
        int cell = where == "home" ? PeopleSamples.Home(p) : where == "cold" ? PeopleSamples.Cold(p) : where == "cool" ? PeopleSamples.Cool(p) : int.Parse(where);
        var row = new Row { Seed = seed, Cell = cell, Biome = Wtw.Sim.Planet.PlanetNames.Of(p.Biome[cell]), Temp = p.Temp10[cell] / 10.0 };
        var life = w.People.Life(cell);
        row.AnimalsStart = w.People.AnimalCounts(life); row.Caps = (int[])life.AnimalCap.Clone();
        foreach (var (x, y) in PeopleSamples.SpawnSpots(life, Math.Max(1, people / 3)))
            w.Apply(new Intervention(0, Intervention.Kinds.SpawnFamily, new long[] { cell, x, y }));
        if (Know is "fire" or "coat") { w.AdvanceTo(1); Teach(w, Know); }
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
                row.PersonDays += alive; row.Peak = Math.Max(row.Peak, alive);
                if (day == 1) row.SurvivedNight1 = alive > 0;
                if (day == 3) row.AliveDay3 = alive;
                if (day == GameTime.DaysPerYear) row.AliveYear1 = alive;
                if (day % GameTime.DaysPerYear == 0) row.PopByYear.Add(alive);
                if (Detail && seed == Seed0 && day % 10 == 0)
                {
                    var al = w.People.All.Where(q => !q.Dead).ToList();
                    var cr = w.People.Craft;
                    string hides = string.Join(",", new[] { "raw_hide", "scraped_hide", "dried_hide", "tanned_hide", "sinew", "bone_needle", "fur_coat" }.Select(id => $"{id}:{al.Sum(q => q.CarryCount(cr.ItemIndex(id)))}"));
                    Console.WriteLine($"  {day}일 {al.Count}명 · 입을것 바람 {al.Sum(q => q.Wish[w.People.Craft.Wishes.First(x => x.Id == "clothing").Index])} · 옷 닳음 평균 {(al.Count > 0 ? al.Average(q => q.Worn >= 0 ? q.WornWear : 0) : 0):0} · 옷 입은 {al.Count(q => q.Worn >= 0)} · {hides} · 불 {w.People.Provinces.Values.Sum(l => l.Fires.Count(f => f.Value.Lit(w.Now)))}");
                }
                if (alive == 0) { row.ExtinctAt = (int)(day / GameTime.DaysPerYear); break; }
            }
        }
        finally { SpeedBench.WorldDone(); }
        row.Seconds = sw.Elapsed.TotalSeconds;
        Fill(row, w, cell);
        if (Detail && seed == Seed0) Console.WriteLine(DiscoverBench.Summary(w) + HuntDetail(w));
        return row;
    }

    /// <summary>뿌린 사람들에게 처음부터 아는 것(과 입은 것 · 든 것).</summary>
    public static void Teach(GameWorld w, string know)
    {
        var ps = w.People; var cr = ps.Craft;
        int I(string id) => cr.ItemIndex(id);
        var rules = new List<string> { "sparks", "campfire" };
        if (know == "coat") rules.AddRange(new[] { "cooked_meat", "scrape_hide", "dry_hide", "tan_hide", "soften_hide", "bone_needle", "fur_coat", "fur_boots", "stone_spear", "flake" });
        foreach (var q in ps.All)
        {
            foreach (var id in rules) { var r = cr.Rules.First(x => x.Id == id); ps.Learn(q, r, 0, -1, -1, -1); }
            if (know == "coat")
            {
                // (T1) 지어 입은 때가 사람마다 다르다 — 모두 새 옷(1000)이면 같은 날 한꺼번에 해져 같은 밤에 함께 얼어 죽었다(4년 7일 · 12년 21일 떼죽음). 남은 몫 40~100%.
                q.Worn = I("fur_coat"); q.WornWear = 400 + (int)(Wtw.Sim.Core.StableHash.Combine(w.Seed, (ulong)q.Id, 1) % 601);
                q.WornFeet = I("fur_boots"); q.FeetWear = 400 + (int)(Wtw.Sim.Core.StableHash.Combine(w.Seed, (ulong)q.Id, 2) % 601);
                if (q.Stage(w.Now) >= LifeStage.Adult) { q.AddCarry(I("stone_spear"), 1); q.AddCarry(I("flake"), 1); q.AddCarry(I("bone_needle"), 1); q.AddCarry(I("flint"), 2); q.AddCarry(I("dry_grass"), 2); }
            }
        }
    }

    public static void Fill(Row row, GameWorld w, int cell)
    {
        var ps = w.People; var st = ps.Stats;
        row.Alive = ps.AliveCount; row.Ever = ps.All.Count;
        Array.Copy(st.Deaths, row.Deaths, 12);
        row.HuntsStarted = st.HuntsStarted; row.HuntsTried = st.HuntsTried; row.HuntSuccess = st.HuntSuccess; row.HuntInjuries = st.HuntInjuries; row.HuntDeaths = st.Deaths[(int)DeathCause.HuntAccident];
        row.BeastAttacks = st.BeastAttacks; row.BeastDeaths = st.Deaths[(int)DeathCause.Beast]; row.FireRepelled = st.FireRepelled; row.FishCaught = st.FishCaught; row.Tamed = st.Tamed; row.Butchered = st.Butchered;
        row.Injuries = st.Injuries; row.Rotted = st.Rotted; row.WornOut = st.ClothesWornOut; row.LeftForGame = st.LeftForGame; row.Born = st.AnimalsBorn; row.Immigrant = st.ImmigrantHerds; row.PreyKilled = st.PreyKilled; row.RotMeat = st.RotMeat; row.RotHide = st.RotHide; row.RotOther = st.RotOther; row.RotGround = st.RotGround; row.MeatGot = st.MeatGot; row.Dropped = st.ButcherDropped; row.Shared = st.MeatShared; row.Dried = st.MeatDried; row.Stash = st.StashEaten;
        Array.Copy(st.KillsBySpecies, row.Kills, 16);
        row.HerdEvents = st.HerdEvents; row.PersonEvents = st.Events;
        if (ps.Provinces.TryGetValue(cell, out var life)) row.AnimalsEnd = ps.AnimalCounts(life);
        else if (ps.Dormant.TryGetValue(cell, out var d)) { row.AnimalsEnd = new int[ps.Fauna.Animals.Count]; foreach (var h in d.Herds) if (!h.Tame) row.AnimalsEnd[h.Species] += h.Count; }
        foreach (var (rule, dd) in ps.Discoveries)
        {
            string item = ps.Craft.Catalog.Items[ps.Craft.Rules[rule].Result].Id;
            int year = (int)(dd.Tick / GameTime.TicksPerYear);
            if (!row.FirstYear.TryGetValue(item, out int y0) || year < y0) row.FirstYear[item] = year;
        }
        int coat = ps.Craft.ItemIndex("fur_coat");
        var cells = new SortedSet<int>(); var cold = new SortedSet<int>();
        foreach (var q in ps.All)
        {
            if (q.Dead) continue;
            cells.Add(q.Cell);
            if (q.Worn == coat) row.WearingCoat++;
            if (q.Worn >= 0) row.WearingAny++;
            bool isCold = w.Planet.Temp10[q.Cell] < 0;
            if (isCold) { row.ColdPeople++; cold.Add(q.Cell); if (q.Worn == coat) row.CoatColdPeople++; }
            double t = w.Planet.Temp10[q.Cell] / 10.0;
            if (double.IsNaN(row.ColdestTemp) || t < row.ColdestTemp) row.ColdestTemp = t;
        }
        row.Inhabited = cells.Count; row.ColdCells = cold.Count;
    }

    public static void Run(int seeds, int people, int years, ulong seed0, int parallel, string? outFile, string where, int cap)
    {
        var rows = new Row[seeds];
        Seed0 = seed0;
        var sw = Stopwatch.StartNew();
        Parallel.For(0, seeds, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, parallel) }, i =>
        {
            rows[i] = Simulate(seed0 + (ulong)i, people, years, where, cap);
            var r = rows[i];
            Console.WriteLine($"시드 {r.Seed}: {r.Biome}({r.Temp:0.0}°C) 산 사람 {r.Alive}/{r.Ever} · 사냥 {r.HuntSuccess}/{r.HuntsTried} · 덮침 {r.BeastAttacks} · 털옷 {r.WearingCoat} · {r.Seconds:0}초");
        });
        var a = Wtw.Sim.Data.AnimalData.Default;
        var sb = new StringBuilder();
        sb.AppendLine($"# 짐승 · 사냥 측정 — 시드 {seeds}개 · {where} · 가족 {Math.Max(1, people / 3)} · {years}년 · 아는 것 '{(Know.Length == 0 ? "없음" : Know)}' · 상한 {(NoCap ? "없음(5000)" : cap.ToString())} · 걸린 시간 {sw.Elapsed.TotalMinutes:0.0}분");
        sb.AppendLine();
        sb.AppendLine("| 시드 | 칸(땅, 연평균) | 끝 인구 | 가장 많을 때 | 멸종(년) | 첫날 밤 | 3일 | 1년 | 사냥 나섬 | 몰아 봄 | 잡음 | 사냥 다침 | 사냥 사고 죽음 | 덮침 | 짐승에게 죽음 | 불에 물러남 | 물고기 | 길들임 | 털옷 입은 사람 | 추운 땅 사람(털옷) | 산 칸 | 초 |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var r in rows)
            sb.AppendLine($"| {r.Seed} | {r.Cell}({r.Biome}, {r.Temp:0.0}°C) | {r.Alive} | {r.Peak} | {(r.ExtinctAt >= 0 ? r.ExtinctAt.ToString() : "-")} | {(r.SurvivedNight1 ? "O" : "X")} | {r.AliveDay3} | {r.AliveYear1} | {r.HuntsStarted} | {r.HuntsTried} | {r.HuntSuccess} | {r.HuntInjuries} | {r.HuntDeaths} | {r.BeastAttacks} | {r.BeastDeaths} | {r.FireRepelled} | {r.FishCaught} | {r.Tamed} | {r.WearingCoat} | {r.ColdPeople}({r.CoatColdPeople}) | {r.Inhabited} | {r.Seconds:0} |");
        sb.AppendLine();
        double Med(IEnumerable<double> xs) { var l = xs.OrderBy(x => x).ToList(); return l.Count == 0 ? double.NaN : l.Count % 2 == 1 ? l[l.Count / 2] : (l[l.Count / 2 - 1] + l[l.Count / 2]) / 2; }
        sb.AppendLine("## 중앙값");
        sb.AppendLine($"- 끝 인구 {Med(rows.Select(r => (double)r.Alive))} · 가장 많을 때 {Med(rows.Select(r => (double)r.Peak))} · 멸종 {rows.Count(r => r.ExtinctAt >= 0)}/{seeds} · 첫날 밤을 넘김 {rows.Count(r => r.SurvivedNight1 && r.AliveDay3 > 0)}/{seeds} · 1년 뒤 살아 있는 시드 {rows.Count(r => r.AliveYear1 > 0)}/{seeds}");
        long tried = rows.Sum(r => r.HuntsTried), succ = rows.Sum(r => r.HuntSuccess);
        sb.AppendLine($"- 사냥: 나섬 {Med(rows.Select(r => (double)r.HuntsStarted))} · 몰아 봄 {Med(rows.Select(r => (double)r.HuntsTried))} · 잡음 {Med(rows.Select(r => (double)r.HuntSuccess))} (모두 합 성공률 {succ * 100.0 / Math.Max(1, tried):0}%) · 사냥 다침 {Med(rows.Select(r => (double)r.HuntInjuries))} · 사냥 사고 죽음 {Med(rows.Select(r => (double)r.HuntDeaths))}");
        sb.AppendLine($"- 포식자: 사람을 덮침 {Med(rows.Select(r => (double)r.BeastAttacks))} · 짐승에게 죽음 {Med(rows.Select(r => (double)r.BeastDeaths))} · 불을 보고 물러남 {Med(rows.Select(r => (double)r.FireRepelled))} · 포식자가 잡은 짐승 {Med(rows.Select(r => (double)r.PreyKilled))}");
        sb.AppendLine($"- 물고기 {Med(rows.Select(r => (double)r.FishCaught))} · 길들임 {Med(rows.Select(r => (double)r.Tamed))} · 손질 {Med(rows.Select(r => (double)r.Butchered))} · 썩어 버린 것 {Med(rows.Select(r => (double)r.Rotted))} · 해진 옷 {Med(rows.Select(r => (double)r.WornOut))}");
        sb.AppendLine($"- 썩음(중앙값): 들고 있던 날고기 · 날생선 {Med(rows.Select(r => (double)r.RotMeat))} · 날가죽 {Med(rows.Select(r => (double)r.RotHide))} · 그 밖(열매 등) {Med(rows.Select(r => (double)r.RotOther))} · 땅에 둔 짐승 것 {Med(rows.Select(r => (double)r.RotGround))} · 손질로 얻은 고기 {Med(rows.Select(r => (double)r.MeatGot))} · 그 자리에 둔 것 {Med(rows.Select(r => (double)r.Dropped))} · 나눠 준 고기 {Med(rows.Select(r => (double)r.Shared))} · 말려 둔 고기 {Med(rows.Select(r => (double)r.Dried))} · 둔 먹을 것 가지러 {Med(rows.Select(r => (double)r.Stash))}");
        sb.AppendLine($"- (이어서) 짐승이 줄어 떠남 {Med(rows.Select(r => (double)r.LeftForGame))}");
        sb.AppendLine($"- 끝에 털옷 입은 사람 {Med(rows.Select(r => (double)r.WearingCoat))} · 무엇이든 입은 사람 {Med(rows.Select(r => (double)r.WearingAny))} · 사람 사는 칸 {Med(rows.Select(r => (double)r.Inhabited))} · 연평균 0°C 아래 칸에 사는 사람 {Med(rows.Select(r => (double)r.ColdPeople))}(그중 털옷 {Med(rows.Select(r => (double)r.CoatColdPeople))}) · 추운 칸에 사람이 사는 시드 {rows.Count(r => r.ColdPeople > 0)}/{seeds} · 가장 추운 산 칸 연평균 {Med(rows.Where(r => !double.IsNaN(r.ColdestTemp)).Select(r => r.ColdestTemp)):0.0}°C");
        long deaths = rows.Sum(r => r.Deaths.Sum());
        var names = new[] { "-", "굶주림", "목마름", "추위", "더위", "늙음", "병", "짐승", "사냥 사고" };
        sb.AppendLine($"- 죽음 원인(모든 시드 합 {deaths}): " + string.Join(" · ", Enumerable.Range(1, 8).Select(c => $"{names[c]} {rows.Sum(r => r.Deaths[c]) * 100.0 / Math.Max(1, deaths):0.#}% ({rows.Sum(r => r.Deaths[c])})")));
        sb.AppendLine($"- 인구(10년마다 중앙값): " + string.Join(" · ", Enumerable.Range(0, years / 10 + 1).Select(k => Med(rows.Select(r => k * 10 < r.PopByYear.Count ? (double)r.PopByYear[k * 10] : 0)).ToString("0"))));
        sb.AppendLine($"- 잡은 짐승(합): " + string.Join(" · ", a.Animals.Select(x => $"{x.Name} {rows.Sum(r => r.Kills[x.Index])}")));
        sb.AppendLine($"- 뿌린 지방의 짐승(중앙값, 처음 → 끝 / 살 수 있는 수): " + string.Join(" · ", a.Animals.Where(x => rows.Any(r => r.Caps.Length > x.Index && r.Caps[x.Index] > 0)).Select(x => $"{x.Name} {Med(rows.Select(r => (double)r.AnimalsStart[x.Index]))}→{Med(rows.Select(r => r.AnimalsEnd.Length > x.Index ? (double)r.AnimalsEnd[x.Index] : 0))}/{Med(rows.Select(r => (double)r.Caps[x.Index]))}")));
        sb.AppendLine($"- 짐승: 태어남 {Med(rows.Select(r => (double)r.Born))} · 이웃에서 들어온 무리 {Med(rows.Select(r => (double)r.Immigrant))}");
        foreach (var item in new[] { "campfire", "spear", "flake", "stone_spear", "cooked_meat", "cooked_fish", "dried_meat", "smoked_meat", "scraped_hide", "dried_hide", "tanned_hide", "bone_needle", "fur_boots", "fur_coat", "pot" })
        {
            var ys = rows.Where(r => r.FirstYear.ContainsKey(item)).Select(r => (double)r.FirstYear[item]).ToList();
            sb.AppendLine($"  - 발견 {Wtw.Sim.Data.CraftData.Default.ItemName(Wtw.Sim.Data.CraftData.Default.ItemIndex(item))}: {ys.Count}/{seeds} 시드" + (ys.Count > 0 ? $" · 처음 나온 해 중앙값 {Med(ys)}" : ""));
        }
        long pd = rows.Sum(r => r.PersonDays); double sec = rows.Sum(r => r.Seconds);
        long he = rows.Sum(r => r.HerdEvents), pe = rows.Sum(r => r.PersonEvents);
        sb.AppendLine($"- 속도: 사람 하루당 {sec * 1e6 / Math.Max(1, pd):0}µs (시드 {Math.Max(1, parallel)}개씩 함께) · 예약 처리: 사람 {pe:N0} · 짐승 무리 {he:N0}({he * 100.0 / Math.Max(1, he + pe):0.0}%)");
        Console.WriteLine(sb.ToString());
        if (outFile is not null) { File.WriteAllText(outFile, sb.ToString()); Console.WriteLine($"→ {outFile}"); }
    }
}

/// <summary>(E5b 개발용) 툰드라 털옷 가족: 날마다 입을 것 바람이 있는 사람의 계획을 들여다본다.</summary>
public static class HuntDebug
{
    public static void Run(ulong seed, int days)
    {
        var w = GameWorld.Create(seed, new WorldConfig { DotCount = 0 });
        int cell = PeopleSamples.Cold(w.Planet);
        foreach (var (x, y) in PeopleSamples.SpawnSpots(w.People.Life(cell), 3)) w.Apply(new Intervention(0, Intervention.Kinds.SpawnFamily, new long[] { cell, x, y }));
        w.AdvanceTo(1); HuntBench.Teach(w, "coat");
        var ps = w.People; var cr = ps.Craft;
        int wc = cr.Wishes.First(x => x.Id == "clothing").Index;
        var acts = new SortedDictionary<string, int>();
        for (int d = 1; d <= days; d++)
        {
            for (int h = 0; h < 24; h++)
            {
                w.AdvanceTo(w.Now + GameTime.TicksPerHour);
                foreach (var q in ps.All) if (!q.Dead && q.Wish[wc] >= 20) { string k = $"{q.Act}/{q.Why}"; acts[k] = acts.GetValueOrDefault(k) + 1; }
            }
            if (d % 20 == 0)
            {
                Console.WriteLine($"{d}일: 입을 것 바람이 있는 사람의 일(시간): " + string.Join(", ", acts.OrderByDescending(kv => kv.Value).Take(14).Select(kv => $"{kv.Key} {kv.Value}")));
                acts.Clear();
                foreach (var q in ps.All.Where(q => !q.Dead && q.Wish[wc] >= 20).Take(3))
                    Console.WriteLine($"   {ps.DisplayName(q)} 바람 " + string.Join(",", cr.Wishes.Where(x => q.Wish[x.Index] > 0).Select(x => $"{x.Name}{q.Wish[x.Index]}")) + " · 든 것 " + string.Join(",", q.Carry.Select(c => $"{cr.ItemName(c.Item)}×{c.Count}")) + $" · 입은 {cr.ItemName(q.Worn)} {q.WornWear} · 실패 {(w.Now - q.LastCraftFail) / 2500}시간 전");
            }
        }
    }
}
