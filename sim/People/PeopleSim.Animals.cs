using Wtw.Sim.Core;
using Wtw.Sim.Data;
using Wtw.Sim.Planet;
using Wtw.Sim.Provinces;
using Wtw.Sim.Time;
using Wtw.Sim.World;

namespace Wtw.Sim.People;

/// <summary>
/// (E5b) 짐승(기획서 15절). 짐승은 지방마다 무리로 산다 — 무리는 지방 예약표에서 사람과 같은 방식(예약)으로 움직이고, 지방 계산 중에는 그 지방만 만진다(지방마다 CPU 그대로).
///   - 처음 사람이 들어간 지방: 먹이(생물군계 · 행성의 사냥감 자원)로 정해지는 마릿수만큼 무리를 만든다.
///   - 하루 경계(한 줄기): 마릿수가 살 수 있는 수보다 적으면 늘고(가까울수록 덜), 많으면 준다. 한 종이 다 사라지면 가끔 이웃에서 무리가 들어온다.
///   - 사람이 다 떠나 잠든 지방은 무리도 멈췄다가, 깨어나면 흐른 날만큼 하루씩 늘고 줄기를 한꺼번에 맞춘다.
///   - 겁: 사람이 다가오면 달아난다. 포식자는 배가 고프면 다른 무리를 쫓고, 가끔 혼자 있는 사람을 노린다 — 불 곁의 사람은 노리지 않는다.
/// 난수는 ("animals", 지방) 스트림에서만.
/// </summary>
public sealed partial class PeopleSim
{
    public const string AnimalStream = "animals";
    public AnimalData Fauna { get; } = AnimalData.Default;

    private Pcg32 ARng(ProvinceLife life) => life.AnimalRng ??= _w.Rng.Get(AnimalStream, life.Cell);

    // ───────── 살 수 있는 마릿수 ─────────

    /// <summary>종마다 그 지방에 살 수 있는 마릿수(먹이 — 생물군계 무게 × 사냥감 자원)와 물고기 몫의 한도. 지도로만 정해진다(저장 안 함).</summary>
    internal void ComputeCaps(ProvinceLife life)
    {
        var planet = _w.Planet;
        int cell = life.Cell;
        int bio = (int)planet.Biome[cell];
        int game = planet.Resource(cell, ResourceKinds.Game);
        int gf = game == 0 ? 400 : game == 1 ? 1000 : 1500;
        var caps = new int[Fauna.Animals.Count];
        for (int s = 0; s < caps.Length; s++)
        {
            var a = Fauna.Animals[s];
            caps[s] = (int)((long)a.Capacity * a.Biome1000[bio] / 1000 * gf / 1000);
        }
        life.AnimalCap = caps;
        int fish = planet.Resource(cell, ResourceKinds.Fish);
        life.FishCap = life.DrinkTiles == 0 ? 0 : Fauna.FishPerLevel * fish * Math.Min(life.DrinkTiles, 300) / 300 + (fish > 0 ? 4 : 0);
    }

    /// <summary>처음 열린 지방: 살 수 있는 만큼 무리를 만든다(먹이가 넉넉한 채로 시작).</summary>
    private void InitAnimals(ProvinceLife life)
    {
        ComputeCaps(life);
        var rng = ARng(life);
        for (int s = 0; s < Fauna.Animals.Count; s++)
        {
            var a = Fauna.Animals[s];
            int left = life.AnimalCap[s];
            if (left < a.GroupMin) continue;
            while (left >= a.GroupMin)
            {
                int g = Math.Min(left, rng.NextInt(a.GroupMin, a.GroupMax + 1));
                left -= g;
                var h = NewHerd(life, s, g, RandomLand(life, rng, 6), rng);
                if (h is null) break;
            }
        }
        life.FishStock = life.FishCap;
    }

    /// <summary>무리 하나를 만든다(home 타일에서). 지방 계산 밖에서도 안에서도.</summary>
    private Herd? NewHerd(ProvinceLife life, int species, int count, int home, Pcg32 rng)
    {
        if (home < 0) return null;
        var a = Fauna.Animals[species];
        var h = new Herd { Id = life.NextHerdId++, Species = species, Count = count, Home = home, LastMeal = Now - rng.NextInt((int)(a.HuntDays * GameTime.TicksPerDay)), Since = Now };
        h.Motion.SetStill(Now, home % S * 1000 + 500, home / S * 1000 + 500);
        h.Prev.CopyFrom(h.Motion);
        life.Herds[h.Id] = h;
        ScheduleHerd(life, h, Now + 1 + rng.NextInt((int)GameTime.TicksPerHour));
        return h;
    }

