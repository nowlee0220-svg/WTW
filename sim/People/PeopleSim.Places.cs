using Wtw.Sim.Core;
using Wtw.Sim.Planet;
using Wtw.Sim.Time;

namespace Wtw.Sim.People;

/// <summary>땅 이름 하나: 그 말(LangId)로 지은 낱말 · 뜻(뜻 번호들) · 언제 · 누가 · 왜(무엇을 보고).</summary>
public sealed record LandName(int LangId, string Word, int[] Concepts, long Tick, int Namer, string Reason);

/// <summary>
/// (E3b) 지명. 사람들이 한 지방에 오래 머물거나(5일) 그 땅의 눈에 띄는 것(큰 강 · 화산 · 바다 · 호수 · 동굴 · 산)을 보면(하루)
/// 자기 말로 그 땅에 이름을 붙인다. 낱말은 처음 낱말 24개로만 짓는다(새 낱말은 E4): 큰 강 = "빠른 물", 바다 = "큰 물", 화산 = "붉은 돌" …
/// 말이 다른 무리는 같은 땅을 다르게 부른다 — 화면에는 처음 이름을 붙인 무리의 이름을 보이고, 다른 이름은 칸 정보에 같이 적는다.
/// 하루 경계에서 지방 번호 순 · 말 번호 순으로 정한다(결정론).
/// </summary>
public sealed partial class PeopleSim
{
    public readonly SortedDictionary<int, List<LandName>> LandNames = new();
    public const long NameStayTicks = 5 * GameTime.TicksPerDay, NameFeatureTicks = GameTime.TicksPerDay;

    /// <summary>그 땅의 (처음) 이름 — 없으면 null.</summary>
    public LandName? LandNameOf(int cell) => LandNames.TryGetValue(cell, out var l) && l.Count > 0 ? l[0] : null;

    public string LandMeaning(LandName n) => string.Join(" ", n.Concepts.Where(c => (uint)c < Lang.Concepts.Count).Select(c => Lang.Concepts[c].Meaning));

    /// <summary>"타레(빠른 물)" — 이름이 없으면 "이름 없는 땅".</summary>
    public string LandLabel(int cell) => LandNameOf(cell) is { } n ? $"{n.Word}({LandMeaning(n)})" : "이름 없는 땅";

    /// <summary>이름이 있으면 "타레(빠른 물)", 없으면 fallback.</summary>
    public string LandLabelOr(int cell, string fallback) => LandNameOf(cell) is { } n ? $"{n.Word}({LandMeaning(n)})" : fallback;

    /// <summary>그 사람의 말로 그 땅을 뭐라고 부르나(없으면 처음 이름).</summary>
    public LandName? LandNameFor(int cell, int langId)
    {
        if (!LandNames.TryGetValue(cell, out var l) || l.Count == 0) return null;
        foreach (var n in l) if (n.LangId == langId) return n;
        return l[0];
    }

    internal void AddLoadedLandName(int cell, LandName n)
    {
        if (!LandNames.TryGetValue(cell, out var l)) LandNames[cell] = l = new List<LandName>();
        l.Add(n);
    }

    /// <summary>하루 경계: 깨어 있는 지방마다 아직 그 땅을 부를 이름이 없는 말 무리가 이름을 붙인다.</summary>
    private void NameLands()
    {
        foreach (var (cell, life) in Provinces)
        {
            // 말마다 그 땅에 가장 오래 머문 어른
            SortedDictionary<int, Person>? namers = null;
            foreach (var q in life.People)
            {
                if (q.Dead || q.Stage(Now) < LifeStage.Adult || q.LangId < 0) continue;
                namers ??= new SortedDictionary<int, Person>();
                if (!namers.TryGetValue(q.LangId, out var cur) || q.ArrivedTick < cur.ArrivedTick) namers[q.LangId] = q;
            }
            if (namers is null) continue;
            foreach (var (langId, namer) in namers)
            {
                if (LandNames.TryGetValue(cell, out var have) && have.Exists(n => n.LangId == langId)) continue;
                long stay = Now - namer.ArrivedTick;
                var feature = Feature(cell, life);
                if (stay < NameStayTicks && (feature.Reason.Length == 0 || stay < NameFeatureTicks)) continue;
                NameLand(cell, life, namer, feature);
            }
        }
    }

