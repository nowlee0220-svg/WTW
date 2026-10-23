using Wtw.Sim.Core;
using Wtw.Sim.Data;
using Wtw.Sim.Provinces;
using Wtw.Sim.Time;

namespace Wtw.Sim.People;

/// <summary>세계에서 처음 그 짐승을 잡은 일(종마다).</summary>
public readonly record struct HuntFirst(long Tick, int Person, int Cell, int Helpers);

/// <summary>
/// (E5b) 사냥 · 손질 · 물고기 · 옷 · 다침 · 길들이기. 결과를 정답표로 정하지 않는다:
///   - 사냥은 바람(먹을 것 · 입을 것 · 사냥 도구)과 배고픔에서 나온다. 잡을 수 있을지는 짐승의 크기 · 빠르기 · 사나움과 든 것(맨손 · 던지는 돌 · 창)과 함께한 사람 수로 어림한다.
///   - 큰 짐승은 혼자 어렵다 → 곁의 어른을 불러 함께 몬다. 놓치면 "사냥 도구가 아쉽다"(바람) → 실험으로 창이 나온다(E4 규칙 — 속성 "찌른다").
///   - 잡으면 그 자리에서 손질: 날카로운 도구가 있으면 고기 · 가죽 · 뼈 · 힘줄, 없으면 고기 조금만. 함께한 사람이 나눠 든다(무게).
///   - 날가죽은 썩는다 → 긁고(날 도구) · 말리고 · 그을려 무두질한 가죽 → 뼈바늘과 힘줄로 꿰매 털가죽 옷 · 신(모두 E4 규칙).
///   - 입은 옷은 따뜻함만큼 추위를 막고(26°C 를 넘게 덥히지는 않는다) 날이 지나면 닳는다.
/// </summary>
public sealed partial class PeopleSim
{
    private int W_food = -1, W_hunting = -1, W_clothing = -1;
    private int I_rawMeat = -1, I_rawFish = -1, I_rawHide = -1, I_bone = -1, I_sinew = -1, I_driedMeat = -1, R_driedMeat = -1;
    private int A_scrape = -1, A_smoke = -1, A_sew = -1;
    internal int H_injured = -1, H_hunted = -1, H_huntFailed = -1, H_attacked = -1, H_warmClothes = -1, H_petNear = -1;

    /// <summary>세계에서 처음 그 짐승을 잡은 일 · 말마다 처음 잡은 때(열쇠 = 말 × 100 + 종). 저장한다.</summary>
    public readonly SortedDictionary<int, HuntFirst> HuntFirsts = new();
    public readonly SortedDictionary<long, long> GroupHunts = new();

    public const long HuntCooldown = 6 * GameTime.TicksPerHour; // (E5b 고침) 4 → 6시간(사냥 나섬이 너무 잦았다)
    /// <summary>(E5b 고침) 사냥하러 찾아가는 가장 먼 거리(타일) — 처음 판은 70 이라 사냥 길이 길었다(보이는 거리 14의 두 배 반).</summary>
    public const int HuntRange = 35;

    private void InitHunt()
    {
        int W(string id) { foreach (var w in Craft.Wishes) if (w.Id == id) return w.Index; throw new InvalidOperationException($"crafting.json 에 바람 '{id}' 가 없습니다."); }
        W_food = W("food"); W_hunting = W("hunting"); W_clothing = W("clothing");
        I_rawMeat = Craft.ItemIndex("raw_meat"); I_rawFish = Craft.ItemIndex("raw_fish"); I_rawHide = Craft.ItemIndex("raw_hide"); I_bone = Craft.ItemIndex("bone"); I_sinew = Craft.ItemIndex("sinew");
        I_driedMeat = Craft.ItemIndex("dried_meat");
        foreach (var r in Craft.Rules) if (r.Result == I_driedMeat) { R_driedMeat = r.Index; break; }
        A_scrape = Craft.ActionIndex("scrape"); A_smoke = Craft.ActionIndex("smoke"); A_sew = Craft.ActionIndex("sew");
        int H(string id) { int i = Data.ThoughtIndex(id); if (i < 0) throw new InvalidOperationException($"people.json 에 생각 '{id}' 가 없습니다."); return i; }
        H_injured = H("injured"); H_hunted = H("hunted"); H_huntFailed = H("hunt_failed"); H_attacked = H("attacked"); H_warmClothes = H("warm_clothes"); H_petNear = H("pet_near");
    }

    // ───────── 든 것 · 잡을 수 있을까 ─────────

    /// <summary>
    /// 사냥에 쓸 든 것의 세기: 0 맨손 · 2 던지는 돌(단단하고 무거운 돌 둘 이상) · 날 선 돌(가까이서) · 3 뾰족한 막대 · 5 돌창.
    /// 찌르는 것(찌른다 ≥ 2)은 무게 1 이상 · 질김 3 이상이어야 무기다(바늘은 아니다).
    /// </summary>
    public int WeaponPower(Person p)
    {
        int best = 0, stones = 0;
        foreach (var (item, n) in p.Carry)
        {
            int pierce = Prop(item, Craft.P_pierces), w = Prop(item, Craft.P_weight);
            if (pierce >= 2 && w >= 1 && Prop(item, Craft.P_toughness) >= 3) best = Math.Max(best, pierce);
            else if (Prop(item, Craft.P_sharp) >= 3) best = Math.Max(best, 2);
            if (Prop(item, Craft.P_hardness) >= 3 && w >= 2 && Prop(item, Craft.P_edible) == 0) stones += n;
        }
        if (stones >= 2) best = Math.Max(best, 2);
        // (E6) 가장 잘 찌르는 것의 품질: 좋음 이상이면 +1, 나쁨이면 −1
        if (best >= 3) { int wi = BestWeapon(p); if (wi >= 0) { int q = QualityOf(p, wi); best += q >= 2 ? 1 : q <= 0 ? -1 : 0; } }
        return best;
    }

    /// <summary>(E6) 가장 잘 찌르는 무기(아이템 번호, 없으면 −1).</summary>
    private int BestWeapon(Person p)
    {
        int best = -1, bv = 0;
        foreach (var (item, _) in p.Carry)
        {
            int pierce = Prop(item, Craft.P_pierces);
            if (pierce >= 2 && Prop(item, Craft.P_weight) >= 1 && Prop(item, Craft.P_toughness) >= 3 && pierce > bv) { bv = pierce; best = item; }
        }
        return best;
    }