    /// <summary>안쪽(가장자리에서 margin 타일 넘게)의 걸을 수 있는 아무 타일. 못 찾으면 −1.</summary>
    private static int RandomLand(ProvinceLife life, Pcg32 rng, int margin)
    {
        for (int tries = 0; tries < 40; tries++)
        {
            int t = rng.NextInt(HexFrame.TileCount);
            if (life.Walk[t] && HexFrame.BorderDistance(t) >= margin && !GroundInfo.IsWater(life.Map.Ground[t])) return t;
        }
        return -1;
    }

    private void ScheduleHerd(ProvinceLife life, Herd h, long tick)
    {
        if (h.PendingHandle != 0) life.Sched.Cancel(h.PendingHandle);
        h.PendingHandle = life.Sched.Schedule(Math.Max(tick, Now + 1), h.Id, EventKinds.Herd);
    }

    internal void RemoveHerd(ProvinceLife life, Herd h)
    {
        if (h.PendingHandle != 0) life.Sched.Cancel(h.PendingHandle);
        h.PendingHandle = 0;
        life.Herds.Remove(h.Id);
    }

    /// <summary>무리를 지금 자리에 세운다(움직이던 중이면 그 자리에서).</summary>
    private void HerdStop(Herd h)
    {
        h.Position(Now, out int x, out int y);
        h.NewMotion();
        h.Motion.SetStill(Now, x, y);
    }

    /// <summary>(E5b 고침) 무리는 곧은 길로만 걷는다(길 찾기 없이 — 막히면 그쪽으로 가지 않는다).</summary>
    private bool HerdWalk(ProvinceLife life, Herd h, int goal, int ticksPerTile)
    {
        h.Position(Now, out int x, out int y);
        var path = L.Path;
        if (!life.Paths.TryStraight(x, y, goal, path)) return false;
        h.NewMotion();
        h.Motion.SetPath(Now, path, ticksPerTile);
        return true;
    }

    // ───────── 무리의 판단 ─────────

    /// <summary>예약된 시각이 된 무리(지방 계산 중).</summary>
    internal void OnHerdEvent(ProvinceLife life, int id)
    {
        if (!life.Herds.TryGetValue(id, out var h)) return;
        h.PendingHandle = 0;
        St.HerdEvents++;
        var a = Fauna.Animals[h.Species];
        var rng = ARng(life);
        if (h.State == HerdState.Stalk) { ResolveStalk(life, h, a, rng); return; }
        HerdStop(h);
        if (h.Tame) { FollowOwner(life, h, a, rng); return; }
        int here = h.Tile(Now);
        h.Position(Now, out int hx, out int hy);
        // 사람이 다가오면 달아난다(포식자는 여럿이거나 불이 있을 때만)
        var q = NearestPerson(life, hx, hy, a.Fear, out int near, out int nearest);
        if (q is not null && (!a.Predator || near >= 2 || life.NearestFire(hx / 1000, hy / 1000, Now, 5) >= 0))
        {
            q.Position(Now, out int qx, out int qy);
            if (HerdFlee(life, h, a, qx, qy, rng)) return;
        }
        bool night = life.IsNight(Now);
        if (a.Predator && Now - h.LastMeal > a.HuntDays * GameTime.TicksPerDay && (night || rng.NextInt(100) < 40) && TryStalk(life, h, a, rng)) return;
        // (E5b 고침) 둘레 20타일 안에 아무도 없는 풀 뜯는 무리는 그 자리에서 풀을 뜯으며 오래 머문다(걷기 · 길 보기 없이 — 계산을 덜)
        if (!a.Predator && nearest > 20 && !night)
        {
            h.State = HerdState.Graze;
            ScheduleHerd(life, h, Now + 12 * GameTime.TicksPerHour + rng.NextInt((int)(8 * GameTime.TicksPerHour)));
            return;
        }
        // 쉬기 · 자기: 풀 뜯는 짐승은 밤에, 포식자는 낮에 많이
        if (!a.Predator && night)
        {
            long tod = life.LocalTickOfDay(Now);
            long untilMorning = (6 * GameTime.TicksPerHour - tod + GameTime.TicksPerDay) % GameTime.TicksPerDay;
            h.State = HerdState.Sleep;
            ScheduleHerd(life, h, Now + Math.Max(GameTime.TicksPerHour / 2, untilMorning) + rng.NextInt((int)GameTime.TicksPerHour));
            return;
        }
        if (rng.NextInt(100) < (a.Predator && !night ? 65 : 30))
        {
            h.State = HerdState.Rest;
            ScheduleHerd(life, h, Now + 4 * GameTime.TicksPerHour + rng.NextInt((int)(4 * GameTime.TicksPerHour))); // (E5b 고침) 더 길게 쉰다(판단을 덜 자주 — 계산)
            return;
        }
        // 풀 뜯으며 옮겨 간다(사는 곳에서 멀면 그쪽으로)
        int hxT = here % S, hyT = here / S, homeX = h.Home % S, homeY = h.Home / S;
        bool far = Math.Max(Math.Abs(hxT - homeX), Math.Abs(hyT - homeY)) > 22;
        for (int tries = 0; tries < 2; tries++)
        {
            int tx = far ? hxT + Math.Sign(homeX - hxT) * rng.NextInt(4, 11) + rng.NextInt(-3, 4) : hxT + rng.NextInt(-9, 10);
            int ty = far ? hyT + Math.Sign(homeY - hyT) * rng.NextInt(4, 11) + rng.NextInt(-3, 4) : hyT + rng.NextInt(-9, 10);
            if ((uint)tx >= S || (uint)ty >= S) continue;
            int t = ty * S + tx;
            if (t == here || !life.Walk[t] || HexFrame.BorderDistance(t) < 3 || GroundInfo.IsWater(life.Map.Ground[t])) continue;
            if (!HerdWalk(life, h, t, a.Walk)) continue;
            h.State = HerdState.Graze;
            ScheduleHerd(life, h, h.Motion.Arrive + 4 * GameTime.TicksPerHour + rng.NextInt((int)(4 * GameTime.TicksPerHour))); // (E5b 고침) 닿은 곳에서 더 오래 풀을 뜯는다
            return;
        }
        h.State = HerdState.Rest;
        ScheduleHerd(life, h, Now + GameTime.TicksPerHour + rng.NextInt((int)GameTime.TicksPerHour));
    }

