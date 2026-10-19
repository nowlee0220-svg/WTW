using System.Diagnostics;
using System.Text;
using Wtw.Sim.People;
using Wtw.Sim.Time;
using Wtw.Sim.World;

/// <summary>
/// (E4) 발견 측정: 시드마다 따뜻한 땅에 N 명을 뿌리고 Y 년 → 발견마다 처음 나온 해(중앙값) · 나오지 않은 시드 수 · 사라졌다 다시 나온 수,
/// 실험 · 부분 성공 횟수, 불 피운 횟수, 죽음 원인(굶주린 세계), 인구 곡선. --cell cold 면 툰드라(추운 땅의 생존).
/// </summary>
public static class DiscoverBench
{
    public sealed class Row
    {
        public ulong Seed; public int Cell; public string Biome = "";
        public int Alive, Ever, Peak; public long[] Deaths = new long[8];
        public readonly SortedDictionary<string, int> FirstYear = new(); // 결과 아이템 → 처음 나온 해
        public int PartialRepeats, CraftFails, Lost, Refound, Experiments, Discoveries, Partials, Crafts, FiresLit, FiresTaken, Mined, Cooked, Watched, Wildfires;
        public List<int> PopByYear = new();
        public double Seconds; public long PersonDays;
        public int Known1y;
        public string Detail = "";
        public int Popups1, Popups5, Level3, Level2;
        // (E5) 지식
        public long LostByForget, Lessons, TaughtFull, WatchedFull, Forgot, Refreshed, Paintings, Studies, FromPainting, GroupLost, GroupRefound, Known, Sole, ClueShared, ClueAvoid, FadedFails, Und; public long[] How = new long[4]; public List<long> Delays = new(); // 끝에 살아 있는 사람 중 아는 법이 하나라도 있는 사람
    }

    /// <summary>know = "fire" 이면 뿌린 사람들이 처음부터 불 피우는 법(부싯돌 불씨 · 모닥불)을 안다(추운 땅에서 "불을 아는 사람"의 생존을 따로 재기 위해).</summary>
    public static string Know = "";
    /// <summary>(S1) 끝난 세계를 저장할 곳(시드 하나일 때 — 속도 측정용 장면 만들기).</summary>
    public static string? SaveTo;

    public static Row Simulate(ulong seed, int people, int years, string where, int cap)
    {
        var w = GameWorld.Create(seed, new WorldConfig { DotCount = 0, PeopleCap = cap });
        var p = w.Planet;
        int cell = where == "home" ? PeopleSamples.Home(p) : where == "cold" ? PeopleSamples.Cold(p) : where == "cool" ? PeopleSamples.Cool(p) : int.Parse(where);
        var row = new Row { Seed = seed, Cell = cell, Biome = Wtw.Sim.Planet.PlanetNames.Of(p.Biome[cell]) };
        var life = w.People.Life(cell);
        foreach (var (x, y) in PeopleSamples.SpawnSpots(life, Math.Max(1, people / 3)))
            w.Apply(new Intervention(0, Intervention.Kinds.SpawnFamily, new long[] { cell, x, y }));
        if (Know == "fire")
        {
            w.AdvanceTo(1);
            var cr = w.People.Craft;
            int I(string id) => cr.ItemIndex(id);
            foreach (var q in w.People.All)
            {
                w.People.Learn(q, cr.Rules.First(r => r.Id == "sparks"), 0, I("flint"), I("flint"), -1);
                w.People.Learn(q, cr.Rules.First(r => r.Id == "campfire"), 0, I("twigs"), I("ember"), -1);
            }
        }
        var gate1 = new Wtw.Sim.Runtime.PopupGate(); var gate5 = new Wtw.Sim.Runtime.PopupGate();
        long seen = 0;
        var sw = Stopwatch.StartNew();
        long end = years * GameTime.TicksPerYear;
        for (long t = 0; t < end; t += GameTime.TicksPerDay)
        {
            w.AdvanceTo(Math.Min(end, t + GameTime.TicksPerDay));
            int alive = w.People.AliveCount;
            row.PersonDays += alive; row.Peak = Math.Max(row.Peak, alive);
            // (E4) 큰 일 팝업: 하루에 생긴 ★★★ 를 한 팝업으로 묶는다(1배속 · 5배속 규칙으로 각각)
            bool p1 = false, p5 = false;
            foreach (var e in w.People.Events)
            {
                if (e.Seq <= seen) continue;
                int lv = Wtw.Sim.Runtime.EventLevels.Of(e.Kind);
                if (lv == 3) row.Level3++; else if (lv == 2) row.Level2++;
                p1 |= gate1.ShouldPause(e, w.Now, -1, 1);
                p5 |= gate5.ShouldPause(e, w.Now, -1, 5);
            }
            if (w.People.Events.Count > 0) seen = w.People.Events[^1].Seq;
            if (p1) row.Popups1++;
            if (p5) row.Popups5++;
            if (t % GameTime.TicksPerYear == 0) row.PopByYear.Add(alive);
            if (alive == 0) break;
        }
        row.Seconds = sw.Elapsed.TotalSeconds;
        Fill(row, w);
        row.Detail = Summary(w);
        if (SaveTo is not null) Wtw.Sim.Persistence.SaveGame.SaveFile(w, SaveTo);
        return row;
    }

