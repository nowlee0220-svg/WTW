using System.Text.Json;

namespace Wtw.Sim.Data;

public sealed record CraftAction(int Index, string Id, string Name);

/// <summary>"~가 있었으면" 바람 한 가지(crafting.json 의 wishes).</summary>
public sealed record WishDef(int Index, string Id, string Name, int[] Props, int[] Actions, bool NeedsFire);

/// <summary>부분 성공 한 가지: 어느 조건이 모자랐을 때 무엇이 남고 무슨 단서가 생기나.</summary>
public sealed record PartialDef(string Key, string Text, string Hint, int Result);

/// <summary>
/// 다루는 규칙 하나(crafting.json). 조건은 속성 번호 → 최소값(Input · With · Tool) · 최대값(InputMax). Heat 0~3, Tinder = 불쏘시개의 burns 최소값(0 = 없어도 됨).
/// </summary>
public sealed class CraftRule
{
    public int Index;
    public string Id = "", Name = "";
    public int Action;
    public readonly SortedDictionary<int, int> Input = new(), InputMax = new(), With = new(), WithMax = new(), Tool = new();
    public int Heat, Tinder, Chance = 100, Result = -1, Count = 1, Uses = 1, WithUses, ToolUses, TimeMin = 60, Skill = -1;
    public readonly Dictionary<string, PartialDef> Partial = new();
    public bool HasWith => With.Count > 0;
    public bool HasTool => Tool.Count > 0;
    /// <summary>조건 수(주 재료 · 둘째 · 도구 · 불 · 불쏘시개).</summary>
    public int ConditionCount => Input.Count + InputMax.Count + With.Count + WithMax.Count + Tool.Count + (Heat > 0 ? 1 : 0) + (Tinder > 0 ? 1 : 0);
}

/// <summary>판정 결과: 성공(Result 아이템) · 부분 성공(Partial — 단서) · 아무 일 없음.</summary>
public readonly record struct CraftOutcome(CraftRule? Rule, bool Success, PartialDef? Partial, string FailedKey)
{
    public bool IsPartial => Rule is not null && !Success && Partial is not null;
}

/// <summary>
/// data/crafting.json + properties.json + items.json 의 속성 — 재료 다루기(E4). 결과는 정답표가 아니라 속성에서 나온다: Judge 는
/// 행동 · 주 재료 속성 · 둘째 재료 속성 · 도구 속성 · 불 세기 · 불쏘시개로 규칙을 고른다. 모두 순수 함수(결정론).
/// </summary>
public sealed class CraftData
{
    public const string File = "crafting.json";
    public IReadOnlyList<PropertyDef> Properties { get; private init; } = Array.Empty<PropertyDef>();
    public IReadOnlyList<CraftAction> Actions { get; private init; } = Array.Empty<CraftAction>();
    public IReadOnlyList<string> HeatNames { get; private init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, string> GenericHints { get; private init; } = new Dictionary<string, string>();
    public IReadOnlyList<WishDef> Wishes { get; private init; } = Array.Empty<WishDef>();
    public IReadOnlyList<CraftRule> Rules { get; private init; } = Array.Empty<CraftRule>();
    /// <summary>[아이템 번호][속성 번호] 세기.</summary>
    public int[][] ItemProps { get; private init; } = Array.Empty<int[]>();
    public ObjectCatalog Catalog { get; private init; } = null!;

    public int PropIndex(string id) { foreach (var p in Properties) if (p.Id == id) return p.Index; return -1; }
    public int ActionIndex(string id) { foreach (var a in Actions) if (a.Id == id) return a.Index; return -1; }
    public int ItemIndex(string id)
    {
        // (S1) 판단마다 불리므로 아이디 → 번호 표를 한 번 만든다(같은 아이디가 여럿이면 앞의 것 — 훑을 때와 같다)
        var map = _itemIndex ??= BuildItemIndex();
        return map.TryGetValue(id, out int i) ? i : -1;
    }
    private Dictionary<string, int>? _itemIndex;
    private Dictionary<string, int> BuildItemIndex()
    {
        var m = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < Catalog.Items.Count; i++) m.TryAdd(Catalog.Items[i].Id, i);
        return m;
    }
    public CraftRule? Rule(string id) { foreach (var r in Rules) if (r.Id == id) return r; return null; }
    public int Prop(int item, int prop) => (uint)item < (uint)ItemProps.Length && prop >= 0 ? ItemProps[item][prop] : 0;
    public int Prop(int item, string prop) => Prop(item, PropIndex(prop));
    public string ItemName(int item) => (uint)item < (uint)Catalog.Items.Count ? Catalog.Items[item].Name : "무언가";
    public string ActionName(int a) => (uint)a < (uint)Actions.Count ? Actions[a].Name : "";

