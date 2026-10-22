using Wtw.Sim.Persistence;
using Wtw.Sim.People;
using Wtw.Sim.Time;
using Wtw.Sim.World;

/// <summary>
/// (E4) 시험용 장면 저장 파일 만들기 — zip 에 넣고 [불러오기]에서 고른다.
/// 첫 불 직전 · 첫 토기 직전(따뜻한 땅 가족 셋, 그 발견 2일 전) · 50년 뒤 마을. 결정론이라 같은 시드면 늘 같은 장면.
/// scenes/index.txt: 파일|제목|설명.
/// </summary>
public static class SceneBench
{
    public static void Run(string outDir, ulong seed, bool onlyArtisan = false, bool onlyCopper = false, bool onlyTundra = false, bool onlyFarm = false, bool onlyBuild = false)
    {
        Directory.CreateDirectory(outDir);
        var index = new List<string>();
        if (onlyBuild)
        {
            index.AddRange(File.ReadAllLines(Path.Combine(outDir, "index.txt")).Where(l => !l.StartsWith("hut-village")));
            BuildScene(outDir, seed, index);
            File.WriteAllLines(Path.Combine(outDir, "index.txt"), index);
            return;
        }
        if (onlyFarm)
        {
            index.AddRange(File.ReadAllLines(Path.Combine(outDir, "index.txt")).Where(l => !l.StartsWith("river-farm")));
            FarmScene(outDir, seed, index);
            File.WriteAllLines(Path.Combine(outDir, "index.txt"), index);
            return;
        }
        if (onlyTundra)
        {
            index.AddRange(File.ReadAllLines(Path.Combine(outDir, "index.txt")).Where(l => !l.StartsWith("tundra-edge")));
            TundraScene(outDir, seed, index);
            File.WriteAllLines(Path.Combine(outDir, "index.txt"), index);
            return;
        }
        if (onlyArtisan) index.AddRange(File.ReadAllLines(Path.Combine(outDir, "index.txt")).Where(l => !l.StartsWith("last-artisan")));
        if (onlyCopper)
        {
            index.AddRange(File.ReadAllLines(Path.Combine(outDir, "index.txt")).Where(l => !l.StartsWith("copper-village")));
            CopperScene(outDir, seed, index);
            File.WriteAllLines(Path.Combine(outDir, "index.txt"), index);
            return;
        }
        GameWorld New()
        {
            var w = GameWorld.Create(seed, new WorldConfig { DotCount = 0 });
            int cell = PeopleSamples.Home(w.Planet);
            foreach (var (x, y) in PeopleSamples.SpawnSpots(w.People.Life(cell), 3))
                w.Apply(new Intervention(0, Intervention.Kinds.SpawnFamily, new long[] { cell, x, y }));
            return w;
        }
        long FirstTick(string item, int maxYears)
        {
            var w = New();
            var c = w.People.Craft;
            for (long t = 0; t < maxYears * GameTime.TicksPerYear; t += GameTime.TicksPerDay)
            {
                w.AdvanceTo(t + GameTime.TicksPerDay);
                foreach (var (rule, d) in w.People.Discoveries)
                    if (c.Catalog.Items[c.Rules[rule].Result].Id == item) return d.Tick;
            }
            return -1;
        }
        void Save(string file, string title, string desc, long tick)
        {
            var w = New();
            w.AdvanceTo(Math.Max(1, tick));
            SaveGame.SaveFile(w, Path.Combine(outDir, file));
            index.Add($"{file}|{title}|{desc} ({GameTime.Format(w.Now)} · 사람 {w.People.AliveCount}명)");
            Console.WriteLine($"{file}: {GameTime.Format(w.Now)} · 사람 {w.People.AliveCount}명 · {new FileInfo(Path.Combine(outDir, file)).Length / 1024}KB");
        }
        if (!onlyArtisan) {
        long fire = FirstTick("campfire", 200); // (E5c 고침) 장면 시드에서 첫 불이 100년 뒤로 밀려 200년까지 찾는다
        if (fire > 0) Save("first-fire.wtw", "첫 불 직전", "따뜻한 숲의 세 가족 — 이틀 뒤에 누군가 처음으로 모닥불을 피운다. 1배속으로 지켜보세요", fire - 2 * GameTime.TicksPerDay);
        long pot = FirstTick("pot", 150);
        if (pot > 0) Save("first-pot.wtw", "첫 토기 직전", "이틀 뒤에 누군가 처음으로 진흙 그릇을 불에 구워 토기를 만든다", pot - 2 * GameTime.TicksPerDay);
        Save("village-50y.wtw", "50년 뒤 마을", "세 가족이 50년을 산 뒤의 마을 — 가르치고 · 배우는 모습. [지식의 행방] 창과 카드의 [지식]을 보세요", 50 * GameTime.TicksPerYear);
        }
        if (!onlyArtisan) CopperScene(outDir, seed, index);
        if (!onlyArtisan) TundraScene(outDir, seed, index);
        if (!onlyArtisan) FarmScene(outDir, seed, index);
        // (E5) 마지막 장인이 늙음: 세계에서 그 법을 아는 사람이 셋 이하이고 모두 40살 넘은 때 가운데 가장 "끊기기 쉬운" 때
        // (아는 사람이 적을수록 · 가장 젊은 아는 사람도 늙었을수록). 200년 안. 결정론이라 늘 같은 장면.
        // (E5c) 150년 · 45살로는 찾지 못해(지식이 더 널리 퍼졌다) 200년 · 40살로 넓혔다.
        {
            var w = New();
            var c = w.People.Craft;
            string? best = null; byte[]? bestBytes = null; int bestScore = int.MinValue;
            for (long t = 0; t < 200 * GameTime.TicksPerYear; t += GameTime.TicksPerDay)
            {
                w.AdvanceTo(t + GameTime.TicksPerDay);
                if (t < 10 * GameTime.TicksPerYear || (t / GameTime.TicksPerDay) % 5 != 0) continue;
                foreach (var (rule, d) in w.People.Discoveries)
                {
                    if (d.Lost || c.Rules[rule].Id == "paint") continue;
                    var knowers = w.People.All.Where(q => !q.Dead && q.Knows(rule)).ToList();
                    if (knowers.Count == 0 || knowers.Count > 3) continue;
                    int young = knowers.Min(q => q.AgeYears(w.Now));
                    if (young < 40) continue;
                    int score = young - knowers.Count * 8;
                    if (score <= bestScore) continue;
                    var oldest = knowers.OrderByDescending(q => q.AgeYears(w.Now)).First();
                    int pics = w.People.PaintingsOf(rule);
                    string who = string.Join(" · ", knowers.Select(q => $"{w.People.DisplayName(q)}(나이 {q.AgeYears(w.Now)}, {w.People.LandLabelOr(q.Cell, "이름 없는 땅")})"));
                    best = $"세계에서 {c.ItemName(c.Rules[rule].Result)} 만드는 법을 아는 사람은 {knowers.Count}명뿐 — {who}" + (pics > 0 ? $" · 그림 {pics}점이 남아 있다" : " · 그림은 없다") + $" ({GameTime.Format(w.Now)} · 사람 {w.People.AliveCount}명)";
                    bestScore = score; bestBytes = SaveGame.ToBytes(w);
                }
            }
            string file = "last-artisan.wtw";
            if (bestBytes is not null)
            {
                File.WriteAllBytes(Path.Combine(outDir, file), bestBytes);
                index.Add($"{file}|마지막 장인이 늙음|{best}. [넘기기] → 지식 · 세계에서 사라짐, 그리고 [지식의 행방] 창");
                Console.WriteLine($"{file}: {best}");
            }
            else Console.WriteLine("마지막 장인 장면: 200년 안에 찾지 못함");
        }
        File.WriteAllLines(Path.Combine(outDir, "index.txt"), index);
    }

