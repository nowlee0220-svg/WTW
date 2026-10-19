using Wtw.Sim.Core;
using Wtw.Sim.Provinces;
using Wtw.Sim.Time;

namespace Wtw.Sim.People;

public sealed partial class PeopleSim
{
    private const int S = HexFrame.Size;
    public const int PickTicks = 300;          // 한 몫 따는 데 (7분 남짓)
    public const int TalkMin = 500, TalkMax = 1_500;

    // ───────── 시작 도우미 ─────────

    private void SetWhy(Person p, Act act, Why why, int arg = 0) { p.Act = act; p.Why = why; p.WhyArg = arg; }

    /// <summary>제자리에서 duration 동안 한다(급한 일이 닥치면 그때 다시 판단).</summary>
    private void DoStill(Person p, long duration)
    {
        p.Position(Now, out int x, out int y);
        p.NewMotion().SetStill(Now, x, y);
        p.Walking = false;
        p.ActUntil = Now + Math.Max(1, duration);
        Schedule(p, PredictUrgent(p, Now, p.ActUntil, p.Tile(Now)));
    }

    private void StandStill(Person p)
    {
        p.Position(Now, out int x, out int y);
        p.NewMotion().SetStill(Now, x, y);
    }

    /// <summary>goal 타일까지 걷기 시작. 길이 없으면 false. straightOnly 면 곧게 갈 수 있을 때만(둘러보기).</summary>
    private bool WalkTo(Person p, int goal, bool straightOnly = false)
    {
        var life = Life(p.Cell);
        p.Position(Now, out int x, out int y);
        if (straightOnly) { if (!life.Paths.TryStraight(x, y, goal, _path)) return false; }
        else
        {
            Stats.Paths++;
            if (!life.Paths.Find(x, y, goal, _path)) { Stats.PathFails++; return false; }
        }
        var m = p.NewMotion();
        m.SetPath(Now, _path, TicksPerTile(p));
        p.Walking = true;
        p.ActUntil = m.Arrive;
        Schedule(p, m.Arrive);
        return true;
    }

    /// <summary>goal 둘레에서 비어 있는 걸을 수 있는 타일(사람마다 다른 순서) — 여럿이 한 타일에 겹쳐 서지 않게.</summary>
    private int Beside(Person p, int goal, int radius = 1)
    {
        var life = Life(p.Cell);
        int gx = goal % S, gy = goal / S;
        int start = (int)(StableHash.Mix((ulong)p.Id) % 8);
        for (int r = 1; r <= radius; r++)
            for (int k = 0; k < 8; k++)
            {
                int d = (start + k) % 8;
                int nx = gx + DX8[d] * r, ny = gy + DY8[d] * r;
                if ((uint)nx >= S || (uint)ny >= S) continue;
                int t = ny * S + nx;
                if (life.Walk[t] && life.Region[t] == life.Region[goal]) return t;
            }
        return goal;
    }

    private readonly Dictionary<int, int> _claims = new();

    private static readonly int[] DX8 = { 1, 1, 0, -1, -1, -1, 0, 1 };
    private static readonly int[] DY8 = { 0, 1, 1, 1, 0, -1, -1, -1 };

    private static int Cheb(int a, int b) => Math.Max(Math.Abs(a % S - b % S), Math.Abs(a / S - b / S));

    // ───────── 판단 ─────────

