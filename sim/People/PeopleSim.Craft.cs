using Wtw.Sim.Core;
using Wtw.Sim.Data;
using Wtw.Sim.Provinces;
using Wtw.Sim.Time;

namespace Wtw.Sim.People;

/// <summary>세계에서 처음 나온 것 하나(발견 목록): 어느 규칙을 · 누가 · 언제 · 어디서 · 어떤 바람에서 · 그 전의 부분 성공 · 사라졌나.</summary>
public sealed class Discovery
{
    public int Rule, Person = -1, Cell = -1, Wish = -1, LangId = -1;
    public long Tick;
    public int PartialsBefore;
    public readonly List<string> PartialTexts = new();
    public bool Lost;
    public long LostTick;
    public int LostCount, RefoundCount;
}

/// <summary>
/// (E4) 재료와 발견(기획서 8절). 필요가 실험을 끈다: 욕구 · 사건이 "~가 있었으면" 바람(Wish)을 남기고, 바람이 있으면 사람은 들고 있는 것 ·
/// 주변의 것 가운데 그 바람에 맞을 것 같은 재료(속성으로 짐작)를 골라 행동을 해 본다(Experiment). 판정은 CraftData.Judge — 속성 × 행동 × 조건(불 세기 · 도구).
/// 성공하면 그 사람이 그 법을 알고(Known), 곁에서 본 사람도 따라 할 수 있다. 부분 성공은 단서(Clue)로 남아 다음 실험을 이끈다.
/// 아는 법은 바람이 생기면 바로 만든다(Craft). 발견은 세계 목록(Discoveries)에 남고, 아는 사람이 다 죽으면 사라진다.
/// </summary>
public sealed partial class PeopleSim
{
    public CraftData Craft { get; } = CraftData.Default;
    public readonly SortedDictionary<int, Discovery> Discoveries = new();
    /// <summary>발견 전의 부분 성공(규칙 → 횟수 · 글 몇 개). 저장한다.</summary>
    public readonly SortedDictionary<int, (int Count, List<string> Texts)> Partials = new();
    /// <summary>말마다 처음 그 법을 알게 된 시각(열쇠 = 말 번호 × 1000 + 규칙 번호).</summary>
    public readonly SortedDictionary<long, long> GroupFirst = new();

    public const long ExperimentCooldown = 4 * GameTime.TicksPerHour;
    public const long EmberLife = 2 * GameTime.TicksPerHour;
    public const int CarryCap = 12;
    public const long FireSleepRange = 40;

    private int W_warmth, W_sharp, W_hardTool, W_container, W_cooking, W_carry, W_shiny;
    private int I_ember, I_campfire, I_hearth, I_charcoal, I_coconut, I_crackedCoconut, I_dryGrass, I_twigs, I_wood, I_splitWood, I_stone;
    private int A_strike, A_break, A_grind, A_cut, A_twist, A_weave, A_shape, A_dry, A_roast, A_burn, A_melt, A_boil, A_pile, A_tear;
    private CraftRule? R_campfire, R_hearth, R_crack;

    private void InitCraft()
    {
        int W(string id) { foreach (var w in Craft.Wishes) if (w.Id == id) return w.Index; throw new InvalidOperationException($"crafting.json 에 바람 '{id}' 가 없습니다."); }
        W_warmth = W("warmth"); W_sharp = W("sharpness"); W_hardTool = W("hard_tool"); W_container = W("container"); W_cooking = W("cooking"); W_carry = W("carry"); W_shiny = W("shiny");
        I_ember = Craft.ItemIndex("ember"); I_campfire = Craft.ItemIndex("campfire"); I_hearth = Craft.ItemIndex("hearth"); I_charcoal = Craft.ItemIndex("charcoal");
        I_coconut = Craft.ItemIndex("coconut"); I_crackedCoconut = Craft.ItemIndex("cracked_coconut"); I_dryGrass = Craft.ItemIndex("dry_grass"); I_twigs = Craft.ItemIndex("twigs");
        I_wood = Craft.ItemIndex("wood"); I_splitWood = Craft.ItemIndex("split_wood"); I_stone = Craft.ItemIndex("stone");
        A_strike = Craft.ActionIndex("strike"); A_break = Craft.ActionIndex("break"); A_grind = Craft.ActionIndex("grind"); A_cut = Craft.ActionIndex("cut"); A_twist = Craft.ActionIndex("twist");
        A_weave = Craft.ActionIndex("weave"); A_shape = Craft.ActionIndex("shape"); A_dry = Craft.ActionIndex("dry"); A_roast = Craft.ActionIndex("roast"); A_burn = Craft.ActionIndex("burn");
        A_melt = Craft.ActionIndex("melt"); A_boil = Craft.ActionIndex("boil"); A_pile = Craft.ActionIndex("pile"); A_tear = Craft.ActionIndex("tear");
        R_campfire = Craft.Rule("campfire"); R_hearth = Craft.Rule("hearth"); R_crack = Craft.Rule("crack_coconut");
        if (Craft.Wishes.Count > Person.WishSlots) throw new InvalidOperationException($"바람은 {Person.WishSlots}가지까지(Person.Wish 배열).");
    }

    // ───────── 바람 ─────────

    internal void AddWish(Person p, int wish, int amount)
    {
        if (wish < 0 || p.Stage(Now) < LifeStage.Child) return;
        if (p.Wish[wish] <= 0) p.WishSince[wish] = Now;
        p.Wish[wish] = Math.Clamp(p.Wish[wish] + amount, 0, 100);
    }

    private int StrongestWish(Person p)
    {
        int best = -1, bv = 0;
        for (int w = 0; w < Craft.Wishes.Count; w++) if (p.Wish[w] > bv) { bv = p.Wish[w]; best = w; }
        return best;
    }

    public int WishStrength(Person p, string id) { foreach (var w in Craft.Wishes) if (w.Id == id) return p.Wish[w.Index]; return 0; }

    /// <summary>하루 경계: 바람은 천천히 식는다(이루어지지 않아도 잊는다), 불씨는 두 시간이면 꺼진다.</summary>
    private void CraftDaily(Person p)
    {
        for (int w = 0; w < Craft.Wishes.Count; w++) p.Wish[w] = p.Wish[w] * 4 / 5;
        KnowledgeDaily(p);
    }

    // ───────── 속성 도우미 ─────────

    private int[] PropsOf(int item) => (uint)item < (uint)Craft.ItemProps.Length ? Craft.ItemProps[item] : new int[Craft.Properties.Count];
    private int Prop(int item, int prop) => Craft.Prop(item, prop);
    private bool IsMade(int item) => (uint)item < (uint)Craft.Catalog.Items.Count && Craft.Catalog.Items[item].Kind == "made";

    public int CarryWeight(Person p) { int w = 0; foreach (var c in p.Carry) w += Math.Max(1, Prop(c.Item, Craft.P_weight)) * c.Count; return w; } // 가벼운 것도 하나에 1(손이 둘뿐)
    public int CarryCapOf(Person p) { int cap = CarryCap; foreach (var c in p.Carry) { if (Prop(c.Item, Craft.P_holds) >= 3) cap += 8; } return cap + Ability(p, PeopleData.Str) / 2; }

    /// <summary>들고 있는 것 중 속성 prop 이 가장 높은 것(min 이상). except 두 개는 빼고. 없으면 −1.</summary>
    private int BestCarried(Person p, int prop, int min = 1, int except1 = -1, int except2 = -1)
    {
        int best = -1, bv = min - 1;
        foreach (var c in p.Carry)
        {
            if (c.Item == except1 || c.Item == except2) continue;
            int v = Prop(c.Item, prop);
            if (v > bv || (v == bv && best >= 0 && c.Item < best)) { bv = v; best = c.Item; }
        }
        return best;
    }

    /// <summary>들고 있는 도구 중 가장 좋은 것(날이 섰거나 단단한 만든 것). 없으면 −1.</summary>
    public int BestTool(Person p, int prop, int min)
    {
        int best = -1, bv = min - 1;
        foreach (var c in p.Carry)
        {
            int v = Prop(c.Item, prop);
            if (v > bv) { bv = v; best = c.Item; }
        }
        return best;
    }

    public bool HasHardTool(Person p) { foreach (var c in p.Carry) if (IsMade(c.Item) && Prop(c.Item, Craft.P_hardness) >= 4 && Prop(c.Item, Craft.P_sharp) >= 2) return true; return false; }
    public bool HasSharpTool(Person p) => BestTool(p, Craft.P_sharp, 2) >= 0;

    private void WearTool(Person p, int tool, Pcg32 rng)
    {
        if (tool < 0 || !IsMade(tool)) return;
        int tough = Prop(tool, Craft.P_toughness);
        if (rng.NextInt(100) < 100 / (8 + tough * 10)) p.RemoveCarry(tool, 1);
    }

    // ───────── 판단에서 ─────────

    /// <summary>실험을 해 볼 만한가(점수). 바람이 세거나 호기심이 많을 때, 낮에, 쉬었다가.</summary>
    private int ExperimentScore(Person p, ProvinceLife life)
    {
        // 밤에는 실험하지 않는다 — 다만 몸이 얼어 가면 어둠 속에서라도 해 본다
        bool freezing = p.Hypothermia > 10_000 && p.Wish[W_warmth] >= 30;
        if (p.Stage(Now) < LifeStage.Child || (life.IsNight(Now) && !freezing)) return int.MinValue;
        long cd = p.HasTrait(T_curious) ? 6 * GameTime.TicksPerHour : p.HasTrait(T_stubborn) ? 40 * GameTime.TicksPerHour : 16 * GameTime.TicksPerHour;
        if (Now - p.LastExperiment < cd) return int.MinValue;
        if (Now - p.LastCraftFail < 3 * GameTime.TicksPerHour) return int.MinValue; // 방금 계획이 서지 않았다
        int w = StrongestWish(p);
        if (w >= 0 && p.Wish[w] < 20 && !p.HasTrait(T_curious)) return int.MinValue; // 조금 아쉬운 정도로는 실험하지 않는다
        int s = (w >= 0 ? p.Wish[w] / 3 : 0) + (p.HasTrait(T_curious) ? 12 : 0) - (p.HasTrait(T_stubborn) ? 10 : 0) + (Ability(p, PeopleData.Int) - 5);
        if (w < 0 && !p.HasTrait(T_curious)) return int.MinValue;
        return s;
    }

    /// <summary>바람이 있고 아는 법이 있으면 만든다(점수).</summary>
    private int CraftScore(Person p)
    {
        if (p.Known.Count == 0 || p.Stage(Now) < LifeStage.Child) return int.MinValue;
        if (Now - p.LastCraftFail < 6 * GameTime.TicksPerHour) return int.MinValue; // 방금 재료가 없어 못 했다
        int w = StrongestWish(p);
        return w < 0 ? int.MinValue : p.Wish[w] / 2 + 8;
    }

    /// <summary>실험 시작(또는 재료부터 모으기). 못 하면 false.</summary>
    private bool TryExperiment(Person p)
    {
        var rng = Rng(p.Cell);
        // 바람은 세기에 비례해 고른다(가장 센 것만 붙들지 않게)
        int total = 0;
        for (int w = 0; w < Craft.Wishes.Count; w++) total += p.Wish[w];
        int wish = -1;
        if (total > 0) { int r = rng.NextInt(total); for (int w = 0; w < Craft.Wishes.Count; w++) { r -= p.Wish[w]; if (r < 0) { wish = w; break; } } }
        if (wish < 0) { wish = rng.NextInt(Craft.Wishes.Count); if (wish == W_record) wish = W_warmth; } // 호기심: 아무 바람이나 — (E5 고침) "남겨 두기"는 남길 까닭이 있을 때만
        if (Plan(p, wish, known: false, rng)) return true;
        p.LastCraftFail = Now;
        return false;
    }

    /// <summary>(E4) 몸이 식는데 불 피우는 법을 안다 → 불부터 피운다(따뜻함 바람으로 아는 법 계획).</summary>
    private bool TryLightKnownFire(Person p)
    {
        if (R_campfire is null || !p.Knows(R_campfire.Index) || p.Stage(Now) < LifeStage.Child) return false;
        return Plan(p, W_warmth, known: true, Rng(p.Cell));
    }

    private bool TryCraftKnown(Person p)
    {
        var rng = Rng(p.Cell);
        // 바람 센 순서로 아는 법을 찾는다
        var order = Enumerable.Range(0, Craft.Wishes.Count).Where(w => p.Wish[w] > 0).OrderByDescending(w => p.Wish[w]).ThenBy(w => w).Take(2).ToList(); // 가장 센 바람 둘만
        foreach (int w in order) if (Plan(p, w, known: true, rng)) return true;
        p.LastCraftFail = Now;
        return false;
    }