    /// <summary>
    /// (E5b) 털옷을 아는 무리, 툰드라 가장자리: 침엽수림과 맞닿은 툰드라 지방(강이 있으면 더 좋다)에 세 가족 — 어른들은 불 피우기 · 고기 익히기 · 가죽 일 · 바늘 · 털옷 짓는 법을 알고
    /// 털가죽 옷 · 신을 입고 돌창 · 돌 날 · 뼈바늘을 든다. 첫날 밤을 넘기는지, 옷이 닳기 전에 새 옷을 짓는지, 늑대가 불 없는 곳에 오는지.
    /// </summary>
    public static int TundraEdge(Wtw.Sim.Planet.Planet p) => PeopleSamples.Ranked(p, (pl, c) =>
    {
        if (pl.Biome[c] != Wtw.Sim.Planet.Biome.Tundra) return int.MinValue;
        bool boreal = false; foreach (int n in pl.Grid.Neighbors(c)) if (pl.Biome[n] == Wtw.Sim.Planet.Biome.Boreal) boreal = true;
        return boreal ? 100 - Math.Abs(pl.Temp10[c] + 50) / 10 + (pl.RiverSize[c] > 0 ? 15 : 0) : int.MinValue;
    }).FirstOrDefault(-1);

    private static void TundraScene(string outDir, ulong seed, List<string> index)
    {
        var w = GameWorld.Create(seed, new WorldConfig { DotCount = 0 });
        int cell = TundraEdge(w.Planet);
        if (cell < 0) { Console.WriteLine("툰드라 가장자리: 이 행성에 침엽수림과 맞닿은 툰드라가 없음"); return; }
        foreach (var (x, y) in PeopleSamples.SpawnSpots(w.People.Life(cell), 3))
            w.Apply(new Intervention(0, Intervention.Kinds.SpawnFamily, new long[] { cell, x, y }));
        w.AdvanceTo(1);
        HuntBench.Teach(w, "coat");
        // 해 질 녘(그 지방 17시)에서 시작 — 첫날 밤을 바로 볼 수 있게
        var life = w.People.Life(cell);
        long t = 1;
        while (life.LocalHour(t) != 17) t += GameTime.TicksPerHour / 4;
        w.AdvanceTo(t);
        string file = "tundra-edge.wtw";
        SaveGame.SaveFile(w, Path.Combine(outDir, file));
        var c = w.People.AnimalCounts(life);
        string animals = string.Join(" · ", w.People.Fauna.Animals.Where(a => c[a.Index] > 0).Select(a => $"{a.Name} {c[a.Index]}"));
        index.Add($"{file}|털옷을 아는 무리, 툰드라 가장자리|침엽수림과 맞닿은 툰드라(칸 {cell}, 연평균 {w.Planet.Temp10[cell] / 10.0:0.0}°C)에 세 가족 — 털가죽 옷 · 신을 입고 불 피우기와 가죽 일을 안다. 해 질 녘에서 시작: 첫날 밤을 넘기는지, 옷이 닳기 전에 새 옷을 짓는지, 밤에 불 없는 곳에 늑대가 오는지. 짐승: {animals} ({GameTime.Format(w.Now)} · 사람 {w.People.AliveCount}명)");
        Console.WriteLine($"{file}: 칸 {cell} · 사람 {w.People.AliveCount}명 · {animals}");
    }

