using System.Text;
using Wtw.Sim.People;
using Wtw.Sim.Provinces;
using Wtw.Sim.Time;
using Wtw.Sim.World;

/// <summary>
/// (E5b 고침 · 재기만) 툰드라 (가)에서 밤에 불을 얼마나 지키나. dotnet run -c Release --project tools/bench -- people firenight [--seeds 20] [--years 5] [--out 파일.md]
/// 툰드라에 털옷 · 불 · 가죽 일을 아는 가족 셋(HuntBench --know coat 와 같은 세계). 밤(그 땅 20~5시) 한 시간마다:
///   잠든 사람 곁(6타일)에 불이 켜져 있나 · 지방에 켜진 불이 있나, 불이 켜졌다가 사람이 곁에 자는데 꺼진 일(밤에 꺼진 불).
/// 낮 · 밤 "땔감 찾으러" 나선 일과 찾지 못한 일, 추위로 죽을 때 곁에 불이 있었나 · 자고 있었나 · 옷.
/// </summary>
public static class FireNightBench
{
    private sealed class Row
    {
        public ulong Seed; public int Cell;
        public long SleepHours, SleepByFire, NightHours, NightAnyFire, FireOut, FuelTrips, FuelFail;
        public int ColdDeaths, ColdByFire, ColdAsleep, ColdClothed, ColdNoFireInLand, Deaths, FuelTiles, Alive;
    }

