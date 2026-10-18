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
        Save("village-50y.wtw", "50년 뒤 마을", "세 가족이 50년을 산 뒤 — 불 · 돌칼 · 그릇을 아는 마을. [지식의 행방] 창과 카드의 [지식]을 보세요", 50 * GameTime.TicksPerYear);
        // (E5) 마지막 장인이 늙음: 세계에서 한 사람만 아는 법이 있고 그 사람이 60살 넘음 — 처음 그런 때를 찾는다(결정론이라 늘 같은 장면)
        {
            var w = New();
            var c = w.People.Craft;
            string? found = null;
            for (long t = 0; t < 250 * GameTime.TicksPerYear && found is null; t += GameTime.TicksPerDay)
            {
                w.AdvanceTo(t + GameTime.TicksPerDay);
                if (t < 30 * GameTime.TicksPerYear) continue;
                foreach (var (rule, d) in w.People.Discoveries)
                {
                    if (d.Lost || c.Rules[rule].Id == "paint") continue;
                    Person? only = null; int n = 0;
                    foreach (var q in w.People.All) if (!q.Dead && q.Knows(rule)) { n++; only = q; }
                    if (n != 1 || only!.AgeYears(w.Now) < 60) continue;
                    int pics = w.People.PaintingsOf(rule);
                    found = $"{w.People.DisplayName(only)}(나이 {only.AgeYears(w.Now)})만 {c.ItemName(c.Rules[rule].Result)} 만드는 법을 안다" + (pics > 0 ? $" — 그림 {pics}점이 남아 있다" : " — 아직 그림도 없다");
                    break;
                }
            }
            if (found is not null)
            {
                string file = "last-artisan.wtw";
                SaveGame.SaveFile(w, Path.Combine(outDir, file));
                index.Add($"{file}|마지막 장인이 늙음|{found}. [넘기기] → 지식 · 세계에서 사라짐, 그리고 [지식의 행방] 창 ({GameTime.Format(w.Now)} · 사람 {w.People.AliveCount}명)");
                Console.WriteLine($"{file}: {GameTime.Format(w.Now)} · {found}");
            }
            else Console.WriteLine("마지막 장인 장면: 250년 안에 찾지 못함");
        }
        File.WriteAllLines(Path.Combine(outDir, "index.txt"), index);
    }
}