    public static void Fill(Row row, GameWorld w)
    {
        var ps = w.People;
        row.Alive = ps.AliveCount; row.Ever = ps.All.Count;
        Array.Copy(ps.Stats.Deaths, row.Deaths, 8);
        foreach (var (rule, d) in ps.Discoveries)
        {
            string item = ps.Craft.Catalog.Items[ps.Craft.Rules[rule].Result].Id;
            int year = (int)(d.Tick / GameTime.TicksPerYear);
            if (!row.FirstYear.TryGetValue(item, out int y0) || year < y0) row.FirstYear[item] = year;
            row.Lost += d.LostCount; row.Refound += d.RefoundCount;
        }
        var st = ps.Stats;
        row.Experiments = (int)st.Experiments; row.Discoveries = (int)st.Discoveries; row.Partials = (int)st.Partials; row.Crafts = (int)st.Crafts; row.CraftFails = (int)st.CraftFails; row.PartialRepeats = (int)st.PartialRepeats;
        row.FiresLit = (int)st.FiresLit; row.FiresTaken = (int)st.FiresTaken; row.Mined = (int)st.Mined; row.Cooked = (int)st.CookedMeals; row.Watched = (int)st.Watched; row.Wildfires = (int)st.Wildfires;
        row.Known1y = ps.All.Count(q => !q.Dead && q.Known.Count > 0);
        row.LostByForget = st.LostByForget; row.Lessons = st.Lessons; row.TaughtFull = st.TaughtFull; row.WatchedFull = st.WatchedFull; row.Forgot = st.Forgot; row.Refreshed = st.Refreshed; row.Paintings = st.Paintings;
        row.Studies = st.Studies; row.FromPainting = st.LearnedFromPainting; row.GroupLost = st.GroupLost; row.GroupRefound = st.GroupRefound; row.Known = st.GroupKnownSamples; row.Sole = st.GroupSoleSamples;
        row.ClueShared = st.CluesShared; row.ClueAvoid = st.ClueAvoid; row.FadedFails = st.FadedFails; row.Und = st.LessonUnderstand; Array.Copy(st.RefoundHow, row.How, 4); row.Delays.AddRange(st.RefoundDelays);
    }