    /// <summary>잡을 확률 어림(%) — 크기 · 빠르기 · 사나움, 든 것, 함께한 사람, 사냥 솜씨, 따르는 짐승.</summary>
    private int HuntChance(Person p, AnimalDef a, int helpers, int weapon)
    {
        int c = a.Size == 1 ? 22 : a.Size == 2 ? 0 : -18;
        c += weapon * 6 + p.SkillLevel(PeopleData.Hunting) * 3 + helpers * (a.Size == 1 ? 2 : 9) - a.Fierce * 2 - (a.Run <= 5 ? 8 : 0);
        if (a.Size == 2 && weapon < 3 && helpers < 1) c -= 15;
        if (a.Size == 3 && weapon < 5 && !(weapon >= 3 && helpers >= 2)) c -= 25;
        if (CompanionNear(Life(p.Cell), p, 12)) c += 8;
        if (p.Injury > 30_000) c -= 15;
        return Math.Clamp(c, 0, 85);
    }

    /// <summary>함께 사냥할 수 있는 곁의 어른(20타일 안, 바쁘지 않고 배 · 잠이 괜찮은).</summary>
    private void HuntHelpers(Person p, List<Person> into, int max)
    {
        into.Clear();
        var life = Life(p.Cell);
        int here = p.Tile(Now);
        foreach (var q in life.People)
        {
            if (q.Id == p.Id || !CanBeTalkedTo(q) || q.Stage(Now) != LifeStage.Adult || q.Food < 30_000 || q.Rest < 30_000 || q.Injury > 20_000) continue;
            if (Cheb(q.Tile(Now), here) > 20 || !life.Paths.Reachable(here, q.Tile(Now))) continue;
            if (q.GetRelation(p.Id).Opinion < -10) continue;
            into.Add(q);
        }
        // 가까운 순(같으면 번호 순) — 몇 안 되니 끼워 넣기 정렬로(람다 없이)
        for (int i = 1; i < into.Count; i++)
        {
            var q = into[i]; int dq = Cheb(q.Tile(Now), here), j = i - 1;
            while (j >= 0 && (Cheb(into[j].Tile(Now), here) > dq || (Cheb(into[j].Tile(Now), here) == dq && into[j].Id > q.Id))) { into[j + 1] = into[j]; j--; }
            into[j + 1] = q;
        }
        if (into.Count > max) into.RemoveRange(max, into.Count - max);
    }

    private readonly List<Person> _noHelpers = new();

    /// <summary>
    /// 사냥하고 싶은 마음(점수)과 노릴 무리. 배고픔 · 먹을 것 바람 · 입을 것 바람(가죽) · 사냥 솜씨 · 성격에서, 보이는(30타일) 무리 가운데
    /// 잡을 만한 것(어림 확률이 겁나지 않을 만큼)을 고른다. 없으면 int.MinValue.
    /// </summary>
    private int HuntScore(Person p, ProvinceLife life, out Herd? target, int bar = int.MinValue)
    {
        target = null;
        if (life.Herds.Count == 0 || p.Stage(Now) < LifeStage.Child || p.AgeYears(Now) < 12 || p.AgeYears(Now) > 68 || p.Injury > 30_000 || life.IsNight(Now)) return int.MinValue;
        if (Now - p.LastHunt < HuntCooldown) return int.MinValue;
        // 까닭이 있어야 나선다: 배가 고프거나(들고 있는 먹을 것이 넉넉하면 아니다) · 먹을 것 · 입을 것 바람 · 날카로운 것(뼈) 바람
        int food = FoodCarried(p);
        bool hungry = p.Food < 70_000 && food < 3;
        bool wantFood = p.Wish[W_food] >= 20 && food < 4; // (E5b 고침) 먹을 것을 넉넉히 들고 있으면 먹을 것 바람으로는 나서지 않는다(배부르면 덜 잡는다)
        if (!hungry && !wantFood && p.Wish[W_clothing] < 20 && p.Wish[W_sharp] < 30) return int.MinValue;
        int desire = (hungry ? (75_000 - p.Food) / 900 : 0) + (wantFood ? p.Wish[W_food] / 2 : 0) + p.Wish[W_clothing] / 3 + p.Wish[W_sharp] / 8
            + p.SkillLevel(PeopleData.Hunting) * 2 + (p.HasTrait(T_bloodlust) ? 6 : 0) + (p.HasTrait(T_brave) ? 3 : 0) - (p.HasTrait(T_coward) ? 6 : 0);
        if (desire < 10) return int.MinValue;
        if (desire + 85 / 4 + 11 < bar) return int.MinValue; // (E5b 고침) 가장 잘 돼도 다른 할 일 셋을 못 이긴다 — 무리를 훑을 것도 없다
        int here = p.Tile(Now);
        int weapon = WeaponPower(p);
        int avail = -1;
        int bestValue = 0, bestChance = 0;
        int min = p.HasTrait(T_brave) || p.HasTrait(T_bloodlust) ? 10 : p.HasTrait(T_coward) ? 25 : 15;
        bool skipFail = p.FailHerdCell == p.Cell && Now < p.FailHerdUntil;
        foreach (var (_, h) in life.Herds)
        {
            if (h.Tame || h.State == HerdState.Stalk || (skipFail && h.Id == p.FailHerd)) continue;
            int ht = h.Tile(Now);
            int d = Cheb(ht, here);
            if (d > HuntRange || !life.Paths.Reachable(here, ht)) continue; // 보이는 곳(14타일) 너머도 발자국 · 똥을 따라 찾아간다
            var a = Fauna.Animals[h.Species];
            if (a.Size >= 2 && avail < 0)
            {
                if (HuntChance(p, a, 4, weapon) < min) continue; // 넷이 함께여도 어렵다 — 함께할 사람을 셀 것도 없다(결과는 같다)
                HuntHelpers(p, L.Hunters, 4); avail = L.Hunters.Count;
            }
            int chance = HuntChance(p, a, a.Size >= 2 ? Math.Max(0, avail) : 0, weapon);
            if (chance < min) continue;
            int hide = 0; foreach (var (it, n) in a.Yields) if (it != I_rawMeat) hide += n;
            // (E5b 고침) 고기는 함께 들고 갈 수 있는 만큼만 값으로 친다(혼자면 작은 짐승을, 여럿이면 큰 짐승을) — 남는 고기는 버려진다
            int usable = Math.Min(a.MeatYield, (1 + (a.Size >= 2 ? Math.Max(0, avail) : 0)) * 4 + 2);
            int value = chance * (usable * 3 + (p.Wish[W_clothing] > 0 ? hide * 2 : hide / 2)) / (10 + d);
            if (value > bestValue || (value == bestValue && target is not null && h.Id < target.Id)) { bestValue = value; bestChance = chance; target = h; }
        }
        if (target is null)
        {
            p.LastHunt = Now - HuntCooldown + GameTime.TicksPerHour; // (E5b 고침) 잡을 만한 무리가 없다 — 한 시간은 다시 둘러보지 않는다
            return int.MinValue;
        }
        return desire + bestChance / 4;
    }

