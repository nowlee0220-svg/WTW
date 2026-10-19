using Wtw.Sim.Movers;
using Wtw.Sim.People;
using Wtw.Sim.World;

namespace Wtw.Sim.Runtime;

/// <summary>
/// 화면이 읽는 "지금 상태" 의 사본. 시뮬 스레드가 채우고 화면 스레드는 읽기만 한다.
/// 점마다 최근 구간 몇 개(일정 + 경로)를 담아, 화면이 원하는 소수 틱에서 보간할 수 있다.
/// </summary>
public sealed class WorldSnapshot
{
    public long Epoch;          // 새로 시작/불러오기 때마다 바뀜
    public long Now;            // 시뮬이 계산을 마친 시각 (이 시각까지는 일정이 확정)
    public ulong Seed;
    public int MapUnits;
    public int DotCount;
    public long EventsProcessed;
    public double SimDaysPerSecond; // 최근 1초 동안 실제로 흐른 게임 하루 수
    public double SimLoad;          // 시뮬 스레드가 계산에 쓴 시간 비율 (0~1)
    public WorldConfig Config = new();
    /// <summary>행성(읽기 전용 — 시뮬이 만든 뒤 바꾸지 않으므로 복사하지 않고 같이 본다). 아직 없으면 null.</summary>
    public Planet.Planet? Planet;

    // 점마다 Dot.History 칸
    public int[] ColorIndex = Array.Empty<int>();
    public int[] LegCount = Array.Empty<int>();
    public Leg[] Legs = Array.Empty<Leg>(); // [dot * History + k], k=0 이 가장 최근

    // ── 사람(E3) ──
    public int PeopleCount;
    public PersonView[] People = Array.Empty<PersonView>();
    /// <summary>사람이 사는 칸과 살아 있는 사람 수(칸 번호 순).</summary>
    public (int Cell, int Alive)[] CellPeople = Array.Empty<(int, int)>();
    /// <summary>한 번이라도 사람이 산 칸(열린 칸).</summary>
    public int[] OpenedCells = Array.Empty<int>();
    public WorldEvent[] RecentEvents = Array.Empty<WorldEvent>();
    /// <summary>(E4) 최근의 큰 일(★★ 이상) 30개.</summary>
    public WorldEvent[] BigEvents = Array.Empty<WorldEvent>();
    public int AliveCount;
    public int PeopleCap;
    /// <summary>사람이 들어가 걷힌 타일(칸 → 타일 번호들). 바뀔 때만 새 배열.</summary>
    public Dictionary<int, int[]> Revealed = new();

    // ── (E3b) 지방 넘기 · 지명 · 말 ──
    /// <summary>최근에 경계를 넘은 일(오래된 것부터). 바뀔 때만 새 배열.</summary>
    public Crossing[] RecentCrossings = Array.Empty<Crossing>();
    /// <summary>땅 이름(칸 → 이름들, 처음 이름이 맨 앞). 바뀔 때만 새 사전.</summary>
    public Dictionary<int, LandNameView[]> LandNames = new();
    /// <summary>사람 사는 칸마다 가장 많이 쓰는 말과 그 말을 쓰는 사람 수, 그 칸 사람 수(칸 번호 순).</summary>
    public (int Cell, int LangId, int Speakers, int Alive)[] CellLanguages = Array.Empty<(int, int, int, int)>();
    /// <summary>지금 쓰이는 말들(사본 — 화면이 낱말 · 알아듣기를 셈할 수 있게). 바뀔 때만 새 배열.</summary>
    public LanguageView[] Languages = Array.Empty<LanguageView>();
    private long _langSig = -1, _nameSig = -1;