    public static string Summary(GameWorld w)
    {
        var ps = w.People;
        var sb = new StringBuilder();
        var st = ps.Stats;
        sb.AppendLine($"(E5) 그림 까닭: 그 밖 {st.PaintWhy[0]} · 늙은 장인 {st.PaintWhy[1]} · 혼자 앎 {st.PaintWhy[2]} · 처음 알아냄 {st.PaintWhy[3]} · 그리기를 처음 알아낸 실험 {st.PaintWhy[4]} — 매체: 나무껍질 {st.PaintMedium[0]} · 바위 면 {st.PaintMedium[1]} · 동굴 벽 {st.PaintMedium[2]}");
        sb.AppendLine($"(E4) 실험 {st.Experiments} · 알아냄 {st.Discoveries} · 거의 될 듯 {st.Partials} · 아는 법대로 만듦 {st.Crafts} · 불 피움 {st.FiresLit} · 불씨 얻음 {st.FiresTaken} · 캠 {st.Mined} · 익힌 끼니 {st.CookedMeals} · 배탈 {st.BadStomachs} · 보고 배움 {st.Watched} · 사라짐 {st.Lost} · 되찾음 {st.Refound} · 들불 {st.Wildfires}");
        foreach (var (rule, d) in ps.Discoveries)
            sb.AppendLine($"  발견 {ps.Craft.ItemName(ps.Craft.Rules[rule].Result)}: {GameTime.Format(d.Tick)} · {ps.DisplayName(ps.Get(d.Person))} · {ps.WishName(d.Wish)} · 그 전 부분 성공 {d.PartialsBefore} · 아는 사람 {ps.Knowers(rule)}{(d.Lost ? " · 사라짐" : "")}");
        foreach (var (rule, pr) in ps.Partials) sb.AppendLine($"  단서만: {ps.Craft.Rules[rule].Name} ×{pr.Count}: {string.Join(" / ", pr.Texts)}");
        var wishes = new int[ps.Craft.Wishes.Count];
        foreach (var q in ps.All) if (!q.Dead) for (int i = 0; i < wishes.Length; i++) wishes[i] += q.Wish[i];
        sb.AppendLine("  바람(살아 있는 사람 합): " + string.Join(" · ", ps.Craft.Wishes.Select(x => $"{x.Name} {wishes[x.Index]}")));
        int fires = 0; foreach (var (_, l) in ps.Provinces) fires += l.Fires.Count(f => f.Value.Lit(w.Now));
        int hearths = 0, hearthLit = 0; foreach (var (_, l) in ps.Provinces) foreach (var f in l.Fires.Values) if (f.Kind == 2) { hearths++; if (f.Lit(w.Now)) hearthLit++; }
        int cc = ps.All.Where(q => !q.Dead).SelectMany(q => q.Clues).Count(c => ps.Craft.Rules[c.Rule].Id == "charcoal"), cu = ps.All.Where(q => !q.Dead).SelectMany(q => q.Clues).Count(c => ps.Craft.Rules[c.Rule].Id == "copper");
        sb.AppendLine($"  지금 타는 불 {fires}개 · 돌 화덕 {hearths}개(타는 것 {hearthLit}) · 숯 단서 가진 사람 {cc} · 구리 단서 {cu}");
        int dup = ps.All.Where(q => !q.Dead).Sum(q => q.Clues.Count - q.Clues.Select(c => (c.Rule, c.Key)).Distinct().Count());
        sb.AppendLine($"  같은 단서 겹침 {dup} · 단서 키: " + string.Join(", ", ps.All.Where(q => !q.Dead).SelectMany(q => q.Clues).GroupBy(c => ps.Craft.Rules[c.Rule].Id + "/" + c.Key).OrderByDescending(g => g.Count()).Take(6).Select(g => g.Key + " " + g.Count())));
        sb.AppendLine("  계획: " + string.Join(" · ", st.PlanWhy.Select((v, i) => $"{i}:{v}")));
        sb.AppendLine("  아는 법 헛손질(규칙/까닭): " + string.Join(" · ", st.FailKeys.OrderByDescending(x => x.Value).Take(10).Select(x => $"{x.Key} {x.Value}")));
        sb.AppendLine("  규칙별 성공: " + string.Join(" · ", ps.Craft.Rules.Where(r => st.RuleDone[r.Index] > 0).Select(r => $"{r.Id} {st.RuleDone[r.Index]}")));
        return sb.ToString();
    }