    private void Decide(Person p)
    {
        Stats.Decisions++;
        PurgeThoughts(p);
        p.Target = -1; p.TargetB = -1;
        p.Asleep = false;
        var life = Life(p.Cell);
        var rng = Rng(p.Cell);
        var stage = p.Stage(Now);
        if (stage == LifeStage.Baby) { SetWhy(p, Act.Carried, Why.Carried); p.Walking = false; return; }
        Look(p);
        int here = p.Tile(Now);
        p.Position(Now, out int px, out int py);
        int hx = px / 1000, hy = py / 1000;
        SeeAround(p, life, hx, hy);

        // 정신 붕괴 중
        if (p.BreakUntil > Now && p.Food > 5_000 && p.Water > 5_000 && p.Rest > UrgentRest) { ContinueBreak(p); return; }
        if (p.BreakUntil >= 0 && p.BreakUntil <= Now) { p.BreakUntil = -1; AddThought(p, H_catharsis); }

        // 급한 것부터
        if (p.Rest < UrgentRest) { StartSleep(p, here, Why.Exhausted, collapse: true); return; }
        int felt = FeltNow(p);
        if (felt < 110) AddWish(p, W_warmth, felt < 60 ? 12 : 5);
        if (p.Hypothermia > 10_000 && felt < 60 && (TryWarmByFire(p, Why.VeryCold) || TryLightKnownFire(p) || TryWarm(p, Why.VeryCold))) return;
        if (p.Heatstroke > 10_000 && felt > 400 && TryWarm(p, Why.Hot)) return;
        if (p.Water < UrgentWater && TryDrink(p, Why.VeryThirsty)) return;
        if (p.Food < UrgentFood && TryEat(p, p.Food <= 0 ? Why.Starving : Why.VeryHungry)) return;

        // 정신 붕괴
        int mood = Mood(p);
        int thr = BreakThreshold(p);
        if (mood < thr && Now - p.LastBreak > 2 * GameTime.TicksPerDay)
        {
            // (E4) 아이는 훨씬 드물게(짧은 떼쓰기), 가족이 곁에 있으면 위로받아 덜 무너진다
            int chance = (thr - mood) * 3;
            if (stage == LifeStage.Child) chance /= 4;
            if (FamilyNear(p, 8)) chance /= 2;
            if (rng.NextInt(1000) < chance) { StartBreak(p, mood); return; }
        }

        // 밤: 잔다
        long tod = life.LocalTickOfDay(Now);
        long bed = (stage == LifeStage.Child ? 19 : 20) * GameTime.TicksPerHour + p.BedShift;
        long wake = 6 * GameTime.TicksPerHour + p.WakeShift;
        if ((tod >= bed || tod < wake) && p.Rest < 95_000) { GoSleep(p, Why.Night); return; }
        if (p.Rest < 12_000) { GoSleep(p, Why.Exhausted); return; }

        // (E3b) 떠나고 싶은 마음이 있으면 떠난다(집안과 함께)
        if (p.MoveWhy != Why.None && TryMove(p)) return;

        // (E4) 불씨가 있거나 불이 보이면(따뜻함이 아쉬울 때) 불을 옮겨 온다 · 피운다
        if (TryFireWork(p, life, here, rng)) return;

        // 아이는 어른을 따라다닌다
        if (stage == LifeStage.Child && Guardian(p) is { } g)
        {
            int gt = g.Tile(Now);
            if (Cheb(gt, here) > 6 && rng.NextInt(100) < 80 && life.Paths.Reachable(here, gt) && WalkTo(p, Beside(p, gt, 2))) { SetWhy(p, Act.Follow, Why.FollowParent, g.Id); p.Target = g.Id; return; }
        }

        // 하고 싶은 것 고르기 (점수 + 조금의 무작위) — 1순위가 안 되면 다음 순위(세 번까지)
        _opts.Clear();
        void Opt(int code, int score) => _opts.Add((score + rng.NextInt(12), code));
        if (p.Food < 55_000) Opt(1, (55_000 - p.Food) / 500 + 20);
        if (p.Water < 50_000) Opt(2, (50_000 - p.Water) / 500 + 25);
        if (p.Social < 60_000) Opt(3, (60_000 - p.Social) / 600 + (p.HasTrait(T_eloquent) ? 8 : 0) + (p.HasTrait(T_blunt) ? -5 : 0));
        if (p.Joy < 45_000) Opt(4, (45_000 - p.Joy) / 600 + (p.HasTrait(T_lazy) ? 10 : 0));
        if (p.Rest < 40_000) Opt(5, (40_000 - p.Rest) / 700);
        if (felt < 110 && life.Shelter[here] < 2) Opt(6, (110 - felt) / 2 + 10);
        if (felt > 320 && life.Shelter[here] == 0) Opt(6, (felt - 320) / 2 + 10);
        Opt(7, 5 + (p.HasTrait(T_curious) ? 8 : 0) - (p.HasTrait(T_stubborn) ? 5 : 0) + (stage == LifeStage.Child ? 6 : 0));
        int carried = 0; foreach (var c in p.Carry) carried += c.Count;
        if (carried < 6) Opt(8, 3 + (p.HasTrait(T_curious) ? 10 : 0) - (p.HasTrait(T_stubborn) ? 3 : 0));
        Opt(9, 6);
        // (E4) 실험 · 아는 법대로 만들기 · 불 지키기 · 캐기
        int ex = ExperimentScore(p, life); if (ex > int.MinValue) Opt(10, ex); else Stats.PlanWhy[8]++;
        int cs = CraftScore(p); if (cs > int.MinValue) Opt(11, cs);
        if (p.Known.Count > 0) Opt(12, 14);
        if ((p.Wish[W_shiny] >= 20 || p.Wish[W_hardTool] >= 40 || p.HasTrait(T_curious)) && HasHardTool(p) && !life.IsNight(Now)) Opt(13, 8 + p.Wish[W_shiny] / 4);
        // (E5) 가르치기 · 그림 보기
        int ts = TeachScore(p, life); if (ts > int.MinValue) Opt(14, ts);
        Painting? study = null;
        int ss = StudyScore(p, life, out study); if (ss > int.MinValue) Opt(15, ss);
        _opts.Sort((a, b) => b.Score != a.Score ? b.Score.CompareTo(a.Score) : a.Code.CompareTo(b.Code));
        for (int k = 0; k < Math.Min(3, _opts.Count); k++)
        {
            int best = _opts[k].Code;
            bool ok = best switch
            {
                1 => TryEat(p, Why.Hungry),
                2 => TryDrink(p, Why.Thirsty),
                3 => TryTalk(p),
                4 or 9 => StartRest(p, best == 4 ? Why.Bored : Why.Nothing),
                5 => StartRest(p, Why.Exhausted),
                6 => TryWarm(p, felt > 320 ? Why.Hot : Why.Cold),
                7 => TryWander(p, p.HasTrait(T_curious) ? Why.Curious : Why.Nothing),
                8 => TryGather(p),
                10 => (Stats.PlanWhy[9]++ >= 0) && TryExperiment(p),
                11 => TryCraftKnown(p),
                12 => TryTendFire(p),
                13 => TryMine(p),
                14 => TryTeach(p),
                15 => study is not null && TryStudy(p, study),
                _ => false,
            };
            if (ok && p.ActUntil > Now) return;
            if (ok) Stats.PlanWhy[19]++; // 하겠다고 했는데 아무것도 잡히지 않았다 — 다음 할 일로(멈춰 서서 굶지 않게)
        }
        if (!TryWander(p, Why.Nothing)) StartRest(p, Why.Nothing);
    }

    private readonly List<(int Score, int Code)> _opts = new();

    /// <summary>아이가 따라다닐 어른: 어머니 → 아버지 → 안아 주던 사람.</summary>
    private Person? Guardian(Person p)
    {
        foreach (int id in new[] { p.Mother, p.Father })
            if (id >= 0 && Get(id) is { Dead: false } g && g.Cell == p.Cell && g.Stage(Now) >= LifeStage.Adult) return g;
        return null;
    }

    // ───────── 먹기 · 마시기 ─────────

    // ───────── 쉬기 · 둘러보기 · 줍기 · 추위 피하기 ─────────

    private bool StartRest(Person p, Why why)
    {
        SetWhy(p, Act.Rest, why, p.Joy / 1000);
        DoStill(p, GameTime.TicksPerHour + Rng(p.Cell).NextInt((int)(2 * GameTime.TicksPerHour)));
        return true;
    }