    /// <summary>배가 고픈데 딸 것이 없다 — 보이는 짐승을 잡으러, 아니면 물고기를 잡으러. 나섰으면 true.</summary>
    private bool TryHuntForFood(Person p)
    {
        var life = Life(p.Cell);
        AddWish(p, W_food, 8);
        if (HuntScore(p, life, out var h) > int.MinValue && h is not null && TryHunt(p, h)) return true;
        return FishScore(p, life, out int spot) > int.MinValue && TryFish(p, spot);
    }

    /// <summary>사냥에 나선다: 큰 짐승이면 곁의 어른을 불러 함께(최대 넷), 무리 곁으로 걸어간다.</summary>
    private bool TryHunt(Person p, Herd h)
    {
        var life = Life(p.Cell);
        var a = Fauna.Animals[h.Species];
        int ht = h.Tile(Now);
        int goal = Beside(p, ht, 2);
        if (!WalkTo(p, goal)) return false;
        Why why = p.Food < 50_000 || p.Wish[W_food] >= 20 ? Why.HuntFood : p.Wish[W_clothing] >= 15 ? Why.HuntHide : p.Wish[W_sharp] >= 20 ? Why.HuntBone : Why.HuntFood;
        SetWhy(p, Act.Hunt, why, p.Id);
        p.Target = h.Id; p.TargetB = 0;
        p.LastHunt = Now;
        St.HuntsStarted++;
        St.HuntBySpecies[h.Species]++;
        if (a.Size >= 2)
        {
            var helpers = L.Hunters;
            HuntHelpers(p, helpers, 4);
            // (E5b 고침) 넉넉히(잡을 어림 50%) 될 만큼만 부른다 — 늘 넷을 불러 모두 먼 길을 걸었다
            int weapon = WeaponPower(p), need = 0;
            while (need < helpers.Count && HuntChance(p, a, need, weapon) < 50) need++;
            if (helpers.Count > need) helpers.RemoveRange(need, helpers.Count - need);
            var rng = Rng(p.Cell);
            foreach (var q in helpers.ToList())
            {
                if (q.HasTrait(T_coward) && rng.NextInt(100) < 50) continue;
                Integrate(q, Now);
                q.Position(Now, out int qx, out int qy);
                q.NewMotion().SetStill(Now, qx, qy);
                q.Walking = false;
                if (q.Asleep) WakeUp(q);
                if (!WalkTo(q, Beside(q, ht, 3))) { Schedule(q, Now + 1); continue; }
                SetWhy(q, Act.Hunt, Why.JoinHunt, p.Id);
                q.Target = h.Id; q.TargetB = 0;
                q.LastHunt = Now;
                St.HuntHelpers++;
            }
        }
        return true;
    }

    /// <summary>사냥터에 닿았다: 이끄는 사람은 무리가 가까우면 몰기 시작하고(무리는 그동안 붙잡혀 있다), 멀어졌으면 세 번까지 쫓는다. 함께 온 사람은 기다린다.</summary>
    private void ArriveHunt(Person p)
    {
        var life = Life(p.Cell);
        var rng = Rng(p.Cell);
        int here = p.Tile(Now);
        if (p.WhyArg != p.Id)
        {
            var lead = Here(p.WhyArg, p);
            long wait = lead is { Act: Act.Hunt, Walking: false } && lead.ActUntil > Now ? lead.ActUntil - Now + 1 : GameTime.TicksPerHour;
            DoStill(p, Math.Min(wait, 2 * GameTime.TicksPerHour));
            return;
        }
        if (!life.Herds.TryGetValue(p.Target, out var h)) { HuntGiveUp(p, null); Decide(p); return; }
        int ht = h.Tile(Now), d = Cheb(ht, here);
        if (d > 6)
        {
            if (p.TargetB < 3 && d < 80 && life.Paths.Reachable(here, ht) && WalkTo(p, Beside(p, ht, 2)))
            {
                p.TargetB++;
                p.Why = Why.HuntChase;
                // (E5b 고침) 함께하는 사람은 첫 번째 쫓을 때만 따라간다(그 뒤에는 이끄는 사람이 몰아 오기를 기다린다 — 길 찾기를 덜)
                if (p.TargetB == 1)
                    foreach (var q in life.People)
                        if (!q.Dead && q.Act == Act.Hunt && q.WhyArg == p.Id && q.Id != p.Id && !q.Walking) { Integrate(q, Now); if (WalkTo(q, Beside(q, ht, 3))) { } else Schedule(q, Now + 1); }
                return;
            }
            HuntGiveUp(p, h);
            Decide(p);
            return;
        }
        // 몰고 찌른다: 무리는 그동안 그 자리에 붙잡혀 있다
        long dur = 1_200 + rng.NextInt(1_800);
        HerdStop(h);
        h.State = HerdState.Flee;
        ScheduleHerd(life, h, Now + dur + 2);
        DoStill(p, dur);
    }

    /// <summary>(E5b 고침) 놓친 무리를 하루 동안 기억한다(다시 쫓지 않는다).</summary>
    private void RememberFail(Person p, Herd? h) { if (h is null) return; p.FailHerd = h.Id; p.FailHerdCell = p.Cell; p.FailHerdUntil = Now + GameTime.TicksPerDay; }

    private void HuntGiveUp(Person p, Herd? h)
    {
        St.HuntsLost++;
        RememberFail(p, h);
        p.LastHunt = Now + 6 * GameTime.TicksPerHour; // 놓쳤다 — 한동안은 다시 나서지 않는다(지쳐서)
        AddThought(p, H_huntFailed);
        if (h is not null && Fauna.Animals[h.Species].Size >= 2) AddWish(p, W_hunting, 10);
        ReleaseHunters(p);
    }

    /// <summary>함께 사냥하던 사람들을 풀어 준다(곧 다시 판단).</summary>
    private void ReleaseHunters(Person p)
    {
        foreach (var q in Life(p.Cell).People)
            if (!q.Dead && q.Id != p.Id && q.Act is Act.Hunt or Act.Butcher && q.WhyArg == p.Id) { Integrate(q, Now); SetWhy(q, Act.Idle, Why.None); InterruptPerson(q); }
    }