    /// <summary>r 타일 안의 가장 가까운 사람(깨어 있으면 r, 자면 r/2 — 아기 빼고)과 그 안의 사람 수.</summary>
    private Person? NearestPerson(ProvinceLife life, int mx, int my, int r, out int count) => NearestPerson(life, mx, my, r, out count, out _);

    /// <summary>위와 같고, 가장 가까운 사람까지의 거리(타일, 아무도 없으면 int.MaxValue)도.</summary>
    private Person? NearestPerson(ProvinceLife life, int mx, int my, int r, out int count, out int nearest)
    {
        Person? best = null; long bd = long.MaxValue; count = 0; nearest = int.MaxValue;
        int tx = mx / 1000, ty = my / 1000;
        foreach (var q in life.People)
        {
            if (q.Dead || q.Carrier >= 0) continue;
            q.Position(Now, out int qx, out int qy);
            int d = Math.Max(Math.Abs(qx / 1000 - tx), Math.Abs(qy / 1000 - ty));
            if (d < nearest) nearest = d;
            if (d > (q.Asleep ? r / 2 : r)) continue;
            count++;
            long dd = (long)(qx - mx) * (qx - mx) + (long)(qy - my) * (qy - my);
            if (dd < bd) { bd = dd; best = q; }
        }
        return best;
    }

    /// <summary>(fx, fy)(밀리타일)에서 먼 쪽으로 달아난다. 갔으면 true.</summary>
    private bool HerdFlee(ProvinceLife life, Herd h, AnimalDef a, int fx, int fy, Pcg32 rng)
    {
        h.Position(Now, out int x, out int y);
        int hx = x / 1000, hy = y / 1000;
        long ax = x - fx, ay = y - fy;
        // 8방향을 "멀어지는 정도" 순으로
        Span<int> order = stackalloc int[8];
        Span<long> dot = stackalloc long[8];
        for (int d = 0; d < 8; d++) { order[d] = d; dot[d] = (DX8[d] * ax + DY8[d] * ay) * (DX8[d] != 0 && DY8[d] != 0 ? 7 : 10); }
        for (int i = 1; i < 8; i++) for (int j = i; j > 0 && (dot[order[j]] > dot[order[j - 1]] || (dot[order[j]] == dot[order[j - 1]] && order[j] < order[j - 1])); j--) (order[j], order[j - 1]) = (order[j - 1], order[j]);
        int dist = 12 + rng.NextInt(9);
        for (int k = 0; k < 3; k++)
        {
            int d = order[k];
            int len = DX8[d] != 0 && DY8[d] != 0 ? dist * 7 / 10 : dist;
            int tx = hx + DX8[d] * len, ty = hy + DY8[d] * len;
            if ((uint)tx >= S || (uint)ty >= S) continue;
            int t = ty * S + tx;
            if (!life.Walk[t] || HexFrame.BorderDistance(t) < 2 || GroundInfo.IsWater(life.Map.Ground[t])) continue;
            if (!HerdWalk(life, h, t, a.Run)) continue;
            h.State = HerdState.Flee;
            St.HerdFlees++;
            ScheduleHerd(life, h, h.Motion.Arrive + GameTime.TicksPerHour / 2 + rng.NextInt((int)GameTime.TicksPerHour));
            return true;
        }
        return false;
    }

    // ───────── 포식자 ─────────