    // ── (E4) 재료와 발견 ──
    /// <summary>깨어 있는 지방의 불(칸 → 불들). 해마다가 아니라 매번 새로 — 몇 개 안 된다.</summary>
    public Dictionary<int, FireView[]> Fires = new();
    /// <summary>땅에 놓인 물건(칸 → (타일, 아이템, 개수)). 바뀔 때만 새 사전.</summary>
    public Dictionary<int, (int Tile, int Item, int Count)[]> GroundItems = new();
    /// <summary>캐낸 바위 타일(칸 → 타일들). 바뀔 때만 새 배열.</summary>
    public Dictionary<int, int[]> Dug = new();
    /// <summary>발견 목록(세계에서 처음 나온 순서). 바뀔 때만 새 배열.</summary>
    public DiscoveryView[] Discoveries = Array.Empty<DiscoveryView>();
    /// <summary>아직 발견되지 않은 것의 부분 성공(규칙 이름, 횟수, 글).</summary>
    public (string Rule, int Count, string[] Texts)[] PendingPartials = Array.Empty<(string, int, string[])>();
    private long _discSig = -1, _itemSig = -1;
    // ── (E5) 지식 ──
    /// <summary>남아 있는 그림(칸 → 그림들). 바뀔 때만 새 사전.</summary>
    public Dictionary<int, PaintingView[]> Paintings = new();
    // ── (E5b) 짐승 ──
    /// <summary>깨어 있는 지방의 짐승 무리(칸 → 무리들, 번호 순). 무리 수가 같으면 같은 배열을 다시 채운다.</summary>
    public Dictionary<int, AnimalView[]> Animals = new();
    /// <summary>깨어 있는 지방마다 종별로 살 수 있는 마릿수 · 물고기 몫 · 물고기 한도.</summary>
    public Dictionary<int, (int[] Caps, int Fish, int FishCap)> AnimalLand = new();

    public PaintingView? PaintingById(int id) { foreach (var (_, l) in Paintings) foreach (var g in l) if (g.Id == id) return g; return null; }

    /// <summary>"타레(빠른 물)" — 이름이 없으면 null.</summary>
    public string? LandLabel(int cell) => LandNames.TryGetValue(cell, out var l) && l.Length > 0 ? $"{l[0].Word}({l[0].Meaning})" : null;

    public LanguageView? LanguageOf(int id) { foreach (var l in Languages) if (l.Id == id) return l; return null; }

    internal void Fill(GameWorld w, long epoch, double daysPerSecond, double load)
    {
        if (epoch != Epoch) Revealed = new();
        FillPeople(w);
        Epoch = epoch;
        Now = w.Now;
        Seed = w.Seed;
        MapUnits = w.Dots.MapUnits;
        EventsProcessed = w.EventsProcessed;
        SimDaysPerSecond = daysPerSecond;
        SimLoad = load;
        Config = w.Config;
        Planet = w.HasPlanet ? w.Planet : null;
        var dots = w.Dots.Dots;
        DotCount = dots.Count;
        if (ColorIndex.Length < DotCount)
        {
            int cap = Math.Max(DotCount, ColorIndex.Length * 2);
            ColorIndex = new int[cap];
            LegCount = new int[cap];
            var legs = new Leg[cap * Dot.History];
            for (int i = 0; i < legs.Length; i++) legs[i] = i < Legs.Length ? Legs[i] : new Leg();
            Legs = legs;
        }
        for (int i = 0; i < DotCount; i++)
        {
            var d = dots[i];
            ColorIndex[i] = d.ColorIndex;
            LegCount[i] = d.LegCount;
            for (int k = 0; k < d.LegCount; k++) Legs[i * Dot.History + k].CopyFrom(d.LegAgo(k));
        }
    }

