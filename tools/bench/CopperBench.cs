using System.Diagnostics;
using System.Text;
using Wtw.Sim.Data;
using Wtw.Sim.People;
using Wtw.Sim.Planet;
using Wtw.Sim.Provinces;
using Wtw.Sim.Time;
using Wtw.Sim.World;

/// <summary>
/// (E5 고침) "구리 광맥 곁 마을": 구리 광맥이 드러난 따뜻한 지방에 가족 셋을 광맥 곁에 뿌리고, 어른들이 처음부터 불(부싯돌 불씨 · 모닥불)과
/// 토기(빚기 · 말리기 · 굽기)를 안다 — 그 뒤 Y 년 동안 돌 화덕 · 숯 · 구리가 나오는지. 시드마다 다른 행성(그 행성에서 가장 알맞은 구리 지방).
/// 시험용 장면(SceneBench)도 이것으로 만든다.
/// </summary>
public static class CopperBench
{
    public static readonly string[] KnownAtStart = { "sparks", "campfire", "wet_pot", "dry_pot", "pot" };

    /// <summary>구리 광맥이 드러난(보이는) 지방과 광맥 타일. 따뜻할수록 · 물이 있을수록 먼저. 없으면 (-1, -1).</summary>
    public static (int Cell, int Ore) FindCopperCell(GameWorld w)
    {
        var p = w.Planet;
        int copper = -1;
        var ores = RockCatalog.Default.Ores;
        for (int i = 0; i < ores.Count; i++) if (ores[i].Id == "copper") copper = i;
        var cands = PeopleSamples.Ranked(p, (pl, c) => pl.Resource(c, ResourceKinds.Copper) > 0 && pl.Temp10[c] >= 100 && pl.Biome[c] is not (Biome.Desert or Biome.Ice or Biome.Tundra)
            ? pl.Resource(c, ResourceKinds.Copper) * 20 + (pl.RiverSize[c] > 0 ? 25 : 0) - Math.Abs(pl.Temp10[c] - 190) / 5 : int.MinValue).Take(40);
        foreach (int cell in cands)
        {
            var life = w.People.Life(cell);
            if (life.DrinkTiles == 0) continue;
            int best = -1; long bd = long.MaxValue;
            for (int t = 0; t < HexFrame.TileCount; t++)
            {
                if (life.Map.VisibleOreAt(t) != copper) continue;
                long d = ProvinceLife.D2(t, 75, 75);
                if (d < bd && NearWalk(life, t) >= 0) { bd = d; best = t; }
            }
            if (best >= 0) return (cell, best);
        }
        return (-1, -1);
    }