    private bool TryWander(Person p, Why why)
    {
        var life = Life(p.Cell);
        var rng = Rng(p.Cell);
        p.Position(Now, out int px, out int py);
        int hx = px / 1000, hy = py / 1000, here = hy * S + hx;
        if (!life.Walk[here]) return false;
        // 호기심 많은 사람은 아직 못 본 동굴로
        if (!p.FoundCave && life.CaveTiles > 0 && rng.NextInt(100) < (p.HasTrait(T_curious) ? 25 : 6))
        {
            int cave = life.NearestShelter(hx, hy, 45, caveOnly: true);
            if (cave >= 0 && life.IsHidden(cave) && WalkTo(p, cave)) { SetWhy(p, Act.Wander, Why.Curious); p.Target = cave; return true; }
        }
        int radius = 8 + Ability(p, PeopleData.Nerve) + (p.HasTrait(T_curious) ? 10 : 0) - (p.HasTrait(T_stubborn) ? 4 : 0);
        if (why is Why.NoFood or Why.NoWater) radius += 25;
        for (int tries = 0; tries < 10; tries++)
        {
            int tx = hx + rng.NextInt(-radius, radius + 1), ty = hy + rng.NextInt(-radius, radius + 1);
            if ((uint)tx >= S || (uint)ty >= S) continue;
            int t = ty * S + tx;
            if (t == here || !life.Walk[t] || life.Region[t] != life.Region[here] || HexFrame.BorderDistance(t) < 3) continue;
            if (!WalkTo(p, t, straightOnly: tries < 9)) continue;
            SetWhy(p, Act.Wander, why);
            p.Target = t;
            return true;
        }
        return false;
    }

    private bool TryGather(Person p)
    {
        var life = Life(p.Cell);
        p.Position(Now, out int px, out int py);
        int tile = life.NearestCarry(px / 1000, py / 1000, Now, 25);
        if (tile < 0 || !WalkTo(p, tile)) return false;
        SetWhy(p, Act.Gather, p.HasTrait(T_curious) ? Why.Curious : Why.Nothing);
        p.Target = tile;
        return true;
    }

    /// <summary>추위 · 더위 피하기: 더 나은 그늘(동굴 → 바위 그늘 → 나무 그늘)로.</summary>
    private bool TryWarm(Person p, Why why)
    {
        if (why != Why.Hot && TryWarmByFire(p, why)) return true;
        var life = Life(p.Cell);
        p.Position(Now, out int px, out int py);
        int hx = px / 1000, hy = py / 1000, here = hy * S + hx;
        int cur = life.Shelter[here];
        int tile = life.NearestShelter(hx, hy, 70, caveOnly: false);
        if (tile < 0 || life.Shelter[tile] <= cur)
        {
            if (cur >= 2) return false;
            // 나무 그늘이라도
            int tree = -1; long bd = long.MaxValue;
            for (int dy = -8; dy <= 8; dy++)
                for (int dx = -8; dx <= 8; dx++)
                {
                    int x = hx + dx, y = hy + dy;
                    if ((uint)x >= S || (uint)y >= S) continue;
                    int t = y * S + x;
                    if (life.Shelter[t] == 1 && life.Region[t] == life.Region[here]) { long d = dx * dx + dy * dy; if (d < bd) { bd = d; tree = t; } }
                }
            if (tree < 0 || cur >= 1) return false;
            tile = tree;
        }
        if (tile == here) return false;
        if (!WalkTo(p, tile)) return false;
        SetWhy(p, Act.Warm, why, FeltNow(p));
        p.Target = tile;
        return true;
    }

    // ───────── 잠 ─────────

    private void GoSleep(Person p, Why why)
    {
        var life = Life(p.Cell);
        p.Position(Now, out int px, out int py);
        int hx = px / 1000, hy = py / 1000, here = hy * S + hx;
        int spot = -1;
        Why reason = why;
        if (Get(p.Partner) is { Dead: false } mate && mate.Cell == p.Cell && (mate.Asleep || mate.Act == Act.Sleep))
        {
            int mt = mate.Walking ? TileOf(mate.Current.EndX, mate.Current.EndY) : mate.Tile(Now);
            if (life.Paths.Reachable(here, mt)) { spot = Beside(p, mt); reason = Why.Partner; }
        }
        if (spot < 0 && p.Stage(Now) == LifeStage.Child && Guardian(p) is { } g && (g.Asleep || g.Act == Act.Sleep))
        {
            int gt = g.Walking ? TileOf(g.Current.EndX, g.Current.EndY) : g.Tile(Now);
            if (life.Paths.Reachable(here, gt)) { spot = Beside(p, gt); reason = Why.FamilyNear; }
        }
        bool coldNight = life.Mean10 - life.Range10 / 2 < 120;
        if (spot < 0 && coldNight)
        {
            // (E4) 불이 있으면 불가에서 잔다
            int ft = life.NearestFire(hx, hy, Now, (int)FireSleepRange);
            if (ft >= 0 && TendBeforeSleep(p, life, ft)) return;
            if (ft >= 0) { int b1 = Beside(p, ft, 1); if (b1 == ft) b1 = Beside(p, ft, 2); if (b1 != ft && life.Paths.Reachable(here, b1)) { spot = b1; reason = Why.FireNear; } }
        }
        if (spot < 0 && coldNight)
        {
            // 추운 밤에는 동굴이 가장 따뜻하다(늘 연평균 근처) — 가까운 동굴이 있으면 그리로, 없으면 바위 그늘
            int sh = life.NearestShelter(hx, hy, 60, caveOnly: true);
            if (sh < 0 || !life.Paths.Reachable(here, sh)) sh = life.NearestShelter(hx, hy, 60, caveOnly: false);
            if (sh >= 0 && life.Shelter[sh] > life.Shelter[here]) { spot = Beside(p, sh, 0 + 1); if (life.Shelter[spot] < 2) spot = sh; reason = Why.Cold; }
        }
        if (spot < 0)
        {
            // 잠든 가족 · 친구 곁
            Person? near = null; long bd = 18 * 18;
            foreach (var q in life.People)
            {
                if (q.Dead || q.Id == p.Id || !q.Asleep || q.Carrier >= 0) continue;
                var rel = p.GetRelation(q.Id);
                if (rel.Opinion < 20 && !IsFamily(p, q)) continue;
                long d = ProvinceLife.D2(q.Tile(Now), hx, hy);
                if (d < bd) { bd = d; near = q; }
            }
            if (near is not null) { spot = Beside(p, near.Tile(Now)); reason = Why.FamilyNear; }
        }
        if (spot >= 0 && spot != here && WalkTo(p, spot)) { SetWhy(p, Act.Sleep, reason); p.Target = spot; return; }
        StartSleep(p, here, reason, collapse: false);
    }