    private void FillPeople(GameWorld w)
    {
        var all = w.People.All;
        PeopleCount = all.Count;
        AliveCount = w.People.AliveCount;
        PeopleCap = w.Config.PeopleCap;
        if (People.Length < PeopleCount)
        {
            var arr = new PersonView[Math.Max(PeopleCount, People.Length * 2)];
            for (int i = 0; i < arr.Length; i++) arr[i] = i < People.Length ? People[i] : new PersonView();
            People = arr;
        }
        for (int i = 0; i < PeopleCount; i++)
        {
            var p = all[i];
            var v = People[i];
            v.Fill(p, w.Now);
            v.Name = w.People.DisplayName(p);
            v.Named = p.Name.Length > 0;
            if (v.Talking && p.TalkConcept >= 0) { v.Say = w.People.WordOf(p, p.TalkConcept); v.SayMeaning = w.People.Lang.Concepts[p.TalkConcept].Meaning; }
            else if (v.Teaching && w.People.TeachWord(p) is { } tw) { v.Say = tw.Word; v.SayMeaning = tw.Meaning; }
            else { v.Say = ""; v.SayMeaning = ""; }
        }
        var counts = new List<(int, int)>();
        var opened = new List<int>();
        foreach (var (cell, life) in w.People.Provinces)
        {
            int alive = 0, ever = 0;
            foreach (var p in life.People) { ever++; if (!p.Dead) alive++; }
            if (ever > 0) opened.Add(cell);
            if (alive > 0) counts.Add((cell, alive));
        }
        foreach (var (cell, life) in w.People.Provinces)
            if (life.Revealed.Count > 0 && (!Revealed.TryGetValue(cell, out var old) || old.Length != life.Revealed.Count)) Revealed[cell] = life.Revealed.ToArray();
        foreach (var (cell, d) in w.People.Dormant)
            if (d.People.Count > 0 && !opened.Contains(cell)) opened.Add(cell);
        opened.Sort();
        foreach (var (cell, d) in w.People.Dormant)
            if (d.Revealed.Length > 0 && (!Revealed.TryGetValue(cell, out var old) || old.Length != d.Revealed.Length)) Revealed[cell] = d.Revealed.ToArray();
        CellPeople = counts.ToArray();
        OpenedCells = opened.ToArray();
        FillLanguages(w);
        var ev = w.People.Events;
        int n = Math.Min(30, ev.Count);
        if (RecentEvents.Length != n || (n > 0 && RecentEvents[^1].Seq != ev[^1].Seq)) RecentEvents = ev.Skip(ev.Count - n).ToArray();
        // (E4) 큰 일(★★ · ★★★)은 따로 — 빠른 배속에서 태어남 · 죽음에 밀려 빠지지 않게
        if (ev.Count > 0 && (BigEvents.Length == 0 || ev[^1].Seq != _bigSeq))
        {
            _bigSeq = ev[^1].Seq;
            var big = new List<WorldEvent>();
            for (int i = ev.Count - 1; i >= 0 && big.Count < 30; i--) if (EventLevels.Of(ev[i].Kind) >= EventLevels.Group) big.Add(ev[i]);
            big.Reverse();
            BigEvents = big.ToArray();
        }
    }

    private long _bigSeq = -1, _paintSig = -1;