    private static int NearWalk(ProvinceLife life, int t)
    {
        int x = t % HexFrame.Size, y = t / HexFrame.Size;
        for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if ((uint)nx >= HexFrame.Size || (uint)ny >= HexFrame.Size) continue;
                int j = ny * HexFrame.Size + nx;
                if (life.Walk[j]) return j;
            }
        return -1;
    }

    /// <summary>광맥 곁(8~20타일)의 가장 넓은 이어진 땅에 가족 셋, 어른들은 불 · 토기를 안다. 만든 세계(구리 지방이 없으면 null).</summary>
    public static GameWorld? NewVillage(ulong seed, int cap, out int cell)
    {
        var w = GameWorld.Create(seed, new WorldConfig { DotCount = 0, PeopleCap = cap });
        (cell, int ore) = FindCopperCell(w);
        if (cell < 0) return null;
        var life = w.People.Life(cell);
        int start = NearWalk(life, ore);
        int reg = life.Region[start];
        var spots = new List<(int X, int Y)>();
        int ox = ore % HexFrame.Size, oy = ore / HexFrame.Size;
        for (int r = 6; r <= 24 && spots.Count < 3; r++)
            for (int k = 0; k < 16 && spots.Count < 3; k++)
            {
                double a = (k / 16.0 + r * 0.13) * Math.PI * 2;
                int x = ox + (int)Math.Round(Math.Cos(a) * r), y = oy + (int)Math.Round(Math.Sin(a) * r);
                if ((uint)x >= HexFrame.Size || (uint)y >= HexFrame.Size) continue;
                int t = y * HexFrame.Size + x;
                if (!life.Walk[t] || life.Region[t] != reg || HexFrame.BorderDistance(t) < 6) continue;
                if (spots.Any(s => Math.Abs(s.X - x) + Math.Abs(s.Y - y) < 4)) continue;
                spots.Add((x, y));
            }
        if (spots.Count == 0) return null;
        foreach (var (x, y) in spots) w.Apply(new Intervention(0, Intervention.Kinds.SpawnFamily, new long[] { cell, x, y }));
        w.AdvanceTo(1);
        var cr = w.People.Craft;
        foreach (var q in w.People.All)
        {
            if (q.Stage(w.Now) < LifeStage.Adult) continue;
            foreach (var id in KnownAtStart)
            {
                var r = cr.Rule(id)!;
                int input = id switch { "sparks" => cr.ItemIndex("flint"), "campfire" => cr.ItemIndex("twigs"), "wet_pot" => cr.ItemIndex("clay"), "dry_pot" => cr.ItemIndex("wet_pot"), _ => cr.ItemIndex("dry_pot") };
                w.People.Learn(q, r, 0, input, id == "campfire" ? cr.ItemIndex("ember") : id == "sparks" ? cr.ItemIndex("flint") : -1, -1);
            }
        }
        return w;
    }

    public static void Run(int seeds, int years, ulong first, int parallel, string? outPath, int cap)
    {
        var rows = new (ulong Seed, int Cell, string Biome, SortedDictionary<string, int> First, int Alive, long Mined, int OreSeen, double Sec, int MeltTries, int OreCarried, int ShinyWish)[seeds];
        var sw = Stopwatch.StartNew();
        Parallel.For(0, seeds, new ParallelOptions { MaxDegreeOfParallelism = parallel }, k =>
        {
            ulong seed = first + (ulong)k;
            var t0 = Stopwatch.StartNew();
            var w = NewVillage(seed, cap, out int cell);
            var firsts = new SortedDictionary<string, int>();
            if (w is null) { rows[k] = (seed, -1, "", firsts, 0, 0, 0, 0, 0, 0, 0); return; }
            int melt = 0, carried = 0, shiny = 0; int I_ore = w.People.Craft.ItemIndex("copper_ore"); int W_shiny = w.People.Craft.Wishes.First(x => x.Id == "shiny").Index;
            long end = years * GameTime.TicksPerYear;
            var cr = w.People.Craft;
            for (long t = 0; t < end && w.People.AliveCount > 0; t += GameTime.TicksPerDay)
            {
                w.AdvanceTo(Math.Min(end, t + GameTime.TicksPerDay));
                if (t % (10 * GameTime.TicksPerDay) != 0) continue;
                foreach (var q in w.People.All) if (!q.Dead) { if (q.CarryCount(I_ore) > 0) carried++; if (q.Wish[W_shiny] >= 20) shiny++; }
                foreach (var (rule, d) in w.People.Discoveries)
                {
                    string id = cr.Catalog.Items[cr.Rules[rule].Result].Id;
                    if (!firsts.ContainsKey(id)) firsts[id] = (int)(d.Tick / GameTime.TicksPerYear);
                }
            }
            foreach (var (rule, d) in w.People.Discoveries) { string id = cr.Catalog.Items[cr.Rules[rule].Result].Id; if (!firsts.ContainsKey(id)) firsts[id] = (int)(d.Tick / GameTime.TicksPerYear); }
            int oreClue = w.People.All.Where(q => !q.Dead).Count(q => q.Clues.Any(c => cr.Rules[c.Rule].Id == "copper"));
            var copperRule = cr.Rule("copper")!;
            melt = (int)w.People.Stats.RuleFail[copperRule.Index] + (w.People.Partials.TryGetValue(copperRule.Index, out var pp) ? pp.Count : 0) + (w.People.Discoveries.TryGetValue(copperRule.Index, out var dd) ? dd.PartialsBefore + 1 : 0);
            rows[k] = (seed, cell, PlanetNames.Of(w.Planet.Biome[cell]), firsts, w.People.AliveCount, w.People.Stats.Mined, oreClue, t0.Elapsed.TotalSeconds, melt, carried, shiny);
        });
        var sb = new StringBuilder();
        sb.AppendLine($"## (E5 고침) 구리 광맥 곁 마을: 시드 {seeds}개 × 가족 셋(어른은 불 · 토기를 앎) × {years}년, 상한 {cap} (걸린 시간 {sw.Elapsed.TotalSeconds:0}초, 코어 {parallel})");
        sb.AppendLine();
        string[] items = { "hearth", "charcoal", "copper_lump", "copper_blade", "molten_copper" };
        var cat = ObjectCatalog.Default;
        string Name(string id) => cat.Items.First(i => i.Id == id).Name;
        sb.AppendLine("| 시드 | 칸 | 끝 인구 | 캠 | 구리를 녹여 본 횟수 | 구리 광석을 든 사람(10일마다 센 합) | 반짝이는 돌 바람 20 넘은 사람(10일마다 센 합) | 구리 단서 가진 사람(끝) | " + string.Join(" | ", items.Select(Name)) + " | 초 |");
        sb.AppendLine("|" + string.Concat(Enumerable.Repeat("---|", 9 + items.Length)));
        foreach (var r in rows)
            sb.AppendLine(r.Cell < 0 ? $"| {r.Seed} | (구리가 드러난 따뜻한 지방 없음) |" : $"| {r.Seed} | {r.Cell}({r.Biome}) | {r.Alive} | {r.Mined} | {r.MeltTries} | {r.OreCarried} | {r.ShinyWish} | {r.OreSeen} | " + string.Join(" | ", items.Select(i => r.First.TryGetValue(i, out int y) ? $"{y}년" : "—")) + $" | {r.Sec:0} |");
        sb.AppendLine();
        sb.AppendLine($"- 구리 덩이가 나온 시드: {rows.Count(r => r.First.ContainsKey("copper_lump"))}/{rows.Count(r => r.Cell >= 0)} · 숯: {rows.Count(r => r.First.ContainsKey("charcoal"))} · 돌 화덕: {rows.Count(r => r.First.ContainsKey("hearth"))}");
        Console.Write(sb.ToString());
        if (outPath is not null) File.WriteAllText(outPath, sb.ToString());
    }
}