    /// <summary>배고픈 포식자: 가끔 혼자 있는(불 곁이 아닌) 사람을, 아니면 잡을 수 있는 무리를 쫓는다.</summary>
    private bool TryStalk(ProvinceLife life, Herd h, AnimalDef a, Pcg32 rng)
    {
        h.Position(Now, out int x, out int y);
        int here = h.Tile(Now);
        bool night = life.IsNight(Now);
        // 사람은 드물게: 밤에 혼자 자는 사람 · 곁에 어른이 없는 아이(낮에는 아이만) — 사람에게 쫓겨난 뒤 한동안은 노리지 않는다
        if (Now >= h.Wary && rng.NextInt(100) < (night ? 4 : 1) && Vulnerable(life, x, y, 30, here, night) is { } q)
        {
            int qt = q.Tile(Now);
            int goal = HerdBeside(life, qt, h.Id);
            if (HerdWalk(life, h, goal, a.Walk))
            {
                h.State = HerdState.Stalk; h.Target = q.Id; h.TargetKind = 1;
                St.Stalks++;
                ScheduleHerd(life, h, h.Motion.Arrive);
                return true;
            }
        }
        Herd? prey = null; long bd = 60L * 60 * 1_000_000;
        foreach (var (_, o) in life.Herds)
        {
            if (o.Id == h.Id || o.Tame) continue;
            var oa = Fauna.Animals[o.Species];
            if (oa.Predator || oa.Size > a.PreySize) continue;
            o.Position(Now, out int ox, out int oy);
            long d = (long)(ox - x) * (ox - x) + (long)(oy - y) * (oy - y);
            if (d < bd) { bd = d; prey = o; }
        }
        if (prey is null) return false;
        int pt = prey.Tile(Now);
        if (!HerdWalk(life, h, HerdBeside(life, pt, h.Id), a.Walk)) return false; // (E5b 고침) 곧게 갈 수 있는 먹잇감만(길 찾기 없이)
        h.State = HerdState.Stalk; h.Target = prey.Id; h.TargetKind = 2;
        ScheduleHerd(life, h, h.Motion.Arrive);
        return true;
    }

    /// <summary>
    /// 포식자가 노릴 만한 사람: r 타일 안 · 같은 이어진 땅 · 6타일 안에 타는 불이 없고 · 곁(6타일)에 다른 사람이 없거나,
    /// 아이 · 다친 사람 · 늙은 사람인데 곁에 깨어 있는 어른이 없다 · 따르는 짐승이 곁에 없다. 가장 가까운 사람.
    /// </summary>
    internal Person? Vulnerable(ProvinceLife life, int mx, int my, int r, int here, bool night)
    {
        int reg = (uint)here < HexFrame.TileCount ? life.Region[here] : -1;
        Person? best = null; long bd = long.MaxValue;
        foreach (var q in life.People)
        {
            if (q.Dead || q.Carrier >= 0 || q.Stage(Now) == LifeStage.Baby) continue;
            q.Position(Now, out int qx, out int qy);
            int qt = (qy / 1000) * S + qx / 1000;
            if (Math.Max(Math.Abs(qx - mx), Math.Abs(qy - my)) > r * 1000 || life.Region[qt] != reg) continue;
            long dd = (long)(qx - mx) * (qx - mx) + (long)(qy - my) * (qy - my);
            if (dd >= bd) continue;
            if (life.NearestFire(qx / 1000, qy / 1000, Now, 6) >= 0) continue;
            int others = 0, awakeAdults = 0;
            foreach (var o in life.People)
            {
                if (o.Dead || o.Id == q.Id || o.Carrier >= 0 || o.Stage(Now) == LifeStage.Baby) continue;
                if (Cheb(o.Tile(Now), qt) > 6) continue;
                others++;
                if (!o.Asleep && o.Stage(Now) >= LifeStage.Adult) awakeAdults++;
            }
            bool child = q.Stage(Now) == LifeStage.Child, weak = child || q.Injury > 20_000 || q.Stage(Now) == LifeStage.Elder;
            bool alone = others == 0 && (q.Asleep || weak);
            if (!(night ? alone || (weak && awakeAdults == 0) : child && awakeAdults == 0 && others == 0)) continue;
            if (CompanionNear(life, q, 5)) continue;
            bd = dd; best = q;
        }
        return best;
    }

    private bool CompanionNear(ProvinceLife life, Person q, int r)
    {
        foreach (var (_, c) in life.Herds) if (c.Owner == q.Id && c.Bond >= 20 && Cheb(c.Tile(Now), q.Tile(Now)) <= r) return true;
        return false;
    }