    private void FillLanguages(GameWorld w)
    {
        var ps = w.People;
        var cr = ps.Crossings;
        if (RecentCrossings.Length != cr.Count || (cr.Count > 0 && !RecentCrossings[^1].Equals(cr[^1]))) RecentCrossings = cr.ToArray();
        // 땅 이름: 이름 수가 바뀔 때만
        long nsig = 0; foreach (var (c, l) in ps.LandNames) nsig += c * 31 + l.Count;
        if (nsig != _nameSig)
        {
            _nameSig = nsig;
            var d = new Dictionary<int, LandNameView[]>();
            foreach (var (c, l) in ps.LandNames)
                d[c] = l.Select(n => new LandNameView(n.LangId, n.Word, ps.LandMeaning(n), n.Reason, n.Tick, ps.DisplayName(ps.Get(n.Namer)))).ToArray();
            LandNames = d;
        }
        // 칸마다 가장 많이 쓰는 말
        var cl = new List<(int, int, int, int)>();
        var live = new SortedDictionary<int, int>();
        foreach (var (cell, life) in ps.Provinces)
        {
            var cnt = new SortedDictionary<int, int>();
            int alive = 0;
            foreach (var p in life.People)
            {
                if (p.Dead) continue;
                alive++;
                cnt[p.LangId] = cnt.GetValueOrDefault(p.LangId) + 1;
                live[p.LangId] = live.GetValueOrDefault(p.LangId) + 1;
            }
            if (alive == 0) continue;
            var top = cnt.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).First();
            cl.Add((cell, top.Key, top.Value, alive));
        }
        CellLanguages = cl.ToArray();
        FillCraft(w);
        FillAnimals(w);
        long sig = live.Count;
        foreach (var (id, n) in live) if (ps.Languages.TryGetValue(id, out var l)) sig = sig * 1_000_003 + id * 7919 + l.Version * 31 + n;
        if (sig != _langSig)
        {
            _langSig = sig;
            var arr = new List<LanguageView>();
            foreach (var (id, n) in live)
            {
                if (!ps.Languages.TryGetValue(id, out var l)) continue;
                var copy = new Language { Id = l.Id, Seed = l.Seed, Parent = l.Parent, Born = l.Born, Length = l.Length, Sounds = (int[])l.Sounds.Clone(), Changes = l.Changes };
                foreach (int k in l.Known) copy.Known.Add(k);
                foreach (var (k, v) in l.Overrides) copy.Overrides[k] = v;
                foreach (var (k, v) in l.Loans) copy.Loans[k] = v;
                int depth = 0; int cur = l.Parent;
                while (cur >= 0 && ps.Languages.TryGetValue(cur, out var par) && depth < 1000) { depth++; cur = par.Parent; }
                arr.Add(new LanguageView(id, l.Parent, ps.RootLanguage(id), depth, n, copy, ps.LanguageLabel(id)));
            }
            Languages = arr.ToArray();
        }
    }

    private void FillAnimals(GameWorld w)
    {
        var ps = w.People;
        List<int>? gone = null;
        foreach (var (cell, _) in Animals) if (!ps.Provinces.ContainsKey(cell)) (gone ??= new()).Add(cell);
        if (gone is not null) foreach (int c in gone) { Animals.Remove(c); AnimalLand.Remove(c); }
        foreach (var (cell, life) in ps.Provinces)
        {
            int n = life.Herds.Count;
            if (!Animals.TryGetValue(cell, out var arr) || arr.Length != n)
            {
                var na = new AnimalView[n];
                for (int i = 0; i < n; i++) na[i] = arr is not null && i < arr.Length ? arr[i] : new AnimalView();
                Animals[cell] = arr = na;
            }
            int k = 0;
            foreach (var (_, h) in life.Herds) arr[k++].Fill(h);
            if (!AnimalLand.TryGetValue(cell, out var land) || land.Caps != life.AnimalCap || land.Fish != life.FishStock) AnimalLand[cell] = (life.AnimalCap, life.FishStock, life.FishCap);
        }
    }

    private void FillCraft(GameWorld w)
    {
        var ps = w.People;
        var fires = new Dictionary<int, FireView[]>();
        long isig = 0;
        foreach (var (cell, life) in ps.Provinces)
        {
            if (life.Fires.Count > 0 || life.VolcanoFireTiles.Count > 0)
            {
                var list = new List<FireView>();
                foreach (var (t, f) in life.Fires) list.Add(new FireView(t, f.Kind, f.Lit(w.Now), (float)(Math.Max(0, f.Remaining(w.Now)) / (double)Time.GameTime.TicksPerHour), f.Charcoal));
                fires[cell] = list.ToArray();
            }
            foreach (var (t, l) in life.Items) foreach (var (item, n) in l) isig = isig * 31 + cell * 7 + t * 13 + item * 3 + n;
            isig = isig * 17 + life.Dug.Count;
        }
        Fires = fires;
        if (isig != _itemSig)
        {
            _itemSig = isig;
            var gi = new Dictionary<int, (int, int, int)[]>();
            var dug = new Dictionary<int, int[]>();
            foreach (var (cell, life) in ps.Provinces)
            {
                if (life.Items.Count > 0) gi[cell] = life.Items.SelectMany(kv => kv.Value.Select(c => (kv.Key, c.Item, c.Count))).ToArray();
                if (life.Dug.Count > 0) dug[cell] = life.Dug.ToArray();
            }
            GroundItems = gi; Dug = dug;
        }
        var st = ps.Stats;
        long psig = ps.NextPaintingId * 7919L + ps.Paintings.Count * 31 + ps.Paintings.Values.Sum(g => (long)g.Seen + g.Taught * 3) + w.Now / Time.GameTime.TicksPerDay;
        if (psig != _paintSig)
        {
            _paintSig = psig;
            var pd = new Dictionary<int, List<PaintingView>>();
            foreach (var (_, g) in ps.Paintings)
            {
                if (!g.Alive(w.Now)) continue;
                if (!pd.TryGetValue(g.Cell, out var l)) pd[g.Cell] = l = new List<PaintingView>();
                l.Add(new PaintingView(g.Id, g.Cell, g.Tile, g.Stand, g.Rule, g.Rule >= 0 ? ps.Craft.ItemName(ps.Craft.Rules[g.Rule].Result) : "알아볼 수 없는 무늬", g.Rule >= 0 ? ps.Craft.Rules[g.Rule].Name : "",
                    g.Medium, ps.DisplayName(ps.Get(g.Painter)), g.Tick, (int)((g.Until - w.Now) / Time.GameTime.TicksPerYear), g.Seen, g.Taught, ps.LandLabelOr(g.Cell, "이름 없는 땅")));
            }
            Paintings = pd.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
        }
        long dsig = ps.Discoveries.Count * 1_000_003L + st.Discoveries * 7919 + st.Watched * 31 + st.Lost * 13 + st.Refound * 17 + ps.Partials.Count * 5 + ps.Partials.Sum(kv => kv.Value.Count) + w.Now / Time.GameTime.TicksPerDay * 3;
        if (dsig == _discSig) return;
        _discSig = dsig;
        var craft = ps.Craft;
        Discoveries = ps.Discoveries.Values.OrderBy(d => d.Tick).ThenBy(d => d.Rule).Select(d =>
        {
            var r = craft.Rules[d.Rule];
            var who = ps.Get(d.Person);
            string word = "", meaning = "";
            if (ps.ConceptForItem(craft.Catalog.Items[r.Result].Id) is { } cid && ps.Lang.ConceptIndex(cid) is int c && c >= 0 && ps.Languages.TryGetValue(d.LangId, out var lang) && lang.Known.Contains(c))
            { word = Words.Word(lang, null, ps.Lang.Concepts[c], ps.Lang); meaning = ps.Lang.Concepts[c].Meaning; }
            string origin = "";
            if (word.Length > 0 && ps.Languages.TryGetValue(d.LangId, out var dl) && ps.ConceptForItem(craft.Catalog.Items[r.Result].Id) is { } cid2 && ps.Lang.ConceptIndex(cid2) is int c2 && c2 >= 0) origin = ps.WordOrigin(dl, c2);
            // (E5) 지금 누가 아나(무리 · 아는 정도) · 무리마다 아는 사람 수 · 그림 · 역사 · 다른 말들의 낱말(빌려 온 곳)
            var people = new List<KnowerView>();
            var cells = new SortedDictionary<int, int>();
            foreach (var q in ps.All)
            {
                if (q.Dead) continue;
                int ki = q.KnownIndex(d.Rule);
                if (ki < 0) continue;
                var k = q.Known[ki];
                cells[q.Cell] = cells.GetValueOrDefault(q.Cell) + 1;
                people.Add(new KnowerView(q.Id, ps.DisplayName(q), q.Cell, ps.LandLabelOr(q.Cell, "이름 없는 땅"), k.Level, k.Via switch { Wtw.Sim.People.Via.Teach => "배움", Wtw.Sim.People.Via.Watch => "보고 배움", Wtw.Sim.People.Via.Painting => "그림", _ => "스스로" }, q.AgeYears(w.Now)));
            }
            var pics = ps.Paintings.Values.Where(g => g.Rule == d.Rule && g.Alive(w.Now)).Select(g => $"{ps.LandLabelOr(g.Cell, "이름 없는 땅")}의 {Wtw.Sim.People.Painting.MediumName(g.Medium)} — {ps.DisplayName(ps.Get(g.Painter))}, {Time.GameTime.Format(g.Tick)} (본 사람 {g.Seen}번 · 그림으로 배운 사람 {g.Taught}명 · 앞으로 약 {(g.Until - w.Now) / Time.GameTime.TicksPerYear}년)").ToArray();
            var hist = ps.KnowHistory.TryGetValue(d.Rule, out var hl) ? hl.Select(e => (e.Tick, e.Kind, e.Text)).ToArray() : Array.Empty<(long, byte, string)>();
            int groups = 0, groupsLost = 0;
            foreach (var (key, gk) in ps.GroupKnows) if (Wtw.Sim.People.PeopleSim.GroupRule(key) == d.Rule) { if (gk.Known) groups++; else groupsLost++; }
            var words = new List<string>();
            if (ps.ConceptForItem(craft.Catalog.Items[r.Result].Id) is { } cid3 && ps.Lang.ConceptIndex(cid3) is int c3 && c3 >= 0)
                foreach (var (lid, lang2) in ps.Languages)
                    if (lang2.Known.Contains(c3) && lid != d.LangId && ps.Speakers(lid) > 0 && words.Count < 6) words.Add($"'{Words.Word(lang2, null, ps.Lang.Concepts[c3], ps.Lang)}'({ps.LanguageLabel(lid)} — {ps.WordOrigin(lang2, c3)})");
            return new DiscoveryView(d.Rule, craft.ItemName(r.Result), r.Name, craft.ActionName(r.Action), d.Person, ps.DisplayName(who) + (who is { Nickname.Length: > 0 } ? $" '{who.Nickname}'" : ""), d.Cell,
                ps.LandLabelOr(d.Cell, Wtw.Sim.Planet.PlanetNames.Of(w.Planet.Biome[d.Cell]) + " 땅"), d.Tick, ps.WishName(d.Wish), d.PartialsBefore, d.PartialTexts.ToArray(),
                ps.Knowers(d.Rule), d.Lost, d.LostCount, d.RefoundCount, word, meaning, r.Heat > 0 ? craft.HeatName(r.Heat) : "")
            {
                People = people.OrderByDescending(x => x.Level).ThenBy(x => x.Id).ToArray(), Cells = cells.Select(kv => (kv.Key, kv.Value)).ToArray(), Paintings = pics, History = hist,
                Groups = groups, GroupsLost = groupsLost, WordOrigin = origin, OtherWords = words.ToArray(),
            };
        }).ToArray();
        PendingPartials = ps.Partials.Select(kv => (craft.Rules[kv.Key].Name, kv.Value.Count, kv.Value.Texts.ToArray())).ToArray();
    }

    /// <summary>점 i 의 tick 시점 위치(밀리타일, 실수). 출발이 tick 이하인 가장 최근 구간을 쓴다.</summary>
    public void DotPosition(int i, double tick, out double x, out double y)
    {
        int n = LegCount[i];
        int baseIdx = i * Dot.History;
        for (int k = 0; k < n; k++)
        {
            var leg = Legs[baseIdx + k];
            if (leg.Depart <= tick || k == n - 1)
            {
                leg.PositionAt(tick, out x, out y);
                return;
            }
        }
        x = y = 0;
    }
}