    private static int TileOf(int mx, int my) => (my / 1000) * S + mx / 1000;

    private void StartSleep(Person p, int tile, Why why, bool collapse)
    {
        var life = Life(p.Cell);
        SetWhy(p, collapse ? Act.Collapse : Act.Sleep, why);
        p.Asleep = true;
        p.SleptCold = false;
        p.SleepShelter = life.Shelter[tile];
        p.Huddle = false;
        foreach (var q in life.People)
        {
            if (q.Dead || q.Id == p.Id || !q.Asleep || q.Carrier >= 0 || Cheb(q.Tile(Now), tile) > 2) continue;
            p.Huddle = true;
            if (!q.Huddle) { Integrate(q, Now); q.Huddle = true; }
        }
        if (life.Shelter[tile] == 3) Discover(p, tile);
        long tod = life.LocalTickOfDay(Now);
        long wake = 6 * GameTime.TicksPerHour + p.WakeShift;
        long untilWake = (wake - tod + GameTime.TicksPerDay) % GameTime.TicksPerDay;
        long need = (long)(Person.Full - p.Rest) * GameTime.TicksPerHour / RestAsleep;
        long dur = life.IsNight(Now) || tod >= 17 * GameTime.TicksPerHour ? Math.Max(untilWake, Math.Min(need, 3 * GameTime.TicksPerHour)) : Math.Max(need, GameTime.TicksPerHour / 2);
        if (collapse) dur = Math.Max(need / 2, GameTime.TicksPerHour);
        p.Target = tile;
        DoStill(p, dur);
    }

    private void WakeUp(Person p)
    {
        if (!p.Asleep) return;
        p.Asleep = false;
        int tile = p.Tile(Now);
        bool partnerNear = Get(p.Partner) is { Dead: false } mate && mate.Cell == p.Cell && Cheb(mate.Tile(Now), tile) <= 2;
        if (p.SleptCold) AddThought(p, H_sleptCold);
        else if (p.SleepShelter >= 2 || p.Huddle) AddThought(p, H_slept);
        else AddThought(p, H_rough);
        if (partnerNear) AddThought(p, H_withPartner);
        bool fireNear = Life(p.Cell).NearestFire(tile % S, tile / S, Now, 4) >= 0;
        if (fireNear) AddThought(p, H_firelight);
        else if (p.SleepShelter == 0 && !p.Huddle && Ability(p, PeopleData.Nerve) <= 3) { AddThought(p, H_dark); AddWish(p, W_warmth, 12); } // 어둠이 무서웠다 → 밝고 따뜻한 것이 있었으면
        else if (p.SleptCold) AddWish(p, W_warmth, 15);
        if (partnerNear && p.Female) TryConceive(p, Get(p.Partner)!);
        p.Huddle = false;
    }

    private void Discover(Person p, int tile)
    {
        int n = Life(p.Cell).RevealAround(tile);
        if (n > 0 && !p.FoundCave)
        {
            p.FoundCave = true;
            AddThought(p, H_cave);
            AddBio(p, BioKind.CaveFound);
        }
    }

    // ───────── 도착 · 끝 ─────────

    /// <summary>걷기가 끝났다: 하려던 일을 시작한다.</summary>
    private void Arrive(Person p)
    {
        var life = Life(p.Cell);
        int here = p.Tile(Now);
        StandStill(p);
        if ((life.Map.Flags[here] & TileFlags.Roof) != 0) Discover(p, here);
        if (p.Act is Act.Eat or Act.Drink) Look(p);
        var rng = Rng(p.Cell);
        switch (p.Act)
        {
            case Act.Eat when p.How == 3:
            case Act.Drink when p.How == 3:
                Decide(p); // 찾아다니다 닿은 곳: 둘러보고 다시 판단(보이면 그리로)
                return;
            case Act.Eat:
            {
                var h = life.HarvestAt(p.Target);
                int stock = life.StockAt(p.Target, Now);
                if (h is null || stock <= 0)
                {
                    if (p.Food <= 0) p.PassionPoints[PeopleData.Gathering] -= 10;
                    Forget(p.KnownFood, Person.Place(p.Cell, p.Target));
                    Look(p);
                    if (!TryEat(p, Why.NoFood, p.Target)) Decide(p);
                    return;
                }
                int want = (int)Math.Clamp((Person.Full - p.Food + h.Nutrition - 1) / Math.Max(1, h.Nutrition), 1, Math.Min(stock, 5));
                p.TargetB = want;
                DoStill(p, (long)want * PickTicks * WorkPercent(p) / 100);
                return;
            }
            case Act.Drink:
                DoStill(p, 150);
                return;
            case Act.Gather:
                DoStill(p, 200L * WorkPercent(p) / 100);
                return;
            case Act.EatCorpse:
                if (Get(p.Target) is { Dead: true, Eaten: false }) { DoStill(p, 2 * GameTime.TicksPerHour); return; }
                Decide(p);
                return;
            case Act.Sleep:
                StartSleep(p, here, p.Why, collapse: false);
                return;
            case Act.Warm:
                if (p.Target >= 0 && life.Fires.ContainsKey(p.Target)) { AddThought(p, H_byFire); p.Wish[W_warmth] = Math.Max(0, p.Wish[W_warmth] - 30); }
                if (life.IsNight(Now)) { StartSleep(p, here, p.Target >= 0 && life.Fires.ContainsKey(p.Target) ? Why.FireNear : Why.Cold, collapse: false); return; }
                DoStill(p, GameTime.TicksPerHour + rng.NextInt((int)GameTime.TicksPerHour));
                return;
            case Act.Wander:
                DoStill(p, 1_200 + rng.NextInt(2_400));
                return;
            case Act.Follow:
                DoStill(p, 600 + rng.NextInt(1_200));
                return;
            case Act.Talk:
            case Act.BreakArgue:
                BeginTalk(p);
                return;
            case Act.BreakWander:
                if (Now < p.BreakUntil && TryBreakWalk(p)) return;
                DoStill(p, 900);
                return;
            case Act.BreakLeave:
                DoStill(p, Math.Max(600, p.BreakUntil - Now));
                return;
            case Act.Cross when here == p.Target:
                ArriveAtBorder(p);
                return;
            // (E4)
            case Act.Experiment or Act.Craft:
                BeginWork(p);
                return;
            case Act.Material:
                ArriveMaterial(p);
                return;
            case Act.TakeFire:
                ArriveTakeFire(p);
                return;
            case Act.TendFire:
                DoStill(p, 200);
                return;
            case Act.Mine:
                ArriveMine(p);
                return;
            case Act.Cook:
                DoStill(p, 1_000L * WorkPercent(p) / 100);
                return;
            // (E5)
            case Act.Teach:
                BeginLesson(p);
                return;
            case Act.Study:
                DoStill(p, GameTime.TicksPerHour);
                return;
            default:
                Decide(p);
                return;
        }
    }