    private bool ServesWish(CraftRule r, WishDef w)
    {
        if (r.Result < 0) return false;
        if (w.Index == W_hunting) return Prop(r.Result, Craft.P_weight) >= 1 && (Prop(r.Result, Craft.P_pierces) >= 1 || Prop(r.Result, Craft.P_sharp) >= 3); // (E5b) 바늘은 무기가 아니다
        foreach (int pr in w.Props) if (Prop(r.Result, pr) > 0) return true;
        if (w.Index == W_warmth && (r.Result == I_ember || r.Result == I_campfire || r.Result == I_hearth)) return true;
        if (w.Index == W_record) return r.Result == I_painting;
        if (r.Result == I_painting) return false; // 벽 그림은 남겨 두기에만
        return false;
    }

    /// <summary>
    /// 계획: 바람 wish 에 맞는 행동 · 재료를 고른다. known 이면 아는 법 가운데서(없으면 false). 재료가 없으면 재료부터 얻으러 간다(Material).
    /// 불이 필요하면 불가로 간다. 계획이 서면 Experiment/Craft 로 걸어가거나 제자리에서 시작한다.
    /// </summary>
    internal bool Plan(Person p, int wish, bool known, Pcg32 rng, CraftRule? force = null)
    {
        St.PlanWhy[0]++;
        bool r = PlanInner(p, wish, known, rng, force);
        if (r) St.PlanWhy[7]++;
        return r;
    }

    private bool IsPileAction(int action) => action == A_pile;

    private bool PlanInner(Person p, int wish, bool known, Pcg32 rng, CraftRule? force = null)
    {
        var life = Life(p.Cell);
        var wd = Craft.Wishes[wish];
        int here = p.Tile(Now);
        p.PlanAction = p.PlanInput = p.PlanWith = p.PlanTool = p.PlanRule = -1; p.PlanWish = wish; p.PlanHeat = 0;
        CraftRule? rule = null;
        int action;
        Clue? follow = null;
        if (force is not null) { rule = force; action = rule.Action; }
        else if (known)
        {
            // 아는 법 중 이 바람에 맞고 재료가 있는 것(들고 있는 것으로 주 재료 조건이 맞는 것 우선)
            CraftRule? best = null; int bs = int.MinValue;
            foreach (var k in p.Known)
            {
                var r = Craft.Rules[k.Rule];
                if (!ServesWish(r, wd) || r.Result == I_ember && !NeedsFire(p, life)) continue;
                if (r.Result == I_field || r.Result == I_sownPatch) continue; // (E5c) 심기는 밭일로(머무는 곳 곁에) — 아무 데서나 하지 않는다
                bool more = wish == W_clothing && Craft.WearSlot(r.Result) == 1 && Prop(r.Result, Craft.P_covers) < 5 && p.CarryCount(r.Result) < 3; // (E5b) 가죽은 셋까지 모은다(옷 한 벌)
                if ((p.CarryCount(r.Result) > 0 && !more) || AlreadyCovered(p, wish, r.Result)) continue; // 이미 들고 있거나 그만한 것이 있으면 또 만들지 않는다
                if (r.Result == I_hearth && life.Fires.TryGetValue(NearestFireAny(life, here), out var hf) && hf.Kind == 2) continue;
                bool hasIn = HasInputFor(p, r);
                if (!hasIn && !InputInWorld(r)) continue; // 주 재료가 만든 것(빚은 그릇 등)이면 먼저 그것을 들고 있어야
                int sc = hasIn ? 200 : 0;
                sc += r.Chance;
                if (r.Heat > 0 && life.NearestFire(here % S, here / S, Now, 60) < 0) continue;
                if (sc > bs) { bs = sc; best = r; }
            }
            if (best is null) return false;
            rule = best; action = rule.Action;
        }
        else
        {
            // 단서를 따라가기: 이 바람에 맞는 규칙의 단서가 있으면 반쯤은 그 행동을 다시(모자랐던 것을 바꿔서)
            for (int i = p.Clues.Count - 1; i >= 0; i--)
            {
                var c = p.Clues[i];
                var r = Craft.Rules[c.Rule];
                if (!ServesWish(r, wd) || Array.IndexOf(wd.Actions, r.Action) < 0) continue;
                if (rng.NextInt(100) < 50) follow = c; // 가장 최근의 단서 하나만, 반쯤
                break;
            }
            action = follow is { } f ? Craft.Rules[f.Rule].Action : PickAction(p, wd, life, here, rng);
            if (action < 0) { St.PlanWhy[1]++; return false; }
            // (E5) "그건 이미 해 봤는데 안 됐다": 이 행동으로 남은 단서가 있으면 대개 그 단서를 따른다(같은 헛수고를 되풀이하지 않게)
            if (follow is null)
                for (int i = p.Clues.Count - 1; i >= 0; i--)
                {
                    var c = p.Clues[i];
                    if (Craft.Rules[c.Rule].Action != action || !ServesWish(Craft.Rules[c.Rule], wd)) continue;
                    if (rng.NextInt(100) < 75) { follow = c; St.ClueAvoid++; }
                    break;
                }
        }
        // 불이 필요한 바람인데 불이 없다 → 먼저 불을 얻어야 한다(따뜻함 바람과 같은 실험)
        if (!known && wd.NeedsFire && life.NearestFire(here % S, here / S, Now, 60) < 0)
        {
            AddWish(p, W_warmth, 10);
            wd = Craft.Wishes[W_warmth]; wish = W_warmth; p.PlanWish = wish;
            action = follow is { } f2 && ServesWish(Craft.Rules[f2.Rule], wd) ? Craft.Rules[f2.Rule].Action : PickAction(p, wd, life, here, rng);
            if (action < 0) return false;
        }
        // 불이 있어야 하나
        bool needFire = rule is not null ? rule.Heat > 0 : wd.NeedsFire || action == A_roast || action == A_melt || action == A_boil || action == A_pile || action == A_smoke || (action == A_burn && p.CarryCount(I_ember) == 0);
        // 불씨 없이 태워 보기: 둘레에 타는 불이 있으면 그 불에 넣어 본다(숯은 가둔 불에서 나온다), 없으면 제자리에서
        if (action == A_burn && p.CarryCount(I_ember) == 0 && rule is null) needFire = life.NearestFire(here % S, here / S, Now, 30) >= 0;
        // 단서가 "불이 모자람"이면: 모닥불이 있고 돌이 있으면 화덕을 쌓아 본다
        if (follow is { Key: "heat" or "rain" or "fuel" } && !known)
        {
            int ft = life.NearestFire(here % S, here / S, Now, 60);
            if (ft < 0 && Craft.Rules[follow.Value.Rule].Result == I_hearth) ft = life.NearestFire(here % S, here / S, Now, 60, litOnly: false); // 꺼진 모닥불 자리도 돌로 둘러쌀 수 있다
            Fire hf = default;
            bool has = ft >= 0 && life.Fires.TryGetValue(ft, out hf);
            if (has && hf.Kind == 1)
            {
                int hc = CountCarried(p, Craft.P_hardness, 3, Craft.P_weight, 3);
                if (hc >= 2 || (hc >= 1 && hc + PileStonesAt(life, ft) >= 4)) { action = A_pile; needFire = true; }
                else if (StartMaterialFor(p, it => Prop(it, Craft.P_hardness) >= 3 && Prop(it, Craft.P_weight) >= 3, Craft.P_weight, Why.Clue)) return true; // 불을 둘러쌀 무거운 돌을 모은다
            }
            else if (has && hf.Kind == 2 && !hf.Charcoal)
            {
                if (p.CarryCount(I_charcoal) > 0) return StartTend(p, ft, I_charcoal, Why.Clue);
                if (BestCarried(p, Craft.P_burns, 4) >= 0) { action = A_burn; needFire = true; } // 가둔 불에 나무를 태워 숯을 만들어 본다
            }
        }
        // 단서가 "도구 · 불쏘시개 · 둘째 재료가 모자람"이면 그것부터 구한다
        if (follow is { } fk && !known)
        {
            var fr = Craft.Rules[fk.Rule];
            if (fk.Key == "tool" && fr.HasTool)
            {
                var (tp, tm) = fr.Tool.First();
                if (CountCarried(p, tp, tm, tp, tm) < (fr.Tool.ContainsKey(Craft.P_hardness) ? 2 : 1) && StartMaterialFor(p, fr.Tool, Why.Clue)) return true;
            }
            else if (fk.Key == "tinder" && BestCarried(p, Craft.P_burns, fr.Tinder) < 0)
            {
                if (StartMaterialFor(p, Craft.P_burns, fr.Tinder, Why.Clue)) return true;
                // 잘 타는 마른 것이 없다 → 풀을 말려 본다
                int grass = BestCarried(p, Craft.P_dries, 4);
                if (grass >= 0 && Prop(grass, Craft.P_fibrous) >= 3) { p.PlanAction = A_dry; p.PlanInput = grass; p.PlanWith = p.PlanTool = p.PlanRule = -1; SetWhy(p, Act.Experiment, Why.Clue, wish); p.Target = here; BeginWork(p); return true; }
                if (StartMaterialFor(p, Craft.P_dries, 4, Why.Clue)) return true;
            }
            else if (fk.Key == "with" && fr.HasWith)
            {
                if (PickCarried(p, Array.Empty<int>(), fk.Input, -1, fr.With) < 0 && StartMaterialFor(p, fr.With, Why.Clue)) return true;
            }
        }
        // 쌓기(불 둘러싸기)는 무거운 돌이 넷 있어야 한다 — 한꺼번에 못 들면 두엇씩 날라 불가에 모은다
        if (action == A_pile && CountCarried(p, Craft.P_hardness, 3, Craft.P_weight, 3) is int hc2
            && !(hc2 >= 2 || (hc2 >= 1 && hc2 + PileStonesAt(life, life.NearestFire(here % S, here / S, Now, 60, litOnly: false)) >= 4)))
            return StartMaterialFor(p, it => Prop(it, Craft.P_hardness) >= 3 && Prop(it, Craft.P_weight) >= 3, Craft.P_weight, known ? Why.KnowHow : WishWhy(wish));
        // 재료 고르기
        var inPref = InputPrefs(action, wish, p);
        int input = PickCarried(p, inPref, -1, -1, rule?.Input);
        if (rule is not null && input >= 0 && !MeetsInput(input, rule)) input = -1;
        // (E5b 고침) 실험인데 이 재료 · 행동이 이미 아는 법(이 바람에 맞지 않는)과 같다 — 해 보나 마나다(뼈를 갈아 아는 뼈바늘을 거듭 만들던 것)
        if (rule is null && !known && input >= 0)
            foreach (var k in p.Known)
            {
                var kr = Craft.Rules[k.Rule];
                if (kr.Action == action && MeetsInput(input, kr) && !ServesWish(kr, wd)) { St.PlanWhy[12]++; return false; }
            }
        if (follow is { } fc && input >= 0 && Craft.PropIndex(fc.Key) is int pk && pk >= 0 && Prop(input, pk) <= Prop(fc.Input, pk))
        {
            // 단서: 그 속성이 더 높은 다른 재료로
            int alt = BestCarried(p, pk, Prop(fc.Input, pk) + 1, fc.Input);
            if (alt >= 0) input = alt;
            else if (StartMaterialFor(p, pk, Prop(fc.Input, pk) + 1, Why.Clue)) return true;
            else if (pk is var k2 && (k2 == Craft.P_hardness || k2 == Craft.P_toughness) && HasHardTool(p) && TryMine(p)) return true; // 바위를 캐면 더 단단한 돌이 나온다
        }
        // (E5b 고침) 아는 법인데 재료가 모자라다(바구니 — 풀 셋): 같은 것을 더 모으러 간다. 전에는 하나로 엮기 시작해 끝에 "모자람"으로 실패하고 곧바로 다시 했다
        if (rule is not null && input >= 0 && !IsPileAction(action) && rule.Uses > 1 && p.CarryCount(input) < rule.Uses)
        {
            int it0 = input;
            if (StartMaterialFor(p, it => it == it0, rule.Input.Keys[0], Why.KnowHow)) return true;
            St.PlanWhy[3]++;
            return false;
        }
        if (input < 0)
        {
            // 재료가 없다 → 그 속성이 높은 것을 얻으러
            St.PlanWhy[2]++;
            if (rule is not null) { if (StartMaterialFor(p, rule.Input, Why.KnowHow)) return true; St.PlanWhy[3]++; return false; }
            foreach (int pr in inPref) if (StartMaterialFor(p, pr, 2, WishWhy(wish))) return true;
            St.PlanWhy[3]++;
            return false;
        }
        int with = -1;
        var withPref = WithPrefs(action, wish);
        if (withPref.Length > 0 || rule is { HasWith: true })
        {
            with = PickCarried(p, withPref, input, -1, rule?.With);
            if (rule is { HasWith: true } && with >= 0 && !WithOk(with, rule)) with = -1; // 아는 법: 맞는 것만
            if (with < 0 && rule is { HasWith: true }) foreach (var c in p.Carry) if (c.Item != input && WithOk(c.Item, rule)) { with = c.Item; break; }
            if (with < 0 && rule is { HasWith: true })
            {
                St.PlanWhy[5]++;
                var wr = rule;
                return (rule.WithMax.Count == 0 ? StartMaterialFor(p, rule.With, Why.KnowHow) : StartMaterialFor(p, it => WithOk(it, wr), rule.With.Keys.First(), Why.KnowHow))
                    || (force is null && MakeFirst(p, rule.With, wish, rng));
            }
            if (with < 0 && (action == A_strike && wish == W_warmth || action == A_grind) && !known) { foreach (int pr in withPref) if (StartMaterialFor(p, pr, 3, WishWhy(wish))) return true; return false; }
        }
        int tool = -1;
        if (action == A_strike || action == A_break || action == A_grind) tool = PickTool(p, input, with);
        else if (action == A_cut) tool = BestCarried(p, Craft.P_sharp, 2, input, with);
        // (E5b) 긁기는 날 선 것으로 · 꿰매기는 뾰족한 것으로 · 묶기(사냥 도구)는 끈으로
        else if (action == A_scrape) tool = BestCarried(p, Craft.P_sharp, 2, input, with);
        else if (action == A_sew) tool = BestCarried(p, Craft.P_pierces, 2, input, with);
        else if (action == A_dig) tool = BestCarried(p, P_digs, 1, input, with); // (E5c) 땅 파는 것
        else if (action == A_twist && wish == W_hunting) tool = BestCarried(p, Craft.P_fibrous, 4, input, with);
        // 같은 돌이 둘 있으면 하나로 다른 하나를 친다
        if (tool < 0 && (action == A_strike || action == A_break) && p.CarryCount(input) >= 2 && Prop(input, Craft.P_hardness) >= 3 && Prop(input, Craft.P_edible) == 0) tool = input;
        if (rule is { HasTool: true } && (tool < 0 || !MeetsAll(tool, rule.Tool)))
        {
            tool = -1;
            foreach (var c in p.Carry) if (c.Item != input && c.Item != with && MeetsAll(c.Item, rule.Tool)) { tool = c.Item; break; }
        }
        if (rule is { HasTool: true } && tool < 0) { St.PlanWhy[6]++; return StartMaterialFor(p, rule.Tool, Why.KnowHow) || (force is null && MakeFirst(p, rule.Tool, wish, rng)); }
        // 아는 법에 불쏘시개가 들면(부싯돌 불씨): 아주 잘 타는 마른 것부터 — 없으면 풀을 말려 본다, 그것도 안 되면 하지 않는다
        if (rule is { Tinder: > 0 } && BestCarried(p, Craft.P_burns, rule.Tinder, input, with) < 0)
        {
            if (StartMaterialFor(p, Craft.P_burns, rule.Tinder, Why.KnowHow)) return true;
            int grass = BestCarried(p, Craft.P_dries, 4);
            var dry = Craft.Rules.FirstOrDefault(r => r.Result == I_dryGrass);
            if (grass >= 0 && dry is not null && p.Knows(dry.Index)) { p.PlanAction = dry.Action; p.PlanInput = grass; p.PlanWith = p.PlanTool = -1; p.PlanRule = dry.Index; SetWhy(p, Act.Craft, Why.KnowHow, wish); p.Target = here; BeginWork(p); return true; }
            St.PlanWhy[11]++;
            return false;
        }
        // (E5) 그리기: 바위 면 · 동굴 벽 · 나무 곁으로 가서
        if (action == A_draw)
        {
            // (E5 고침) 아는 법으로 그릴 때는 오래가는 동굴 벽 · 바위 면만 — 나무껍질은 늙은 장인이 다른 데가 없을 때만
            bool bark = !known || p.RecordWhy == Why.RecordOld;
            if (!FindSurface(p, life, here, out int stand, out _, out _, bark)) { St.PlanWhy[4]++; return false; }
            p.PlanAction = action; p.PlanInput = input; p.PlanWith = with; p.PlanTool = tool; p.PlanRule = rule?.Index ?? -1;
            if (stand != here && !WalkTo(p, stand)) return false;
            SetWhy(p, known ? Act.Craft : Act.Experiment, known ? Why.KnowHow : follow is not null ? Why.Clue : WishWhy(wish), wish);
            p.Target = stand;
            if (stand == here) BeginWork(p);
            return true;
        }
        if (needFire)
        {
            int ft = life.NearestFire(here % S, here / S, Now, 60, litOnly: action != A_pile); // 돌 쌓기는 꺼진 모닥불 자리에도
            if (ft < 0) { St.PlanWhy[4]++; return false; }
            int spot = Beside(p, ft);
            p.PlanAction = action; p.PlanInput = input; p.PlanWith = with; p.PlanTool = tool; p.PlanRule = rule?.Index ?? -1;
            if (spot != here && !WalkTo(p, spot)) return false;
            SetWhy(p, known ? Act.Craft : Act.Experiment, known ? Why.KnowHow : follow is not null ? Why.Clue : WishWhy(wish), wish);
            p.Target = ft;
            if (spot == here) BeginWork(p);
            return true;
        }
        p.PlanAction = action; p.PlanInput = input; p.PlanWith = with; p.PlanTool = tool; p.PlanRule = rule?.Index ?? -1;
        SetWhy(p, known ? Act.Craft : Act.Experiment, known ? Why.KnowHow : follow is not null ? Why.Clue : WishWhy(wish), wish);
        // (E4) 모닥불을 피우려는데 둘레에 돌 화덕이 있으면 그리로 가서 그 안에 피운다
        if (action == A_burn && with == I_ember && HearthNear(life, here, 30) is int hz && hz >= 0 && Cheb(hz, here) > 2)
        {
            int spot = Beside(p, hz);
            if (spot != here && WalkTo(p, spot)) { p.Target = hz; return true; }
        }
        p.Target = here;
        BeginWork(p);
        return true;
    }