    /// <summary>몰기가 끝났다(이끄는 사람): 잡았나 · 다쳤나 · 놓쳤나.</summary>
    private void ResolveHunt(Person p)
    {
        var life = Life(p.Cell);
        var rng = Rng(p.Cell);
        int here = p.Tile(Now);
        if (!life.Herds.TryGetValue(p.Target, out var h) || Cheb(h.Tile(Now), here) > 8) { HuntGiveUp(p, h); return; }
        var a = Fauna.Animals[h.Species];
        int ht = h.Tile(Now);
        var helpers = new List<Person>();
        foreach (var q in life.People)
            if (!q.Dead && q.Id != p.Id && q.Act == Act.Hunt && q.WhyArg == p.Id && !q.Walking && Cheb(q.Tile(Now), ht) <= 10) helpers.Add(q);
        int weapon = WeaponPower(p);
        foreach (var q in helpers) weapon = Math.Max(weapon, WeaponPower(q));
        int chance = HuntChance(p, a, helpers.Count, weapon);
        bool success = rng.NextInt(100) < chance;
        St.HuntsTried++;
        // 다침: 사나운 큰 짐승은 덤빈다(든 것이 좋고 사람이 많을수록 덜)
        if (a.Fierce > 0 && a.Size >= 2)
        {
            int inj = Math.Max(2, a.Fierce * 5 + a.Size * 3 - weapon * 2 - helpers.Count * 2);
            if (rng.NextInt(100) < inj)
            {
                int k = rng.NextInt(helpers.Count + 1);
                var victim = k == 0 ? p : helpers[k - 1];
                long sev = (a.Fierce * 12_000L + a.Size * 9_000L) * (40 + rng.NextInt(80)) / 100;
                sev = sev * 100 / (100 + WeaponPower(victim) * 8 + Ability(victim, PeopleData.Str) * 3);
                St.HuntInjuries++;
                Integrate(victim, Now);
                AddBio(victim, BioKind.Injured, h.Species, (int)(sev / 1000));
                Injure(victim, (int)sev, DeathCause.HuntAccident);
                PassionEvent(victim, PeopleData.Hunting, -30); // (E6) 크게 다치면 열정이 식는다
                if (victim.Dead) { St.HuntDeaths++; if (victim == p) { ReleaseHunters(p); return; } helpers.Remove(victim); }
            }
        }
        foreach (var q in helpers) GainSkill(q, PeopleData.Hunting, success ? 12 : 4);
        GainSkill(p, PeopleData.Hunting, success ? 20 : 6);
        if (!success)
        {
            St.HuntsLost++;
            p.LastHunt = Now + 6 * GameTime.TicksPerHour;
            RememberFail(p, h);
            AddThought(p, H_huntFailed);
            if (a.Size >= 2) { AddWish(p, W_hunting, 25); if (a.Size >= 2) AddBio(p, BioKind.HuntFailed, h.Species, helpers.Count); }
            else AddWish(p, W_hunting, 8);
            foreach (var q in helpers) { AddThought(q, H_huntFailed); if (a.Size >= 2) AddWish(q, W_hunting, 10); }
            HerdStop(h); HerdFlee(life, h, a, here % S * 1000 + 500, here / S * 1000 + 500, Fauna.Animals.Count > 0 ? ARng(life) : rng);
            ReleaseHunters(p);
            return;
        }
        // 잡았다
        St.HuntSuccess++;
        St.KillsBySpecies[h.Species]++;
        p.Kills++;
        h.Count--;
        bool tameChance = a.Tame && rng.NextInt(100) < 35;
        if (h.Count <= 0) RemoveHerd(life, h); else { HerdStop(h); HerdFlee(life, h, a, here % S * 1000 + 500, here / S * 1000 + 500, ARng(life)); }
        AddThought(p, H_hunted);
        AddBio(p, BioKind.Hunted, h.Species, helpers.Count);
        foreach (var q in helpers) AddThought(q, H_hunted);
        p.Wish[W_hunting] = Math.Max(0, p.Wish[W_hunting] - 20);
        // (E6) 큰 짐승 · 포식자를 잡은 무기에는 이름이 붙는다(사연 있는 물건) · 사냥 열정
        if (a.Size >= 3 || a.Predator) { WantName(p, BestWeapon(p), 1, h.Species); PassionEvent(p, PeopleData.Hunting, 25); }
        else PassionEvent(p, PeopleData.Hunting, 4);
        if (InProvince) Defer(new PendingOp { Kind = PendingOp.Kinds.Hunt, A = p.Id, B = h.Species, C = helpers.Count, D = -1 });
        else HuntWorld(p, h.Species, helpers.Count);
        // 함께 잡은 사람(다른 말을 쓰면): 그 짐승의 낱말을 이끈 사람의 말에서 빌려 온다
        foreach (var q in helpers)
        {
            if (q.LangId == p.LangId) continue;
            if (InProvince) Defer(new PendingOp { Kind = PendingOp.Kinds.Hunt, A = q.Id, B = h.Species, C = -1, D = p.Id });
            else NewWordConcept(q, Fauna.Animals[h.Species].Id, p.Id);
        }
        // 어미를 잃은 새끼가 곁에 남는다(길들이기의 첫걸음)
        if (tameChance) MakeCompanion(life, p, h.Species, ht);
        // 그 자리에서 손질
        bool sharp = HasSharpTool(p) || helpers.Any(HasSharpTool);
        long dur = (a.Size == 1 ? 600 : a.Size == 2 ? 3_600 : 7_200) * (sharp ? 3 : 2) / 3;
        dur = dur * WorkPercent(p) / 100;
        SetWhy(p, Act.Butcher, Why.Butchering, p.Id);
        p.Target = -1; p.TargetB = h.Species;
        DoStill(p, dur);
        foreach (var q in helpers) { Integrate(q, Now); SetWhy(q, Act.Butcher, Why.Butchering, p.Id); q.Target = -1; q.TargetB = h.Species; DoStill(q, dur + 1); }
    }