    /// <summary>goal 둘레의 걸을 수 있는 타일(무리마다 다른 순서).</summary>
    private static int HerdBeside(ProvinceLife life, int goal, int id)
    {
        int gx = goal % S, gy = goal / S;
        int start = (int)(StableHash.Mix((ulong)id * 7919UL) % 8);
        for (int k = 0; k < 8; k++)
        {
            int d = (start + k) % 8;
            int nx = gx + DX8[d] * 2, ny = gy + DY8[d] * 2;
            if ((uint)nx >= S || (uint)ny >= S) continue;
            int t = ny * S + nx;
            if (life.Walk[t] && life.Region[t] == life.Region[goal]) return t;
        }
        return goal;
    }

    /// <summary>쫓던 곳에 닿았다: 무리면 잡아 보고, 사람이면 덮친다(불이 가까우면 물러난다).</summary>
    private void ResolveStalk(ProvinceLife life, Herd h, AnimalDef a, Pcg32 rng)
    {
        HerdStop(h);
        h.State = HerdState.Rest;
        int here = h.Tile(Now);
        h.Position(Now, out int hx, out int hy);
        long restFor = 2 * GameTime.TicksPerHour + rng.NextInt((int)(3 * GameTime.TicksPerHour));
        if (h.TargetKind == 2 && life.Herds.TryGetValue(h.Target, out var prey))
        {
            var pa = Fauna.Animals[prey.Species];
            if (Cheb(prey.Tile(Now), here) <= 6)
            {
                int chance = 40 - pa.Size * 8 + a.Size * 5 + Math.Min(h.Count, 4) * 4;
                if (rng.NextInt(100) < chance)
                {
                    prey.Count--;
                    St.PreyKilled++;
                    h.LastMeal = Now;
                    if (prey.Count <= 0) RemoveHerd(life, prey);
                    else { HerdStop(prey); HerdFlee(life, prey, pa, hx, hy, rng); }
                    restFor += 4 * GameTime.TicksPerHour;
                }
                else { HerdStop(prey); HerdFlee(life, prey, pa, hx, hy, rng); }
            }
        }
        else if (h.TargetKind == 1 && Get(h.Target) is { Dead: false } q && q.Cell == life.Cell && q.Carrier < 0)
        {
            int qt = q.Tile(Now);
            q.Position(Now, out int qx, out int qy);
            if (Cheb(qt, here) <= 3)
            {
                int ft = life.NearestFire(qx / 1000, qy / 1000, Now, 5);
                if (ft >= 0)
                {
                    // 불은 포식자를 쫓는다
                    St.FireRepelled++;
                    h.Target = -1; h.TargetKind = 0;
                    h.Wary = Now + 3 * GameTime.TicksPerDay;
                    if (HerdFlee(life, h, a, ft % S * 1000 + 500, ft / S * 1000 + 500, rng)) return;
                }
                else
                {
                    h.Target = -1; h.TargetKind = 0;
                    h.Wary = Now + 15 * GameTime.TicksPerDay; // 사람을 덮친 뒤(쫓겨났든 아니든) 한동안은 다시 노리지 않는다
                    BeastAttack(life, h, a, q, rng);
                    if (life.Herds.ContainsKey(h.Id) && h.PendingHandle == 0) ScheduleHerd(life, h, Now + restFor);
                    return;
                }
            }
        }
        h.Target = -1; h.TargetKind = 0;
        ScheduleHerd(life, h, Now + restFor);
    }