    /// <summary>(E5c) 씨를 아는 무리, 강가 초원: 큰 강이 흐르는 따뜻한 초원(사바나 · 온대림도 받음)에 세 가족 — 어른들은 불 · 낟알 볶기 · 씨 묻기 · 밭 일구는 법을 알고 낟알 씨와 땅 파는 막대를 든다.</summary>
    public static int RiverGrassland(Wtw.Sim.Planet.Planet p) => PeopleSamples.Ranked(p, (pl, c) =>
        pl.Biome[c] is Wtw.Sim.Planet.Biome.Grassland or Wtw.Sim.Planet.Biome.Savanna or Wtw.Sim.Planet.Biome.Temperate && pl.RiverSize[c] > 0 && pl.Temp10[c] is >= 140 and <= 260 && pl.PrecipMm[c] >= 500
            ? (pl.Biome[c] == Wtw.Sim.Planet.Biome.Grassland ? 30 : 0) + Math.Min((int)pl.RiverSize[c], 40) - Math.Abs(pl.Temp10[c] - 200) / 10 : int.MinValue).FirstOrDefault(-1);

    private static void FarmScene(string outDir, ulong seed, List<string> index)
    {
        var w = GameWorld.Create(seed, new WorldConfig { DotCount = 0 });
        int cell = RiverGrassland(w.Planet);
        if (cell < 0) { Console.WriteLine("강가 초원: 이 행성에 맞는 땅이 없음"); return; }
        foreach (var (x, y) in PeopleSamples.SpawnSpots(w.People.Life(cell), 3))
            w.Apply(new Intervention(0, Intervention.Kinds.SpawnFamily, new long[] { cell, x, y }));
        w.AdvanceTo(1);
        FarmBench.Teach(w, "field");
        // 아침(그 지방 7시)에서 시작 — 바로 일하는 모습을 볼 수 있게
        var life = w.People.Life(cell);
        long t = 1;
        while (life.LocalHour(t) != 7) t += GameTime.TicksPerHour / 4;
        w.AdvanceTo(t);
        string file = "river-farm.wtw";
        SaveGame.SaveFile(w, Path.Combine(outDir, file));
        string land = Wtw.Sim.Planet.PlanetNames.Of(w.Planet.Biome[cell]);
        index.Add($"{file}|씨를 아는 무리, 강가 초원|큰 강이 흐르는 {land}(칸 {cell}, 연평균 {w.Planet.Temp10[cell] / 10.0:0.0}°C)에 세 가족 — 어른들은 씨를 묻고 땅을 일구는 법을 알고 낟알 씨와 땅 파는 막대를 든다. 1~2배속으로 밭을 일구는 모습을, 5배속으로 며칠 넘겨 싹 → 익음 → 거두기와 밭에 오는 짐승을 보세요 ({GameTime.Format(w.Now)} · 사람 {w.People.AliveCount}명)");
        Console.WriteLine($"{file}: 칸 {cell}({land}) · 사람 {w.People.AliveCount}명");
    }

