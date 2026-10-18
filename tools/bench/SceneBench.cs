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
    public static void Run(string outDir, ulong seed)
    {
        Directory.CreateDirectory(outDir);
        var index = new List<string>();
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
        long fire = FirstTick("campfire", 80);
        if (fire > 0) Save("first-fire.wtw", "첫 불 직전", "따뜻한 숲의 세 가족 — 이틀 뒤에 누군가 처음으로 모닥불을 피운다. 1배속으로 지켜보세요", fire - 2 * GameTime.TicksPerDay);
        long pot = FirstTick("pot", 150);
        if (pot > 0) Save("first-pot.wtw", "첫 토기 직전", "이틀 뒤에 누군가 처음으로 진흙 그릇을 불에 구워 토기를 만든다", pot - 2 * GameTime.TicksPerDay);
        Save("village-50y.wtw", "50년 뒤 마을", "세 가족이 50년을 산 뒤 — 불 · 돌칼 · 그릇을 아는 마을. [발견] 창과 카드의 [지식]을 보세요", 50 * GameTime.TicksPerYear);
        File.WriteAllLines(Path.Combine(outDir, "index.txt"), index);
    }
}
