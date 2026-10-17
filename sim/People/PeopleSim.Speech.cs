using Wtw.Sim.Core;
using Wtw.Sim.Time;

namespace Wtw.Sim.People;

/// <summary>
/// 말(소리와 낱말)과 이름. 문법은 없다.
///   - 처음 뿌린 사람(가족)마다 말이 하나 생긴다. 사람은 말 하나(LangId) + 사람마다 조금 다른 말씨(Accent)를 가진다.
///   - 같은 말을 쓰는 사람끼리 이야기하면 말씨가 서로 한 걸음씩 닮아 간다 → 한 무리 안에서는 같은 말이 된다.
///   - 다른 말을 쓰는 사람끼리 여러 번 이야기하면, 쓰는 사람이 적은 쪽이 많은 쪽 말을 배운다(옛 말의 소리 버릇은 말씨로 조금 남는다).
///   - 뿌린 사람은 이름이 없다(생김새로 부른다). 같은 말을 쓰는 사람과 지내기 시작하면 그 말로 이름을 지어 부른다.
///   - 낱말은 말의 시드 + 소리 규칙으로 그때그때 만든다(사람마다 낱말을 들고 있지 않음). 알아듣는 정도 = 두 사람의 낱말이 얼마나 같은가.
/// </summary>
public sealed partial class PeopleSim
{
    public LanguageData Lang { get; } = LanguageData.Default;
    public readonly SortedDictionary<int, Language> Languages = new();
    public int NextLanguageId { get; internal set; } = 1;
    private readonly Dictionary<(int, int, ulong, int, int), string> _wordCache = new();

    public const int TalksToLearnSpeech = 4;

    internal Language NewLanguage()
    {
        int id = NextLanguageId++;
        var l = Language.Create(id, StableHash.Combine(StableHash.Of(_w.Seed), StableHash.Of("language"), (ulong)id), Now, Lang);
        Languages.Add(id, l);
        return l;
    }

    internal void AddLoadedLanguage(Language l) => Languages[l.Id] = l;
    internal int NewLanguageForLoad() => NewLanguage().Id;

    private static ulong AccentHash(sbyte[] a)
    {
        ulong h = 1469598103934665603UL;
        foreach (sbyte v in a) h = (h ^ (byte)v) * 1099511628211UL;
        return h;
    }

    /// <summary>그 사람이 말하는 뜻 c 의 낱말(그 사람의 말 + 말씨).</summary>
    public string WordOf(Person p, int concept, int minSyllables = 1)
    {
        if (!Languages.TryGetValue(p.LangId, out var lang) || (uint)concept >= Lang.Concepts.Count) return "…";
        var key = (p.LangId, lang.Version, AccentHash(p.Accent), concept, minSyllables);
        if (!_wordCache.TryGetValue(key, out var w))
        {
            if (_wordCache.Count > 50_000) _wordCache.Clear();
            w = Words.Word(lang, p.Accent, Lang.Concepts[concept], Lang, minSyllables);
            _wordCache[key] = w;
        }
        return w;
    }

    /// <summary>두 사람이 서로 얼마나 알아듣나(0~100): 기본 뜻들의 낱말이 얼마나 같은가. 같은 무리면 거의 100, 남남이면 낮다.</summary>
    public int Understanding(Person a, Person b)
    {
        if (!Languages.TryGetValue(a.LangId, out var la) || !Languages.ContainsKey(b.LangId)) return 0;
        int sum = 0, n = 0;
        foreach (int c in la.Known)
        {
            if (!Lang.Concepts[c].Base) continue;
            sum += Words.Similarity(WordOf(a, c), WordOf(b, c));
            n++;
        }
        return n == 0 ? 0 : sum / n;
    }

    public int Speakers(int langId, int cell = -1)
    {
        int n = 0;
        foreach (var p in _all) if (!p.Dead && p.LangId == langId && (cell < 0 || p.Cell == cell)) n++;
        return n;
    }

    // ───────── 이름 ─────────

    /// <summary>화면에 쓰는 이름: 지은 이름, 없으면 생김새("키 큰 붉은 머리 여자"), 같은 생김새가 여럿이면 번호("(2)").</summary>
    public string DisplayName(Person? p)
    {
        if (p is null) return "누군가";
        if (p.Name.Length > 0) return p.Name;
        string d = Descriptor(p);
        int n = 1;
        foreach (var q in Life(p.Cell).People)
        {
            if (q.Id >= p.Id) break;
            if (q.Name.Length == 0 && Descriptor(q) == d) n++;
        }
        return n == 1 ? d : $"{d} ({n})";
    }

    public string Descriptor(Person p)
    {
        long at = p.Dead ? p.DeathTick : Now;
        var st = p.Stage(at);
        var parts = new List<string>(3);
        if (st >= LifeStage.Adult)
        {
            if (p.Look.Height >= 190) parts.Add("키 큰"); else if (p.Look.Height <= 65) parts.Add("키 작은");
        }
        parts.Add(st == LifeStage.Elder ? "흰 머리" : HairWord(p.Look.Hair) + " 머리");
        parts.Add(st switch
        {
            LifeStage.Baby => "아기",
            LifeStage.Child => p.Female ? "여자아이" : "남자아이",
            LifeStage.Elder => p.Female ? "할머니" : "할아버지",
            _ => p.Female ? "여자" : "남자",
        });
        return string.Join(" ", parts);
    }