    /// <summary>
    /// (E6) 움집을 짓는 마을: 나무가 있는 따뜻한 땅(온대림 · 강가)에 세 가족 — 어른들은 불 · 바람막이 · 움집 · 저장 구덩이 짓는 법을 알고 돌날 · 뾰족한 막대를 든다.
    /// 비 오는 밤을 한데서 보내고 온 무리라 머물 곳이 아쉽다(바람 30). 그 지방 아침 7시에서 시작.
    /// </summary>
    private static void BuildScene(string outDir, ulong seed, List<string> index)
    {
        var w = GameWorld.Create(seed, new WorldConfig { DotCount = 0 });
        int cell = PeopleSamples.Ranked(w.Planet, (pl, c) => pl.Biome[c] == Wtw.Sim.Planet.Biome.Temperate && pl.RiverSize[c] > 0 && pl.Temp10[c] is >= 120 and <= 220 ? Math.Min((int)pl.RiverSize[c], 40) - Math.Abs(pl.Temp10[c] - 170) / 10 : int.MinValue).FirstOrDefault(-1);
        if (cell < 0) cell = RiverGrassland(w.Planet);
        foreach (var (x, y) in PeopleSamples.SpawnSpots(w.People.Life(cell), 3))
            w.Apply(new Intervention(0, Intervention.Kinds.SpawnFamily, new long[] { cell, x, y }));
        w.AdvanceTo(1);
        BuildBench.Teach(w, "build");
        foreach (var q in w.People.All) if (q.Stage(w.Now) >= LifeStage.Adult) q.Wish[12] = 30;
        var life = w.People.Life(cell);
        long t = 1;
        while (life.LocalHour(t) != 7) t += GameTime.TicksPerHour / 4;
        w.AdvanceTo(t);
        string file = "hut-village.wtw";
        SaveGame.SaveFile(w, Path.Combine(outDir, file));
        string land = Wtw.Sim.Planet.PlanetNames.Of(w.Planet.Biome[cell]);
        index.Add($"{file}|움집을 짓는 마을|강이 흐르는 {land}(칸 {cell}, 연평균 {w.Planet.Temp10[cell] / 10.0:0.0}°C)에 세 가족 — 어른들은 불 · 바람막이 · 움집 · 저장 구덩이 짓는 법을 안다. 비 오는 밤을 한데서 보내 머물 곳이 아쉽다. 1~2배속으로 자리를 정하고 · 나무를 베어 · 풀을 날라 · 쌓는 모습을, 3~4배속으로 움집이 서는 것을 보세요. 건물을 누르면 건물 창 ({GameTime.Format(w.Now)} · 사람 {w.People.AliveCount}명)");
        Console.WriteLine($"{file}: 칸 {cell}({land}) · 사람 {w.People.AliveCount}명");
    }