    /// <summary>
    /// 포식자가 사람을 덮친다: 다침의 크기 = 사나움 · 크기 · 함께 덮친 수 × 운, 곁의 깨어 있는 어른 · 든 것(창) · 힘 · 용감함 · 깨어 있음이 막아 준다.
    /// 너무 크면 죽는다. 곁에 어른이 있거나 창이 있으면 포식자를 쓰러뜨리기도 한다. 그 밖에는 포식자가 달아난다.
    /// </summary>
    private void BeastAttack(ProvinceLife life, Herd h, AnimalDef a, Person q, Pcg32 rng)
    {
        Integrate(q, Now);
        int qt = q.Tile(Now);
        int defenders = 0;
        Person? helper = null;
        foreach (var o in life.People)
        {
            if (o.Dead || o.Id == q.Id || o.Asleep || o.Carrier >= 0 || o.Stage(Now) < LifeStage.Adult || Cheb(o.Tile(Now), qt) > 6) continue;
            defenders++;
            if (helper is null || WeaponPower(o) > WeaponPower(helper)) helper = o;
        }
        int w = WeaponPower(q);
        int attackers = a.Size >= 3 ? 1 : Math.Min(h.Count, 3);
        long sev = (a.Fierce * 7_000L + a.Size * 6_000L) * (2 + attackers) / 3;
        sev = sev * (40 + rng.NextInt(71)) / 100;
        int defense = 100 + defenders * 70 + w * 8 + (q.Asleep ? 0 : 25) + (q.HasTrait(T_brave) ? 15 : 0) + Ability(q, PeopleData.Str) * 3;
        sev = sev * 100 / defense;
        if (q.Stage(Now) == LifeStage.Child) sev = sev * 13 / 10;
        St.BeastAttacks++;
        bool wasAsleep = q.Asleep;
        string who = DisplayName(q);
        AddThought(q, H_attacked);
        AddBio(q, BioKind.Attacked, h.Species, (int)(sev / 1000));
        Injure(q, (int)Math.Min(sev, int.MaxValue), DeathCause.Beast);
        AddWish(q, W_hunting, 15);
        // 막아 낸 사람이 포식자를 쓰러뜨린다
        int kill = defenders * 12 + Math.Max(w, helper is null ? 0 : WeaponPower(helper)) * 4;
        bool killed = false;
        if ((defenders > 0 || w >= 3) && rng.NextInt(100) < kill)
        {
            killed = true;
            var killer = helper ?? q;
            if (!killer.Dead)
            {
                AddBio(killer, BioKind.KilledBeast, h.Species, defenders);
                killer.Kills++;
                St.BeastsKilledDefending++;
            }
            h.Count--;
            foreach (var (item, n) in a.Yields) life.Drop(qt, item, n);
            if (h.Count <= 0) RemoveHerd(life, h); else { HerdStop(h); HerdFlee(life, h, a, qt % S * 1000 + 500, qt / S * 1000 + 500, rng); }
        }
        else if (q.Dead) h.LastMeal = Now;
        else { HerdStop(h); HerdFlee(life, h, a, qt % S * 1000 + 500, qt / S * 1000 + 500, rng); }
        string pack = a.Size < 3 && h.Count + (killed ? 1 : 0) > 1 ? " 무리" : "";
        string outcome = q.Dead ? "목숨을 잃었습니다" : sev >= 50_000 ? "크게 다쳤습니다" : "다쳤습니다";
        Notify(WorldEventKind.BeastAttack, q, h.Species, $"{Hangul.I(a.Name + pack)} {(wasAsleep ? "자고 있던 " : "")}{Hangul.Eul(who)} 덮쳤습니다 — {outcome}" + (killed ? $" (곁에 있던 사람들이 {Hangul.Eul(a.Name)} 쓰러뜨렸습니다)" : ""));
        if (!q.Dead) InterruptPerson(q);
    }

    /// <summary>사람의 하던 일을 그 자리에서 끊고 곧 다시 판단하게 한다(자고 있었으면 깬다).</summary>
    private void InterruptPerson(Person q)
    {
        q.Position(Now, out int x, out int y);
        q.NewMotion().SetStill(Now, x, y);
        q.Walking = false;
        q.ActUntil = Now;
        Schedule(q, Now + 1);
    }

    // ───────── 길들이기의 첫걸음 ─────────

    /// <summary>따르는 짐승: 그 사람 곁으로 간다(멀면 걸어가고, 곁이면 쉰다). 따르던 사람이 없으면 가족에게, 그것도 없으면 들로 돌아간다.</summary>
    private void FollowOwner(ProvinceLife life, Herd h, AnimalDef a, Pcg32 rng)
    {
        var owner = Get(h.Owner);
        if (owner is null || owner.Dead || owner.Cell != life.Cell)
        {
            Person? next = null;
            if (owner is not null)
                foreach (var q in life.People)
                    if (!q.Dead && q.Carrier < 0 && q.Stage(Now) != LifeStage.Baby && IsFamily(q, owner)) { next = q; break; }
            if (next is not null) { h.Owner = next.Id; h.Bond /= 2; }
            else { h.Owner = -1; h.State = HerdState.Graze; ScheduleHerd(life, h, Now + GameTime.TicksPerHour); return; }
            owner = next;
        }
        int here = h.Tile(Now), ot = owner.Walking ? TileOf(owner.Current.EndX, owner.Current.EndY) : owner.Tile(Now);
        h.State = HerdState.Follow;
        if (Cheb(here, ot) > 3)
        {
            int goal = HerdBeside(life, ot, h.Id);
            if (goal != here && HerdWalk(life, h, goal, Math.Max(8, a.Walk - 6)))
            {
                ScheduleHerd(life, h, h.Motion.Arrive + 600 + rng.NextInt(1_200));
                return;
            }
            // (E5b 고침) 곧게 못 가면(길 찾기 없이) 멀리 처졌을 때 따라잡는다 — 보이지 않는 사이 사람 곁으로
            if (Cheb(here, ot) > SightTiles && goal >= 0 && life.Walk[goal])
            {
                h.NewMotion(); h.Motion.SetStill(Now, goal % S * 1000 + 500, goal / S * 1000 + 500);
                ScheduleHerd(life, h, Now + 600 + rng.NextInt(1_200));
                return;
            }
        }
        ScheduleHerd(life, h, Now + (owner.Asleep ? 2 * GameTime.TicksPerHour : 1_500 + rng.NextInt(1_500)));
    }