    /// <summary>손질이 끝났다(이끄는 사람): 날카로운 도구가 있으면 다 나오고, 없으면 고기 조금 · 뼈만. 함께한 사람이 돌아가며 나눠 들고, 못 든 것은 그 자리에 둔다.</summary>
    private void FinishButcher(Person p, bool done)
    {
        if (p.WhyArg != p.Id) return;
        var life = Life(p.Cell);
        int species = p.TargetB;
        if ((uint)species >= (uint)Fauna.Animals.Count) return;
        var a = Fauna.Animals[species];
        int here = p.Tile(Now);
        var crew = new List<Person> { p };
        foreach (var q in life.People)
            if (!q.Dead && q.Id != p.Id && q.Act == Act.Butcher && q.WhyArg == p.Id && Cheb(q.Tile(Now), here) <= 10) crew.Add(q);
        bool sharp = crew.Any(HasSharpTool);
        var yields = new List<(int Item, int Count)>();
        foreach (var (item, n) in a.Yields)
        {
            int k = n;
            if (!sharp || !done)
            {
                if (item == I_rawMeat) k = a.Size == 1 ? n : Math.Max(1, n / 3);
                else if (item == I_bone) k = 1;
                else k = 0;
            }
            if (k > 0) yields.Add((item, k));
        }
        St.Butchered++;
        foreach (var (item, n) in yields) if (item == I_rawMeat) St.MeatGot += n;
        if (sharp) WearTool(p, BestTool(p, Craft.P_sharp, 2), Rng(p.Cell));
        // (E5b 고침) 들고 갈 만큼만 든다 — 전에는 다 들고 다니다 썩혔다(잡은 고기의 몇 배가 썩음)
        //   고기: 손질한 사람마다 먹을 것이 넉넉할 때(4개)까지 → 둘레(20타일)의 배고픈 사람에게 2개씩 나눠 줌 → 남는 것은 말리는 법을 알면 그 자리에 말려 두고(말린 고기 — 썩지 않는다), 모르면 그냥 둔다.
        //   가죽: 추운 땅이거나 입을 것이 아쉽거나 가죽 일을 알 때만(따뜻한 땅에서는 짐이다).
        bool cold = ColdLand(life);
        bool wantHide = cold || crew.Exists(q => q.Wish[W_clothing] >= 10 || KnowsHideWork(q));
        bool canDry = R_driedMeat >= 0 && crew.Exists(q => q.Knows(R_driedMeat));
        int turn = 0, meatLeft = 0;
        bool Give(Person q, int item) { if (CarryWeight(q) + Math.Max(1, Prop(item, Craft.P_weight)) > CarryCapOf(q) + 4) return false; q.AddCarry(item, 1); return true; }
        foreach (var (item, n) in yields)
        {
            if (item == I_rawMeat) { meatLeft = n; continue; }
            if (Prop(item, Craft.P_covers) > 0 && !wantHide) { St.ButcherDropped += n; continue; } // 짐승 몸에 그대로 남긴다(땅에 쌓아 두지 않는다)
            bool spare = Prop(item, Craft.P_covers) == 0; // 뼈 · 힘줄 · 기름: 이미 둘 넘게 든 사람은 더 들지 않는다(짐이 되고 바닥에 쌓였다)
            for (int u = 0; u < n; u++)
            {
                bool given = false;
                for (int t = 0; t < crew.Count && !given; t++)
                {
                    var q = crew[(turn + t) % crew.Count];
                    if (spare && q.CarryCount(item) >= 2) continue;
                    if (!Give(q, item)) continue;
                    given = true; turn = (turn + t + 1) % crew.Count;
                }
                if (!given) St.ButcherDropped++; // 들 수 없는 것은 짐승 몸에 남긴다
            }
        }
        foreach (var q in crew)
            for (int need = Math.Max(0, 4 - FoodCarried(q)); need > 0 && meatLeft > 0 && Give(q, I_rawMeat); need--) meatLeft--;
        if (meatLeft > 0)
            foreach (var q in life.People)
            {
                if (meatLeft <= 0) break;
                if (q.Dead || q.Carrier >= 0 || crew.Contains(q) || q.Stage(Now) < LifeStage.Child || q.Food >= 70_000 || FoodCarried(q) >= 2 || Cheb(q.Tile(Now), here) > 20) continue;
                for (int k = 0; k < 2 && meatLeft > 0 && Give(q, I_rawMeat); k++) { meatLeft--; St.MeatShared++; }
            }
        if (meatLeft > 0 && life.Walk[here])
        {
            if (canDry && I_driedMeat >= 0) { life.Drop(here, I_driedMeat, (meatLeft + 1) / 2); St.MeatDried += meatLeft; } // 얇게 저며 말린다(둘이 하나로)
            else St.ButcherDropped += meatLeft; // 말리는 법을 모르면 남는 고기는 짐승 몸에 남긴다(곧 다른 짐승이 먹는다)
        }
        GainSkill(p, PeopleData.Hunting, 6);
        foreach (var q in crew) WearBest(q);
        ReleaseHunters(p);
    }

    // ───────── 세계에서 처음 · 무리에서 처음 · 별명 · 낱말 (맞추는 시각에) ─────────

    private void HuntWorld(Person p, int species, int helpers)
    {
        var a = Fauna.Animals[species];
        // 그 무리의 말에 그 짐승의 낱말이 생긴다(처음 잡은 무리)
        NewWordConcept(p, a.Id, -1);
        if (a.Size < 2) return;
        bool world = !HuntFirsts.ContainsKey(species);
        long gk = (long)p.LangId * 100 + species;
        bool group = !GroupHunts.ContainsKey(gk);
        string with = helpers > 0 ? $" — {helpers + 1}명이 함께 몰아서" : " — 혼자서";
        if (world)
        {
            HuntFirsts[species] = new HuntFirst(Now, p.Id, p.Cell, helpers);
            Notify(WorldEventKind.HuntFirst, p, species, $"세계에서 처음: {Hangul.I(DisplayName(p))} {Hangul.Eul(a.Name)} 잡았습니다{with}");
        }
        else if (group) Notify(WorldEventKind.HuntGroup, p, species, $"이 무리에서 처음: {Hangul.I(DisplayName(p))} {Hangul.Eul(a.Name)} 잡았습니다{with}");
        if (group) GroupHunts[gk] = Now;
        if ((world || group) && (a.Size >= 3 || a.Predator)) HuntNickname(p, a);
    }

    /// <summary>큰 짐승 · 포식자를 처음 잡은 사람에게 별명("곰을 잡은 이") — 그 무리의 말로.</summary>
    private void HuntNickname(Person p, AnimalDef a)
    {
        if (p.Nickname.Length > 0 || !Languages.TryGetValue(p.LangId, out var lang)) return;
        int c = C(a.Id), hunter = C("hunter");
        if (c < 0 || hunter < 0) return;
        foreach (int k in new[] { c, hunter }) if (!lang.Known.Contains(k)) { lang.Known.Add(k); lang.Origins[k] = (-1, Now); lang.Version++; }
        var concepts = new[] { c, hunter };
        p.Nickname = Words.Name(lang, p.Accent, concepts.Select(k => Lang.Concepts[k]).ToList(), Lang);
        p.NicknameConcepts = concepts;
        p.NicknameReason = $"{Hangul.Eul(a.Name)} 잡은 사람";
        AddBio(p, BioKind.Nicknamed, -1, -1 - a.Index);
    }

    // ───────── 물고기 ─────────

    /// <summary>물고기를 잡고 싶은 마음: 배가 고프거나 먹을 것이 아쉽고, 지방의 민물에 물고기가 있고, 물가가 보일 때.</summary>
    private int FishScore(Person p, ProvinceLife life, out int spot)
    {
        spot = -1;
        if (life.FishStock <= 0 || Now - p.LastFish < 6 * GameTime.TicksPerHour || life.IsNight(Now) || p.Stage(Now) < LifeStage.Child || p.AgeYears(Now) < 8) return int.MinValue; // (E5b 고침) 물고기는 6시간에 한 번까지(3시간이던 것)
        if (p.Food >= 60_000 && p.Wish[W_food] < 20) return int.MinValue;
        p.Position(Now, out int px, out int py);
        spot = life.NearestDrink(px / 1000, py / 1000, SightTiles); // (E5b 고침) 보이는 물가(둘러보기와 같은 — 타일마다 한 번만 셈한다)
        if (spot < 0) return int.MinValue;
        return (60_000 - Math.Min(60_000, p.Food)) / 1000 + p.Wish[W_food] / 3 + 6 + p.SkillLevel(PeopleData.Hunting);
    }