/// <summary>(E4) 땅 위의 불 하나(화면용): 타일 · 종류(1 모닥불 · 2 돌 화덕) · 켜졌나 · 남은 시간 · 숯을 넣었나.</summary>
public readonly record struct FireView(int Tile, byte Kind, bool Lit, float HoursLeft, bool Charcoal);

/// <summary>(E4) 발견 목록 한 줄(화면용). (E5) 지식의 행방: 아는 사람(무리 · 아는 정도) · 칸마다 아는 사람 수 · 남은 그림 · 역사 · 아는 무리 수 · 끊긴 무리 수 · 낱말의 출처 · 다른 말의 낱말.</summary>
public sealed record DiscoveryView(int Rule, string Item, string RuleName, string Action, int Person, string PersonName, int Cell, string Place, long Tick, string Wish,
    int PartialsBefore, string[] PartialTexts, int Knowers, bool Lost, int LostCount, int RefoundCount, string Word, string WordMeaning, string Heat)
{
    public KnowerView[] People { get; init; } = Array.Empty<KnowerView>();
    public (int Cell, int Count)[] Cells { get; init; } = Array.Empty<(int, int)>();
    public string[] Paintings { get; init; } = Array.Empty<string>();
    public (long Tick, byte Kind, string Text)[] History { get; init; } = Array.Empty<(long, byte, string)>();
    public int Groups { get; init; }
    public int GroupsLost { get; init; }
    public string WordOrigin { get; init; } = "";
    public string[] OtherWords { get; init; } = Array.Empty<string>();
}