    /// <summary>하던 일이 끝났다(또는 급한 일로 끊었다): 결과를 남기고 다음을 판단.</summary>
    private void Finish(Person p)
    {
        var life = Life(p.Cell);
        bool done = Now >= p.ActUntil;
        switch (p.Act)
        {
            case Act.Eat when done:
            {
                var h = life.HarvestAt(p.Target);
                int got = h is null ? 0 : life.Take(p.Target, Math.Max(1, p.TargetB), Now);
                if (h is not null && got > 0)
                {
                    if (p.PlanItem == 1)
                    {
                        // (E4) 익혀 먹으려고 들고 간다
                        p.AddCarry(h.ItemIndex, got); p.PlanItem = -1;
                        GainGather(p, got);
                        break;
                    }
                    p.Food = Clamp(p.Food + (long)got * h.Nutrition);
                    p.Water = Clamp(p.Water + (long)got * h.Water);
                    GainGather(p, got);
                    if (p.Food >= 85_000) AddThought(p, H_ate);
                    // (E4) 날것의 독(버섯) → 가끔 배탈 → 익히기가 아쉽다 · 딱딱한 열매(야자)는 깨야 제대로 먹는다 → 단단한 도구가 아쉽다
                    int poison = Craft.Prop(h.ItemIndex, Craft.P_poison);
                    if (poison > 0 && Rng(p.Cell).NextInt(100) < poison * 6) BadStomach(p, h.ItemIndex);
                    if (Craft.Prop(h.ItemIndex, Craft.P_hardness) >= 4) { if (!HasHardTool(p)) AddWish(p, W_hardTool, 20); if (HasHardTool(p) && p.CarryCount(h.ItemIndex) == 0) p.AddCarry(h.ItemIndex, 1); }
                    // (E4) 그릇이 있으면 남는 몫을 담아 둔다
                    if (p.PlanItem == 2 && life.StockAt(p.Target, Now) > 0) { int extra = life.Take(p.Target, Math.Min(3, life.StockAt(p.Target, Now)), Now); p.AddCarry(h.ItemIndex, extra); p.PlanItem = -1; }
                }
                break;
            }
            case Act.Drink when done:
                p.Water = Person.Full;
                p.Dehydration = Math.Max(0, p.Dehydration - 20_000);
                FillPots(p);
                break;
            case Act.Gather when done:
            {
                var h = life.HarvestAt(p.Target);
                if (h is not null && life.Take(p.Target, 1, Now) > 0)
                {
                    int item = h.ItemIndex;
                    int k = p.Carry.FindIndex(c => c.Item == item);
                    if (k >= 0) p.Carry[k] = (item, p.Carry[k].Count + 1); else p.Carry.Add((item, 1));
                }
                break;
            }
            case Act.EatCorpse when done:
                if (Get(p.Target) is { Dead: true, Eaten: false } body) { body.Eaten = true; p.Food = Person.Full; p.Malnutrition = Math.Max(0, p.Malnutrition - 30_000); }
                break;
            case Act.Talk or Act.BreakArgue:
                EndTalk(p);
                break;
            case Act.Rest when p.HasTrait(T_lazy) && done:
                AddThought(p, H_lazyRest);
                break;
            // (E4)
            case Act.Experiment or Act.Craft when done:
                FinishWork(p);
                break;

            case Act.Material when done:
                FinishMaterial(p);
                break;
            case Act.Experiment or Act.Craft:
                Stats.PlanWhy[16]++;
                break;
            case Act.Material:
                Stats.PlanWhy[17]++;
                break;
            case Act.TendFire when done:
                FinishTend(p);
                break;
            case Act.Mine when done:
                FinishMine(p);
                break;
            case Act.Cook when done:
                FinishCook(p);
                break;
            case Act.EatCarried when done:
                FinishEatCarried(p);
                break;
            // (E5)
            case Act.Teach when !p.Walking:
                FinishLesson(p, done);
                break;
            case Act.Study when done:
                FinishStudy(p);
                break;
            case Act.DrinkCarried when done:
                if (p.PotWater > 0) { p.PotWater--; p.Water = Clamp(p.Water + 45_000); p.Dehydration = Math.Max(0, p.Dehydration - 10_000); }
                break;
        }
        if (p.Asleep) WakeUp(p);
        Decide(p);
    }

