using System.Text.Json;
using Wtw.Sim.Core;
using Wtw.Sim.Data;

namespace Wtw.Sim.People;

public sealed record ConceptDef(int Index, string Id, string Meaning, string Kind, bool Base, bool NameUse);

/// <summary>
/// data/language.json — 뜻 목록 · 소리 바탕 무게 · 걸러낼 낱말. 문법은 없다(소리와 낱말만).
/// </summary>
public sealed class LanguageData
{
    public const string File = "language.json";
    /// <summary>쓸 수 있는 소리(한글 자모 번호). 순서 = 말씨 무게 배열의 순서.</summary>
    public int[] Cho { get; private init; } = Array.Empty<int>();
    public int[] Jung { get; private init; } = Array.Empty<int>();
    public int[] Jong { get; private init; } = Array.Empty<int>();
    public int[] ChoBase { get; private init; } = Array.Empty<int>();
    public int[] JungBase { get; private init; } = Array.Empty<int>();
    public int[] JongBase { get; private init; } = Array.Empty<int>();
    public IReadOnlyList<ConceptDef> Concepts { get; private init; } = Array.Empty<ConceptDef>();
    public HashSet<string> Banned { get; private init; } = new();
    public HashSet<char> BannedSyllables { get; private init; } = new();

    /// <summary>걸러낼 낱말인가: 목록에 있거나, 걸러낼 글자가 들어 있거나.</summary>
    public bool IsBanned(string w)
    {
        if (Banned.Contains(w)) return true;
        foreach (char c in w) if (BannedSyllables.Contains(c)) return true;
        return false;
    }
    public int SoundCount => Cho.Length + Jung.Length + Jong.Length;

    public int ConceptIndex(string id) { foreach (var c in Concepts) if (c.Id == id) return c.Index; return -1; }

    private static readonly Lazy<LanguageData> DefaultData = new(() => Load(ObjectCatalog.EmbeddedFiles(File)[File]), LazyThreadSafetyMode.ExecutionAndPublication);
    public static LanguageData Default => DefaultData.Value;

    public static LanguageData Load(string text)
    {
        var problems = new List<string>();
        using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        var concepts = new List<ConceptDef>();
        foreach (var e in root.GetProperty("concepts").EnumerateArray())
        {
            string id = e.GetProperty("id").GetString() ?? "", meaning = e.GetProperty("meaning").GetString() ?? "";
            if (id.Length == 0 || meaning.Length == 0) problems.Add($"{File}: 뜻에 id · meaning 이 없습니다.");
            if (concepts.Any(c => c.Id == id)) problems.Add($"{File}: 뜻 '{id}' 가 겹칩니다.");
            concepts.Add(new ConceptDef(concepts.Count, id, meaning, e.TryGetProperty("kind", out var k) ? k.GetString() ?? "" : "",
                e.TryGetProperty("base", out var b) && b.ValueKind == JsonValueKind.True, e.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.True));
        }
        (int[] Ids, int[] W) Sounds(string key, Func<string, int> index)
        {
            var ids = new List<int>(); var w = new List<int>();
            foreach (var p in root.GetProperty("sounds").GetProperty(key).EnumerateObject())
            {
                int i = index(p.Name);
                if (i < 0) { problems.Add($"{File}: 소리 '{p.Name}' 를 모릅니다."); continue; }
                ids.Add(i); w.Add(p.Value.GetInt32());
            }
            return (ids.ToArray(), w.ToArray());
        }
        var cho = Sounds("initials", Hangul.ChoIndex);
        var jung = Sounds("vowels", Hangul.JungIndex);
        var jong = Sounds("finals", Hangul.JongIndex);
        if (!jong.Ids.Contains(0)) problems.Add($"{File}: 끝소리에 빈칸(받침 없음)이 있어야 합니다.");
        var banned = new HashSet<string>(root.GetProperty("banned").EnumerateArray().Select(x => x.GetString() ?? ""));
        var bannedSyl = new HashSet<char>(root.TryGetProperty("banned_syllables", out var bs) ? bs.EnumerateArray().Select(x => (x.GetString() ?? " ")[0]) : Array.Empty<char>());
        foreach (var id in new[] { "water", "food", "danger", "you", "me" })
            if (concepts.All(c => c.Id != id)) problems.Add($"{File}: 꼭 있어야 하는 뜻 '{id}' 가 없습니다.");
        if (problems.Count > 0) throw new DataValidationException(problems);
        return new LanguageData
        {
            Cho = cho.Ids, ChoBase = cho.W, Jung = jung.Ids, JungBase = jung.W, Jong = jong.Ids, JongBase = jong.W,
            Concepts = concepts, Banned = banned, BannedSyllables = bannedSyl,
        };
    }
}

