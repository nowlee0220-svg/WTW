using System.Text.Json;
using Wtw.Sim.Data;

namespace Wtw.Sim.People;

public sealed record NamedDef(int Index, string Id, string Name);

public sealed record TraitDef(int Index, string Id, string Name, string Effect, string Opposite)
{
    /// <summary>[능력 번호] 더하는 값.</summary>
    public int[] Abilities { get; init; } = new int[PeopleData.AbilityCount];
}

public sealed record ThoughtDef(int Index, string Id, string Name, int Mood, int Hours, int Stack);

/// <summary>따거나 주울 수 있는 오브젝트(카탈로그 번호)와 그 몫.</summary>
public sealed record HarvestDef(int Object, string ObjectName, string Item, string ItemName, int Stock, long RegrowTicks, int Nutrition, int Water, bool Food)
{
    /// <summary>아이템 목록(items.json) 번호.</summary>
    public int ItemIndex { get; init; }
}

/// <summary>data/people.json — 사람에 관한 숫자 · 이름표 · 특성 · 생각 · 먹을 것.</summary>
public sealed class PeopleData
{
    public const string File = "people.json";
    public const int AbilityCount = 5, SkillCount = 10;
    public const int Str = 0, Dex = 1, Int = 2, Soc = 3, Nerve = 4;
    public const int Hunting = 0, Gathering = 1, Farming = 2, Cooking = 3, Crafting = 5, Research = 6, Teaching = 7;

    public IReadOnlyList<NamedDef> Abilities { get; private init; } = Array.Empty<NamedDef>();
    public IReadOnlyList<NamedDef> Skills { get; private init; } = Array.Empty<NamedDef>();
    public IReadOnlyList<TraitDef> Traits { get; private init; } = Array.Empty<TraitDef>();
    public IReadOnlyList<ThoughtDef> Thoughts { get; private init; } = Array.Empty<ThoughtDef>();
    /// <summary>[오브젝트 카탈로그 번호] → 딸 것 / 주울 것 (없으면 null).</summary>
    public HarvestDef?[] Harvest { get; private init; } = Array.Empty<HarvestDef?>();

    public int TraitIndex(string id) { foreach (var t in Traits) if (t.Id == id) return t.Index; return -1; }
    public int ThoughtIndex(string id) { foreach (var t in Thoughts) if (t.Id == id) return t.Index; return -1; }

    private static readonly Lazy<PeopleData> DefaultData = new(() => Load(ObjectCatalog.EmbeddedFiles(File)[File], ObjectCatalog.Default), LazyThreadSafetyMode.ExecutionAndPublication);
    public static PeopleData Default => DefaultData.Value;

    public static PeopleData Load(string text, ObjectCatalog cat)
    {
        var problems = new List<string>();
        using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;

        List<NamedDef> Named(string key)
        {
            var list = new List<NamedDef>();
            if (!root.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array) { problems.Add($"{File}: \"{key}\" 목록이 없습니다."); return list; }
            foreach (var e in arr.EnumerateArray()) list.Add(new NamedDef(list.Count, Str(e, "id"), Str(e, "name")));
            return list;
        }
        string Str(JsonElement e, string k)
        {
            if (e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String) return v.GetString()!;
            problems.Add($"{File}: '{k}' 칸이 없습니다 ({e}).");
            return "";
        }
        double Num(JsonElement e, string k, double dflt = double.NaN)
        {
            if (e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number) return v.GetDouble();
            if (double.IsNaN(dflt)) problems.Add($"{File}: 숫자 칸 '{k}' 이(가) 없습니다 ({e}).");
            return double.IsNaN(dflt) ? 0 : dflt;
        }

        var abilities = Named("abilities");
        var skills = Named("skills");
        if (abilities.Count != AbilityCount) problems.Add($"{File}: abilities 는 {AbilityCount}개여야 합니다.");
        if (skills.Count != SkillCount) problems.Add($"{File}: skills 는 {SkillCount}개여야 합니다.");

        var traits = new List<TraitDef>();
        if (root.TryGetProperty("traits", out var ta))
            foreach (var e in ta.EnumerateArray())
            {
                var ab = new int[AbilityCount];
                if (e.TryGetProperty("abilities", out var ae))
                    foreach (var p in ae.EnumerateObject())
                    {
                        int k = abilities.FindIndex(a => a.Id == p.Name);
                        if (k < 0) problems.Add($"{File}: 특성 능력 '{p.Name}' 을(를) 모릅니다."); else ab[k] = p.Value.GetInt32();
                    }
                traits.Add(new TraitDef(traits.Count, Str(e, "id"), Str(e, "name"), Str(e, "effect"), e.TryGetProperty("opposite", out var op) ? op.GetString() ?? "" : "") { Abilities = ab });
            }
        foreach (var t in traits)
            if (t.Opposite.Length > 0 && traits.All(o => o.Id != t.Opposite)) problems.Add($"{File}: 특성 {t.Id} 의 반대 '{t.Opposite}' 가 없습니다.");

        var thoughts = new List<ThoughtDef>();
        if (root.TryGetProperty("thoughts", out var tha))
            foreach (var e in tha.EnumerateArray())
                thoughts.Add(new ThoughtDef(thoughts.Count, Str(e, "id"), Str(e, "name"), (int)Num(e, "mood"), (int)Num(e, "hours"), (int)Num(e, "stack", 1)));

        var harvest = new HarvestDef?[cat.Objects.Count];
        void ReadHarvest(string key, bool food)
        {
            if (!root.TryGetProperty(key, out var arr)) { problems.Add($"{File}: \"{key}\" 목록이 없습니다."); return; }
            foreach (var e in arr.EnumerateArray())
            {
                string obj = Str(e, "object"), item = Str(e, "item");
                var def = cat.Find(obj);
                if (def is null) { problems.Add($"{File}: 오브젝트 '{obj}' 가 objects.json 에 없습니다."); continue; }
                var it = cat.Items.FirstOrDefault(i => i.Id == item);
                if (it is null) { problems.Add($"{File}: 아이템 '{item}' 이 items.json 에 없습니다."); continue; }
                double days = Num(e, "regrow_days");
                harvest[def.Index] = new HarvestDef(def.Index, def.Name, item, it.Name, (int)Num(e, "stock"), (long)Math.Round(days * Time.GameTime.TicksPerDay),
                    food ? (int)Num(e, "nutrition") * 1000 : 0, food ? (int)Num(e, "water") * 1000 : 0, food) { ItemIndex = cat.Items.ToList().IndexOf(it) };
            }
        }
        ReadHarvest("foods", true);
        ReadHarvest("carry", false);

        foreach (var id in PeopleSim.RequiredThoughts) if (thoughts.All(t => t.Id != id)) problems.Add($"{File}: 꼭 있어야 하는 생각 '{id}' 가 없습니다.");
        foreach (var id in PeopleSim.RequiredTraits) if (traits.All(t => t.Id != id)) problems.Add($"{File}: 꼭 있어야 하는 특성 '{id}' 가 없습니다.");
        if (problems.Count > 0) throw new DataValidationException(problems);
        return new PeopleData
        {
            Abilities = abilities, Skills = skills, Traits = traits, Thoughts = thoughts, Harvest = harvest,
        };
    }
}