    /// <summary>바람마다 "이루어졌나"를 보는 속성(만든 물건의 쓸모).</summary>
    private int[] GoalProps(int wish) =>
        wish == W_warmth ? new[] { Craft.P_warm } : wish == W_sharp ? new[] { Craft.P_sharp } : wish == W_hardTool ? new[] { Craft.P_hardness, Craft.P_sharp } :
        wish == W_container ? new[] { Craft.P_holds, Craft.P_waterproof } : wish == W_cooking ? new[] { Craft.P_edible } : wish == W_carry ? new[] { Craft.P_holds } : wish == W_record ? new[] { Craft.PropIndex("stains") } :
        wish == W_food ? new[] { Craft.P_edible } : wish == W_hunting ? new[] { Craft.P_pierces } : wish == W_clothing ? new[] { Craft.P_covers, Craft.P_warm } : wish == W_farming ? new[] { P_digs } : new[] { Craft.P_malleable };

    /// <summary>들고 있는 만든 물건 중 result 만큼 쓸모가 있는(바람의 속성이 모두 같거나 높은) 것이 있나.</summary>
    private bool AlreadyCovered(Person p, int wish, int result)
    {
        if (wish == W_cooking || wish == W_warmth || wish == W_record || wish == W_food) return false; // 먹을 것 · 불은 쓰면 없어진다, 그림은 들고 다니지 않는다
        if (wish == W_clothing) // (E5b) 이미 그만한 옷을 입었거나 남는 옷이 있다(가죽 같은 중간 것은 따지지 않는다)
        {
            if (WornCovers(p, result)) return true;
            foreach (var c in p.Carry) if (c.Item != result && Prop(c.Item, Craft.P_covers) >= 5 && Craft.WearSlot(c.Item) == Craft.WearSlot(result)) return true;
            return false;
        }
        var gp = GoalProps(wish);
        foreach (var c in p.Carry)
        {
            if (c.Item == result || !IsMade(c.Item)) continue;
            bool all = true;
            foreach (int pr in gp) if (Prop(c.Item, pr) < Prop(result, pr)) { all = false; break; }
            if (all) return true;
        }
        return false;
    }

    /// <summary>짐이 무거우면 덜 쓸모 있는 것(만든 것이 아니고, 지금 찾는 속성이 낮고, 무거운 것)을 그 자리에 내려놓는다.</summary>
    private void MakeRoom(Person p, int wantProp, int need)
    {
        var life = Life(p.Cell);
        int here = p.Tile(Now);
        for (int guard = 0; guard < 6 && CarryWeight(p) + need > CarryCapOf(p) && p.Carry.Count > 0; guard++)
        {
            int worst = -1, ws = int.MaxValue;
            foreach (var c in p.Carry)
            {
                if (c.Item == I_ember) continue;
                // (E5b 고침) 그릇 · 바구니(담는 것)는 마지막까지 든다 — 뼈 · 힘줄 · 기름에 밀려 바구니를 내려놓고 다시 엮기를 끝없이 되풀이했다
                int sc = (IsMade(c.Item) ? 40 : Craft.IsAnimal(c.Item) ? 10 : 0) + (Prop(c.Item, Craft.P_holds) > 0 ? 60 : 0) + Prop(c.Item, wantProp) * 10 + Prop(c.Item, Craft.P_edible) * 6 - Prop(c.Item, Craft.P_weight) * 3 - (c.Count > 2 ? 15 : 0);
                if (sc < ws) { ws = sc; worst = c.Item; }
            }
            if (worst < 0) break;
            int n = Math.Max(1, p.CarryCount(worst) - 1);
            p.RemoveCarry(worst, n);
            SpillSeed(p, worst, Rng(p.Cell)); // (E5c) 버린 씨
            if (life.Walk[here] && (IsMade(worst) || Craft.IsAnimal(worst))) life.Drop(here, worst, n); // 만든 것만 내려놓는다 — 흔한 돌 · 가지 · 풀은 그냥 버린다(자연으로)
        }
    }

    /// <summary>바람의 행동 중 지금 해 볼 수 있는 것(태우기는 불씨가 있을 때, 불을 쓰는 행동은 불이 가까울 때)에서 하나.</summary>
    private int PickAction(Person p, WishDef wd, ProvinceLife life, int here, Pcg32 rng)
    {
        bool fire = life.NearestFire(here % S, here / S, Now, 60) >= 0, ember = p.CarryCount(I_ember) > 0;
        var ok = new List<int>();
        foreach (int a in wd.Actions)
        {
            if (a == A_burn && !ember && !fire) continue;
            if ((a == A_roast || a == A_melt || a == A_boil || a == A_pile || a == A_smoke) && !fire) continue;
            ok.Add(a);
        }
        return ok.Count == 0 ? -1 : ok[rng.NextInt(ok.Count)];
    }

    private bool NeedsFire(Person p, ProvinceLife life) => life.NearestFire(p.Tile(Now) % S, p.Tile(Now) / S, Now, 25) < 0;
    private int NearestFireAny(ProvinceLife life, int here) => life.NearestFire(here % S, here / S, Now, 60, litOnly: false);

    /// <summary>단서를 기억한다. 같은 일 · 같은 까닭의 단서는 하나만(가장 새것).</summary>
    private static void AddClue(Person p, Clue c)
    {
        p.Clues.RemoveAll(x => x.Rule == c.Rule && x.Key == c.Key);
        p.Clues.Add(c);
        if (p.Clues.Count > Person.ClueMemory) p.Clues.RemoveAt(0);
    }

    private void TakePileStones(ProvinceLife life, int tile, int n)
    {
        if (!life.Items.TryGetValue(tile, out var list)) return;
        for (int k = 0; k < list.Count && n > 0; k++)
        {
            var c = list[k];
            if (Prop(c.Item, Craft.P_hardness) < 3 || Prop(c.Item, Craft.P_weight) < 3) continue;
            int t = Math.Min(n, c.Count); n -= t;
            list[k] = (c.Item, c.Count - t);
        }
        list.RemoveAll(c => c.Count <= 0);
        if (list.Count == 0) life.RemoveItems(tile);
    }

    /// <summary>(E4) 불 타일 땅에 모아 둔 무거운 돌(화덕 쌓을 돌) 수.</summary>
    private int PileStonesAt(ProvinceLife life, int fireTile)
    {
        if (fireTile < 0 || !life.Items.TryGetValue(fireTile, out var list)) return 0;
        int n = 0;
        foreach (var c in list) if (Prop(c.Item, Craft.P_hardness) >= 3 && Prop(c.Item, Craft.P_weight) >= 3) n += c.Count;
        return n;
    }

    private int CountCarried(Person p, int prop, int min, int prop2, int min2)
    {
        int n = 0;
        foreach (var c in p.Carry) if (Prop(c.Item, prop) >= min && Prop(c.Item, prop2) >= min2) n += c.Count;
        return n;
    }