/// <summary>
/// 말 하나(한 무리의 말). 저장하는 것은 시드 · 즐겨 쓰는 소리의 무게 · (나중에) 바뀐 낱말뿐 — 낱말은 소리 규칙 + 시드로 그때그때 만든다.
/// 이 지방은 ○○어 처럼 미리 정하지 않는다: 처음 뿌린 사람(가족)마다 말이 하나씩 생기고, 자주 이야기하는 사람끼리 한 말로 모인다.
/// Parent 는 나중에 갈라진 말(사투리 → 다른 말)을 얹을 자리.
/// </summary>
public sealed class Language
{
    public int Id;
    public ulong Seed;
    public int Parent = -1;
    public long Born;
    /// <summary>즐겨 쓰는 소리 무게: 첫소리 · 가운데 소리 · 끝소리 순(LanguageData 의 순서).</summary>
    public int[] Sounds = Array.Empty<int>();
    /// <summary>0 짧은 낱말을 좋아함 · 1 보통 · 2 긴 낱말.</summary>
    public int Length = 1;
    /// <summary>이 무리가 아는 뜻(낱말이 있는 뜻). 처음엔 기본 뜻들 — E4 부터 늘어난다.</summary>
    public readonly SortedSet<int> Known = new();
    /// <summary>뜻 → 다른 시드(낱말이 바뀌었거나 빌려 왔으면).</summary>
    public readonly SortedDictionary<int, ulong> Overrides = new();
    /// <summary>(E4) 빌려 온 낱말: 뜻 → 그 말의 소리 그대로(빌려 온 때의 낱말).</summary>
    public readonly SortedDictionary<int, string> Loans = new();
    /// <summary>(E4) 낱말이 어디서 왔나: 뜻 → (어느 말에서 — −1 이면 이 무리가 지어냄, 언제). 처음 낱말은 적지 않는다.</summary>
    public readonly SortedDictionary<int, (int FromLang, long Tick)> Origins = new();

    /// <summary>(E3b) 이 말에서 낱말이 새로 바뀐 횟수(정해진 순서대로 바꾼다).</summary>
    public int Changes;
    /// <summary>소리 무게 · 낱말이 바뀔 때마다 하나씩(낱말 기억 열쇠 — 저장 안 함).</summary>
    public int Version;

    /// <summary>
    /// (E3b) 낱말 하나를 새로 바꾼다: 이 말만의 순서(시드 + 번호로 정함 — 갈라진 말은 다른 순서)에서 다음 뜻. 같은 낱말이 거듭 바뀔 수도 있다.
    /// 바뀐 뜻을 돌려준다.
    /// </summary>
    public int ReplaceNextWord(IReadOnlyList<ConceptDef> concepts)
    {
        int n = concepts.Count;
        // 순서: (시드, 번호)로 섞은 뜻 목록을 한 바퀴씩
        int round = Changes / n, pos = Changes % n;
        var order = new int[n];
        for (int i = 0; i < n; i++) order[i] = i;
        var rng = new Pcg32(StableHash.Combine(Seed, (ulong)Id, (ulong)round), 0xC4A6);
        for (int i = n - 1; i > 0; i--) { int j = rng.NextInt(i + 1); (order[i], order[j]) = (order[j], order[i]); }
        var c = concepts[order[pos]];
        Overrides[c.Index] = StableHash.Combine(StableHash.Combine(Seed, (ulong)Id), (ulong)Changes + 1, StableHash.Of(c.Id));
        Changes++;
        Version++;
        return c.Index;
    }