    // 자주 쓰는 속성 번호
    public int P_burns, P_hardness, P_toughness, P_sharp, P_sharpens, P_splits, P_weight, P_edible, P_poison, P_holds, P_waterproof, P_moldable, P_fibrous, P_bends, P_rots, P_melts, P_reduces, P_warm, P_malleable, P_fires_hard, P_dries, P_tears, P_meat, P_covers, P_pierces;

    private static readonly Lazy<CraftData> DefaultData = new(() =>
    {
        var files = ObjectCatalog.EmbeddedFiles(File, ContentLoader.PropertiesFile);
        return Load(files[File], files[ContentLoader.PropertiesFile], ObjectCatalog.Default, People.PeopleData.Default.Skills.Select(s => s.Id).ToArray());
    }, LazyThreadSafetyMode.ExecutionAndPublication);
    public static CraftData Default => DefaultData.Value;

    public static CraftData Load(string craftText, string propsText, ObjectCatalog cat, string[] skillIds)
    {
        var problems = new List<string>();
        var set = ContentLoader.Load(new Dictionary<string, string> { [ContentLoader.PropertiesFile] = propsText, [ContentLoader.ItemsFile] = "{ \"items\": [] }" });
        var props = set.Properties;
        int PI(string id) { for (int i = 0; i < props.Count; i++) if (props[i].Id == id) return i; return -1; }
        var itemProps = new int[cat.Items.Count][];
        for (int i = 0; i < cat.Items.Count; i++)
        {
            itemProps[i] = new int[props.Count];
            foreach (var (k, v) in cat.Items[i].Props)
            {
                int pi = PI(k);
                if (pi < 0) problems.Add($"{ObjectCatalog.ItemsFile} 의 {cat.Items[i].Id}: 속성 '{k}' 은(는) {ContentLoader.PropertiesFile} 에 없습니다.");
                else itemProps[i][pi] = v;
            }
        }
        using var doc = JsonDocument.Parse(craftText, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        var actions = new List<CraftAction>();
        if (root.TryGetProperty("actions", out var aa))
            foreach (var e in aa.EnumerateArray()) actions.Add(new CraftAction(actions.Count, e.GetProperty("id").GetString() ?? "", e.GetProperty("name").GetString() ?? ""));
        else problems.Add($"{File}: actions 목록이 없습니다.");
        int AI(string id) { for (int i = 0; i < actions.Count; i++) if (actions[i].Id == id) return i; return -1; }
        int II(string id) { for (int i = 0; i < cat.Items.Count; i++) if (cat.Items[i].Id == id) return i; return -1; }
        var heat = root.TryGetProperty("heat_names", out var hn) ? hn.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : new List<string>();
        if (heat.Count != 4) problems.Add($"{File}: heat_names 는 4개(불 없음 · 맨불 · 가둔 불 · 숯 + 가둔 불)여야 합니다.");
        var hints = new Dictionary<string, string>();
        if (root.TryGetProperty("generic_hints", out var gh)) foreach (var p in gh.EnumerateObject()) hints[p.Name] = p.Value.GetString() ?? "";
        var wishes = new List<WishDef>();
        if (root.TryGetProperty("wishes", out var ww))
            foreach (var e in ww.EnumerateArray())
            {
                string id = e.GetProperty("id").GetString() ?? "";
                var ps = e.GetProperty("props").EnumerateArray().Select(x => PI(x.GetString() ?? "")).ToArray();
                var acts = e.GetProperty("actions").EnumerateArray().Select(x => AI(x.GetString() ?? "")).ToArray();
                if (ps.Contains(-1)) problems.Add($"{File} 의 바람 {id}: 모르는 속성이 있습니다.");
                if (acts.Contains(-1)) problems.Add($"{File} 의 바람 {id}: 모르는 행동이 있습니다.");
                wishes.Add(new WishDef(wishes.Count, id, e.GetProperty("name").GetString() ?? "", ps, acts, e.TryGetProperty("fire", out var f) && f.ValueKind == JsonValueKind.True));
            }
        var rules = new List<CraftRule>();
        var seen = new HashSet<string>();
        if (!root.TryGetProperty("rules", out var rr)) problems.Add($"{File}: rules 목록이 없습니다.");
        else
            foreach (var e in rr.EnumerateArray())
            {
                var r = new CraftRule { Index = rules.Count, Id = e.GetProperty("id").GetString() ?? "", Name = e.TryGetProperty("name", out var nm) ? nm.GetString() ?? "" : "" };
                string where = $"{File} 의 규칙 {r.Id}";
                if (!seen.Add(r.Id)) problems.Add($"{where}: 아이디가 겹칩니다.");
                r.Action = AI(e.GetProperty("action").GetString() ?? "");
                if (r.Action < 0) problems.Add($"{where}: 모르는 행동입니다.");
                void Conds(string key, SortedDictionary<int, int> into)
                {
                    if (!e.TryGetProperty(key, out var o)) return;
                    foreach (var p in o.EnumerateObject())
                    {
                        int pi = PI(p.Name);
                        if (pi < 0) problems.Add($"{where}: {key} 의 속성 '{p.Name}' 을(를) 모릅니다."); else into[pi] = p.Value.GetInt32();
                    }
                }
                Conds("input", r.Input); Conds("input_max", r.InputMax); Conds("with", r.With); Conds("with_max", r.WithMax); Conds("tool", r.Tool);
                if (r.Input.Count == 0) problems.Add($"{where}: 주 재료 조건(input)이 없습니다.");
                r.Heat = e.TryGetProperty("heat", out var he) ? he.GetInt32() : 0;
                r.Tinder = e.TryGetProperty("tinder", out var ti) ? ti.GetInt32() : 0;
                r.Chance = e.TryGetProperty("chance", out var ch) ? ch.GetInt32() : 100;
                r.Count = e.TryGetProperty("count", out var cn) ? cn.GetInt32() : 1;
                r.Uses = e.TryGetProperty("uses", out var us) ? us.GetInt32() : 1;
                r.WithUses = e.TryGetProperty("with_uses", out var wu) ? wu.GetInt32() : 0;
                r.ToolUses = e.TryGetProperty("tool_uses", out var tu) ? tu.GetInt32() : 0;
                r.TimeMin = e.TryGetProperty("time_min", out var tm) ? tm.GetInt32() : 60;
                string res = e.GetProperty("result").GetString() ?? "";
                r.Result = II(res);
                if (r.Result < 0) problems.Add($"{where}: 결과 '{res}' 가 {ObjectCatalog.ItemsFile} 에 없습니다.");
                if (e.TryGetProperty("skill", out var sk)) { r.Skill = Array.IndexOf(skillIds, sk.GetString() ?? ""); if (r.Skill < 0) problems.Add($"{where}: 기술 '{sk.GetString()}' 을(를) 모릅니다."); }
                if (e.TryGetProperty("partial", out var pa))
                    foreach (var p in pa.EnumerateObject())
                    {
                        string key = p.Name;
                        if (key is not ("heat" or "tool" or "with" or "tinder" or "chance" or "rain" or "fuel") && PI(key) < 0) problems.Add($"{where}: partial 의 '{key}' 을(를) 모릅니다.");
                        string text = p.Value.TryGetProperty("text", out var tx) ? tx.GetString() ?? "" : "";
                        string hint = p.Value.TryGetProperty("hint", out var hi) ? hi.GetString() ?? "" : (hints.TryGetValue(key, out var g) ? g : "");
                        int pres = -1;
                        if (p.Value.TryGetProperty("result", out var pr)) { pres = II(pr.GetString() ?? ""); if (pres < 0) problems.Add($"{where}: partial 의 결과 '{pr.GetString()}' 가 없습니다."); }
                        r.Partial[key] = new PartialDef(key, text, hint, pres);
                    }
                rules.Add(r);
            }
        if (problems.Count > 0) throw new DataValidationException(problems);
        var d = new CraftData { Properties = props, Actions = actions, HeatNames = heat, GenericHints = hints, Wishes = wishes, Rules = rules, ItemProps = itemProps, Catalog = cat };
        d.P_burns = PI("burns"); d.P_hardness = PI("hardness"); d.P_toughness = PI("toughness"); d.P_sharp = PI("sharp"); d.P_sharpens = PI("sharpens"); d.P_splits = PI("splits");
        d.P_weight = PI("weight"); d.P_edible = PI("edible"); d.P_poison = PI("poison"); d.P_holds = PI("holds"); d.P_waterproof = PI("waterproof"); d.P_moldable = PI("moldable");
        d.P_fibrous = PI("fibrous"); d.P_bends = PI("bends"); d.P_rots = PI("rots"); d.P_melts = PI("melts"); d.P_reduces = PI("reduces"); d.P_warm = PI("warm");
        d.P_malleable = PI("malleable"); d.P_fires_hard = PI("fires_hard"); d.P_dries = PI("dries"); d.P_tears = PI("tears");
        d.P_meat = PI("meat"); d.P_covers = PI("covers"); d.P_pierces = PI("pierces");
        return d;
    }

    /// <summary>
    /// 판정. action 으로 input(속성 배열)을 with(둘째, null 가능) · tool(null 가능) · heat · tinderBurns 조건에서 다룬다.
    /// roll = 0~99 (성공 확률에 씀). 맞는 조건이 가장 많은 규칙을 고르고: 다 맞으면 성공, 주 재료 조건이 절반 넘게 맞으면 부분 성공(모자란 첫 조건의 단서).
    /// </summary>
    public CraftOutcome Judge(int action, int[] input, int[]? with, int[]? tool, int heat, int tinderBurns, int roll, int bonusPercent = 0, CraftRule? only = null)
    {
        CraftRule? best = null; int bestOk = 0, bestTotal = 1; string bestKey = ""; bool bestFull = false;
        foreach (var r in Rules)
        {
            if (r.Action != action || (only is not null && r != only)) continue;
            if (r.HasWith && with is null) continue; // 둘째 재료가 아예 없으면 이 규칙은 해 본 것이 아니다
            int ok = 0; string firstFail = "";
            void Check(bool pass, string key) { if (pass) ok++; else if (firstFail.Length == 0) firstFail = key; }
            int inputOk = 0;
            foreach (var (p, min) in r.Input) { bool pass = input[p] >= min; Check(pass, Properties[p].Id); if (pass) inputOk++; }
            foreach (var (p, max) in r.InputMax) { bool pass = input[p] <= max; Check(pass, Properties[p].Id); if (pass) inputOk++; }
            foreach (var (p, min) in r.With) Check(with is not null && with[p] >= min, "with");
            foreach (var (p, max) in r.WithMax) Check(with is not null && with[p] <= max, "with");
            foreach (var (p, min) in r.Tool) Check(tool is not null && tool[p] >= min, "tool");
            if (r.Heat > 0) Check(heat >= r.Heat, "heat");
            if (r.Tinder > 0) Check(tinderBurns >= r.Tinder, "tinder");
            int total = r.ConditionCount;
            // 주 재료 조건이 절반도 안 맞으면 이 규칙은 아예 아니다(절반 이상 맞으면 부분 성공이 될 수 있다)
            if (inputOk * 2 < r.Input.Count + r.InputMax.Count || inputOk == 0) continue;
            bool full = ok == total;
            // 맞은 조건의 비율이 높은 규칙(같으면 다 맞은 것 → 조건이 많은 것 → 먼저 적은 것)
            long a = (long)ok * bestTotal, b = (long)bestOk * total;
            bool better = best is null || a > b || (a == b && full && !bestFull) || (a == b && full == bestFull && total > bestTotal);
            if (better) { best = r; bestOk = ok; bestTotal = total; bestKey = firstFail; bestFull = full; }
        }
        if (best is null) return new CraftOutcome(null, false, null, "");
        if (bestFull)
        {
            int chance = Math.Clamp(best.Chance + (best.Chance < 100 ? bonusPercent : 0), 1, 100);
            if (roll < chance) return new CraftOutcome(best, true, null, "");
            return new CraftOutcome(best, false, best.Partial.TryGetValue("chance", out var pc) ? pc : new PartialDef("chance", "이번엔 되지 않았다", GenericHints.GetValueOrDefault("chance", ""), -1), "chance");
        }
        _ = bestTotal;
        if (!best.Partial.TryGetValue(bestKey, out var pd))
            pd = new PartialDef(bestKey, DefaultPartialText(bestKey), GenericHints.GetValueOrDefault(bestKey, ""), -1);
        return new CraftOutcome(best, false, pd, bestKey);
    }

    private string DefaultPartialText(string key) => key switch
    {
        "heat" => "뜨거워지기만 하고 달라지지 않았다",
        "tool" => "손으로는 되지 않았다",
        "with" => "함께 쓸 것이 맞지 않았다",
        "tinder" => "불꽃만 튀고 붙지 않았다",
        _ => $"{People.Hangul.I(PropIndex(key) >= 0 ? Properties[PropIndex(key)].Name : key)} 모자라 되지 않았다",
    };

    /// <summary>익혔거나 말려 둔 먹을 것(들고 다니며 먹는 것)인가.</summary>
    /// <summary>(E5b) 입을 수 있는 것: 0 아님 · 1 몸 · 2 발.</summary>
    public int WearSlot(int item) => (uint)item < (uint)Catalog.Items.Count ? Catalog.Items[item].Wear switch { "body" => 1, "feet" => 2, _ => 0 } : 0;
    /// <summary>(E5b) 짐승에게서 나온 것(날고기 · 날가죽 · 뼈 · 힘줄)인가.</summary>
    public bool IsAnimal(int item) => (uint)item < (uint)Catalog.Items.Count && Catalog.Items[item].Kind == "animal";

    public bool IsCookedOrKept(int item) => (uint)item < (uint)Catalog.Items.Count && Catalog.Items[item].Kind == "made" && Prop(item, P_edible) > 0;

    /// <summary>불 세기 이름.</summary>
    public string HeatName(int h) => (uint)h < (uint)HeatNames.Count ? HeatNames[h] : "";
}