    private bool MeetsInput(int item, CraftRule r)
    {
        foreach (var (pr, mn) in r.Input) if (Prop(item, pr) < mn) return false;
        foreach (var (pr, mx) in r.InputMax) if (Prop(item, pr) > mx) return false;
        return true;
    }

    /// <summary>주 재료를 세계(자원 아이템)에서 구할 수 있나 — 만든 것만 맞으면 false.</summary>
    private bool InputInWorld(CraftRule r)
    {
        for (int i = 0; i < Craft.Catalog.Items.Count; i++) if (Craft.Catalog.Items[i].Kind == "" && MeetsInput(i, r)) return true;
        return false;
    }

    /// <summary>(E5b) 아는 법에 쓸 도구 · 둘째 재료(만든 것 — 바늘 · 막대 · 끈)가 없다: 그것을 만드는 법을 알고 재료를 들고 있으면 그것부터 만든다.</summary>
    private bool MakeFirst(Person p, Conds conds, int wish, Pcg32 rng)
    {
        foreach (var k in p.Known)
        {
            var r = Craft.Rules[k.Rule];
            if (r.Result < 0 || !MeetsAll(r.Result, conds) || !HasInputFor(p, r)) continue;
            if (Plan(p, wish, known: true, rng, force: r)) return true;
        }
        return false;
    }

    private bool HasInputFor(Person p, CraftRule r)
    {
        foreach (var c in p.Carry)
        {
            if (c.Count < Math.Max(1, r.Uses)) continue; // (E5b) 셋이 드는 것(털가죽 옷 — 가죽 셋)은 셋 다 있어야
            bool ok = true;
            foreach (var (pr, mn) in r.Input) if (Prop(c.Item, pr) < mn) { ok = false; break; }
            if (ok) foreach (var (pr, mx) in r.InputMax) if (Prop(c.Item, pr) > mx) { ok = false; break; }
            if (ok) return true;
        }
        return false;
    }

    /// <summary>행동 × 바람에 따라 주 재료로 고를 때 보는 속성(앞이 중요).</summary>
    private int[] InputPrefs(int action, int wish, Person p)
    {
        var c = Craft;
        if (action == A_strike)
        {
            if (wish == W_warmth) return new[] { c.P_hardness };
            if (wish == W_shiny && BestCarried(p, c.P_malleable, 4) >= 0) return new[] { c.P_malleable };
            if (wish == W_warmth || BestCarried(p, c.P_splits, 3) < 0 && BestCarried(p, c.P_burns, 3) >= 0 && HasSharpTool(p) && wish == W_warmth) return new[] { c.P_burns };
            return new[] { c.P_sharpens, c.P_splits };
        }
        if (action == A_twist && wish == W_hunting) return new[] { c.P_sharp, c.P_pierces };
        if (action == A_twist && wish == W_clothing) return new[] { c.P_covers, c.P_toughness };
        if (action == A_grind && wish == W_clothing) return new[] { c.P_sharpens, c.P_splits, c.P_toughness };
        if (action == A_dry && wish == W_clothing) return new[] { c.P_covers, c.P_dries };
        if (action == A_dry && wish == W_food) return new[] { c.P_meat, c.P_edible };
        if (action == A_roast && wish == W_food) return new[] { c.P_meat, c.P_edible };
        if (action == A_roast && wish == W_hunting) return new[] { c.P_toughness };
        if (action == A_break) return wish == W_cooking ? new[] { c.P_hardness, c.P_edible } : new[] { c.P_hardness, c.P_toughness };
        if (action == A_grind) return wish == W_warmth ? new[] { c.P_burns, c.P_toughness } : new[] { c.P_sharpens, c.P_sharp, c.P_hardness };
        if (action == A_cut) return new[] { c.P_toughness, c.P_splits };
        if (action == A_tear) return new[] { c.P_tears, c.P_fibrous };
        if (action == A_twist) return wish == W_hardTool ? new[] { c.P_sharp, c.P_hardness } : new[] { c.P_fibrous };
        if (action == A_weave) return new[] { c.P_bends, c.P_fibrous, c.P_tears };
        if (action == A_shape) return new[] { c.P_moldable };
        if (action == A_dry) return wish == W_cooking ? new[] { c.P_edible } : wish == W_container ? new[] { c.P_moldable, c.P_fires_hard } : new[] { c.P_fibrous, c.P_dries };
        if (action == A_roast) return wish == W_cooking ? new[] { c.P_edible } : wish == W_container ? new[] { c.P_fires_hard } : new[] { c.P_toughness };
        if (action == A_burn) return new[] { c.P_burns };
        if (action == A_melt) return new[] { c.P_melts, c.P_reduces };
        if (action == A_boil) return new[] { c.P_edible };
        if (action == A_pile) return new[] { c.P_hardness, c.P_weight };
        if (action == A_draw) return new[] { c.PropIndex("stains") };
        if (action == A_scrape) return new[] { c.P_covers, c.P_rots };
        if (action == A_smoke) return wish == W_clothing ? new[] { c.P_covers, c.P_toughness } : new[] { c.P_meat, c.P_edible };
        if (action == A_sew) return new[] { c.P_covers, c.P_toughness };
        if (action == A_dig || action == A_bury) return new[] { P_sprouts, c.P_edible }; // (E5c) 땅에 묻을 씨
        return new[] { c.P_edible };
    }

    private int[] WithPrefs(int action, int wish)
    {
        var c = Craft;
        if (action == A_strike) return wish == W_warmth ? new[] { c.P_hardness } : Array.Empty<int>();
        if (action == A_grind) return wish == W_warmth ? new[] { c.P_burns } : new[] { c.P_hardness };
        if (action == A_twist && wish == W_hardTool) return new[] { c.P_bends, c.P_toughness };
        if (action == A_twist && wish == W_hunting) return new[] { c.P_toughness, c.P_hardness };
        if (action == A_sew) return new[] { c.P_fibrous, c.P_toughness };
        if (action == A_boil) return new[] { c.P_holds, c.P_waterproof };
        if (action == A_burn) return new[] { c.P_warm };
        return Array.Empty<int>();
    }

    /// <summary>들고 있는 것 중 prefs 속성이 높은 것(앞 속성이 우선, 둘 다 0 이면 없음). must = 규칙 조건(있으면 그것을 만족하는 것 우선).</summary>
    /// <summary>(E4) 치는 · 가는 도구: 먹을 것이 아니고, 다룰 재료보다 단단하거나 같은 것 가운데 가장 단단한 것. 없으면 −1.</summary>
    /// <summary>(E5b) 둘째 재료 조건(최소 · 최대)이 모두 맞나.</summary>
    private bool WithOk(int item, CraftRule r)
    {
        if (!MeetsAll(item, r.With)) return false;
        foreach (var (pr, mx) in r.WithMax) if (Prop(item, pr) > mx) return false;
        return true;
    }

    private bool MeetsAll(int item, Conds conds)
    {
        foreach (var (pr, mn) in conds) if (Prop(item, pr) < mn) return false;
        return true;
    }

    private int PickTool(Person p, int input, int with)
    {
        int need = Math.Max(3, input >= 0 ? Prop(input, Craft.P_hardness) : 0);
        int best = -1, bv = 0;
        foreach (var c in p.Carry)
        {
            if (c.Item == input || c.Item == with || Prop(c.Item, Craft.P_edible) > 0) continue;
            int v = Prop(c.Item, Craft.P_hardness);
            if (v >= need && v > bv) { bv = v; best = c.Item; }
        }
        return best;
    }

    private int PickCarried(Person p, int[] prefs, int except1, int except2, Conds? must)
    {
        int best = -1, bs = 0;
        foreach (var c in p.Carry)
        {
            if (c.Item == except1 || c.Item == except2) continue;
            int s = 0, k = prefs.Length;
            foreach (int pr in prefs) { s += Prop(c.Item, pr) * k * 4; k--; }
            // (E4) 먹을 것은 재료로 잘 쓰지 않는다(익히기가 아니면) — 도토리로 돌을 치는 따위를 줄인다
            if (Prop(c.Item, Craft.P_edible) > 0 && Array.IndexOf(prefs, Craft.P_edible) < 0) { s -= 40; if (s <= 0 && must is null) continue; }
            if (must is not null)
            {
                bool ok = true;
                foreach (var (pr, mn) in must) if (Prop(c.Item, pr) < mn) { ok = false; break; }
                if (ok) s += 100; else if (prefs.Length == 0) continue;
            }
            if (s > bs) { bs = s; best = c.Item; }
        }
        return best;
    }

    private Why WishWhy(int wish) =>
        wish == W_record ? Why.WantRecord : wish == W_warmth ? Why.WantWarmth : wish == W_sharp ? Why.WantSharp : wish == W_hardTool ? Why.WantHardTool : wish == W_container ? Why.WantContainer :
        wish == W_cooking ? Why.WantCooking : wish == W_carry ? Why.WantCarry : wish == W_food ? Why.WantFood : wish == W_hunting ? Why.WantHunting : wish == W_clothing ? Why.WantClothing : wish == W_farming ? Why.WantFarming : Why.WantShiny;

    /// <summary>제자리에서 일을 시작한다(걸리는 시간 = 규칙의 시간 또는 실험 45~75분, 손재주 · 근면에 따라).</summary>
    private void BeginWork(Person p)
    {
        var rng = Rng(p.Cell);
        long min = p.PlanRule >= 0 ? Craft.Rules[p.PlanRule].TimeMin : 45 + rng.NextInt(30);
        long dur = min * GameTime.TicksPerHour / 60 * WorkPercent(p) / 100;
        DoStill(p, Math.Max(600, dur));
    }

    // ───────── 재료 얻기 ─────────

    /// <summary>속성 prop 이 min 이상인 재료를 가까운 곳에서 얻으러 간다(주울 것 · 오브젝트의 다루는 법). 못 찾으면 false.</summary>
    private bool StartMaterialFor(Person p, int prop, int min, Why why)
    {
        long key = 1 + prop * 16L + min;
        if (p.MissKey == key && Now - p.MissTick < MissMemory) return false; // 방금 둘레에 없었다
        if (StartMaterialFor(p, it => Prop(it, prop) >= min, prop, why)) return true;
        p.MissKey = key; p.MissTick = Now;
        return false;
    }

    /// <summary>(E4) 재료를 찾았는데 둘레에 없었으면 이만큼은 같은 것을 다시 찾지 않는다(빠르기 — 사람이 기억한다).</summary>
    private const long MissMemory = 12 * GameTime.TicksPerHour;

    /// <summary>조건(속성 → 최소값)을 모두 맞는 재료를 얻으러 간다.</summary>
    private bool StartMaterialFor(Person p, Conds conds, Why why)
    {
        if (conds.Count == 0) return false;
        long key = 1000;
        foreach (var (pr, mn) in conds) key = key * 131 + pr * 16 + mn;
        if (p.MissKey == key && Now - p.MissTick < MissMemory) return false;
        if (StartMaterialFor(p, it => { foreach (var (pr, mn) in conds) if (Prop(it, pr) < mn) return false; return true; }, conds.Keys.First(), why)) return true;
        p.MissKey = key; p.MissTick = Now;
        return false;
    }