    private bool TryFish(Person p, int spot)
    {
        int here = p.Tile(Now);
        p.LastFish = Now;
        SetWhy(p, Act.Fish, Why.FishFood, p.Food / 1000);
        p.Target = spot;
        if (spot == here) { StandStill(p); DoStill(p, GameTime.TicksPerHour * WorkPercent(p) / 100); return true; }
        if (!WalkTo(p, spot)) return false;
        SetWhy(p, Act.Fish, Why.FishFood, p.Food / 1000);
        p.Target = spot;
        St.Fishings++;
        return true;
    }

    /// <summary>물가에서 한 시간: 손 · 뾰족한 막대 · 돌창으로. 물고기가 적을수록 어렵다.</summary>
    private void FinishFish(Person p)
    {
        var life = Life(p.Cell);
        if (life.FishStock <= 0 || life.FishCap <= 0) return;
        var rng = Rng(p.Cell);
        int w = WeaponPower(p);
        int chance = Math.Min(70, 18 + (w >= 3 ? w * 7 : 0) + p.SkillLevel(PeopleData.Hunting) * 2);
        chance = chance * (50 + 50 * life.FishStock / life.FishCap) / 100;
        GainSkill(p, PeopleData.Hunting, 3);
        if (rng.NextInt(100) >= chance) return;
        int n = Math.Min(life.FishStock, 1 + (rng.NextInt(100) < 30 ? 1 : 0));
        life.FishStock -= n;
        p.AddCarry(I_rawFish, n);
        St.FishCaught += n;
    }

    // ───────── 옷 ─────────

    /// <summary>입은 옷의 따뜻함(0.1°C): 몸 따뜻함 × 4°C + 발 따뜻함 × 3°C (털가죽 옷 · 신 = +26°C, 날가죽 두름 = +8°C).</summary>
    /// <summary>입은 것의 따뜻함(0.1°C) — (E6) 품질이 좋으면 더(나쁨 −20% · 좋음 +15% · 훌륭함 +30%).</summary>
    public int ClothWarm(Person p) => (p.Worn >= 0 ? Prop(p.Worn, Craft.P_warm) * 40 * ClothPct(p.WornQ) / 100 : 0) + (p.WornFeet >= 0 ? Prop(p.WornFeet, Craft.P_warm) * 30 * ClothPct(p.FeetQ) / 100 : 0);
    private static int ClothPct(int q) => q <= 0 ? 80 : q == 1 ? 100 : q == 2 ? 115 : 130;

    /// <summary>추운 밤이 있는 땅인가(그 지방 밤 기온이 12°C 아래) — 따뜻한 땅에서는 날가죽을 걸치지 않는다.</summary>
    private static bool ColdLand(ProvinceLife life) => life.Mean10 - life.Range10 / 2 < 120;

    /// <summary>들고 있는 입을 것 가운데 지금 입은 것보다 따뜻한 것을 입는다(벗은 것은 든다). 추운 밤이 있는 땅에서만(옷은 그 밖에는 짐).</summary>
    private void WearBest(Person p)
    {
        if (p.Dead || p.Carrier >= 0) return;
        var life = Life(p.Cell);
        bool cold = ColdLand(life);
        for (int slot = 1; slot <= 2; slot++)
        {
            int cur = slot == 1 ? p.Worn : p.WornFeet;
            int curW = cur >= 0 ? Prop(cur, Craft.P_warm) * 10 + Prop(cur, Craft.P_toughness) : -1;
            int best = -1, bw = curW;
            foreach (var (item, _) in p.Carry)
            {
                if (Craft.WearSlot(item) != slot) continue;
                int v = Prop(item, Craft.P_warm) * 10 + Prop(item, Craft.P_toughness);
                if (v > bw) { bw = v; best = item; }
            }
            if (best < 0) continue;
            if (!cold && Prop(best, Craft.P_covers) < 4 && cur < 0) continue; // 따뜻한 땅: 날가죽 · 마른 가죽은 걸치지 않는다(지은 옷은 입는다)
            int bq = QualityOf(p, best);
            p.RemoveCarry(best, 1);
            if (cur >= 0) { p.AddCarry(cur, 1); SetQuality(p, cur, slot == 1 ? p.WornQ : p.FeetQ); }
            if (slot == 1) { p.Worn = best; p.WornWear = 1000; p.WornQ = (byte)bq; } else { p.WornFeet = best; p.FeetWear = 1000; p.FeetQ = (byte)bq; }
            AddBio(p, BioKind.Wore, -1, best);
        }
    }