    // ───────── 하루 경계: 늘고 줄기 ─────────

    /// <summary>하루 경계(한 줄기, 지방마다): 마릿수가 늘고 줄고 · 물고기가 다시 늘고 · 따르는 짐승과 친해진다.</summary>
    private void AnimalsDaily(ProvinceLife life)
    {
        if (life.AnimalCap.Length == 0) ComputeCaps(life);
        var rng = ARng(life);
        PopulationDay(life, rng, catchUp: false);
        foreach (var (_, h) in life.Herds)
        {
            if (!h.Tame) continue;
            var o = Get(h.Owner);
            if (o is { Dead: false } && o.Cell == life.Cell && Cheb(o.Tile(Now), h.Tile(Now)) <= 10) h.Bond = Math.Min(100, h.Bond + 2);
        }
    }

    /// <summary>하루치: 종마다 늘고 줄기(살 수 있는 수에 가까울수록 덜 는다), 한 종이 다 사라졌으면 가끔 이웃에서 들어온다, 굶은 포식자는 준다 · 물고기.</summary>
    internal void PopulationDay(ProvinceLife life, Pcg32 rng, bool catchUp)
    {
        var tmp = new List<Herd>();
        for (int s = 0; s < Fauna.Animals.Count; s++)
        {
            var a = Fauna.Animals[s];
            int k = life.AnimalCap[s];
            tmp.Clear();
            int n = 0;
            foreach (var (_, h) in life.Herds) if (h.Species == s && !h.Tame) { tmp.Add(h); n += h.Count; }
            if (n == 0)
            {
                if (k >= a.GroupMin && rng.NextInt(1000) < 25)
                {
                    // 이웃 땅에서 무리가 들어온다(가장자리 가까이에)
                    int t = -1;
                    for (int tries = 0; tries < 40 && t < 0; tries++)
                    {
                        int c = rng.NextInt(HexFrame.TileCount);
                        if (life.Walk[c] && HexFrame.BorderDistance(c) is >= 3 and < 10 && !GroundInfo.IsWater(life.Map.Ground[c])) t = c;
                    }
                    if (t >= 0 && NewHerd(life, s, a.GroupMin, t, rng) is { } nh) { St.ImmigrantHerds++; if (catchUp) Unsched(life, nh); }
                }
                continue;
            }
            if (n * 3 < k && k >= a.GroupMin && rng.NextInt(1000) < 15)
            {
                // 많이 줄었다 — 가끔 이웃 땅의 무리가 들어온다(빈 땅의 짐승은 따로 계산하지 않는다)
                int t = -1;
                for (int tries = 0; tries < 40 && t < 0; tries++)
                {
                    int c = rng.NextInt(HexFrame.TileCount);
                    if (life.Walk[c] && HexFrame.BorderDistance(c) is >= 3 and < 10 && !GroundInfo.IsWater(life.Map.Ground[c])) t = c;
                }
                if (t >= 0 && NewHerd(life, s, a.GroupMin, t, rng) is { } ih) { St.ImmigrantHerds++; tmp.Add(ih); n += ih.Count; if (catchUp) Unsched(life, ih); }
            }
            if (n < k)
            {
                bool fed = !a.Predator || catchUp || tmp.Any(h => Now - h.LastMeal < 2L * a.HuntDays * GameTime.TicksPerDay);
                if (!fed) continue;
                long e1000 = (long)a.Growth1000 * n * (k - n) / k / GameTime.DaysPerYear;
                int births = (int)(e1000 / 1000) + (rng.NextInt(1000) < e1000 % 1000 ? 1 : 0);
                for (int b = 0; b < births; b++)
                {
                    Herd small = tmp[0];
                    foreach (var h in tmp) if (h.Count < small.Count) small = h;
                    small.Count++;
                    St.AnimalsBorn++;
                    if (small.Count > a.GroupMax && small.Count >= 2)
                    {
                        // 무리가 커지면 둘로 갈라진다
                        int half = small.Count / 2;
                        int home = NearTile(life, small.Home, 12, rng);
                        if (home >= 0 && NewHerd(life, s, half, home, rng) is { } nh2)
                        {
                            small.Count -= half;
                            tmp.Add(nh2);
                            if (catchUp) Unsched(life, nh2);
                        }
                    }
                }
            }
            else if (n > k)
            {
                // 먹이보다 많다 — 조금씩 준다(큰 무리부터)
                int deaths = (n - k + 9) / 10;
                for (int d = 0; d < deaths; d++)
                {
                    Herd big = tmp[0];
                    foreach (var h in tmp) if (h.Count > big.Count) big = h;
                    big.Count--;
                    if (big.Count <= 0) { RemoveHerd(life, big); tmp.Remove(big); if (tmp.Count == 0) break; }
                }
            }
            if (a.Predator && !catchUp)
                foreach (var h in tmp.ToList())
                    if (Now - h.LastMeal > 8L * a.HuntDays * GameTime.TicksPerDay && life.Herds.ContainsKey(h.Id))
                    {
                        // 오래 굶은 포식자
                        h.Count--;
                        h.LastMeal = Now - 4L * a.HuntDays * GameTime.TicksPerDay;
                        if (h.Count <= 0) RemoveHerd(life, h);
                    }
        }
        // 물고기
        int f = life.FishStock, fk = life.FishCap;
        if (fk > 0 && f < fk)
        {
            long e1000 = (long)Fauna.FishGrowth1000 * Math.Max(f, 2) * (fk - f) / fk / GameTime.DaysPerYear;
            life.FishStock = Math.Min(fk, f + (int)(e1000 / 1000) + (rng.NextInt(1000) < e1000 % 1000 ? 1 : 0));
        }
    }