    private bool StartMaterialFor(Person p, Func<int, bool> itemOk, int prop, Why why)
    {
        var life = Life(p.Cell);
        if (CarryWeight(p) + 3 > CarryCapOf(p)) { AddWish(p, W_carry, 8); MakeRoom(p, prop, 3); if (CarryWeight(p) + 1 > CarryCapOf(p)) return false; }
        p.Position(Now, out int px, out int py);
        int hx = px / 1000, hy = py / 1000;
        // 1) 땅에 놓인 물건(죽은 사람이 남긴 것 등) 바로 곁 — 타일 번호 순으로 처음 맞는 것
        bool GroundTile(int t, List<(int Item, int Count)> list)
        {
            if (life.Fires.ContainsKey(t)) return false; // 불가에 모아 둔 돌은 화덕감이다
            if (Cheb(t, hy * S + hx) > 20 || !life.Paths.Reachable(hy * S + hx, t)) return false;
            foreach (var (item, _) in list)
                if (itemOk(item))
                {
                    if (!WalkTo(p, t)) return false;
                    SetWhy(p, Act.Material, why); p.Target = t; p.TargetB = -2; p.PlanItem = item;
                    return true;
                }
            return false;
        }
        if (life.Items.Count <= 64) { foreach (var (t, list) in life.Items) if (GroundTile(t, list)) return true; }
        else
        {
            // (S1) 물건 놓인 자리가 많으면 둘레(20타일) 네모만 번호 순으로 본다 — 결과는 모두 훑을 때와 같다
            for (int y = Math.Max(0, hy - 20); y <= Math.Min(S - 1, hy + 20); y++)
                for (int x = Math.Max(0, hx - 20); x <= Math.Min(S - 1, hx + 20); x++)
                {
                    int t = y * S + x;
                    if (life.HasItems(t) && GroundTile(t, life.Items[t])) return true;
                }
        }
        // (E5) 불 자리의 재(그림 물감) — 타고 난 불 자리에서 긁어모은다
        if (I_ash >= 0 && itemOk(I_ash))
        {
            int best = -1; long bd2 = 26L * 26;
            foreach (var (t, f) in life.Fires)
            {
                if (f.Lit(Now) && Now - f.Stamp < GameTime.TicksPerHour) continue;
                long d = ProvinceLife.D2(t, hx, hy);
                if (d < bd2 && life.BesideRegion(t, life.Region[hy * S + hx])) { bd2 = d; best = t; }
            }
            if (best >= 0)
            {
                int spot = Beside(p, best);
                bool there = spot == hy * S + hx;
                if (there || WalkTo(p, spot)) { SetWhy(p, Act.Material, why); p.Target = best; p.TargetB = -3; p.PlanItem = I_ash; if (there) ArriveMaterial(p); return true; }
            }
        }
        // 2) 주울 것(돌 · 도토리 — 몫이 있음)
        bool sharp = HasSharpTool(p);
        int bestTile = -1; long bd = long.MaxValue; int bestUse = -1, bestItem = -1;
        int carryTile = life.NearestCarryWhere(hx, hy, Now, 30, h => itemOk(h.ItemIndex));
        if (carryTile >= 0) { bestTile = carryTile; bd = ProvinceLife.D2(carryTile, hx, hy); bestUse = -1; bestItem = life.HarvestAt(carryTile)!.ItemIndex; }
        // 3) 오브젝트의 다루는 법(베기는 날카로운 도구가 있어야)
        var cat = Craft.Catalog;
        var uses = ObjectUses;
        int mt = life.NearestMaterial(hx, hy, 30, o =>
        {
            foreach (var (kind, yields) in uses[o])
            {
                if (kind == 1 && !sharp) continue;
                if (kind == 2) continue; // 먹을 것 · 주울 것은 몫으로
                foreach (int it in yields) if (itemOk(it)) return true;
            }
            return false;
        });
        if (mt >= 0 && ProvinceLife.D2(mt, hx, hy) < bd)
        {
            int o = life.Map.ObjectAt(mt);
            for (int ui = 0; ui < cat.Objects[o].Uses.Count && bestUse < 0; ui++)
            {
                var u = cat.Objects[o].Uses[ui];
                if (u.Action.Id == "cut" && !sharp || u.Action.Id == "pick" || u.Action.Id == "gather") continue;
                foreach (var y in u.Yields) { int it = Craft.ItemIndex(y.Id); if (itemOk(it)) { bestTile = mt; bestUse = ui; bestItem = it; break; } }
            }
        }
        St.PlanWhy[bestTile < 0 ? 15 : 14]++;
        if (bestTile < 0)
        {
            // 베어야 얻는 것(나무)인데 도구가 없다 → 날카로움이 아쉽다
            if (!sharp && life.NearestMaterial(hx, hy, 30, o => uses[o].Any(u => u.Kind == 1 && u.Yields.Any(itemOk))) >= 0) AddWish(p, W_sharp, 25);
            return false;
        }
        int goal = bestUse < 0 ? bestTile : Beside(p, bestTile);
        if (!WalkTo(p, goal)) return false;
        SetWhy(p, Act.Material, why);
        p.Target = bestTile; p.TargetB = bestUse; p.PlanItem = bestItem;
        return true;
    }

    /// <summary>(S1) 오브젝트마다 다루는 법: (0 그 밖 · 1 베기 · 2 따기/줍기, 얻는 아이템 번호들) — 판단마다 이름을 찾지 않게 한 번만.</summary>
    private (int Kind, int[] Yields)[][] ObjectUses => _objectUses ??= Craft.Catalog.Objects.Select(o => o.Uses.Select(u =>
        (u.Action.Id == "cut" ? 1 : u.Action.Id is "pick" or "gather" ? 2 : 0, u.Yields.Select(y => Craft.ItemIndex(y.Id)).ToArray())).ToArray()).ToArray();
    private (int Kind, int[] Yields)[][]? _objectUses;

    /// <summary>재료 자리에 닿았다: 조금 걸린 뒤 얻는다.</summary>
    private void ArriveMaterial(Person p) => DoStill(p, (p.TargetB >= 0 ? 300L : 200L) * WorkPercent(p) / 100);

    private void FinishMaterial(Person p)
    {
        int w0 = CarryWeight(p), n0 = p.Carry.Sum(c => c.Count);
        FinishMaterialInner(p);
        St.PlanWhy[p.Carry.Sum(c => c.Count) > n0 ? 10 : 11]++;
        _ = w0;
    }

    private void FinishMaterialInner(Person p)
    {
        var life = Life(p.Cell);
        int t = p.Target;
        if (p.TargetB == -2)
        {
            // 땅에 놓인 것을 집는다
            if (life.Items.TryGetValue(t, out var list))
            {
                int k = list.FindIndex(c => c.Item == p.PlanItem);
                if (k >= 0)
                {
                    int n = Math.Min(list[k].Count, 3);
                    p.AddCarry(p.PlanItem, n);
                    if (list[k].Count == n) list.RemoveAt(k); else list[k] = (p.PlanItem, list[k].Count - n);
                    if (list.Count == 0) life.RemoveItems(t);
                }
            }
            return;
        }
        if (p.TargetB == -3)
        {
            if (life.Fires.ContainsKey(t)) p.AddCarry(I_ash, 2);
            return;
        }
        if (p.TargetB < 0)
        {
            if (life.HarvestAt(t) is { } h && life.Take(t, 1, Now) > 0) p.AddCarry(h.ItemIndex, 1);
            return;
        }
        int o = life.Map.ObjectAt(t);
        if (o < 0 || p.TargetB >= Craft.Catalog.Objects[o].Uses.Count) return;
        var use = Craft.Catalog.Objects[o].Uses[p.TargetB];
        var rng = Rng(p.Cell);
        foreach (var y in use.Yields)
        {
            int it = Craft.ItemIndex(y.Id);
            if (it < 0) continue;
            p.AddCarry(it, use.Action.Id == "cut" ? 2 : 1);
        }
        if (use.Action.Id == "cut") WearTool(p, BestTool(p, Craft.P_sharp, 2), rng);
    }

    // ───────── 실험 · 만들기의 끝 ─────────

    /// <summary>실험 · 만들기가 끝났다: 판정하고 결과를 남긴다.</summary>
    internal void FinishWork(Person p)
    {
        var life = Life(p.Cell);
        var rng = Rng(p.Cell);
        int here = p.Tile(Now);
        int action = p.PlanAction, input = p.PlanInput, with = p.PlanWith, tool = p.PlanTool, wish = p.PlanWish;
        p.LastExperiment = Now;
        p.Experiments++;
        St.PlanWhy[12]++;
        // (E4) 쌓기: 들고 온 무거운 돌을 불가에 내려놓는다. 넷이 모이면 둘러쌓아 본다.
        bool pile = action == A_pile && p.Target >= 0 && life.Fires.ContainsKey(p.Target);
        if (pile)
        {
            for (int k = p.Carry.Count - 1; k >= 0; k--)
            {
                var c = p.Carry[k];
                if (Prop(c.Item, Craft.P_hardness) >= 3 && Prop(c.Item, Craft.P_weight) >= 3) { life.Drop(p.Target, c.Item, c.Count); p.RemoveCarry(c.Item, c.Count); }
            }
            if (PileStonesAt(life, p.Target) < 4) { St.PlanWhy[18]++; return; } // 아직 모자라다 — 더 날라 온다
            input = life.Items[p.Target].First(c => Prop(c.Item, Craft.P_hardness) >= 3 && Prop(c.Item, Craft.P_weight) >= 3).Item;
        }
        else if (action < 0 || input < 0 || p.CarryCount(input) == 0) { St.PlanWhy[13]++; return; }
        if (with >= 0 && p.CarryCount(with) == 0) with = -1;
        if (tool >= 0 && p.CarryCount(tool) < (tool == input ? 2 : 1)) tool = -1;
        int heat = life.HeatAt(here, Now);
        int tinderItem = BestCarried(p, Craft.P_burns, 1, input, with);
        int tinder = tinderItem >= 0 ? Prop(tinderItem, Craft.P_burns) : 0;
        bool known = p.PlanRule >= 0;
        int skill = known ? Craft.Rules[p.PlanRule].Skill : -1;
        int bonus = (Ability(p, PeopleData.Dex) - 5) * 3 + (Ability(p, PeopleData.Int) - 5) * 2 + (skill >= 0 ? p.SkillLevel(skill) * 3 : 0) + (known ? 30 : 0);
        // (E5) 흐려진 법(아는 정도 60 아래)은 순서를 틀리기도 한다
        int level = known ? p.KnowLevel(p.PlanRule) : 0;
        if (known && level < 60 && rng.NextInt(100) >= 40 + level)
        {
            St.Crafts++; St.CraftFails++; St.FadedFails++;
            UseKnowledge(p, p.PlanRule); // 다시 해 보며 되살아난다
            AddThought(p, H_failedTry);
            return;
        }
        if (known) bonus = bonus - 30 + 10 + 20 * level / 100;
        var outcome = Craft.Judge(action, PropsOf(input), with >= 0 ? PropsOf(with) : null, tool >= 0 ? PropsOf(tool) : null, heat, tinder, rng.NextInt(100), bonus, known ? Craft.Rules[p.PlanRule] : null);
        if (known) St.Crafts++; else St.Experiments++;
        WearTool(p, tool, rng);
        if (outcome.Rule is null) { AddThought(p, H_failedTry); return; }
        var rule = outcome.Rule;
        if (outcome.Success && !pile && rule.Uses > p.CarryCount(input))
        {
            // 재료가 모자랐다. (E5b) 셋 이상 드는 것(털가죽 옷 · 바구니)은 하나로 해 보다가도 "이렇게 하면 되겠다"는 것을 알아낸다 — 만들지는 못한다
            if (!known && rule.Uses >= 3 && !p.Knows(rule.Index)) { Learn(p, rule, wish, input, with, -1); St.ShortDiscoveries++; return; }
            AddThought(p, H_failedTry); return;
        }
        if (outcome.Success)
        {
            // 재료를 쓴다
            if (pile) TakePileStones(life, p.Target, rule.Uses);
            else if (rule.Uses > 0) p.RemoveCarry(input, Math.Min(rule.Uses, p.CarryCount(input)));
            if (rule.WithUses > 0 && with >= 0) p.RemoveCarry(with, Math.Min(rule.WithUses, p.CarryCount(with)));
            if (rule.Tinder > 0 && tinderItem >= 0) p.RemoveCarry(tinderItem, 1);
            if (rule.ToolUses > 0 && tool >= 0) p.RemoveCarry(tool, Math.Min(rule.ToolUses, p.CarryCount(tool)));
            if (rule.Skill >= 0) GainSkill(p, rule.Skill, known ? 10 : 25);
            St.RuleDone[rule.Index]++;
            ApplyResult(p, rule, here, rng);
            if (!known || !p.Knows(rule.Index)) Learn(p, rule, wish, input, with, -1);
            else { AddThought(p, H_madeThing); UseKnowledge(p, rule.Index); }
            // 바람이 조금 풀린다
            if (wish >= 0) p.Wish[wish] = Math.Max(0, p.Wish[wish] - 40);
            TeachWatchers(p, rule, wish, input, with);
            return;
        }
        if (outcome.IsPartial)
        {
            var pd = outcome.Partial!;
            if (pd.Result >= 0)
            {
                p.RemoveCarry(input, 1);
                p.AddCarry(pd.Result, 1);
            }
            if (known)
            {
                // 아는 법인데 이번엔 잘 안 됐다 — 새 단서가 아니라 그냥 헛손질(따로 센다)
                St.CraftFails++;
                St.RuleFail[rule.Index]++; St.FailKeys[rule.Id + "/" + pd.Key] = St.FailKeys.GetValueOrDefault(rule.Id + "/" + pd.Key) + 1;
                AddThought(p, H_failedTry);
                return;
            }
            // 새 단서면 "거의 될 뻔", 이미 아는 단서를 또 겪었으면 "같은 헛수고"(따로 센다)
            bool again = false; foreach (var cl in p.Clues) if (cl.Rule == rule.Index && cl.Key == pd.Key) { again = true; break; }
            if (again)
            {
                // 같은 헛수고를 또 했다 — 그 바람을 조금 접고 하루쯤은 실험을 쉰다(지쳐서)
                St.PartialRepeats++;
                if (wish >= 0) p.Wish[wish] = Math.Max(0, p.Wish[wish] - 15);
                p.LastExperiment = Now + GameTime.TicksPerDay;
            }
            else St.Partials++;
            AddClue(p, new Clue { Rule = rule.Index, Key = pd.Key, Tick = Now, Input = input });
            // (E4) 곁(6타일)에서 본 사람도 그 단서를 얻는다
            foreach (var q in life.People)
                if (q.Id != p.Id && !q.Dead && !q.Asleep && q.Stage(Now) >= LifeStage.Child && Cheb(q.Tile(Now), here) <= 6 && !q.Knows(rule.Index))
                    AddClue(q, new Clue { Rule = rule.Index, Key = pd.Key, Tick = Now, Input = input });
            if (InProvince) Defer(new PendingOp { Kind = PendingOp.Kinds.Partial, A = rule.Index, S = pd.Text }); // (S1) 세계의 "거의 될 뻔" 목록은 맞추는 시각에
            else PartialWorld(rule.Index, pd.Text);
            AddThought(p, H_halfSuccess);
            AddBio(p, BioKind.PartialSuccess, rule.Index, input);
            if (rule.Skill >= 0) GainSkill(p, rule.Skill, 6);
            return;
        }
        AddThought(p, H_failedTry);
    }

