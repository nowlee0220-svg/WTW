using System.Diagnostics;
using Wtw.Sim.Data;
using Wtw.Sim.Planet;
using Wtw.Sim.Provinces;

/// <summary>지방 측정 · 개발용 그림.</summary>
static class ProvinceBench
{
    /// <summary>지방 하나 · 이웃까지 7 지방 만들기 시간과 메모리(시드 opt.Seeds 개 × 종류).</summary>
    public static ProvinceBenchResult Run(int seeds, ulong firstSeed, int land)
    {
        Console.WriteLine($"WTW bench: 지방 만들기, 시드 {seeds}개(첫 시드 {firstSeed}) × 종류 [{string.Join(", ", ProvinceSamples.Kinds)}], 땅 {land}");
        var cat = ObjectCatalog.Default;
        var warm = PlanetCache.Get(firstSeed, new PlanetSettings(land, ClimateBias.Normal), null);
        ProvinceGenerator.Generate(warm, ProvinceSamples.Find(warm, "forest")); // 데우기(JIT)
        var singles = new List<double>();
        var sevens = new List<double>();
        var sevensParallel = new List<double>();
        var rows = new List<ProvinceKindRow>();
        for (int i = 0; i < seeds; i++)
        {
            ulong seed = firstSeed + (ulong)i;
            var p = PlanetGenerator.Generate(seed, new PlanetSettings(land, ClimateBias.Normal));
            foreach (var kind in ProvinceSamples.Kinds)
            {
                int c = ProvinceSamples.Find(p, kind);
                if (c < 0) continue;
                var sw = Stopwatch.StartNew();
                var m = ProvinceGenerator.Generate(p, c);
                singles.Add(sw.Elapsed.TotalSeconds);
                var placed = ProvinceLayout.Around(p, c, nb => !p.IsOcean(nb));
                sw.Restart();
                foreach (var pl in placed) ProvinceGenerator.Generate(p, pl.Cell);
                sevens.Add(sw.Elapsed.TotalSeconds);
                sw.Restart();
                Parallel.ForEach(placed, pl => ProvinceGenerator.Generate(p, pl.Cell));
                sevensParallel.Add(sw.Elapsed.TotalSeconds);
                var oc = m.ObjectCounts(cat.Objects.Count);
                var gc = m.GroundCounts();
                rows.Add(new ProvinceKindRow
                {
                    Seed = seed, Kind = kind, Cell = c, Seconds = Math.Round(singles[^1], 4),
                    Biome = PlanetNames.Of(p.Biome[c]), Terrain = PlanetNames.Of(p.Terrain[c]),
                    Objects = oc.Sum(), Trees = cat.Objects.Where(o => o.IsTree).Sum(o => oc[o.Index]),
                    Ores = cat.Objects.Where(o => o.Kind == "ore").Sum(o => oc[o.Index]),
                    Water = gc[(int)Ground.ShallowWater] + gc[(int)Ground.DeepWater], Grounds = gc, ObjectCountsById = oc,
                    Fingerprint = m.Fingerprint,
                });
            }
        }
        // 메모리: 같은 행성에서 지도 20개를 만들어 들고 있을 때 늘어난 관리 메모리 ÷ 20
        var mp = PlanetCache.Get(firstSeed, new PlanetSettings(land, ClimateBias.Normal), null);
        var cells = Enumerable.Range(0, mp.CellCount).Where(mp.IsLand).Take(20).ToList();
        long before = GC.GetTotalMemory(true);
        var keep = cells.Select(c => ProvinceGenerator.Generate(mp, c)).ToList();
        long after = GC.GetTotalMemory(true);
        GC.KeepAlive(keep);
        double perMap = (after - before) / (double)keep.Count;
        var res = new ProvinceBenchResult
        {
            Runs = singles.Count,
            MedianSeconds = Median(singles), MaxSeconds = Math.Round(singles.Max(), 4),
            MedianSevenSeconds = Median(sevens), MedianSevenParallelSeconds = Median(sevensParallel),
            BytesPerMap = Math.Round(perMap), ApproxBytesPerMap = ProvinceMap.ApproxBytes, Rows = rows.ToArray(),
        };
        foreach (var kind in ProvinceSamples.Kinds)
        {
            var rs = rows.Where(r => r.Kind == kind).ToList();
            if (rs.Count == 0) continue;
            Console.WriteLine($"  {ProvinceSamples.Name(kind),-4}({rs.Count}개): 오브젝트 중앙값 {Median(rs.Select(r => (double)r.Objects))} · 나무 {Median(rs.Select(r => (double)r.Trees))} · 광맥 {Median(rs.Select(r => (double)r.Ores))} · 물 타일 {Median(rs.Select(r => (double)r.Water))}");
            var g = new double[GroundInfo.Count];
            foreach (var r in rs) for (int k = 0; k < g.Length; k++) g[k] += r.Grounds[k];
            double inside = HexFrame.InsideCount * rs.Count;
            Console.WriteLine("        땅 종류: " + string.Join(" · ", Enumerable.Range(1, GroundInfo.Count - 1).Where(k => g[k] > 0).Select(k => $"{GroundInfo.Name((Ground)k)} {g[k] * 100 / inside:0.#}%")));
            var o = new double[cat.Objects.Count];
            foreach (var r in rs) for (int k = 0; k < o.Length; k++) o[k] += r.ObjectCountsById[k];
            Console.WriteLine("        오브젝트(지방당): " + string.Join(" · ", Enumerable.Range(0, o.Length).Where(k => o[k] > 0).OrderByDescending(k => o[k]).Take(10).Select(k => $"{cat.Objects[k].Name} {o[k] / rs.Count:0}")));
        }
        Console.WriteLine($"요약({res.Runs}번): 지방 하나 중앙값 {res.MedianSeconds:0.000}초(최대 {res.MaxSeconds:0.000}) · 이웃까지 7 지방 {res.MedianSevenSeconds:0.000}초(나눠서 동시에 {res.MedianSevenParallelSeconds:0.000}초) · 지도 하나 메모리 약 {res.BytesPerMap / 1024:0}KB");
        return res;
    }