    /// <summary>채집 경험(열정이 있으면 더 빨리, 예민하면 +10%) · 열정이 생기는 길.</summary>
    private void GainGather(Person p, int portions)
    {
        int s = PeopleData.Gathering;
        int pas = _w.Config.Passion ? p.Passion[s] : 0;
        int xp = portions * 8;
        if (pas == 1) xp = xp * 3 / 2; else if (pas == 2) xp *= 2;
        if (p.HasTrait(T_nervous)) xp = xp * 11 / 10;
        int before = p.SkillLevel(s);
        p.SkillXp[s] += xp;
        if (pas > 0) { Stats.GatherXpPassion += xp; Stats.GatherActsPassion++; AddThought(p, H_passion); }
        else { Stats.GatherXpPlain += xp; Stats.GatherActsPlain++; }
        p.LastGather = Now;
        if (!_w.Config.Passion) return;
        // 좋은 일이 겹치면(많이 땄고 기분이 좋음 · 솜씨가 늘었음) 열정 점수가 쌓인다
        if (portions >= 3 && Mood(p) >= 50) p.PassionPoints[s] += portions;
        if (p.SkillLevel(s) > before) p.PassionPoints[s] += 10;
        if (p.Passion[s] < 2 && p.PassionPoints[s] >= 120 * (p.Passion[s] + 1) && Rng(p.Cell).NextInt(100) < 10)
        {
            p.Passion[s]++;
            p.PassionPoints[s] = 0;
            AddBio(p, BioKind.Passion, -1, s);
        }
        if (p.Passion[s] > 0 && p.PassionPoints[s] < -60) { p.Passion[s]--; p.PassionPoints[s] = 0; }
    }

    // ───────── 이야기 ─────────

    private bool TryTalk(Person p)
    {
        var life = Life(p.Cell);
        var rng = Rng(p.Cell);
        int here = p.Tile(Now);
        Person? best = null; int bestScore = int.MinValue;
        foreach (var q in life.People)
        {
            if (q.Id == p.Id || !CanBeTalkedTo(q)) continue;
            int qt = q.Tile(Now);
            int d = Cheb(qt, here);
            if (d > 35 || !life.Paths.Reachable(here, qt)) continue;
            var rel = p.GetRelation(q.Id);
            int sc = rel.Opinion / 2 + (IsFamily(p, q) ? 15 : 0) + (q.Id == p.Partner ? 20 : 0) - d + rng.NextInt(20) - rel.Grudge / 3;
            if (sc > bestScore) { bestScore = sc; best = q; }
        }
        if (best is null) return false;
        int goal = Beside(p, best.Tile(Now));
        if (Cheb(goal, here) <= 1) { SetWhy(p, Act.Talk, Why.WantsTalk, best.Id); p.Target = best.Id; p.TargetB = 1; StandStill(p); BeginTalk(p); return true; }
        if (!WalkTo(p, goal)) return false;
        SetWhy(p, Act.Talk, best.Id == p.Partner ? Why.Partner : p.GetRelation(best.Id).Opinion >= 30 ? Why.Friend : Why.WantsTalk, best.Id);
        p.Target = best.Id; p.TargetB = 1;
        return true;
    }

    private bool CanBeTalkedTo(Person q) =>
        !q.Dead && !q.Asleep && q.Carrier < 0 && q.BreakUntil <= Now && q.Stage(Now) != LifeStage.Baby
        && q.Act is not (Act.Talk or Act.BreakArgue or Act.Eat or Act.Drink or Act.Sleep or Act.Collapse or Act.EatCorpse or Act.Warm or Act.Carried or Act.Dead or Act.Teach or Act.Learn)
        && q.Why is not (Why.VeryHungry or Why.VeryThirsty or Why.Starving or Why.VeryCold);

    /// <summary>다가갔다: 상대가 가까이 있고 말을 받을 수 있으면 상대의 일을 끊고 함께 이야기한다.</summary>
    private void BeginTalk(Person p)
    {
        var q = Get(p.Target);
        int here = p.Tile(Now);
        bool argue = p.Act == Act.BreakArgue;
        if (q is null || !CanBeTalkedTo(q) || Cheb(q.Tile(Now), here) > 3)
        {
            if (argue) { if (!TryBreakWalk(p)) DoStill(p, 900); return; }
            if (!TryWander(p, Why.Nothing)) StartRest(p, Why.Nothing);
            return;
        }
        var rng = Rng(p.Cell);
        long dur = TalkMin + rng.NextInt(TalkMax - TalkMin);
        // 상대: 하던 일을 끊는다("누가 말을 걸어옴")
        Integrate(q, Now);
        q.Position(Now, out int qx, out int qy);
        q.NewMotion().SetStill(Now, qx, qy);
        q.Walking = false;
        SetWhy(q, Act.Talk, Why.TalkedTo, p.Id);
        q.Target = p.Id; q.TargetB = argue ? 2 : 0;
        p.TargetB = argue ? 3 : 1;
        p.TalkConcept = argue ? C("danger") : TalkTopic(p, rng);
        q.TalkConcept = argue ? C("you") : TalkTopic(q, rng);
        p.ActUntil = q.ActUntil = Now + dur;
        p.Walking = false;
        Schedule(p, PredictUrgent(p, Now, p.ActUntil, here));
        Schedule(q, PredictUrgent(q, Now, q.ActUntil, q.Tile(Now)));
        Stats.Talks++;
    }