    private static string HairWord(byte h) => h < 70 ? "검은" : h < 150 ? "갈색" : h < 215 ? "붉은" : "누런";

    private int C(string id) => Lang.ConceptIndex(id);

    /// <summary>생김새에서 이름의 뜻을 고른다(붉은 머리 · 큰 발 · 빠른 발 · 검은 눈 …).</summary>
    private int[] LookConcepts(Person p, Pcg32 rng)
    {
        var opts = new List<int[]>();
        string hair = p.Stage(Now) == LifeStage.Elder ? "white" : p.Look.Hair < 70 ? "black" : p.Look.Hair < 150 ? "stone" : p.Look.Hair < 215 ? "red" : "yellow";
        if (hair == "stone") opts.Add(new[] { C("black"), C("stone") }); else opts.Add(new[] { C(hair), C("head") });
        if (p.Look.Height >= 190) opts.Add(new[] { C("big"), C("foot") });
        if (p.Look.Height <= 65) opts.Add(new[] { C("small"), C("hand") });
        if (p.HasTrait(T_fast)) opts.Add(new[] { C("fast"), C("foot") });
        if (p.HasTrait(T_tough)) opts.Add(new[] { C("strong"), C("hand") });
        opts.Add(new[] { p.Look.Eyes >= 2 ? C("green") : C("black"), C("eye") });
        opts.Add(new[] { C(p.Look.Skin < 100 ? "white" : p.Look.Skin > 180 ? "black" : "yellow"), C("hand") });
        opts.Add(new[] { C(p.Look.Build > 170 ? "big" : p.Look.Build < 70 ? "small" : "strong"), C(rng.NextInt(2) == 0 ? "tree" : "stone") });
        return opts[rng.NextInt(opts.Count)];
    }

    /// <summary>namer 가 자기 말로 p 의 이름을 지어 부른다.</summary>
    private void GiveName(Person p, Person namer, int[] concepts, int inheritedFrom = -1, string? inheritedName = null, Func<int[]>? alternative = null)
    {
        if (!Languages.TryGetValue(namer.LangId, out var lang)) return;
        string name = inheritedName ?? Words.Name(lang, namer.Accent, concepts.Select(c => Lang.Concepts[c]).ToList(), Lang);
        // 같은 지방에 같은 이름이 살아 있으면 다른 뜻으로 몇 번 더 지어 본다(물려받은 이름은 그대로)
        for (int tries = 0; inheritedName is null && alternative is not null && tries < 8 && (NameTaken(p, name) || Lang.IsBanned(name)); tries++)
        {
            concepts = alternative();
            name = Words.Name(lang, namer.Accent, concepts.Select(c => Lang.Concepts[c]).ToList(), Lang);
        }
        p.Name = name;
        p.NameConcepts = concepts;
        p.NameFrom = inheritedFrom;
        p.NamedBy = namer.Id;
        AddBio(p, BioKind.Named, namer.Id, inheritedFrom);
    }

    private bool NameTaken(Person p, string name)
    {
        foreach (var q in Life(p.Cell).People) if (q.Id != p.Id && !q.Dead && q.Name == name) return true;
        return false;
    }

    public string NameMeaning(Person p)
    {
        if (p.Name.Length == 0) return "";
        string m = string.Join(" ", p.NameConcepts.Where(c => (uint)c < Lang.Concepts.Count).Select(c => Lang.Concepts[c].Meaning));
        if (p.NameFrom >= 0) return $"{DisplayName(Get(p.NameFrom))}의 이름을 물려받음" + (m.Length > 0 ? $" — {m}" : "");
        return m;
    }

    /// <summary>아기 이름: 부모가 그 말로 — 태어날 때의 일(밤 · 새벽 · 해 · 물가 · 나무 · 돌) + 바람(강한 · 빠른 · 큰 …). 가끔 죽은 가족 · 존경받던 사람의 이름을 물려받는다.</summary>
    private void NameNewborn(Person baby, Person namer, Pcg32 rng)
    {
        if (rng.NextInt(100) < 15 && InheritCandidate(baby) is { } dead)
        {
            GiveName(baby, namer, dead.NameConcepts, dead.Id, dead.Name);
            return;
        }
        GiveName(baby, namer, BirthConcepts(baby, namer, rng), alternative: () => BirthConcepts(baby, namer, rng));
    }