    public static void Run(int seeds, int years, ulong first, int parallel, string? outPath)
    {
        var rows = new Row[seeds];
        var rowOf = new Dictionary<PeopleSim, Row>();
        PeopleSim.DeathHook = (sim, p, cause) =>
        {
            Row? r; lock (rowOf) rowOf.TryGetValue(sim, out r);
            if (r is null) return;
            lock (r)
            {
                r.Deaths++;
                if (cause != DeathCause.Cold) return;
                var life = sim.Life(p.Cell);
                p.Position(sim.NowTick, out int x, out int y);
                r.ColdDeaths++;
                if (life.NearestFire(x / 1000, y / 1000, sim.NowTick, 6) >= 0) r.ColdByFire++;
                if (life.Fires.Items.All(kv => !kv.Value.Lit(sim.NowTick))) r.ColdNoFireInLand++;
                if (p.Asleep) r.ColdAsleep++;
                if (p.Worn >= 0) r.ColdClothed++;
            }
        };
        PeopleSim.ActHook = (sim, p) =>
        {
            if (p.Act != Act.Material || p.Why != Why.Fuel) return;
            Row? r; lock (rowOf) rowOf.TryGetValue(sim, out r);
            if (r is not null) lock (r) r.FuelTrips++;
        };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Parallel.For(0, seeds, new ParallelOptions { MaxDegreeOfParallelism = parallel }, k =>
        {
            ulong seed = first + (ulong)k;
            var w = GameWorld.Create(seed, new WorldConfig { DotCount = 0 });
            var row = rows[k] = new Row { Seed = seed };
            lock (rowOf) rowOf[w.People] = row;
            int cell = row.Cell = PeopleSamples.Cold(w.Planet);
            var life0 = w.People.Life(cell);
            foreach (var (x, y) in PeopleSamples.SpawnSpots(life0, 3))
                w.Apply(new Intervention(0, Intervention.Kinds.SpawnFamily, new long[] { cell, x, y }));
            w.AdvanceTo(1);
            HuntBench.Teach(w, "coat");
            var cat = Wtw.Sim.Data.ObjectCatalog.Default;
            for (int t = 0; t < HexFrame.TileCount; t++)
            {
                int o = life0.Map.ObjectAt(t);
                if (o >= 0 && cat.Objects[o].Uses.Any(u => u.Yields.Any(y => w.People.Craft.Prop(w.People.Craft.ItemIndex(y.Id), w.People.Craft.P_burns) >= 3))) row.FuelTiles++;
            }
            var litBefore = new HashSet<int>();
            long end = years * GameTime.TicksPerYear;
            for (long t = 2; t < end; t += GameTime.TicksPerHour)
            {
                w.AdvanceTo(t);
                if (w.People.AliveCount == 0) break;
                foreach (var (c, life) in w.People.Provinces)
                {
                    int h = life.LocalHour(w.Now);
                    if (h < 20 && h >= 5) continue;
                    bool anyPeople = false, anyFire = false;
                    foreach (var q in life.People)
                    {
                        if (q.Dead || q.Carrier >= 0) continue;
                        anyPeople = true;
                        if (!q.Asleep) continue;
                        q.Position(w.Now, out int x, out int y);
                        row.SleepHours++;
                        int f = life.NearestFire(x / 1000, y / 1000, w.Now, 6, litOnly: false);
                        if (f >= 0 && life.Fires[f].Lit(w.Now)) row.SleepByFire++;
                        else if (f >= 0 && litBefore.Contains(c * 100_000 + f)) row.FireOut++;
                    }
                    if (!anyPeople) continue;
                    row.NightHours++;
                    foreach (var (ft, fire) in life.Fires.Items) if (fire.Lit(w.Now)) { anyFire = true; break; }
                    if (anyFire) row.NightAnyFire++;
                }
                litBefore.Clear();
                foreach (var (c, life) in w.People.Provinces) foreach (var (ft, fire) in life.Fires.Items) if (fire.Lit(w.Now)) litBefore.Add(c * 100_000 + ft);
            }
            row.Alive = w.People.AliveCount;
            Console.WriteLine($"시드 {seed}: 칸 {cell} · 산 사람 {row.Alive} · 잔 시간 중 불 곁 {row.SleepByFire * 100.0 / Math.Max(1, row.SleepHours):0}% · 추위 죽음 {row.ColdDeaths}");
        });
        PeopleSim.DeathHook = null; PeopleSim.ActHook = null;
        var o = new StringBuilder();
        o.AppendLine($"# 툰드라 (가) 밤의 불 — 시드 {seeds} · 털옷 · 불 · 가죽 일을 아는 가족 셋 · {years}년 · {sw.Elapsed.TotalMinutes:0.0}분");
        o.AppendLine();
        o.AppendLine("| 시드 | 칸 | 끝 인구 | 잔 시간 중 곁(6타일)에 켜진 불 | 사람 있는 밤 시간 중 지방에 켜진 불 | 자는 곁에서 꺼진 불(시간) | 땔감 찾으러 | 추위 죽음 | 그때 곁에 불 | 지방에 켜진 불 없음 | 자다가 | 옷 입음 | 모든 죽음 | 땔감 나는 타일 |");
        o.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var r in rows)
            o.AppendLine($"| {r.Seed} | {r.Cell} | {r.Alive} | {r.SleepByFire * 100.0 / Math.Max(1, r.SleepHours):0}% | {r.NightAnyFire * 100.0 / Math.Max(1, r.NightHours):0}% | {r.FireOut} | {r.FuelTrips} | {r.ColdDeaths} | {r.ColdByFire} | {r.ColdNoFireInLand} | {r.ColdAsleep} | {r.ColdClothed} | {r.Deaths} | {r.FuelTiles} |");
        o.AppendLine();
        double Med(IEnumerable<double> v) { var l = v.OrderBy(x => x).ToList(); return l.Count == 0 ? 0 : l.Count % 2 == 1 ? l[l.Count / 2] : (l[l.Count / 2 - 1] + l[l.Count / 2]) / 2; }
        long cd = rows.Sum(r => (long)r.ColdDeaths);
        o.AppendLine($"- 잔 시간 중 곁(6타일)에 켜진 불: 중앙값 {Med(rows.Select(r => r.SleepByFire * 100.0 / Math.Max(1, r.SleepHours))):0}% · 모두 합 {rows.Sum(r => r.SleepByFire) * 100.0 / Math.Max(1, rows.Sum(r => r.SleepHours)):0}%");
        o.AppendLine($"- 사람이 있는 밤 시간 중 그 지방에 켜진 불이 하나라도 있었던 시간: 중앙값 {Med(rows.Select(r => r.NightAnyFire * 100.0 / Math.Max(1, r.NightHours))):0}%");
        o.AppendLine($"- 자는 사람 곁에서 밤에 꺼진 불: 모두 합 {rows.Sum(r => r.FireOut)}시간 · 땔감 찾으러 나섬 중앙값 {Med(rows.Select(r => (double)r.FuelTrips))}번 · 땔감이 나는 타일(나무 · 덤불 …) 중앙값 {Med(rows.Select(r => (double)r.FuelTiles))}");
        o.AppendLine($"- 추위로 죽은 사람 {cd}명(모든 죽음 {rows.Sum(r => r.Deaths)}): 그때 곁(6타일)에 켜진 불 {rows.Sum(r => r.ColdByFire)} · 그 지방에 켜진 불이 하나도 없음 {rows.Sum(r => r.ColdNoFireInLand)} · 자다가 {rows.Sum(r => r.ColdAsleep)} · 옷을 입고 {rows.Sum(r => r.ColdClothed)}");
        Console.WriteLine(o.ToString());
        if (outPath is not null) File.WriteAllText(outPath, o.ToString());
    }
}