    /// <summary>이야기가 끝남: 말을 건 사람이 두 사람 몫의 결과를 정한다(한 번만).</summary>
    private void EndTalk(Person p)
    {
        if (p.TargetB is not (1 or 3)) return; // 말을 받은 쪽은 결과를 정하지 않는다
        var q = Get(p.Target);
        bool argue = p.TargetB == 3;
        p.TargetB = -1;
        if (q is null || q.Dead) return;
        p.LastTalkTick = q.LastTalkTick = Now;
        var rng = Rng(p.Cell);
        var rp = p.GetRelation(q.Id); var rq = q.GetRelation(p.Id);
        rp.Talks++; rq.Talks++;
        int chance = 4 + (p.HasTrait(T_abrasive) ? 8 : 0) + (q.HasTrait(T_abrasive) ? 8 : 0) + (rp.Opinion < -20 ? 10 : 0) + (rp.Grudge > 30 ? 10 : 0);
        if (argue || rng.NextInt(100) < chance)
        {
            Stats.Arguments++;
            Argue(p, q, ref rp, ref rq);
        }
        else
        {
            rq.Opinion = Math.Clamp(rq.Opinion + TalkDelta(p, q, rng), -100, 100);
            rp.Opinion = Math.Clamp(rp.Opinion + TalkDelta(q, p, rng), -100, 100);
            if (q.HasTrait(T_kind)) rp.Favor = Math.Min(100, rp.Favor + 2);
            if (p.HasTrait(T_kind)) rq.Favor = Math.Min(100, rq.Favor + 2);
            AddThought(p, rp.Opinion >= 40 ? H_goodTalk : H_talked, q.Id);
            AddThought(q, rq.Opinion >= 40 ? H_goodTalk : H_talked, p.Id);
        }
        p.SetRelation(rp); q.SetRelation(rq);
        if (!argue) { Converge(p, q, rng); TryPair(p, q, rng); ShareClue(p, q, rng); }
    }

    private int TalkDelta(Person speaker, Person listener, Pcg32 rng)
    {
        int d = rng.NextInt(-5, 11) + (Ability(speaker, PeopleData.Soc) - 5);
        if (speaker.HasTrait(T_kind)) d += 4;
        if (speaker.HasTrait(T_abrasive)) d -= 4;
        if (listener.HasTrait(T_blunt)) d /= 2;
        return d;
    }

    private void Argue(Person p, Person q, ref Relation rp, ref Relation rq)
    {
        rp.Opinion = Math.Clamp(rp.Opinion - 12, -100, 100); rq.Opinion = Math.Clamp(rq.Opinion - 12, -100, 100);
        rp.Grudge = Math.Min(100, rp.Grudge + (p.HasTrait(T_abrasive) ? 16 : 8));
        rq.Grudge = Math.Min(100, rq.Grudge + (q.HasTrait(T_abrasive) ? 16 : 8));
        if (!p.HasTrait(T_bloodlust)) AddThought(p, H_argument, q.Id); else AddThought(p, H_talked, q.Id);
        if (!q.HasTrait(T_bloodlust)) AddThought(q, H_argument, p.Id); else AddThought(q, H_talked, p.Id);
        // 말다툼은 생각(기분)에 남고, 전기에는 원한이 깊어졌을 때만(자잘한 다툼으로 이야기가 넘치지 않게)
        if (rp.Grudge >= 40 && rp.Grudge - (p.HasTrait(T_abrasive) ? 16 : 8) < 40) AddBio(p, BioKind.Argument, q.Id);
        if (rq.Grudge >= 40 && rq.Grudge - (q.HasTrait(T_abrasive) ? 16 : 8) < 40) AddBio(q, BioKind.Argument, p.Id);
    }

    private bool IsFamily(Person a, Person b) =>
        a.Partner == b.Id || a.Mother == b.Id || a.Father == b.Id || b.Mother == a.Id || b.Father == a.Id
        || (a.Mother >= 0 && a.Mother == b.Mother) || (a.Father >= 0 && a.Father == b.Father);

    /// <summary>혼자 사는 두 어른이 서로 좋아하면 짝이 된다.</summary>
    private void TryPair(Person p, Person q, Pcg32 rng)
    {
        if (p.Sex == q.Sex || p.Partner >= 0 || q.Partner >= 0 || IsFamily(p, q)) return;
        if (p.AgeYears(Now) < 16 || q.AgeYears(Now) < 16 || Math.Abs(p.AgeYears(Now) - q.AgeYears(Now)) > 15) return;
        if (p.GetRelation(q.Id).Opinion < 35 || q.GetRelation(p.Id).Opinion < 35) return;
        if (rng.NextInt(100) >= 25) return;
        p.Partner = q.Id; q.Partner = p.Id;
        AddThought(p, H_newPartner, q.Id); AddThought(q, H_newPartner, p.Id);
        AddBio(p, BioKind.Paired, q.Id); AddBio(q, BioKind.Paired, p.Id);
        Stats.Pairs++;
        Notify(WorldEventKind.Pair, p, q.Id, $"{Hangul.Gwa(DisplayName(p))} {Hangul.I(DisplayName(q))} 짝이 되었습니다");
    }

    // ───────── 정신 붕괴 ─────────