    static double Median(IEnumerable<double> values)
    {
        var s = values.OrderBy(x => x).ToArray();
        return s.Length == 0 ? 0 : Math.Round(s.Length % 2 == 1 ? s[s.Length / 2] : (s[s.Length / 2 - 1] + s[s.Length / 2]) / 2, 4);
    }

    /// <summary>개발용: 가운데 지방 + 이웃을 이어 붙인 그림(PPM). 타일 하나 = px 화소.</summary>
    public static void Dump(ulong seed, int land, string where, string path, int px)
    {
        var p = PlanetCache.Get(seed, new PlanetSettings(land, ClimateBias.Normal), null);
        int c = int.TryParse(where, out int cell) ? cell : ProvinceSamples.Find(p, where);
        if (c < 0) { Console.WriteLine($"{where} 칸이 없음"); return; }
        var placed = ProvinceLayout.Around(p, c, nb => !p.IsOcean(nb));
        var maps = placed.Select(pl => ProvinceGenerator.Generate(p, pl.Cell)).ToList();
        double half = HexFrame.A * 3 + 2;
        int W = (int)(half * 2 * px), H = W;
        var img = new byte[W * H * 3];
        var cat = ObjectCatalog.Default;
        for (int yy = 0; yy < H; yy++)
            for (int xx = 0; xx < W; xx++)
            {
                double lx = (xx + 0.5) / px - half, ly = (yy + 0.5) / px - half;
                (byte r, byte g, byte b) col = (8, 8, 10);
                for (int k = 0; k < placed.Count; k++)
                {
                    var (tx, ty) = placed[k].ToLocal(lx, ly);
                    int ix = (int)Math.Floor(tx), iy = (int)Math.Floor(ty);
                    if (ix < 0 || iy < 0 || ix >= HexFrame.Size || iy >= HexFrame.Size) continue;
                    int i = iy * HexFrame.Size + ix;
                    if (!HexFrame.Inside[i]) continue;
                    var m = maps[k];
                    col = Color(m, i, cat);
                    break;
                }
                int o = (yy * W + xx) * 3;
                img[o] = col.r; img[o + 1] = col.g; img[o + 2] = col.b;
            }
        using var f = File.Create(path);
        var head = System.Text.Encoding.ASCII.GetBytes($"P6\n{W} {H}\n255\n");
        f.Write(head);
        f.Write(img);
        var m0 = maps[0];
        Console.WriteLine($"{path}: 칸 {c} ({PlanetNames.Of(p.Terrain[c])} · {PlanetNames.Of(p.Biome[c])} · 강 {p.RiverSize[c]} · 오각형 {p.Grid.IsPentagon(c)}) · 이웃 {placed.Count - 1} · {m0.GenerationSeconds:0.000}초 · 오브젝트 {m0.ObjectCounts(cat.Objects.Count).Sum()}");
    }