    /// <summary>그 땅의 눈에 띄는 것: (뜻 후보 — 꾸밈말들, 이름말, 까닭). 없으면 까닭이 빈칸(생물군계로 짓는다).</summary>
    private (string[] Adjs, string Noun, string Reason) Feature(int cell, ProvinceLife life)
    {
        var p = _w.Planet;
        if (Volcanoes.IsVolcano(p, cell)) return (new[] { "red", "black" }, "stone", "화산을 보고");
        if (p.RiverSize[cell] >= 2) return (new[] { "fast", "big" }, "water", "큰 강을 보고");
        if ((p.Flags[cell] & CellFlags.Coast) != 0) return (new[] { "big" }, "water", "바다를 보고");
        if ((p.Flags[cell] & CellFlags.LakeShore) != 0) return (new[] { "green", "white" }, "water", "호수를 보고");
        if (life.CaveTiles > 0 && life.Revealed.Count > 0) return (new[] { "night", "black" }, "stone", "동굴을 보고");
        if (p.Terrain[cell] is Terrain.Mountain or Terrain.HighMountain) return (new[] { "big", "white" }, "stone", "높은 산을 보고");
        return (Array.Empty<string>(), "", "");
    }

    private void NameLand(int cell, ProvinceLife life, Person namer, (string[] Adjs, string Noun, string Reason) feature)
    {
        if (!Languages.TryGetValue(namer.LangId, out var lang)) return;
        var rng = Rng(cell);
        string reason = feature.Reason;
        int[] Pick()
        {
            if (feature.Noun.Length > 0) return new[] { C(feature.Adjs[rng.NextInt(feature.Adjs.Length)]), C(feature.Noun) };
            // 눈에 띄는 것이 없으면 그 땅의 모습(생물군계)과 그곳에서 있었던 일(죽음 = 위험, 태어남 = 새벽)
            var (adjs, noun) = _w.Planet.Biome[cell] switch
            {
                Biome.Rainforest or Biome.Temperate or Biome.Boreal => (new[] { "green", "big", "black" }, "tree"),
                Biome.Desert => (new[] { "yellow", "white" }, "sun"),
                Biome.Savanna => (new[] { "yellow", "red" }, "sun"),
                Biome.Grassland => (new[] { "green", "yellow" }, "sun"),
                Biome.Wetland => (new[] { "black", "green" }, "water"),
                Biome.Tundra or Biome.Ice => (new[] { "white" }, "stone"),
                Biome.Alpine => (new[] { "white", "big" }, "stone"),
                _ => (new[] { "big", "small" }, "stone"),
            };
            string adj = adjs[rng.NextInt(adjs.Length)];
            bool died = false, born = false;
            foreach (var q in life.People)
            {
                if (q.LangId != namer.LangId) continue;
                if (q.Dead && q.DeathTick > namer.ArrivedTick) died = true;
                if (!q.Dead && q.BirthTick > namer.ArrivedTick) born = true;
            }
            if (died && rng.NextInt(100) < 40) { adj = "danger"; reason = "사람이 죽은 땅이라"; }
            else if (born && rng.NextInt(100) < 40) { adj = "dawn"; reason = "아이가 태어난 땅이라"; }
            else reason = "오래 머문 땅이라";
            return new[] { C(adj), C(noun) };
        }
        int[] concepts = Pick();
        string word = Words.Name(lang, namer.Accent, concepts.Select(c => Lang.Concepts[c]).ToList(), Lang);
        for (int tries = 0; tries < 8 && (LandWordTaken(word, namer.LangId) || Lang.IsBanned(word)); tries++)
        {
            concepts = Pick();
            word = Words.Name(lang, namer.Accent, concepts.Select(c => Lang.Concepts[c]).ToList(), Lang);
        }
        var n = new LandName(namer.LangId, word, concepts, Now, namer.Id, reason);
        AddLoadedLandName(cell, n);
        AddBio(namer, BioKind.NamedLand, -1, cell);
        bool first = LandNames[cell].Count == 1;
        Notify(WorldEventKind.LandNamed, namer, -1,
            $"{Hangul.I(DisplayName(namer))} 이 땅을 {Hangul.Rago($"'{word}'")} 부르기 시작했습니다 — 뜻: {LandMeaning(n)} ({reason})" + (first ? "" : $" · 다른 무리는 {Hangul.Rago($"'{LandNames[cell][0].Word}'")} 부른다"));
    }

    private bool LandWordTaken(string word, int langId)
    {
        foreach (var (_, list) in LandNames)
            foreach (var n in list)
                if (n.LangId == langId && n.Word == word) return true;
        return false;
    }
}