    private void Unsched(ProvinceLife life, Herd h) { if (h.PendingHandle != 0) life.Sched.Cancel(h.PendingHandle); h.PendingHandle = 0; }

    private static int NearTile(ProvinceLife life, int from, int r, Pcg32 rng)
    {
        for (int tries = 0; tries < 20; tries++)
        {
            int x = from % S + rng.NextInt(-r, r + 1), y = from / S + rng.NextInt(-r, r + 1);
            if ((uint)x >= S || (uint)y >= S) continue;
            int t = y * S + x;
            if (life.Walk[t] && HexFrame.BorderDistance(t) >= 3 && !GroundInfo.IsWater(life.Map.Ground[t])) return t;
        }
        return -1;
    }

    // ───────── 잠들기 · 깨어나기 ─────────

    /// <summary>잠드는 지방: 무리의 예약을 지운다(멈춤). 깨어나면 다시 예약한다.</summary>
    private void SleepAnimals(ProvinceLife life)
    {
        foreach (var (_, h) in life.Herds) Unsched(life, h);
    }

    /// <summary>
    /// 깨어난 지방: 잠든 동안 흐른 날만큼 하루씩 늘고 줄기를 맞춘다(움직임 없이 — 길게 잠들었으면 앞의 1,500일까지만, 그 뒤는 이미 살 수 있는 수에 닿아 있다).
    /// 그리고 무리를 사는 곳에 세우고 다시 예약한다. (짐승을 아직 만든 적 없는 옛 저장 파일이면 처음처럼 만든다.)
    /// </summary>
    private void WakeAnimals(ProvinceLife life, DormantProvince d)
    {
        life.NextHerdId = d.NextHerdId;
        life.FishStock = d.FishStock;
        foreach (var h in d.Herds) life.Herds[h.Id] = h;
        ComputeCaps(life);
        if (!d.AnimalsReady) { InitAnimals(life); return; }
        if (d.SleptAt < 0) return; // 저장할 때 깨어 있던 지방 — 예약이 그대로 있다
        var rng = ARng(life);
        long days = Math.Max(0, (Now - d.SleptAt) / GameTime.TicksPerDay);
        for (long i = 0; i < Math.Min(days, 1_500); i++) PopulationDay(life, rng, catchUp: true);
        foreach (var (_, h) in life.Herds)
        {
            var a = Fauna.Animals[h.Species];
            h.State = HerdState.Graze; h.Target = -1; h.TargetKind = 0;
            if (a.Predator) h.LastMeal = Now - rng.NextInt((int)(a.HuntDays * GameTime.TicksPerDay));
            if (h.Tame) { h.Owner = -1; } // 따르던 사람은 떠났다 — 들로 돌아갔다
            h.Motion.SetStill(Now, h.Home % S * 1000 + 500, h.Home / S * 1000 + 500);
            h.Prev.CopyFrom(h.Motion);
            ScheduleHerd(life, h, Now + 1 + rng.NextInt((int)GameTime.TicksPerHour));
        }
    }

    /// <summary>(E5b) 측정: 그 지방의 짐승(작은 짐승 빼고)이 살 수 있는 수의 절반 아래로 줄었나.</summary>
    private bool GameScarce(ProvinceLife life)
    {
        if (life.AnimalCap.Length == 0) return false;
        int n = 0, k = 0;
        var c = AnimalCounts(life);
        for (int s = 0; s < c.Length; s++) if (Fauna.Animals[s].Size >= 2 && !Fauna.Animals[s].Predator) { n += c[s]; k += life.AnimalCap[s]; }
        return k > 0 && n * 2 < k;
    }

    /// <summary>측정용: 그 지방의 종마다 마릿수(따르는 짐승 빼고).</summary>
    public int[] AnimalCounts(ProvinceLife life)
    {
        var c = new int[Fauna.Animals.Count];
        foreach (var (_, h) in life.Herds) if (!h.Tame) c[h.Species] += h.Count;
        return c;
    }
}