    private void StartBreak(Person p, int mood)
    {
        var rng = Rng(p.Cell);
        Stats.Breaks++;
        Stats.BreaksByStage[(int)p.Stage(Now)]++;
        _tmpThoughts.Clear(); StateThoughts(p, _tmpThoughts);
        foreach (int t in _tmpThoughts) if (Data.Thoughts[t].Mood < 0 && t < 256) Stats.BreakDrag[t] -= Data.Thoughts[t].Mood;
        foreach (var (def, count) in ActiveThoughtCounts(p)) if (Data.Thoughts[def].Mood < 0 && def < 256) Stats.BreakDrag[def] -= Data.Thoughts[def].Mood * count;
        p.Breaks++;
        p.LastBreak = Now;
        if (p.Stage(Now) == LifeStage.Child)
        {
            // 아이: 짧게 떼를 쓰고 만다
            p.BreakUntil = Now + GameTime.TicksPerHour;
            SetWhy(p, Act.BreakWander, Why.Break, mood);
            DoStill(p, GameTime.TicksPerHour);
            AddBio(p, BioKind.Break, -1, 0);
            Notify(WorldEventKind.Break, p, -1, $"{Hangul.I(DisplayName(p))} 떼를 씁니다 (기분 {mood})");
            return;
        }
        int roll = rng.NextInt(100);
        int argue = 25 + (p.HasTrait(T_abrasive) ? 30 : 0) + (p.HasTrait(T_bloodlust) ? 20 : 0) - (p.HasTrait(T_kind) ? 15 : 0);
        int leave = 20 + (p.Social < 20_000 ? 10 : 0);
        string kind;
        if (roll < argue && TryArgueBreak(p)) kind = "화풀이(말다툼)";
        else if (roll < argue + leave && TryLeaveBreak(p)) kind = "혼자 떠나기";
        else
        {
            p.BreakUntil = Now + 3 * GameTime.TicksPerHour + rng.NextInt((int)(2 * GameTime.TicksPerHour));
            SetWhy(p, Act.BreakWander, Why.Break, mood);
            if (!TryBreakWalk(p)) DoStill(p, 900);
            kind = "울며 헤매기";
        }
        AddBio(p, BioKind.Break, -1, p.Act == Act.BreakArgue ? 1 : p.Act == Act.BreakLeave ? 2 : 0);
        Notify(WorldEventKind.Break, p, -1, $"{Hangul.I(DisplayName(p))} 무너졌습니다 — {kind} (기분 {mood})");
    }

    /// <summary>가족(짝 · 부모 · 자식)이 깨어 곁(r 타일 안)에 있나.</summary>
    private bool FamilyNear(Person p, int r)
    {
        int here = p.Tile(Now);
        foreach (var q in Life(p.Cell).People)
        {
            if (q.Id == p.Id || q.Dead || q.Asleep) continue;
            bool fam = q.Id == p.Partner || q.Id == p.Mother || q.Id == p.Father || q.Mother == p.Id || q.Father == p.Id;
            if (fam && Cheb(q.Tile(Now), here) <= r) return true;
        }
        return false;
    }

    private bool TryArgueBreak(Person p)
    {
        var life = Life(p.Cell);
        int here = p.Tile(Now);
        Person? best = null; int bd = int.MaxValue;
        foreach (var q in life.People)
        {
            if (q.Id == p.Id || !CanBeTalkedTo(q)) continue;
            int d = Cheb(q.Tile(Now), here);
            if (d < bd && d <= 40 && life.Paths.Reachable(here, q.Tile(Now))) { bd = d; best = q; }
        }
        if (best is null) return false;
        p.BreakUntil = Now + 2 * GameTime.TicksPerHour;
        SetWhy(p, Act.BreakArgue, Why.Break, Mood(p));
        p.Target = best.Id; p.TargetB = 3;
        int goal = Beside(p, best.Tile(Now));
        if (goal == here || Cheb(goal, here) <= 1) { StandStill(p); BeginTalk(p); return true; }
        if (!WalkTo(p, goal)) return false;
        return true;
    }

    private bool TryLeaveBreak(Person p)
    {
        var life = Life(p.Cell);
        var rng = Rng(p.Cell);
        p.Position(Now, out int px, out int py);
        int hx = px / 1000, hy = py / 1000, here = hy * S + hx;
        // (E3b) 가끔은 아예 이웃 땅으로 떠난다
        if (rng.NextInt(100) < 30 && PickNewLand(p, life, here) is { } nb)
        {
            int gi = NearestGate(nb, life, here, px, py);
            if (gi >= 0 && StartCross(p, nb, gi, Why.Break, Mood(p))) { p.BreakUntil = Now + GameTime.TicksPerDay; Stats.MoveWhy[(int)Why.Break]++; return true; }
        }
        for (int tries = 0; tries < 16; tries++)
        {
            int tx = rng.NextInt(10, S - 10), ty = rng.NextInt(10, S - 10);
            int t = ty * S + tx;
            if (!life.Walk[t] || life.Region[t] != life.Region[here] || Cheb(t, here) < 40 || HexFrame.BorderDistance(t) < 5) continue;
            if (!WalkTo(p, t)) continue;
            p.BreakUntil = Now + GameTime.TicksPerDay;
            SetWhy(p, Act.BreakLeave, Why.Break, Mood(p));
            p.Target = t;
            return true;
        }
        return false;
    }

    private bool TryBreakWalk(Person p)
    {
        var life = Life(p.Cell);
        var rng = Rng(p.Cell);
        p.Position(Now, out int px, out int py);
        int hx = px / 1000, hy = py / 1000, here = hy * S + hx;
        for (int tries = 0; tries < 8; tries++)
        {
            int tx = hx + rng.NextInt(-14, 15), ty = hy + rng.NextInt(-14, 15);
            if ((uint)tx >= S || (uint)ty >= S) continue;
            int t = ty * S + tx;
            if (t == here || !life.Walk[t] || life.Region[t] != life.Region[here] || HexFrame.BorderDistance(t) < 3) continue;
            if (!WalkTo(p, t, straightOnly: tries < 7)) continue;
            p.Act = Act.BreakWander;
            return true;
        }
        return false;
    }

    private void ContinueBreak(Person p)
    {
        switch (p.Act)
        {
            case Act.BreakLeave:
                if (Life(p.Cell).IsNight(Now)) { StartSleep(p, p.Tile(Now), Why.Break, collapse: false); p.Act = Act.BreakLeave; return; }
                SetWhy(p, Act.BreakLeave, Why.Break, p.WhyArg);
                DoStill(p, Math.Min(p.BreakUntil - Now, 2 * GameTime.TicksPerHour));
                return;
            default:
                SetWhy(p, Act.BreakWander, Why.Break, p.WhyArg);
                if (!TryBreakWalk(p)) DoStill(p, 900);
                return;
        }
    }
}