/// <summary>한글 글자 짓기 · 조사(이/가, 을/를 …).</summary>
public static class Hangul
{
    private static readonly string[] Cho = { "ㄱ", "ㄲ", "ㄴ", "ㄷ", "ㄸ", "ㄹ", "ㅁ", "ㅂ", "ㅃ", "ㅅ", "ㅆ", "ㅇ", "ㅈ", "ㅉ", "ㅊ", "ㅋ", "ㅌ", "ㅍ", "ㅎ" };
    private static readonly string[] Jung = { "ㅏ", "ㅐ", "ㅑ", "ㅒ", "ㅓ", "ㅔ", "ㅕ", "ㅖ", "ㅗ", "ㅘ", "ㅙ", "ㅚ", "ㅛ", "ㅜ", "ㅝ", "ㅞ", "ㅟ", "ㅠ", "ㅡ", "ㅢ", "ㅣ" };
    private static readonly string[] Jong = { "", "ㄱ", "ㄲ", "ㄳ", "ㄴ", "ㄵ", "ㄶ", "ㄷ", "ㄹ", "ㄺ", "ㄻ", "ㄼ", "ㄽ", "ㄾ", "ㄿ", "ㅀ", "ㅁ", "ㅂ", "ㅄ", "ㅅ", "ㅆ", "ㅇ", "ㅈ", "ㅊ", "ㅋ", "ㅌ", "ㅍ", "ㅎ" };

    public static int ChoIndex(string s) => Array.IndexOf(Cho, s);
    public static int JungIndex(string s) => Array.IndexOf(Jung, s);
    public static int JongIndex(string s) => Array.IndexOf(Jong, s);

    public static char Compose(int cho, int jung, int jong) => (char)(0xAC00 + (cho * 21 + jung) * 28 + jong);

    /// <summary>마지막 글자에 받침이 있나.</summary>
    public static bool HasFinal(string word)
    {
        if (string.IsNullOrEmpty(word)) return false;
        // 끝의 따옴표 · 괄호 · 빈칸은 건너뛰고 읽히는 마지막 글자로: "키 큰 여자 (2)" → "이"(받침 없음), "(3)" → "삼"(받침)
        int k = word.Length - 1;
        while (k > 0 && word[k] is ')' or '\'' or '"' or ' ' or '’' or '”') k--;
        char c = word[k];
        if (c >= '0' && c <= '9') return c is '0' or '1' or '3' or '6' or '7' or '8';
        if (c < 0xAC00 || c > 0xD7A3) return false;
        return (c - 0xAC00) % 28 != 0;
    }

    /// <summary>받침 있으면 a, 없으면 b: Josa("돌", "이", "가") → "돌이".</summary>
    public static string J(string word, string withFinal, string without) => word + (HasFinal(word) ? withFinal : without);
    public static string I(string w) => J(w, "이", "가");
    public static string Eun(string w) => J(w, "은", "는");
    public static string Eul(string w) => J(w, "을", "를");
    public static string Gwa(string w) => J(w, "과", "와");
    /// <summary>"…이라고" / "…라고".</summary>
    public static string Rago(string w) => J(w, "이라고", "라고");
    /// <summary>"…으로" / "…로" (ㄹ 받침도 "로").</summary>
    public static string Ro(string w) => w + (HasFinal(w) && !EndsWithRieul(w) ? "으로" : "로");
    private static bool EndsWithRieul(string w)
    {
        for (int i = w.Length - 1; i >= 0; i--)
        {
            char c = w[i];
            if (c is '\'' or ')' or ' ' or '(') continue;
            return c >= 0xAC00 && c <= 0xD7A3 && (c - 0xAC00) % 28 == 8;
        }
        return false;
    }
}