    static (byte, byte, byte) Color(ProvinceMap m, int i, ObjectCatalog cat)
    {
        var g = m.Ground[i];
        (int r, int gg, int b) c = g switch
        {
            Ground.Soil => (120, 95, 65), Ground.Grass => (95, 135, 60), Ground.Sand => (215, 195, 140), Ground.Mud => (100, 85, 60),
            Ground.Rock => (130, 128, 122), Ground.Snow => (235, 238, 242), Ground.Ice => (195, 220, 235), Ground.Marsh => (85, 105, 70),
            Ground.ShallowWater => (70, 125, 175), Ground.DeepWater => (35, 70, 125), Ground.Cliff => (70, 60, 55), _ => (0, 0, 0),
        };
        if (m.Biome[i] == Biome.Savanna && g == Ground.Grass) c = (165, 160, 75);
        if (m.Biome[i] == Biome.Tundra && g == Ground.Grass) c = (130, 140, 100);
        if (m.Biome[i] == Biome.Rainforest && g == Ground.Grass) c = (60, 130, 50);
        double shade = 1;
        int x = i % HexFrame.Size, y = i / HexFrame.Size;
        if (!GroundInfo.IsWater(g) && x > 0 && y > 0)
        {
            int j = i - HexFrame.Size - 1;
            double d = m.Height[i] - m.Height[j];
            shade = Math.Clamp(1 + d / 60.0, 0.6, 1.35);
        }
        int o = m.ObjectAt(i);
        if (o >= 0)
        {
            var def = cat.Objects[o];
            c = def.Kind switch
            {
                "tree" => (30, 75, 30), "bush" => (70, 100, 45), "plant" => (140, 170, 70), "rock" => (90, 90, 95),
                "ore" => def.Id == "copper_vein" ? (200, 110, 50) : def.Id == "tin_vein" ? (200, 200, 210) : (150, 70, 60),
                _ => (180, 150, 120),
            };
            shade = 1;
        }
        return ((byte)Math.Clamp(c.r * shade, 0, 255), (byte)Math.Clamp(c.gg * shade, 0, 255), (byte)Math.Clamp(c.b * shade, 0, 255));
    }
}

public sealed class ProvinceBenchResult
{
    public int Runs { get; set; }
    public double MedianSeconds { get; set; }
    public double MaxSeconds { get; set; }
    public double MedianSevenSeconds { get; set; }
    public double MedianSevenParallelSeconds { get; set; }
    public double BytesPerMap { get; set; }
    public int ApproxBytesPerMap { get; set; }
    public ProvinceKindRow[] Rows { get; set; } = Array.Empty<ProvinceKindRow>();
}

public sealed class ProvinceKindRow
{
    public ulong Seed { get; set; }
    public string Kind { get; set; } = "";
    public int Cell { get; set; }
    public double Seconds { get; set; }
    public string Biome { get; set; } = "";
    public string Terrain { get; set; } = "";
    public int Objects { get; set; }
    public int Trees { get; set; }
    public int Ores { get; set; }
    public int Water { get; set; }
    public int[] Grounds { get; set; } = Array.Empty<int>();
    public int[] ObjectCountsById { get; set; } = Array.Empty<int>();
    public ulong Fingerprint { get; set; }
}