    public ulong WordSeed(ConceptDef c) => Overrides.TryGetValue(c.Index, out var s) ? s : StableHash.Combine(Seed, StableHash.Of(c.Id));

    /// <summary>새 말: 소리마다 바탕 무게 × (0~3) — 몇몇 소리는 아예 안 쓴다.</summary>
    public static Language Create(int id, ulong seed, long now, LanguageData d)
    {
        var rng = new Pcg32(seed, StableHash.Mix(seed ^ 0x5EED));
        var lang = new Language { Id = id, Seed = seed, Born = now, Length = rng.NextInt(3) };
        var w = new int[d.SoundCount];
        void Fill(int offset, int[] baseW, int keepPercent, int minKept)
        {
            int kept = 0;
            for (int i = 0; i < baseW.Length; i++)
            {
                bool keep = rng.NextInt(100) < keepPercent;
                w[offset + i] = keep ? baseW[i] * (1 + rng.NextInt(4)) : 0;
                if (keep) kept++;
            }
            for (int i = 0; kept < minKept && i < baseW.Length * 4; i++)
            {
                int k = rng.NextInt(baseW.Length);
                if (w[offset + k] == 0) { w[offset + k] = baseW[k] * 2; kept++; }
            }
        }
        Fill(0, d.ChoBase, 70, 6);
        Fill(d.Cho.Length, d.JungBase, 75, 4);
        Fill(d.Cho.Length + d.Jung.Length, d.JongBase, 70, 2);
        w[d.Cho.Length + d.Jung.Length + Array.IndexOf(d.Jong, 0)] = Math.Max(30, d.JongBase[Array.IndexOf(d.Jong, 0)] * 2); // 받침 없음은 늘 흔하게
        lang.Sounds = w;
        foreach (var c in d.Concepts) if (c.Base) lang.Known.Add(c.Index);
        return lang;
    }
}

/// <summary>낱말 만들기 · 알아듣기. 모두 순수 함수(같은 입력 = 같은 낱말, 결정론).</summary>
public static class Words
{
    public const int AccentLimit = 6;

    /// <summary>
    /// 그 말 + 사람의 말씨(accent: 소리 무게에 더하는 작은 값)로 뜻 c 의 낱말. 우리말 낱말 · 욕 · 놀림말이면 다시 만든다.
    /// </summary>
    public static string Word(Language lang, sbyte[]? accent, ConceptDef c, LanguageData d, int minSyllables = 1)
    {
        if (lang.Loans.TryGetValue(c.Index, out var loan) && loan.Length >= minSyllables) return loan;
        ulong s = lang.WordSeed(c);
        var rng = new Pcg32(s, StableHash.Mix(s + 0x77));
        bool pronoun = c.Kind == "사람";
        for (int attempt = 0; ; attempt++)
        {
            int n = SyllableCount(lang.Length, rng, pronoun);
            n = Math.Max(n, minSyllables);
            var chars = new char[n];
            int prevCho = -1;
            for (int i = 0; i < n; i++)
            {
                // 같은 첫소리가 이어지면 덜 고르고, 소리 없는 첫소리(ㅇ)가 두 번 이어지지 않게("어어" 같은 소리)
                int cho = Pick(lang.Sounds, accent, 0, d.Cho.Length, rng, d.Cho, avoid: prevCho, ban: prevCho == 11 ? 11 : -1);
                int jung = Pick(lang.Sounds, accent, d.Cho.Length, d.Jung.Length, rng, d.Jung);
                int jong = Pick(lang.Sounds, accent, d.Cho.Length + d.Jung.Length, d.Jong.Length, rng, d.Jong, emptyBoost: i < n - 1 ? 5 : 1);
                // 어색한 소리를 줄인다: 이중 모음(ㅑ ㅕ ㅛ ㅠ)은 소리 없는 첫소리(ㅇ) 뒤에만(댜 · 며 → 다 · 머), 이중 모음 + 받침은 받침을 뗀다(겡 같은 소리)
                if (cho != 11 && jung is 2 or 6 or 12 or 17) jung = jung switch { 2 => 0, 6 => 4, 12 => 8, _ => 13 };
                if (jung is 2 or 6 or 12 or 17 or 1 or 5) jong = jong is 4 or 8 ? jong : 0;
                if (i > 0 && jung is 2 or 6 or 12 or 17) jung = jung switch { 2 => 0, 6 => 4, 12 => 8, _ => 13 };
                chars[i] = Hangul.Compose(cho, jung, jong);
                prevCho = cho;
            }
            var w = new string(chars);
            bool repeat = false;
            for (int i = 1; i < n; i++) if (chars[i] == chars[i - 1]) repeat = true;
            if (attempt < 30 && (d.IsBanned(w) || repeat || (n == 1 && !pronoun))) continue;
            return w;
        }
    }