/// <summary>(E5) 아는 사람 한 명(화면용): 번호 · 이름 · 사는 칸 · 땅 이름 · 아는 정도 · 어떻게 알았나 · 나이.</summary>
public readonly record struct KnowerView(int Id, string Name, int Cell, string Place, int Level, string How, int Age);

/// <summary>(E5) 그림 하나(화면용): 벽 타일 · 서서 보는 타일 · 무엇을 · 어떻게(규칙 이름) · 매체 · 그린 사람 · 언제 · 앞으로 몇 해 · 본 횟수 · 배운 사람 수 · 땅 이름.</summary>
public sealed record PaintingView(int Id, int Cell, int Tile, int Stand, int Rule, string What, string How, byte Medium, string Painter, long Tick, int YearsLeft, int Seen, int Taught, string Place);

/// <summary>땅 이름 하나(화면용 사본).</summary>
public sealed record LandNameView(int LangId, string Word, string Meaning, string Reason, long Tick, string Namer);

/// <summary>말 하나(화면용 사본): 번호 · 갈라져 나온 말 · 뿌리 · 몇 번 갈라졌나 · 쓰는 사람 수 · 말 자체(낱말 셈용 사본) · 이름.</summary>
public sealed record LanguageView(int Id, int Parent, int Root, int Depth, int Speakers, Language Lang, string Label)
{
    /// <summary>다른 말과 얼마나 통하나(0~100) — 시뮬의 LanguageUnderstanding 과 같은 셈(말씨 없이).</summary>
    public int Understanding(LanguageView other)
    {
        if (other.Id == Id) return 100;
        var d = LanguageData.Default;
        int sum = 0, n = 0;
        foreach (var c in d.Concepts)
        {
            if (!c.Base) continue;
            sum += Words.Similarity(Words.Word(Lang, null, c, d), Words.Word(other.Lang, null, c, d));
            n++;
        }
        return n == 0 ? 0 : sum / n;
    }
}