    /// <summary>하루 경계: 옷이 닳는다 · 다친 데가 낫는다 · 들고 있는 것이 썩는다 · 옷이 없는 가족에게 남는 옷을 준다.</summary>
    private void BodyDaily(Person p, Pcg32 rng)
    {
        // 다친 데가 낫는다(튼튼하면 빨리, 늙으면 느리게)
        if (p.Injury > 0)
        {
            int heal = 4_000 + (p.HasTrait(T_tough) ? 3_000 : 0) - (p.Stage(Now) == LifeStage.Elder ? 1_500 : 0);
            p.Injury = Math.Max(0, p.Injury - heal);
            if (p.Injury == 0) p.InjuryCause = DeathCause.None;
        }
        // 옷이 닳는다: 날가죽은 열흘쯤 · 마른 가죽은 한 달쯤 · 지은 옷은 몇 해
        for (int slot = 1; slot <= 2; slot++)
        {
            int item = slot == 1 ? p.Worn : p.WornFeet;
            if (item < 0) continue;
            int wear = (3 + (5 - Prop(item, Craft.P_toughness)) * 3 + Prop(item, Craft.P_rots) * 15) * WearPct(slot == 1 ? p.WornQ : p.FeetQ) / 100; // (E6) 품질이 좋으면 덜 닳는다
            if (slot == 1) { p.WornWear -= wear; if (p.WornWear <= 0) { p.Worn = -1; St.ClothesWornOut++; } }
            else { p.FeetWear -= wear; if (p.FeetWear <= 0) { p.WornFeet = -1; St.ClothesWornOut++; } }
        }
        ShareMeat(p);
        // 들고 있는 것이 썩는다(썩는다 3 이상: 하루에 (썩는다 − 2) / 8 — 날고기 · 날가죽은 이틀이면 반, 익힌 고기는 여드레)
        for (int k = p.Carry.Count - 1; k >= 0; k--)
        {
            var (item, n) = p.Carry[k];
            int r = Prop(item, Craft.P_rots);
            if (r < 3) continue;
            int lose = (n * (r - 2) + rng.NextInt(8)) / 8;
            if (lose > 0)
            {
                lose = Math.Min(n, lose);
                p.RemoveCarry(item, lose); St.Rotted += lose;
                if (Prop(item, Craft.P_meat) > 0 && Craft.IsAnimal(item)) St.RotMeat += lose; else if (Prop(item, Craft.P_covers) > 0) St.RotHide += lose; else St.RotOther += lose;
            }
        }
        // (E5b 고침) 옷 일은 추운 땅이거나 입을 것을 든 사람만(따뜻한 땅의 대부분은 볼 것이 없다 — 날마다 사람마다 도는 셈이라)
        bool cold = ColdLand(Life(p.Cell));
        if (cold && (p.Worn < 0 || p.WornFeet < 0)) GiveSpareClothes(p);
        if (p.Wish[W_clothing] >= 20) PoolHides(p);
        if (cold || HasWearable(p)) WearBest(p);
        // 추운 땅: 옷이 없거나 닳아 가거나 가족이 입을 것이 없으면 입을 것이 아쉽다(미리 짓는다)
        if (cold && p.Stage(Now) >= LifeStage.Child && (p.Worn < 0 || p.WornWear < 600 || FamilyNeedsClothes(p))) AddWish(p, W_clothing, 25);
        // (E6 툰드라) 반쯤 닳으면(신도) 더 아쉽다 — 가죽 손질 · 바느질을 앞당긴다
        if (cold && p.Stage(Now) >= LifeStage.Child && (p.Worn >= 0 && p.WornWear < 500 || p.WornFeet >= 0 && p.FeetWear < 500 || p.WornFeet < 0)) AddWish(p, W_clothing, 15);
    }

    /// <summary>옷이 없는 사람: 같은 지방 가족이 들고 있는 남는 입을 것을 받는다.</summary>
    private void GiveSpareClothes(Person p)
    {
        // (E6 고침 — 툰드라) 안겨 있는 아기는 입지 못한다(입기는 안긴 사람을 건너뛴다) — 무두질한 가죽이 날마다 아기 손으로 갔다가
        // 털옷 지을 사람에게 되돌아가기를 되풀이해 털옷 지을 가죽 석 장이 좀처럼 한 손에 모이지 않았다(E5c 부터 있던 구멍)
        if (p.Carrier >= 0) return;
        var life = Life(p.Cell);
        if (!ColdLand(life)) return;
        foreach (var q in life.People)
        {
            if (q.Dead || q.Id == p.Id || !IsFamily(p, q)) continue;
            foreach (var (item, _) in q.Carry)
            {
                int slot = Craft.WearSlot(item);
                if (slot == 0 || (slot == 1 ? p.Worn : p.WornFeet) >= 0) continue;
                q.RemoveCarry(item, 1);
                p.AddCarry(item, 1);
                return;
            }
        }
    }

    /// <summary>
    /// (E5b) 옷 지을 가죽을 한 사람에게 모은다: 털가죽 옷 짓는 법을 아는 어른이 입을 것이 아쉬우면, 같은 지방 가족이 든 옷감(그 법의 주 재료)을 받는다.
    /// </summary>
    private void PoolHides(Person p)
    {
        if (p.Stage(Now) < LifeStage.Adult || p.Wish[W_clothing] < 20) return;
        var life = Life(p.Cell);
        foreach (var k in p.Known)
        {
            var r = Craft.Rules[k.Rule];
            if (r.Uses < 2 || Craft.WearSlot(r.Result) == 0) continue;
            int have = 0, item = -1;
            foreach (var (it, n) in p.Carry) if (MeetsInput(it, r) && n > have) { have = n; item = it; }
            foreach (var q in life.People)
            {
                if (have >= r.Uses) break;
                if (q.Dead || q.Id == p.Id || !IsFamily(p, q)) continue;
                foreach (var (it, n) in q.Carry.ToList())
                {
                    if (!MeetsInput(it, r) || (item >= 0 && it != item)) continue;
                    int give = Math.Min(n, r.Uses - have);
                    q.RemoveCarry(it, give); p.AddCarry(it, give); have += give; item = it;
                    if (have >= r.Uses) break;
                }
            }
        }
    }

    /// <summary>땅에 놓인 것도 썩는다(하루 경계, 지방마다).</summary>
    /// <summary>(E5b 고침) 아이템마다 땅에서 썩거나(썩는다 3 이상) 먹히나(먹을 만한 것 3 이상).</summary>
    private bool[]? _perish;
    private bool[] PerishItems
    {
        get
        {
            if (_perish is not null) return _perish;
            var a = new bool[Craft.Catalog.Items.Count];
            for (int i = 0; i < a.Length; i++) a[i] = Prop(i, Craft.P_rots) >= 3 || Prop(i, Craft.P_edible) >= 3;
            return _perish = a;
        }
    }

    /// <summary>땅에 놓인 것도 썩는다(하루 경계, 지방마다). 썩거나 먹힐 것이 놓였던 타일만 번호 순으로 본다.</summary>
    private void RotGround(ProvinceLife life, Pcg32 rng)
    {
        if (life.PerishTiles.Count == 0) return;
        var tiles = L.RotTiles; tiles.Clear(); tiles.AddRange(life.PerishTiles);
        foreach (int t in tiles)
        {
            if (!life.Items.TryGetValue(t, out var list)) { life.PerishTiles.Remove(t); continue; }
            if (t == life.Stash) continue; // (E5c 고침) 모아 둔 자리는 따로(StashDaily — 담는 것에 따라) — 전에는 두 규칙을 다 받아 두 번 사라졌다
            bool any = false;
            for (int k = list.Count - 1; k >= 0; k--)
            {
                var (item, n) = list[k];
                int r = Prop(item, Craft.P_rots);
                // (E5b 고침) 땅에 둔 먹을 것은 썩지 않아도 짐승 · 벌레가 조금씩 먹는다(하루 1/10) — 말려 둔 고기가 끝없이 쌓이지 않게
                bool food = Prop(item, Craft.P_edible) >= 3;
                if (r < 3 && !food) continue;
                int lose = r >= 3 ? Math.Min(n, (n * (r - 2) + rng.NextInt(8)) / 8) : Math.Min(n, (n + rng.NextInt(10)) / 10);
                if (lose < n) any = true;
                if (lose <= 0) continue;
                if (lose == n) list.RemoveAt(k); else list[k] = (item, n - lose);
                if (Craft.IsAnimal(item)) St.RotGround += lose;
            }
            if (list.Count == 0) life.RemoveItems(t);
            if (!any) life.PerishTiles.Remove(t);
        }
    }