    private static int SyllableCount(int length, Pcg32 rng, bool pronoun)
    {
        int r = rng.NextInt(100);
        if (pronoun) return r < 60 ? 1 : 2;
        return length switch
        {
            0 => r < 25 ? 1 : r < 90 ? 2 : 3,
            1 => r < 10 ? 1 : r < 75 ? 2 : 3,
            _ => r < 5 ? 1 : r < 55 ? 2 : 3,
        };
    }

    private static int Pick(int[] weights, sbyte[]? accent, int offset, int count, Pcg32 rng, int[] ids, int avoid = -1, int emptyBoost = 1, int ban = -1)
    {
        Span<int> w = stackalloc int[count];
        int total = 0;
        for (int i = 0; i < count; i++)
        {
            int v = weights[offset + i];
            if (v > 0 && accent is not null) v = Math.Max(1, v + accent[offset + i]);
            if (ids[i] == avoid) v /= 2;
            if (ids[i] == ban) v = 0;
            if (ids[i] == 0 && emptyBoost > 1) v *= emptyBoost;
            w[i] = v; total += v;
        }
        if (total <= 0) return ids[0];
        int r = rng.NextInt(total);
        for (int i = 0; i < count; i++) { r -= w[i]; if (r < 0) return ids[i]; }
        return ids[count - 1];
    }

    /// <summary>뜻 두 개로 지은 이름: 앞 낱말(꾸밈)의 첫 글자 + 뒤 낱말(이름감)의 앞 두 글자. 하나면 그 낱말(두 글자 이상).</summary>
    public static string Name(Language lang, sbyte[]? accent, IReadOnlyList<ConceptDef> parts, LanguageData d)
    {
        if (parts.Count == 1) return Word(lang, accent, parts[0], d, minSyllables: 2);
        string a = Word(lang, accent, parts[0], d), b = Word(lang, accent, parts[1], d);
        string name = a[..1] + b[..Math.Min(2, b.Length)];
        if (d.IsBanned(name) || name[0] == name[1]) name = a[..Math.Min(2, a.Length)] + b[..1];
        return name;
    }

    /// <summary>두 낱말이 얼마나 비슷한가(0~100): 자모로 풀어 편집 거리.</summary>
    public static int Similarity(string a, string b)
    {
        var x = Jamo(a); var y = Jamo(b);
        int n = x.Count, m = y.Count;
        if (n == 0 && m == 0) return 100;
        var dp = new int[n + 1, m + 1];
        for (int i = 0; i <= n; i++) dp[i, 0] = i;
        for (int j = 0; j <= m; j++) dp[0, j] = j;
        for (int i = 1; i <= n; i++)
            for (int j = 1; j <= m; j++)
                dp[i, j] = Math.Min(Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1), dp[i - 1, j - 1] + (x[i - 1] == y[j - 1] ? 0 : 1));
        return Math.Max(0, 100 - dp[n, m] * 100 / Math.Max(n, m));
    }

    private static List<int> Jamo(string w)
    {
        var l = new List<int>();
        foreach (char ch in w)
        {
            int v = ch - 0xAC00;
            if (v < 0 || v > 11171) continue;
            l.Add(v / 588); l.Add(100 + (v % 588) / 28);
            if (v % 28 != 0) l.Add(200 + v % 28);
        }
        return l;
    }
}