/// <summary>(E5b) 짐승 무리 하나(화면용 사본): 번호 · 종 · 마릿수 · 하는 일 · 노리는 것 · 따르는 사람 · 친해진 정도 · 움직임(지금 · 바로 전).</summary>
public sealed class AnimalView
{
    public int Id, Species, Count, Target = -1, Owner = -1, Bond;
    public byte TargetKind;
    public HerdState State;
    public readonly Motion Motion = new(), Prev = new();

    internal void Fill(Herd h)
    {
        Id = h.Id; Species = h.Species; Count = h.Count; Target = h.Target; TargetKind = h.TargetKind; Owner = h.Owner; Bond = h.Bond; State = h.State;
        Motion.CopyFrom(h.Motion); Prev.CopyFrom(h.Prev);
    }

    /// <summary>tick 시점 위치(밀리타일)와 걷는 방향.</summary>
    public void PositionAt(double tick, out double x, out double y, out double dx, out double dy)
    {
        if (Motion.Depart <= tick || Prev.Count == 0) Motion.PositionAt(tick, out x, out y, out dx, out dy);
        else Prev.PositionAt(tick, out x, out y, out dx, out dy);
    }
}

/// <summary>화면이 사람 하나를 그리는 데 필요한 것(사본).</summary>
public sealed class PersonView
{
    public int Id, Cell, Carrier, AgeYears;
    public long DeathTick;
    public string Name = "";
    public bool Dead, Female, Asleep, Walking, Breaking, Talking, Eaten, Named;
    /// <summary>(E4) 손에 든 도구(아이템 번호, −1 = 없음) · 들고 있는 그릇 · 불씨 · 실험 중 · 별명.</summary>
    public int Tool = -1, Vessel = -1;
    public bool Ember, Working;
    /// <summary>(E5) 가르치는 중 · 배우는 중 · 그림을 보는 중 · 그리는 중.</summary>
    public bool Teaching, Learning, Studying, Painting;
    /// <summary>(E5b) 입은 것(몸 · 발, 아이템 번호 −1 = 없음) · 다쳤나 · 하는 일의 대상(사냥이면 무리 번호).</summary>
    public int Worn = -1, WornFeet = -1, Target = -1;
    public bool Injured;
    public string Nickname = "";
    private static int _ember = -1;
    /// <summary>말풍선: 그 사람의 말로 된 낱말과 뜻(이야기할 때만).</summary>
    public string Say = "", SayMeaning = "";
    public LifeStage Stage;
    public Act Act;
    public Appearance Look;
    public int MotionCount;
    public readonly Motion[] Motions = { new(), new(), new(), new() }; // k = 0 이 가장 최근