    public static void Run(int seeds, int people, int years, ulong first, int parallel, string? outPath, string where, int cap)
    {
        var rows = new Row[seeds];
        var sw = Stopwatch.StartNew();
        Parallel.For(0, seeds, new ParallelOptions { MaxDegreeOfParallelism = parallel }, k => rows[k] = Simulate(first + (ulong)k, people, years, where, cap));
        var sb = new StringBuilder();
        sb.AppendLine($"## (E4) 발견 측정: 시드 {seeds}개 × {(where == "cold" ? "추운 땅(툰드라)" : where == "cool" ? "쌀쌀한 땅" : "따뜻한 땅")} {people}명(가족){(Know == "fire" ? " — 처음부터 불 피우는 법을 앎" : "")} × {years}년 (걸린 시간 {sw.Elapsed.TotalSeconds:0}초, 코어 {parallel})");
        sb.AppendLine();
        string[] items = { "campfire", "flake", "chopper", "cord", "basket", "dry_grass", "cooked_fruit", "roasted_mushroom", "wet_pot", "dry_pot", "pot", "hearth", "charcoal", "copper_lump", "copper_blade", "hafted_axe", "ground_axe", "stone_knife", "spear", "stick", "dried_fruit", "leaf_cup", "cracked_coconut", "stew", "scraper", "split_wood" };
        var cat = Wtw.Sim.Data.ObjectCatalog.Default;
        string Name(string id) => cat.Items.First(i => i.Id == id).Name;
        sb.AppendLine("| 시드 | 칸 | 끝 인구 | 가장 많을 때 | 굶주림 | 목마름 | 추위 | 늙음·병 | 실험 | 거의 됨 | 알아냄 | 불 피움 | 캠 | 익힌 끼니 | 사라짐/되찾음 | " + string.Join(" | ", items.Take(14).Select(Name)) + " | 초 |");
        sb.AppendLine("|" + string.Concat(Enumerable.Repeat("---|", 16 + 14)));
        foreach (var r in rows)
            sb.AppendLine($"| {r.Seed} | {r.Cell}({r.Biome}) | {r.Alive} | {r.Peak} | {r.Deaths[1]} | {r.Deaths[2]} | {r.Deaths[3]} | {r.Deaths[5] + r.Deaths[6]} | {r.Experiments} | {r.Partials} | {r.Discoveries} | {r.FiresLit} | {r.Mined} | {r.Cooked} | {r.Lost}/{r.Refound} | "
                + string.Join(" | ", items.Take(14).Select(i => r.FirstYear.TryGetValue(i, out int y) ? $"{y}년" : "—")) + $" | {r.Seconds:0} |");
        sb.AppendLine();
        sb.AppendLine("| 발견 | 처음 나온 해(중앙값) | 나온 시드 | 나오지 않은 시드 | 가장 이른 · 늦은 |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var it in items)
        {
            var ys = rows.Where(r => r.FirstYear.ContainsKey(it)).Select(r => r.FirstYear[it]).OrderBy(x => x).ToList();
            if (ys.Count == 0) { sb.AppendLine($"| {Name(it)} | — | 0 | {seeds} | — |"); continue; }
            double med = ys.Count % 2 == 1 ? ys[ys.Count / 2] : (ys[ys.Count / 2 - 1] + ys[ys.Count / 2]) / 2.0;
            sb.AppendLine($"| {Name(it)} | {med:0.#}년 | {ys.Count} | {seeds - ys.Count} | {ys[0]} · {ys[^1]}년 |");
        }
        long deaths = rows.Sum(r => r.Deaths.Sum());
        sb.AppendLine();
        sb.AppendLine($"- 멸종(끝 인구 0): {rows.Count(r => r.Alive == 0)}/{seeds} · 끝 인구 중앙값 {PeopleBench.Median(rows.Select(r => (double)r.Alive))} · 가장 많을 때 중앙값 {PeopleBench.Median(rows.Select(r => (double)r.Peak))}");
        sb.AppendLine($"- 죽음 원인(모든 시드 합 {deaths}): " + string.Join(" · ", new[] { (1, "굶주림"), (2, "목마름"), (3, "추위"), (4, "더위"), (5, "늙음"), (6, "병") }.Select(c => $"{c.Item2} {rows.Sum(r => r.Deaths[c.Item1]) * 100.0 / Math.Max(1, deaths):0}%")));
        sb.AppendLine($"- 사라진 발견 {rows.Sum(r => r.Lost)} · 다시 찾아냄 {rows.Sum(r => r.Refound)} · 실험 {rows.Sum(r => r.Experiments):N0} · 그중 거의 될 듯(새 단서) {rows.Sum(r => r.Partials):N0} · 같은 헛수고 {rows.Sum(r => r.PartialRepeats):N0} · 아는 법대로 만들기 {rows.Sum(r => r.Crafts):N0}(그중 헛손질 {rows.Sum(r => r.CraftFails):N0}) · 알아냄 {rows.Sum(r => r.Discoveries):N0} · 보고 배움 {rows.Sum(r => r.Watched):N0} · 불 피움 {rows.Sum(r => r.FiresLit):N0} · 불씨 얻음 {rows.Sum(r => r.FiresTaken):N0} · 들불 · 번개 {rows.Sum(r => r.Wildfires)} · 캠 {rows.Sum(r => r.Mined)} · 익힌 끼니 {rows.Sum(r => r.Cooked):N0}");
        sb.AppendLine($"- 인구(10년마다, 시드 중앙값): " + string.Join(" · ", Enumerable.Range(0, years / 10 + 1).Select(i => PeopleBench.Median(rows.Select(r => (double)(i * 10 < r.PopByYear.Count ? r.PopByYear[i * 10] : 0))).ToString("0"))));
        sb.AppendLine($"- (E4) 큰 일: ★★★ {PeopleBench.Median(rows.Select(r => (double)r.Level3))}번 · ★★ {PeopleBench.Median(rows.Select(r => (double)r.Level2))}번(중앙값) → 멈추는 팝업 1배속 규칙 {PeopleBench.Median(rows.Select(r => (double)r.Popups1))}번 · 5배속 규칙 {PeopleBench.Median(rows.Select(r => (double)r.Popups5))}번 ({years}년, 같은 날의 일은 팝업 하나, 실제 시간 30초 간격은 빼고 셈)");
        sb.AppendLine($"- 사람 하루당 {rows.Sum(r => r.Seconds) * 1e6 / Math.Max(1, rows.Sum(r => r.PersonDays)):0}µs");
        // (E5) 지식
        long exps = rows.Sum(r => (long)r.Experiments), reps = rows.Sum(r => (long)r.PartialRepeats);
        sb.AppendLine($"- (E5) 같은 헛수고 비율 {reps * 100.0 / Math.Max(1, exps):0.0}% (실험 {exps:N0} 중 {reps:N0}) · 단서 나눔 {rows.Sum(r => r.ClueShared):N0} · 단서를 보고 같은 실험을 피함 {rows.Sum(r => r.ClueAvoid):N0}");
        sb.AppendLine($"- (E5) 수업 {rows.Sum(r => r.Lessons):N0}번(평균 알아듣기 {rows.Sum(r => r.Und) / (double)Math.Max(1, rows.Sum(r => r.Lessons)):0}%) · 수업으로 다 배움 {rows.Sum(r => r.TaughtFull):N0} · 보고 다 배움 {rows.Sum(r => r.WatchedFull):N0} · 흐려져 되살림 {rows.Sum(r => r.Refreshed):N0} · 잊음 {rows.Sum(r => r.Forgot):N0} · 흐려진 헛손질 {rows.Sum(r => r.FadedFails):N0}");
        sb.AppendLine($"- (E5) 세계에서 사라짐 {rows.Sum(r => r.Lost)}번 가운데 마지막으로 알던 사람이 잊어서 {rows.Sum(r => r.LostByForget)}번 · 잊음 시드 중앙값 {PeopleBench.Median(rows.Select(r => (double)r.Forgot)):0}번");
        sb.AppendLine($"- (E5) 그림 {rows.Sum(r => r.Paintings)}점(그린 시드 {rows.Count(r => r.Paintings > 0)}/{seeds}) · 그림 보기 {rows.Sum(r => r.Studies):N0}번 · 그림으로 다 배움 {rows.Sum(r => r.FromPainting)}명");
        long gl = rows.Sum(r => r.GroupLost), gr = rows.Sum(r => r.GroupRefound);
        var delays = rows.SelectMany(r => r.Delays).Select(d => d / (double)GameTime.TicksPerYear).ToList();
        long hall = rows.Sum(r => r.How.Sum());
        sb.AppendLine($"- (E5) 무리에서 한 사람만 아는 법 {rows.Sum(r => r.Sole) * 100.0 / Math.Max(1, rows.Sum(r => r.Known)):0.0}% (목표 ~17%, 시드 중앙값 {PeopleBench.Median(rows.Select(r => r.Sole * 100.0 / Math.Max(1, r.Known))):0.0}%) · 무리에서 끊김 {gl} · 되찾음 {gr} ({gr * 100.0 / Math.Max(1, gl):0}%, 목표 ~58%) · 되찾기까지 중앙값 {(delays.Count > 0 ? PeopleBench.Median(delays) : 0):0.#}년(목표 3년)");
        sb.AppendLine($"- (E5) 되찾은 길: 다시 발견 {rows.Sum(r => r.How[0]) * 100.0 / Math.Max(1, hall):0}% · 다른 무리 사람에게 배움 {rows.Sum(r => r.How[1]) * 100.0 / Math.Max(1, hall):0}% · 그림 {rows.Sum(r => r.How[2]) * 100.0 / Math.Max(1, hall):0}% · 아는 사람이 옮겨 옴 {rows.Sum(r => r.How[3]) * 100.0 / Math.Max(1, hall):0}%");
        Console.Write(sb.ToString());
        if (seeds <= 2) foreach (var r in rows) Console.Write(r.Detail);
        if (outPath is not null) File.WriteAllText(outPath, sb.ToString());
    }
}