    // ───────── 다침 ─────────

    /// <summary>다친다(sev: 0~100,000). 다친 정도가 100,000 에 닿거나 건강이 바닥나면 죽는다(그 까닭으로).</summary>
    internal void Injure(Person p, int sev, DeathCause cause)
    {
        if (p.Dead || sev <= 0) return;
        p.Injury = Math.Min(Person.Full, p.Injury + sev);
        p.InjuryCause = cause;
        p.Health = Clamp(p.Health - sev / 2);
        St.Injuries++;
        if (p.Injury >= Person.Full || p.Health <= 0) Kill(p, cause);
    }

    // ───────── 길들이기의 첫걸음 ─────────

    /// <summary>어미를 잃은 새끼가 사람 곁에 남는다: 곁(12타일)의 가장 어린 아이, 없으면 잡은 사람을 따른다.</summary>
    private void MakeCompanion(ProvinceLife life, Person hunter, int species, int tile)
    {
        Person owner = hunter;
        foreach (var q in life.People)
            if (!q.Dead && q.Carrier < 0 && q.Stage(Now) == LifeStage.Child && Cheb(q.Tile(Now), tile) <= 12 && (owner == hunter || q.AgeTicks(Now) < owner.AgeTicks(Now))) owner = q;
        foreach (var (_, c) in life.Herds) if (c.Owner == owner.Id) return; // 하나만
        var h = NewHerd(life, species, 1, tile, ARng(life));
        if (h is null) return;
        h.Owner = owner.Id; h.Bond = 10; h.State = HerdState.Follow;
        St.Tamed++;
        AddBio(owner, BioKind.Tamed, species, hunter.Id);
        var a = Fauna.Animals[species];
        Notify(WorldEventKind.Tamed, owner, species, $"어미를 잃은 새끼 {Hangul.I(a.Name)} {DisplayName(owner)} 곁에 남아 따라다닙니다");
    }

    /// <summary>(경계를 넘을 때) 따르는 짐승도 함께 넘어간다 — 넘겨주기(한 줄기)에서.</summary>
    private void MoveCompanions(Person p, ProvinceLife from, ProvinceLife to, int entry)
    {
        List<Herd>? move = null;
        foreach (var (_, h) in from.Herds) if (h.Owner == p.Id) (move ??= new()).Add(h);
        if (move is null) return;
        foreach (var h in move)
        {
            RemoveHerd(from, h);
            h.Id = to.NextHerdId++;
            h.Motion.SetStill(Now, entry % S * 1000 + 500, entry / S * 1000 + 500);
            h.Prev.CopyFrom(h.Motion);
            h.Home = entry;
            to.Herds[h.Id] = h;
            ScheduleHerd(to, h, Now + 300);
        }
    }

    private bool HasWearable(Person p) { foreach (var (it, _) in p.Carry) if (Craft.WearSlot(it) != 0) return true; return false; }

    /// <summary>들고 있는 먹을 것(먹을 만한 것 3 이상) 수.</summary>
    private int FoodCarried(Person p) { int n = 0; foreach (var (it, c) in p.Carry) if (Prop(it, Craft.P_edible) >= 3) n += c; return n; }

    /// <summary>가죽 일(긁기 · 꿰매기 · 무두질)을 아나.</summary>
    private bool KnowsHideWork(Person p) { foreach (var k in p.Known) { var r = Craft.Rules[k.Rule]; if (r.Action == A_scrape || r.Action == A_sew || (r.Input.ContainsKey(Craft.P_covers))) return true; } return false; }

    /// <summary>
    /// (E5b 고침) 썩을 날고기를 넉넉히 든 사람은 같은 지방의 먹을 것이 없는 가족에게 나눠 준다(하루 경계).
    /// </summary>
    private void ShareMeat(Person p)
    {
        int raw = p.CarryCount(I_rawMeat) + p.CarryCount(I_rawFish);
        if (raw < 4) return;
        var life = Life(p.Cell);
        foreach (var q in life.People)
        {
            if (raw < 4) break;
            if (q.Dead || q.Id == p.Id || q.Carrier >= 0 || q.Stage(Now) < LifeStage.Child || !IsFamily(p, q) || FoodCarried(q) >= 2) continue;
            int it = p.CarryCount(I_rawMeat) > 0 ? I_rawMeat : I_rawFish;
            int give = Math.Min(2, p.CarryCount(it));
            p.RemoveCarry(it, give); q.AddCarry(it, give); raw -= give; St.MeatShared += give;
        }
    }

    private bool HasCoverItem(Person p) { if (p.Worn >= 0) return true; foreach (var (item, _) in p.Carry) if (Prop(item, Craft.P_covers) > 0) return true; return false; }

    /// <summary>입을 것 바람이 이루어졌나: 입은 옷이 이것만큼 따뜻하고 질기면.</summary>
    private bool WornCovers(Person p, int result) => !FamilyNeedsClothes(p) && (
        (Craft.WearSlot(result) == 1 && p.Worn >= 0 && p.WornWear >= 600 && Prop(p.Worn, Craft.P_warm) >= Prop(result, Craft.P_warm) && Prop(p.Worn, Craft.P_covers) >= Prop(result, Craft.P_covers))
        || (Craft.WearSlot(result) == 2 && p.WornFeet >= 0 && p.FeetWear >= 600 && Prop(p.WornFeet, Craft.P_warm) >= Prop(result, Craft.P_warm))
        || Craft.WearSlot(result) == 0 && p.Worn >= 0 && p.WornWear >= 600 && Prop(p.Worn, Craft.P_covers) >= 5);

    /// <summary>
    /// (E5b) 추운 땅에서 입을 것이 모자라다: 입은 옷이 닳아 가거나(닳음 60% 넘게), 같은 지방의 가족(짝 · 아이)이 입을 것이 없고 남는 옷도 들고 있지 않다.
    /// </summary>
    private bool FamilyNeedsClothes(Person p)
    {
        var life = Life(p.Cell);
        if (!ColdLand(life) || p.Stage(Now) < LifeStage.Adult) return false;
        foreach (var (item, _) in p.Carry) if (Craft.WearSlot(item) == 1) return false; // 남는 옷이 있다
        foreach (var q in life.People)
        {
            if (q.Dead || q.Id == p.Id || q.Carrier >= 0 || q.Stage(Now) == LifeStage.Baby) continue;
            if (q.Partner != p.Id && q.Mother != p.Id && q.Father != p.Id) continue;
            if (q.Worn < 0 || (q.Worn >= 0 && Prop(q.Worn, Craft.P_covers) < 4)) return true;
        }
        return false;
    }
}