    /// <summary>(E4) 아직 세계에 없는(또는 사라진) 발견의 "거의 될 뻔"을 세계 목록에 남긴다.</summary>
    private void PartialWorld(int rule, string text)
    {
        if (Discoveries.ContainsKey(rule) && !Discoveries[rule].Lost) return;
        if (!Partials.TryGetValue(rule, out var pr)) Partials[rule] = pr = (0, new List<string>());
        pr.Count++;
        if (pr.Texts.Count < 4 && !pr.Texts.Contains(text)) pr.Texts.Add(text);
        Partials[rule] = pr;
    }

    private void GainSkill(Person p, int skill, int xp)
    {
        if (skill < 0) return;
        if (p.HasTrait(T_nervous)) xp = xp * 11 / 10;
        p.SkillXp[skill] += xp;
    }

    /// <summary>결과를 세계에 놓는다: 물건은 들고(무게가 넘으면 땅에), 불씨는 바로 불을 피워 보고, 모닥불 · 화덕은 타일에.</summary>
    private void ApplyResult(Person p, CraftRule rule, int here, Pcg32 rng)
    {
        var life = Life(p.Cell);
        int res = rule.Result;
        if (res == I_ember)
        {
            p.AddCarry(I_ember, 1); p.EmberTick = Now;
            TryLightWithEmber(p, here, rng);
            return;
        }
        if (res == I_campfire)
        {
            int fuel = p.PlanInput;
            long dur = FuelTicks(fuel);
            if (p.CarryCount(I_ember) > 0) { p.RemoveCarry(I_ember, 1); p.EmberTick = -1; }
            int hh = HearthNear(life, here, 2); // (E4) 곁에 돌 화덕이 있으면 그 안에 피운다
            life.LightFire(hh >= 0 ? hh : here, hh >= 0 ? (byte)2 : (byte)1, dur, Now, p.Id);
            St.FiresLit++;
            AddBio(p, BioKind.LitFire, -1, here);
            return;
        }
        if (res == I_hearth)
        {
            int ft = p.Target >= 0 && life.Fires.ContainsKey(p.Target) ? p.Target : life.NearestFire(here % S, here / S, Now, 3);
            if (ft >= 0)
            {
                var f = life.Fires[ft]; f.Kind = 2; life.Fires[ft] = f;
                AddBio(p, BioKind.MadeThing, -1, I_hearth);
            }
            return;
        }
        if (res == I_painting)
        {
            MakePainting(p, here, rng);
            return;
        }
        if (res == I_field || res == I_sownPatch)
        {
            // (E5c) 그 자리(또는 바로 곁)에 씨를 묻었다 — 일군 밭이면 Tilled
            int t = FreeForPlot(life, here) ? here : -1;
            for (int k = 0; k < 8 && t < 0; k++) { int x = here % S + DX8[k], y = here / S + DY8[k]; if ((uint)x < S && (uint)y < S && FreeForPlot(life, y * S + x)) t = y * S + x; }
            int seed = p.PlanInput;
            if (t >= 0 && seed >= 0 && IsSeed(seed))
            {
                var q = new Plot { Tile = t, Tilled = res == I_field, Stage = PlotStage.Sown, Crop = seed, Stamp = Now, LastTend = Now, Planter = p.Id };
                q.Due = Now + SproutDays * GameTime.TicksPerDay;
                life.AddPlot(q);
                if (q.Tilled) St.Tilled++;
                St.Sown++;
                if (life.FarmSince < 0) life.FarmSince = Now;
                if (life.Stash < 0) life.Stash = ChooseStash(life, here);
            }
            return;
        }
        if (res == I_charcoal && p.Target >= 0 && life.Fires.TryGetValue(p.Target, out var hf) && hf.Kind == 2)
        {
            // 가둔 불에서 태운 나무: 숯이 남고 불도 좀 더 탄다
            hf.Fuel += 2 * GameTime.TicksPerHour; life.Fires[p.Target] = hf;
        }
        int count = rule.Count;
        if (CarryWeight(p) + Math.Max(1, Prop(res, Craft.P_weight)) * count > CarryCapOf(p) + 6) life.Drop(here, res, count);
        else p.AddCarry(res, count);
        AddBio(p, BioKind.MadeThing, -1, res);
        if (Craft.WearSlot(res) > 0) WearBest(p); // (E5b) 지은 옷은 입는다
    }

    /// <summary>땔감 하나가 타는 시간.</summary>
    private long FuelTicks(int item)
    {
        int b = Prop(item, Craft.P_burns), w = Prop(item, Craft.P_weight);
        if (item == I_charcoal) return 8 * GameTime.TicksPerHour;
        return (long)((1 + w * 2) * (b >= 4 ? 1.0 : 0.6) * GameTime.TicksPerHour);
    }

    /// <summary>들고 있는 불씨로 불을 피운다(땔감이 있어야). 피웠으면 true.</summary>
    /// <summary>(E4) r 타일 안의 돌 화덕(타든 꺼졌든), 같은 이어진 땅 곁. 없으면 −1.</summary>
    private int HearthNear(ProvinceLife life, int here, int r)
    {
        int best = -1, bd = int.MaxValue;
        foreach (var (t, f) in life.Fires)
        {
            if (f.Kind != 2) continue;
            int d = Cheb(t, here);
            if (d <= r && d < bd && life.BesideRegion(t, life.Region[here])) { bd = d; best = t; }
        }
        return best;
    }

    private bool TryLightWithEmber(Person p, int here, Pcg32 rng)
    {
        if (p.CarryCount(I_ember) == 0) return false;
        int fuel = BestCarried(p, Craft.P_burns, 3, I_ember);
        if (fuel < 0) return false;
        var life = Life(p.Cell);
        if (!life.Walk[here]) return false;
        if (R_campfire is null) return false;
        var outcome = Craft.Judge(A_burn, PropsOf(fuel), PropsOf(I_ember), null, life.HeatAt(here, Now), 0, 0, 0);
        if (!outcome.Success) { AddClue(p, new Clue { Rule = R_campfire.Index, Key = outcome.FailedKey, Tick = Now, Input = fuel }); return false; }
        p.RemoveCarry(fuel, 1);
        p.RemoveCarry(I_ember, 1); p.EmberTick = -1;
        int hh = HearthNear(life, here, 2);
        life.LightFire(hh >= 0 ? hh : here, hh >= 0 ? (byte)2 : (byte)1, FuelTicks(fuel) + GameTime.TicksPerHour, Now, p.Id);
        St.FiresLit++;
        AddBio(p, BioKind.LitFire, -1, here);
        if (!p.Knows(R_campfire.Index)) Learn(p, R_campfire, W_warmth, fuel, I_ember, -1);
        else UseKnowledge(p, R_campfire.Index);
        TeachWatchers(p, R_campfire, W_warmth, fuel, I_ember);
        return true;
    }

    // ───────── 아는 것 · 발견 ─────────

    public void Learn(Person p, CraftRule rule, int wish, int input, int with, int from, Via via = Via.Self)
    {
        if (p.Knows(rule.Index)) return;
        if (via == Via.Self && from >= 0) via = Via.Watch;
        p.Known.Add(new Knowledge { Rule = rule.Index, Tick = Now, Wish = wish, Input = input, With = with, LearnedFrom = from, Level = StartLevel(via), LastUse = Now, Via = via });
        p.Learning.RemoveAll(l => l.Rule == rule.Index);
        if (via == Via.Self)
        {
            St.Discoveries++;
            AddThought(p, H_discovered);
            AddBio(p, BioKind.Discovered, rule.Index, wish);
        }
        else if (via == Via.Watch)
        {
            St.Watched++;
            AddBio(p, BioKind.LearnedWatching, from, rule.Index);
        }
        else if (via == Via.Teach)
        {
            AddBio(p, BioKind.LearnedTeaching, from, rule.Index);
            AddThought(p, H_learned, from);
        }
        else
        {
            AddBio(p, BioKind.LearnedPainting, from, rule.Index);
            AddThought(p, H_learned);
        }
        // (S1) 세계의 발견 목록 · 무리마다 처음 · 말의 낱말 · 별명은 세계 전체의 것 → 지방 계산 중이면 맞추는 시각에
        if (InProvince) Defer(new PendingOp { Kind = PendingOp.Kinds.Learned, A = p.Id, B = rule.Index, C = (int)via, D = wish, E = from });
        else LearnWorld(p, rule, via, wish, from);
    }

    /// <summary>(S1) 알게 된 일의 세계 쪽: 세계에서 처음(발견 목록 · 알림 · 역사 · 별명 · 남겨 두고 싶음) · 되찾음 · 낱말 · 무리에서 처음.</summary>
    private void LearnWorld(Person p, CraftRule rule, Via via, int wish, int from)
    {
        bool worldFirst = !Discoveries.TryGetValue(rule.Index, out var d);
        long gkey = (long)p.LangId * 1000 + rule.Index;
        bool groupFirst = !GroupFirst.ContainsKey(gkey);
        if (via == Via.Self)
        {
            if (worldFirst)
            {
                d = new Discovery { Rule = rule.Index, Person = p.Id, Cell = p.Cell, Wish = wish, Tick = Now, LangId = p.LangId };
                if (Partials.TryGetValue(rule.Index, out var pr)) { d.PartialsBefore = pr.Count; d.PartialTexts.AddRange(pr.Texts); Partials.Remove(rule.Index); }
                Discoveries[rule.Index] = d;
                Notify(WorldEventKind.Discovery, p, rule.Index, $"세계에서 처음: {Hangul.I(DisplayName(p))} {Made(rule)} — {Hangul.I(WishName(wish))} 아쉬워 {Hangul.Eul(Craft.ActionName(rule.Action))} 해 보다가" + (d.PartialsBefore > 0 ? $" (그 전에 거의 될 듯한 일 {d.PartialsBefore}번)" : ""));
                History(rule.Index, 0, p.Cell, $"{Hangul.I(DisplayName(p))} {LandLabelOr(p.Cell, "한 땅")}에서 처음 알아냈다 — {Hangul.I(WishName(wish))} 아쉬워서");
                GiveNickname(p, rule);
            }
            else if (d!.Lost)
            {
                d.Lost = false; d.RefoundCount++; St.Refound++;
                if (Partials.TryGetValue(rule.Index, out var pr2)) { d.PartialsBefore += pr2.Count; Partials.Remove(rule.Index); }
                Notify(WorldEventKind.Rediscovered, p, rule.Index, $"{Hangul.I(DisplayName(p))} 잊혔던 {Hangul.Eul(Craft.ItemName(rule.Result))} 다시 찾아냈습니다 (잃은 적 {d.LostCount}번)");
                History(rule.Index, 4, p.Cell, $"{Hangul.I(DisplayName(p))} 사라졌던 법을 다시 알아냈다");
            }
            // (E5) 세계에서 처음 알아냈으면 남겨 두고 싶다(그리는 법을 알면 그림으로)
            if (worldFirst && W_record >= 0 && rule != R_paint) { AddWish(p, W_record, 15); if (p.RecordRule < 0) { p.RecordRule = rule.Index; p.RecordWhy = Why.RecordFirst; } }
        }
        else if (via == Via.Painting)
        {
            if (d is { Lost: true }) { d.Lost = false; d.RefoundCount++; St.Refound++; Notify(WorldEventKind.Rediscovered, p, rule.Index, $"{Hangul.I(DisplayName(p))} 남은 그림을 보고 잊혔던 {Hangul.Eul(Craft.ItemName(rule.Result))} 되찾았습니다"); History(rule.Index, 4, p.Cell, $"{Hangul.I(DisplayName(p))} 남은 그림을 보고 되찾았다"); }
        }
        // 그 무리의 말에 낱말이 생긴다(처음 알게 된 무리) — 보고 배웠으면 가르친 사람의 말에서 빌려 온다
        NewWordFor(p, rule, via == Via.Painting ? -1 : from);
        long key = gkey;
        if (groupFirst)
        {
            GroupFirst[key] = Now;
            if (!worldFirst && via == Via.Self) Notify(WorldEventKind.DiscoveryGroup, p, rule.Index, $"이 무리에서 처음: {Hangul.I(DisplayName(p))} {Made(rule)}");
        }
    }