    internal void Fill(Person p, long now)
    {
        Id = p.Id; Cell = p.Cell; Carrier = p.Carrier;
        DeathTick = p.DeathTick;
        Dead = p.Dead; Female = p.Female; Asleep = p.Asleep; Walking = p.Walking; Eaten = p.Eaten;
        long at = p.Dead ? p.DeathTick : now;
        AgeYears = p.AgeYears(at); Stage = p.Stage(at);
        Act = p.Act; Look = p.Look;
        Breaking = p.Act is Act.BreakArgue or Act.BreakLeave or Act.BreakWander;
        Talking = (p.Act == Act.Talk || p.Act == Act.BreakArgue) && !p.Walking;
        MotionCount = p.MotionCount;
        for (int k = 0; k < MotionCount; k++) Motions[k].CopyFrom(p.MotionAgo(k));
        // (E4) 든 것: 날 · 단단함이 가장 좋은 만든 도구, 가장 큰 그릇
        Tool = -1; Vessel = -1; Ember = false;
        var craft = Data.CraftData.Default;
        if (_ember < 0) _ember = craft.ItemIndex("ember");
        int bt = 0, bv = 0;
        foreach (var (item, _) in p.Carry)
        {
            if (craft.Catalog.Items[item].Kind != "made") { continue; }
            if (item == _ember) { Ember = true; continue; }
            int t = craft.Prop(item, craft.P_sharp) * 2 + craft.Prop(item, craft.P_hardness);
            if (craft.Prop(item, craft.P_sharp) > 0 && t > bt) { bt = t; Tool = item; }
            int v = craft.Prop(item, craft.P_holds);
            if (v > bv) { bv = v; Vessel = item; }
        }
        Working = !p.Walking && p.Act is Act.Experiment or Act.Craft or Act.Mine or Act.Cook or Act.TendFire;
        Teaching = !p.Walking && p.Act == Act.Teach; Learning = p.Act == Act.Learn; Studying = !p.Walking && p.Act == Act.Study;
        Painting = !p.Walking && p.Act is Act.Experiment or Act.Craft && p.PlanAction == Data.CraftData.Default.ActionIndex("draw");
        Nickname = p.Nickname;
        Worn = p.Worn; WornFeet = p.WornFeet; Injured = p.Injury > 15_000; Target = p.Target;
    }

    /// <summary>tick 시점 위치(밀리타일)와 걷는 방향. 출발이 tick 이하인 가장 최근 구간.</summary>
    public void PositionAt(double tick, out double x, out double y, out double dx, out double dy)
    {
        for (int k = 0; k < MotionCount; k++)
        {
            var m = Motions[k];
            if (m.Depart <= tick || k == MotionCount - 1) { m.PositionAt(tick, out x, out y, out dx, out dy); return; }
        }
        x = y = dx = dy = 0;
    }
}

/// <summary>
/// 세 칸 버퍼(triple buffer). 시뮬은 뒤 칸에 쓰고 바꿔 놓기만, 화면은 최신 칸을 가져가기만 한다.
/// 잠금이 없어 어느 쪽도 상대를 기다리지 않는다.
/// </summary>
public sealed class SnapshotBuffer
{
    private readonly WorldSnapshot[] _slots = { new(), new(), new() };
    private int _back = 0, _front = 1;
    private int _middle = 2;          // 하위 2비트 = 칸 번호, 4 = 새것 표시
    private const int FreshBit = 4;

    /// <summary>시뮬 스레드: 쓸 칸.</summary>
    public WorldSnapshot Back => _slots[_back];

    /// <summary>시뮬 스레드: 다 쓴 칸을 내놓는다.</summary>
    public void Publish()
    {
        int old = Interlocked.Exchange(ref _middle, _back | FreshBit);
        _back = old & 3;
    }

    /// <summary>화면 스레드: 최신 칸 (새것이 없으면 지난번 것).</summary>
    public WorldSnapshot Acquire()
    {
        if ((Volatile.Read(ref _middle) & FreshBit) != 0)
        {
            int old = Interlocked.Exchange(ref _middle, _front);
            _front = old & 3;
        }
        return _slots[_front];
    }
}