    /// <summary>태어날 때의 일(밤 · 달 · 새벽 · 해 · 물가 · 나무 · 돌 · 몸) + 바람(강한 · 빠른 · 큰 · 색).</summary>
    private int[] BirthConcepts(Person baby, Person namer, Pcg32 rng)
    {
        var life = Life(baby.Cell);
        int hour = life.LocalHour(Now);
        int tile = namer.Tile(Now);
        var nouns = new List<string> { hour is >= 20 or < 4 ? "night" : hour < 7 ? "dawn" : "sun", hour is >= 18 or < 6 ? "moon" : "sun", "hand", "foot", "eye", "head" };
        if (life.Drink[tile]) nouns.Add("water");
        if (life.Shelter[tile] == 1) nouns.Add("tree");
        if (life.Shelter[tile] >= 2) nouns.Add("stone");
        string[] wishes = { "strong", "fast", "big", "small", "white", "green", "red", "black", "yellow" };
        return new[] { C(wishes[rng.NextInt(wishes.Length)]), C(nouns[rng.NextInt(nouns.Count)]) };
    }

    private Person? InheritCandidate(Person baby)
    {
        Person? best = null; int bestScore = int.MinValue;
        foreach (var q in Life(baby.Cell).People)
        {
            if (!q.Dead || q.Name.Length == 0) continue;
            int score;
            bool family = IsFamily(q, Get(baby.Mother) ?? baby) || IsFamily(q, Get(baby.Father) ?? baby);
            if (family) score = 1000;
            else
            {
                int sum = 0, n = 0;
                foreach (var r in Life(baby.Cell).People) if (!r.Dead && r.Stage(Now) >= LifeStage.Adult) { sum += r.GetRelation(q.Id).Opinion; n++; }
                score = n == 0 ? int.MinValue : sum / n;
                if (score < 40) continue;
            }
            if (score > bestScore || (score == bestScore && best is not null && q.Id > best.Id)) { bestScore = score; best = q; }
        }
        return best;
    }

    // ───────── 말이 닮아 감 ─────────

    /// <summary>이야기 한 번: 같은 말이면 말씨가 서로 닮아 가고, 다른 말이면 여러 번 뒤에 적은 쪽이 많은 쪽 말을 배운다. 그다음 이름이 없으면 지어 부른다.</summary>
    internal void Converge(Person p, Person q, Pcg32 rng)
    {
        if (p.LangId == q.LangId)
        {
            for (int k = 0; k < 3; k++)
            {
                int i = rng.NextInt(p.Accent.Length);
                int a = p.Accent[i], b = q.Accent[i];
                if (a < b) { p.Accent[i]++; q.Accent[i]--; }
                else if (a > b) { p.Accent[i]--; q.Accent[i]++; }
            }
        }
        else if (Math.Min(p.GetRelation(q.Id).Talks, q.GetRelation(p.Id).Talks) >= TalksToLearnSpeech && p.Stage(Now) != LifeStage.Baby && q.Stage(Now) != LifeStage.Baby)
        {
            int sp = Speakers(p.LangId, p.Cell), sq = Speakers(q.LangId, q.Cell);
            Person learner = sp < sq ? p : sq < sp ? q
                : Ability(p, PeopleData.Soc) < Ability(q, PeopleData.Soc) ? p : Ability(q, PeopleData.Soc) < Ability(p, PeopleData.Soc) ? q
                : p.Id > q.Id ? p : q;
            Person teacher = learner == p ? q : p;
            LearnSpeech(learner, teacher);
        }
        if (p.LangId == q.LangId)
        {
            if (p.Name.Length == 0 && p.Stage(Now) != LifeStage.Baby) GiveName(p, q, LookConcepts(p, rng), alternative: () => LookConcepts(p, rng));
            if (q.Name.Length == 0 && q.Stage(Now) != LifeStage.Baby) GiveName(q, p, LookConcepts(q, rng), alternative: () => LookConcepts(q, rng));
        }
    }

    /// <summary>learner 가 teacher 의 말을 쓰기 시작한다. 옛 말의 소리 버릇은 말씨로 조금 남는다.</summary>
    private void LearnSpeech(Person learner, Person teacher)
    {
        if (!Languages.TryGetValue(learner.LangId, out var old) || !Languages.TryGetValue(teacher.LangId, out var neu)) return;
        for (int i = 0; i < learner.Accent.Length; i++)
            learner.Accent[i] = (sbyte)Math.Clamp((old.Sounds[i] - neu.Sounds[i]) / 6 + teacher.Accent[i], -Words.AccentLimit, Words.AccentLimit);
        learner.LangId = teacher.LangId;
        AddBio(learner, BioKind.LearnedSpeech, teacher.Id);
    }

    /// <summary>말풍선에 띄울 뜻(이야기를 시작할 때 고른다).</summary>
    private int TalkTopic(Person p, Pcg32 rng)
    {
        string[] topics = p.Food < 40_000 ? new[] { "food", "food", "you", "me" } : p.Water < 40_000 ? new[] { "water", "water", "you" } :
            new[] { "you", "me", "sun", "moon", "tree", "stone", "water", "food", "danger", "night" };
        return C(topics[rng.NextInt(topics.Length)]);
    }

    public string LanguageLabel(int langId)
    {
        if (!Languages.TryGetValue(langId, out var l)) return "말 없음";
        return l.Parent >= 0 ? $"말 {langId}(말 {l.Parent}에서 갈라짐)" : $"말 {langId}";
    }
}