    /// <summary>"모닥불을 피웠습니다" · "불씨를 얻었습니다" · "구운 토기를 만들었습니다".</summary>
    private string Made(CraftRule r)
    {
        string obj = Hangul.Eul(Craft.ItemName(r.Result));
        return r.Result == I_campfire ? $"{obj} 피웠습니다" : r.Result == I_ember ? $"{obj} 얻었습니다" : r.Result == I_painting ? $"{obj} 그렸습니다" : $"{obj} 만들었습니다";
    }
    public string WishName(int wish) => (uint)wish < (uint)Craft.Wishes.Count ? Craft.Wishes[wish].Name : "호기심";

    /// <summary>
    /// 곁에서 본 사람(6타일 안, 깨어 있는 아이 이상)은 따라 배운다 — 그 자리에서 본 경우만. (E5) 한 번에 다 알지는 않고 쌓인다(가르치기보다 느리고 덜 정확,
    /// 말과는 상관없다): 한 번 볼 때 30 + 영리함, 아이는 +10.
    /// </summary>
    private void TeachWatchers(Person p, CraftRule rule, int wish, int input, int with)
    {
        var life = Life(p.Cell);
        int here = p.Tile(Now);
        foreach (var q in life.People)
        {
            if (q.Dead || q.Id == p.Id || q.Asleep || q.Carrier >= 0 || q.Stage(Now) < LifeStage.Child || q.Knows(rule.Index)) continue;
            if (Cheb(q.Tile(Now), here) > 6) continue;
            int amount = 30 + (Ability(q, PeopleData.Int) - 5) * 4 + (q.Stage(Now) == LifeStage.Child ? 10 : 0);
            bool knew = q.Knows(rule.Index);
            AddLearning(q, rule, Math.Max(8, amount), p.Id, Via.Watch, wish, input, with);
            if (!knew && q.Knows(rule.Index)) St.WatchedFull++;
        }
    }

    /// <summary>하루 경계: 아는 사람이 다 죽은 발견은 사라진다(E5 의 되찾음은 다시 발견해야).</summary>
    internal void CheckLostDiscoveries()
    {
        var known = new HashSet<int>();
        foreach (var p in _all) if (!p.Dead) foreach (var k in p.Known) known.Add(k.Rule);
        foreach (var (rule, d) in Discoveries)
        {
            if (d.Lost || known.Contains(rule)) continue;
            d.Lost = true; d.LostTick = Now; d.LostCount++; St.Lost++;
            var who = Get(d.Person);
            // (E5) 마지막으로 알던 사람이 죽었나, 잊었나 · 그림이 남았나
            bool forgot = _forgotToday.Contains(rule);
            if (forgot) St.LostByForget++;
            int pc = PaintingsOf(rule);
            string text = $"{Craft.ItemName(Craft.Rules[rule].Result)} 만드는 법이 세계에서 사라졌습니다 — " + (forgot ? "마지막으로 알던 사람마저 잊었습니다" : "마지막으로 아는 사람이 죽었습니다") + (pc > 0 ? $" (그림 {pc}곳에 남아 있다 — 보고 되찾을 길이 있다)" : " (그림도 남지 않았다)");
            Notify(WorldEventKind.DiscoveryLost, who ?? _all[0], rule, text);
            History(rule, 3, d.Cell, text);
        }
    }

    /// <summary>아는 사람 수(살아 있는).</summary>
    public int Knowers(int rule) { int n = 0; foreach (var p in _all) if (!p.Dead && p.Knows(rule)) n++; return n; }

    // ───────── 불 ─────────

    /// <summary>추위 · 밤: 불가로 간다(알거나 보이는 불). 갔으면 true.</summary>
    private bool TryWarmByFire(Person p, Why why)
    {
        var life = Life(p.Cell);
        int here = p.Tile(Now);
        int ft = life.NearestFire(here % S, here / S, Now, 60);
        if (ft < 0) return false;
        int spot = Beside(p, ft, 2);
        if (spot == here || Cheb(spot, ft) > 2) { if (Cheb(here, ft) <= 2) { SetWhy(p, Act.Warm, why, FeltNow(p)); p.Target = ft; DoStill(p, GameTime.TicksPerHour); AddThought(p, H_byFire); return true; } return false; }
        if (!WalkTo(p, spot)) return false;
        SetWhy(p, Act.Warm, why, FeltNow(p));
        p.Target = ft;
        return true;
    }

    /// <summary>불이 보인다(사람이 피운 것이든 자연의 것이든): 따뜻함이 아쉬운 사람은 불씨를 얻으러 간다. 불씨가 있으면 땔감을 얹어 불을 피운다.</summary>
    private bool TryFireWork(Person p, ProvinceLife life, int here, Pcg32 rng)
    {
        if (p.Stage(Now) < LifeStage.Child) return false;
        // 불씨를 들고 있다 → 땔감이 있으면 피우고, 없으면 땔감부터
        if (p.CarryCount(I_ember) > 0)
        {
            if (Now - p.EmberTick > EmberLife) { p.RemoveCarry(I_ember, 1); p.EmberTick = -1; AddThought(p, H_fireDied); }
            else
            {
                if (BestCarried(p, Craft.P_burns, 3, I_ember) < 0) { if (StartMaterialFor(p, Craft.P_burns, 3, Why.Fuel)) return true; }
                else if (life.NearestFire(here % S, here / S, Now, 4) < 0 || !life.Walk[here]) { if (TryLightWithEmber(p, here, rng)) { Decide(p); return true; } }
                else
                {
                    // 바로 곁에 이미 불이 있다 — 조금 떨어진 곳(자는 사람들 쪽)에 피운다
                    int t = SpotAway(p, life, here, 6);
                    if (t >= 0 && WalkTo(p, t)) { SetWhy(p, Act.TakeFire, Why.FireNear); p.Target = -1; return true; }
                }
            }
        }
        int fire = life.NearestFire(here % S, here / S, Now, SightTiles + 10);
        if (fire < 0) return false;
        bool wants = p.Wish[W_warmth] >= 25 || (FeltNow(p) < 110 && Cheb(fire, here) > 8);
        if (!wants) return false;
        // 이미 그 불 곁에 있으면 몸을 녹이는 것으로 충분
        if (Cheb(fire, here) <= 3) return false;
        if (BestCarried(p, Craft.P_burns, 3) < 0) return StartMaterialFor(p, Craft.P_burns, 3, Why.Fuel) || TryWarmByFire(p, Why.Cold); // 땔감부터(이번 판단에서 정말 나섰을 때만 true)
        int spot = Beside(p, fire);
        if (!WalkTo(p, spot)) return false;
        SetWhy(p, Act.TakeFire, Why.TakeFire);
        p.Target = fire;
        return true;
    }

    private int SpotAway(Person p, ProvinceLife life, int here, int dist)
    {
        var rng = Rng(p.Cell);
        for (int tries = 0; tries < 8; tries++)
        {
            int x = here % S + rng.NextInt(-dist, dist + 1), y = here / S + rng.NextInt(-dist, dist + 1);
            if ((uint)x >= S || (uint)y >= S) continue;
            int t = y * S + x;
            if (life.Walk[t] && life.Region[t] == life.Region[here] && life.NearestFire(x, y, Now, 4) < 0 && !life.Fires.ContainsKey(t)) return t;
        }
        return -1;
    }

    /// <summary>불가에 닿았다: 불씨를 얻는다(그 자리에서 보고 배운 것 — 불을 옮겨 오는 법).</summary>
    private void ArriveTakeFire(Person p)
    {
        var life = Life(p.Cell);
        if (p.Target < 0) { Decide(p); return; } // 떨어진 자리에 왔다 → 다시 판단(불씨로 피운다)
        if (!life.Fires.TryGetValue(p.Target, out var f) || !f.Lit(Now)) { if (!life.IsVolcanoFire(p.Target)) { Decide(p); return; } }
        p.AddCarry(I_ember, 1); p.EmberTick = Now;
        St.FiresTaken++;
        AddBio(p, BioKind.SawFire, f.Lighter, p.Target);
        DoStill(p, 300);
    }

    /// <summary>불이 꺼져 가면 땔감을 넣는다(아는 사람이).</summary>
    private bool TryTendFire(Person p)
    {
        var life = Life(p.Cell);
        if (R_campfire is null || !p.Knows(R_campfire.Index)) return false;
        int here = p.Tile(Now);
        int ft = life.NearestFire(here % S, here / S, Now, 40);
        if (ft < 0 || life.Fires[ft].Remaining(Now) > 3 * GameTime.TicksPerHour) return false;
        int fuel = BestCarried(p, Craft.P_burns, 3, I_ember);
        if (fuel < 0) return StartMaterialFor(p, Craft.P_burns, 3, Why.Fuel);
        return StartTend(p, ft, fuel, Why.Fuel);
    }

    /// <summary>추운 밤 자기 전: 불이 밤새 탈 만큼 땔감이 없으면 넣는다(불 피우는 법을 알고 땔감을 들고 있으면).</summary>
    private bool TendBeforeSleep(Person p, ProvinceLife life, int ft)
    {
        if (R_campfire is null || !p.Knows(R_campfire.Index) || !life.Fires.TryGetValue(ft, out var f) || !f.Lit(Now)) return false;
        if (f.Remaining(Now) > 10 * GameTime.TicksPerHour) return false;
        int fuel = BestCarried(p, Craft.P_burns, 3, I_ember);
        if (fuel < 0) return false;
        return StartTend(p, ft, fuel, Why.Fuel);
    }

    private bool StartTend(Person p, int ft, int fuel, Why why)
    {
        int here = p.Tile(Now);
        int spot = Beside(p, ft);
        p.PlanItem = fuel;
        if (Cheb(here, ft) <= 1) { SetWhy(p, Act.TendFire, why); p.Target = ft; StandStill(p); DoStill(p, 200); return true; }
        if (!WalkTo(p, spot)) return false;
        SetWhy(p, Act.TendFire, why); p.Target = ft;
        return true;
    }

    private void FinishTend(Person p)
    {
        var life = Life(p.Cell);
        if (!life.Fires.TryGetValue(p.Target, out var f) || p.CarryCount(p.PlanItem) == 0) return;
        p.RemoveCarry(p.PlanItem, 1);
        if (R_campfire is not null) UseKnowledge(p, R_campfire.Index);
        long add = FuelTicks(p.PlanItem);
        if (f.Kind == 2) add = add * 3 / 2; // (E4) 가둔 불은 땔감이 오래 간다
        else if (f.Lit(Now) && f.Remaining(Now) < GameTime.TicksPerHour && R_hearth is not null && !p.Knows(R_hearth.Index) && Rng(p.Cell).NextInt(100) < 25)
            AddClue(p, new Clue { Rule = R_hearth.Index, Key = "fuel", Tick = Now, Input = p.PlanItem }); // 땔감이 금방 다 탄다
        if (p.PlanItem == I_charcoal && f.Kind == 2) f.Charcoal = true;
        if (f.Lit(Now)) { f.Fuel += add; }
        else { f.Stamp = Now; f.Fuel = add; f.Charcoal = p.PlanItem == I_charcoal && f.Kind == 2; }
        life.Fires[p.Target] = f;
    }