    /// <summary>(E5b 스크린샷용) 툰드라 가장자리 장면을 흘려 보며 늑대가 불가의 사람에게 다가가다 쫓겨난 때를 찾아, 그 한 시간 전을 저장한다.</summary>
    public static void WolfMoment(string outPath, ulong seed, int days)
    {
        var w = GameWorld.Create(seed, new WorldConfig { DotCount = 0 });
        int cell = TundraEdge(w.Planet);
        foreach (var (x, y) in PeopleSamples.SpawnSpots(w.People.Life(cell), 3))
            w.Apply(new Intervention(0, Intervention.Kinds.SpawnFamily, new long[] { cell, x, y }));
        w.AdvanceTo(1);
        HuntBench.Teach(w, "coat");
        var life = w.People.Life(cell);
        var ring = new Queue<byte[]>();
        long seen = w.People.Stats.FireRepelled; int litNights = 0, minD = int.MaxValue;
        for (long t = GameTime.TicksPerHour / 2; t < days * GameTime.TicksPerDay; t += GameTime.TicksPerHour / 2)
        {
            w.AdvanceTo(t);
            if (w.People.Stats.FireRepelled > seen) { File.WriteAllBytes(outPath, ring.Peek()); Console.WriteLine($"늑대가 불에 쫓겨남: {GameTime.Format(w.Now)} → {outPath}"); return; }
            ring.Enqueue(SaveGame.ToBytes(w)); if (ring.Count > 3) ring.Dequeue();
            // 쫓겨나는 일은 드물다(불가의 사람은 처음부터 노리지 않는다) — 밤에 불 둘레(사람 곁) 20타일 안에 늑대 무리가 와 있는 때도 받는다
            life = w.People.Life(cell); // 사람이 다 떠났다 돌아오면 지방 계산이 새로 만들어진다
            int lh = life.LocalHour(w.Now);
            if (lh < 20 && lh > 3) continue;
            if (life.Fires.Items.Any(kv => kv.Value.Lit(w.Now))) litNights++;
            foreach (var h in life.Herds.Values)
            {
                if (!w.People.Fauna.Animals[h.Species].Predator || h.Owner >= 0) continue;
                int ht = h.Tile(w.Now), hx = ht % Wtw.Sim.Provinces.HexFrame.Size, hy = ht / Wtw.Sim.Provinces.HexFrame.Size;
                foreach (var (ft, f) in life.Fires.Items)
                {
                    if (!f.Lit(w.Now)) continue;
                    int d = Math.Max(Math.Abs(ft % Wtw.Sim.Provinces.HexFrame.Size - hx), Math.Abs(ft / Wtw.Sim.Provinces.HexFrame.Size - hy));
                    minD = Math.Min(minD, d);
                    if (d is >= 8 and <= 20) { File.WriteAllBytes(outPath, SaveGame.ToBytes(w)); Console.WriteLine($"밤 불가 {d}타일에 늑대: {GameTime.Format(w.Now)} → {outPath}"); return; }
                }
            }
        }
        Console.WriteLine($"찾지 못함 — 불 켜진 밤 반시간 {litNights} · 늑대와 불의 가장 가까운 거리 {minD} · 산 사람 {w.People.AliveCount} · 무리 {string.Join(",", life.Herds.Values.Select(h => w.People.Fauna.Animals[h.Species].Id + h.Count))}");
    }

    /// <summary>(E5 고침) 구리 광맥 곁 마을 — 구리 광맥이 드러난 지방, 불 · 토기를 아는 세 가족(CopperBench 와 같은 세계).</summary>
    private static void CopperScene(string outDir, ulong seed, List<string> index)
    {
        var w = CopperBench.NewVillage(seed, 300, out int cell);
        if (w is null) { Console.WriteLine("구리 광맥 곁 마을: 이 행성에 구리가 드러난 따뜻한 지방이 없음"); return; }
        string file = "copper-village.wtw";
        SaveGame.SaveFile(w, Path.Combine(outDir, file));
        string land = Wtw.Sim.Planet.PlanetNames.Of(w.Planet.Biome[cell]);
        index.Add($"{file}|구리 광맥 곁 마을|구리 광맥이 드러난 {land} 지방(칸 {cell})에 세 가족 — 어른들은 처음부터 불 피우기와 토기 굽기를 안다. 돌 화덕 · 숯 · 구리까지 갈까요? [넘기기] → 고른 물건 · 구리 덩이 ({GameTime.Format(w.Now)} · 사람 {w.People.AliveCount}명)");
        Console.WriteLine($"{file}: 칸 {cell}({land}) · 사람 {w.People.AliveCount}명");
    }
}