    /// <summary>하루 경계(지방마다): 비가 오면 바깥의 불이 꺼진다 · 오래 꺼진 모닥불 자리는 없어진다 · 들불 · 번개.</summary>
    private void FiresDaily(int cell, ProvinceLife life)
    {
        var rng = Rng(cell);
        var planet = _w.Planet;
        int precip = planet.PrecipMm[cell];
        bool rain = rng.NextInt(10_000) < Math.Min(6_000, precip * 10_000 / 3650);
        List<int>? remove = null;
        foreach (var (t, f0) in life.Fires.ToList())
        {
            var f = f0;
            if (rain && f.Lit(Now) && life.Shelter[t] < 3 && f.Kind == 1) // (E4) 돌로 가둔 불은 비에 꺼지지 않는다
            {
                f.Fuel = Math.Max(0, Now - f.Stamp); life.Fires[t] = f;
                if (Get(f.Lighter) is { Dead: false } l && l.Cell == cell)
                {
                    AddThought(l, H_fireDied); AddBio(l, BioKind.FireDied, -1, t);
                    if (R_hearth is not null && !l.Knows(R_hearth.Index)) { AddClue(l, new Clue { Rule = R_hearth.Index, Key = "rain", Tick = Now, Input = I_stone }); AddWish(l, W_warmth, 10); }
                }
            }
            else if (!f.Lit(Now) && f.Kind == 1 && Now - (f.Stamp + f.Fuel) > 10 * GameTime.TicksPerDay) (remove ??= new()).Add(t);
        }
        if (remove is not null) foreach (int t in remove) life.Fires.Remove(t);
        // 자연의 불: 마른 더운 땅은 들불, 어디든 드물게 번개 — 사람이 보는 곳(사는 사람 둘레)에
        bool alive = false; foreach (var q in life.People) if (!q.Dead) { alive = true; break; }
        if (!alive) return;
        int temp = planet.Temp10[cell];
        int chance = temp > 200 && precip < 900 ? 40 : 10; // 10,000 분의(하루)
        if (rng.NextInt(10_000) >= chance) return;
        Person? someone = null; int n = 0;
        foreach (var q in life.People) if (!q.Dead) { n++; if (rng.NextInt(n) == 0) someone = q; }
        if (someone is null) return;
        int ht = someone.Tile(Now);
        for (int tries = 0; tries < 12; tries++)
        {
            int x = ht % S + rng.NextInt(-12, 13), y = ht / S + rng.NextInt(-12, 13);
            if ((uint)x >= S || (uint)y >= S) continue;
            int t = y * S + x;
            if (!life.Walk[t] || life.Fires.ContainsKey(t) || life.Map.Ground[t] is not (Ground.Grass or Ground.Soil or Ground.Sand)) continue;
            life.LightFire(t, 1, 8 * GameTime.TicksPerHour, Now, -1);
            St.Wildfires++;
            Notify(WorldEventKind.Wildfire, someone, -1, (chance == 40 ? "마른 풀에 들불이 났습니다" : "번개가 쳐 불이 났습니다") + $" — {LandLabelOr(cell, "한 땅")}에서 {Hangul.I(DisplayName(someone))} 불을 보았습니다");
            foreach (var q in life.People) if (!q.Dead && Cheb(q.Tile(Now), t) <= SightTiles + 6) { AddWish(q, W_warmth, 10); AddBio(q, BioKind.SawFire, -1, t); }
            break;
        }
    }

    // ───────── 캐기 ─────────

    /// <summary>돌 도구가 있으면 바위를 캔다(반짝이는 돌이 아쉽거나 · 단단한 도구가 아쉬우거나 · 궁금하면). 갔으면 true.</summary>
    private bool TryMine(Person p)
    {
        if (!HasHardTool(p)) return false;
        var life = Life(p.Cell);
        int here = p.Tile(Now);
        bool ore = p.Wish[W_shiny] > 0;
        int t = life.NearestStone(here % S, here / S, 35, ore);
        if (t < 0 && !ore) return false;
        if (t < 0) t = life.NearestStone(here % S, here / S, 35, false);
        if (t < 0) return false;
        int spot = StandBeside(life, t, here);
        if (spot < 0) return false;
        if (spot != here && !WalkTo(p, spot)) return false;
        SetWhy(p, Act.Mine, life.Map.OreAt(t) >= 0 ? Why.MineOre : Why.MineStone);
        p.Target = t;
        if (spot == here) { StandStill(p); ArriveMine(p); }
        return true;
    }

    /// <summary>바위 t 와 맞닿은, here 에서 걸어갈 수 있는 타일(here 가 맞닿았으면 here). 없으면 −1.</summary>
    private static int StandBeside(ProvinceLife life, int t, int here)
    {
        if (Cheb(t, here) <= 1 && life.Walk[here]) return here;
        int reg = life.Region[here], best = -1; long bd = long.MaxValue;
        int x = t % S, y = t / S;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if ((uint)nx >= S || (uint)ny >= S) continue;
                int n = ny * S + nx;
                if (!life.Walk[n] || life.Region[n] != reg) continue;
                long d = ProvinceLife.D2(n, here % S, here / S);
                if (d < bd) { bd = d; best = n; }
            }
        return best;
    }

    private void ArriveMine(Person p) => DoStill(p, (long)(2 * GameTime.TicksPerHour) * WorkPercent(p) / 100);

    private void FinishMine(Person p)
    {
        var life = Life(p.Cell);
        int t = p.Target;
        if (t < 0 || !life.Mine(t)) return;
        var rng = Rng(p.Cell);
        var rocks = RockCatalog.Default;
        int rock = life.Map.Rock[t];
        int here = p.Tile(Now);
        if (rock < rocks.Rocks.Count) foreach (var y in rocks.Rocks[rock].Yields) DropOrCarry(p, life, here, Craft.ItemIndex(y.Id), 2);
        int ore = life.Map.OreAt(t);
        if (ore >= 0) { foreach (var y in rocks.Ores[ore].Yields) DropOrCarry(p, life, here, Craft.ItemIndex(y.Id), 2); AddWish(p, W_shiny, 30); }
        life.RevealAround(t, 2);
        St.Mined++; p.Mined++;
        AddBio(p, BioKind.Mined, ore, t);
        AddThought(p, H_madeThing);
        GainSkill(p, PeopleData.Gathering, 8);
        WearTool(p, BestTool(p, Craft.P_hardness, 4), rng);
        p.Wish[W_hardTool] = Math.Max(0, p.Wish[W_hardTool] - 20);
    }

    private void DropOrCarry(Person p, ProvinceLife life, int here, int item, int n)
    {
        if (item < 0) return;
        if (CarryWeight(p) + Math.Max(1, Prop(item, Craft.P_weight)) * n > CarryCapOf(p)) life.Drop(here, item, n);
        else p.AddCarry(item, n);
    }

    // ───────── 들고 있는 것 먹기 · 익히기 · 그릇의 물 ─────────

    /// <summary>들고 있는 먹을 것(독이 적은 것 우선). 없으면 −1.</summary>
    private int BestCarriedFood(Person p, bool desperate)
    {
        int best = -1, bs = 0;
        foreach (var c in p.Carry)
        {
            int e = Prop(c.Item, Craft.P_edible), poison = Prop(c.Item, Craft.P_poison);
            if (e <= 0 || (!desperate && e < 2 && poison > 0)) continue;
            int s = e * 10 - poison * (desperate ? 2 : 12) + (c.Item == I_coconut && !HasHardTool(p) ? -30 : 0);
            if (s > bs) { bs = s; best = c.Item; }
        }
        return best;
    }

    private void FinishEatCarried(Person p)
    {
        int item = p.PlanItem;
        if (item < 0 || p.CarryCount(item) == 0) return;
        int e = Prop(item, Craft.P_edible), poison = Prop(item, Craft.P_poison);
        int want = Math.Clamp((Person.Full - p.Food) / Math.Max(1, e * 5000), 1, Math.Min(4, p.CarryCount(item)));
        p.RemoveCarry(item, want);
        p.Food = Clamp(p.Food + (long)want * e * 5000);
        p.Water = Clamp(p.Water + (long)want * (item == I_crackedCoconut ? 25_000 : 2_000));
        var rng = Rng(p.Cell);
        if (IsMade(item) && poison == 0 && e >= 4) { AddThought(p, H_cookedMeal); St.CookedMeals++; }
        SpillSeed(p, item, rng); // (E5c) 먹다 흘린 씨
        if (poison > 0 && rng.NextInt(100) < poison * 8) BadStomach(p, item);
        if (item == I_coconut && !HasHardTool(p)) AddWish(p, W_hardTool, 20);
        if (p.Food >= 85_000) AddThought(p, H_ate);
    }

    private void BadStomach(Person p, int item)
    {
        AddThought(p, H_badStomach);
        p.Food = Math.Max(0, p.Food - 12_000);
        AddWish(p, W_cooking, 35);
        St.BadStomachs++;
        AddBio(p, BioKind.BadStomach, -1, item);
    }

    /// <summary>배가 고프고 · 익히는 법을 알고 · 날것을 들고 있고 · 불이 가까우면 불가로 가서 익혀 먹는다.</summary>
    private bool TryCook(Person p, Why why)
    {
        if (!KnowsAnyRoast(p)) return false;
        int raw = -1;
        foreach (var c in p.Carry) if (Prop(c.Item, Craft.P_edible) > 0 && !IsMade(c.Item) && c.Item != I_coconut) { raw = c.Item; break; }
        if (raw < 0) return false;
        var life = Life(p.Cell);
        int here = p.Tile(Now);
        int ft = life.NearestFire(here % S, here / S, Now, 45);
        if (ft < 0) return false;
        p.PlanItem = raw;
        int spot = Beside(p, ft);
        if (Cheb(here, ft) <= 1) { SetWhy(p, Act.Cook, why, p.Food / 1000); p.Target = ft; StandStill(p); DoStill(p, 1_000L * WorkPercent(p) / 100); return true; }
        if (!WalkTo(p, spot)) return false;
        SetWhy(p, Act.Cook, why, p.Food / 1000); p.Target = ft;
        return true;
    }

    public bool KnowsAnyRoast(Person p) { foreach (var k in p.Known) if (Craft.Rules[k.Rule].Action == A_roast && Prop(Craft.Rules[k.Rule].Result, Craft.P_edible) > 0) return true; return false; }

    private void FinishCook(Person p)
    {
        var life = Life(p.Cell);
        int here = p.Tile(Now);
        int heat = life.HeatAt(here, Now);
        if (heat <= 0) return;
        var rng = Rng(p.Cell);
        // 들고 있는 날것을 모두 익힌다(규칙으로 판정 — 익힐 수 없는 것은 그대로)
        var raws = p.Carry.Where(c => Prop(c.Item, Craft.P_edible) > 0 && !IsMade(c.Item) && c.Item != I_coconut).Select(c => c.Item).ToList();
        int cooked = -1;
        foreach (int raw in raws)
        {
            var outcome = Craft.Judge(A_roast, PropsOf(raw), null, null, heat, 0, 0, 0);
            if (!outcome.Success || outcome.Rule is null) continue;
            int n = p.CarryCount(raw);
            p.RemoveCarry(raw, n);
            p.AddCarry(outcome.Rule.Result, n);
            SpillSeed(p, raw, rng); // (E5c) 불가에서 볶다 흘린 씨
            cooked = outcome.Rule.Result;
            if (!p.Knows(outcome.Rule.Index)) Learn(p, outcome.Rule, W_cooking, raw, -1, -1);
            else UseKnowledge(p, outcome.Rule.Index);
            GainSkill(p, outcome.Rule.Skill, 4);
        }
        if (cooked >= 0) { p.PlanItem = cooked; FinishEatCarried(p); p.Wish[W_cooking] = Math.Max(0, p.Wish[W_cooking] - 20); }
    }

    /// <summary>그릇이 있으면 물가에서 물을 담아 둔다.</summary>
    private void FillPots(Person p)
    {
        int cap = 0;
        foreach (var c in p.Carry) if (Prop(c.Item, Craft.P_holds) >= 1 && Prop(c.Item, Craft.P_waterproof) >= 2) cap += Prop(c.Item, Craft.P_holds) * c.Count;
        p.PotWater = Math.Min(8, cap);
    }

    public bool HasContainer(Person p) { foreach (var c in p.Carry) if (Prop(c.Item, Craft.P_holds) >= 3) return true; return false; }
    public bool HasWaterContainer(Person p) { foreach (var c in p.Carry) if (Prop(c.Item, Craft.P_holds) >= 1 && Prop(c.Item, Craft.P_waterproof) >= 2) return true; return false; }

    // ───────── 둘러보며 바람이 생기는 것 ─────────

    /// <summary>둘러보다 광맥 · 바위를 보면 호기심 많은 사람은 반짝이는 돌이 궁금하다.</summary>
    private void SeeAround(Person p, ProvinceLife life, int hx, int hy)
    {
        if (!p.HasTrait(T_curious) && p.Wish[W_shiny] == 0 && p.Wish[W_hardTool] == 0) return;
        if (Now - p.WishSince[W_shiny] < GameTime.TicksPerDay) return; // 하루에 한 번만 둘러본다(비용)
        p.WishSince[W_shiny] = Now;
        int ore = life.NearestStone(hx, hy, SightTiles, true);
        if (ore >= 0) { AddWish(p, W_shiny, p.HasTrait(T_curious) ? 15 : 6); p.WishSince[W_shiny] = Now; if (!HasHardTool(p)) AddWish(p, W_hardTool, 10); }
    }

    /// <summary>발견 목록용: 규칙마다 아는 사람 수 · 발견 정보.</summary>
    public IEnumerable<(Discovery D, int Knowers)> DiscoveryList()
    {
        foreach (var (rule, d) in Discoveries) yield return (d, Knowers(rule));
    }
}
