using Wtw.Sim.Provinces;
using Wtw.Sim.Time;

namespace Wtw.Sim.People;

/// <summary>
/// 물 · 먹을 것 찾기(E3 고침). 사람은 다 알지 못한다:
///   1) 둘레 SightTiles 타일 안에 보이는 것 → 그리로(보이는 것은 기억해 둔다)
///   2) 한 번 본 물가 · 열매 자리(기억) → 가장 가까운 곳으로
///   3) 다른 사람이 마시러 · 따러 가고 있으면 → 따라간다(가 보면 그곳을 알게 된다)
///   4) 그래도 없으면 찾아다닌다 — 물은 낮은 곳으로, 먹을 것은 초록이 짙은 쪽으로(가던 쪽을 조금 더 좋아함). 닿으면 둘러보고 다시 판단.
/// 카드의 "지금 하는 일"은 이 넷 중 실제로 한 것을 말한다.
/// (E3b) 경계 너머도 보이는 만큼은 보고, 이웃 지방의 물가도 기억한다(기억 = 칸 × 타일 수 + 타일). 그쪽에 있으면 경계를 넘어간다.
/// 찾아다니다 오래 못 찾거나 가장자리 가까이면 이웃 지방(물은 더 낮은 쪽, 먹을 것은 더 축축한 쪽)도 후보가 된다.
/// </summary>
public sealed partial class PeopleSim
{
    public const int SightTiles = 14;

    /// <summary>둘레를 둘러보고 보이는 물가 · 열매 자리를 기억한다(경계 너머 물가도).</summary>
    private void Look(Person p)
    {
        var life = Life(p.Cell);
        p.Position(Now, out int px, out int py);
        int hx = px / 1000, hy = py / 1000;
        int w = life.NearestDrink(hx, hy, SightTiles);
        int key = -1;
        if (w >= 0) key = Person.Place(p.Cell, w);
        else if (SeenAcross(p, life, px, py) is { } far) key = Person.Place(far.B.Nb, far.B.SightTile[far.K]);
        if (key >= 0)
        {
            if (p.KnownWater.Count == 0) AddBio(p, BioKind.FoundWater);
            Remember(p.KnownWater, key, Person.WaterMemory);
        }
        int f = life.NearestFood(hx, hy, Now, SightTiles);
        if (f >= 0) Remember(p.KnownFood, Person.Place(p.Cell, f), Person.FoodMemory);
    }

    /// <summary>경계 너머 보이는(SightTiles 안) 가장 가까운 물 마실 자리.</summary>
    private (Border B, int K)? SeenAcross(Person p, ProvinceLife life, int px, int py)
    {
        int here = (py / 1000) * S + px / 1000;
        if (HexFrame.BorderDistance(here) >= SightTiles) return null;
        (Border, int)? best = null; long bd = (long)SightTiles * SightTiles * 1_000_000;
        for (int s = 0; s < 6; s++)
        {
            if (HexFrame.EdgeDistance(here, s) >= SightTiles) continue;
            var b = BorderOf(p.Cell, s);
            if (b is null || b.Gates.Length == 0) continue;
            for (int k = 0; k < b.SightTile.Length; k++)
            {
                long dx = b.SightX[k] - px, dy = b.SightY[k] - py, d = dx * dx + dy * dy;
                if (d < bd) { bd = d; best = (b, k); }
            }
        }
        return best;
    }

    /// <summary>기억 목록 맨 앞에 넣는다(같은 지방의 가까운 자리 — 4타일 안 — 는 하나로 친다).</summary>
    private static void Remember(List<int> list, int place, int cap)
    {
        int cell = Person.PlaceCell(place), tile = Person.PlaceTile(place);
        for (int i = 0; i < list.Count; i++)
            if (Person.PlaceCell(list[i]) == cell && Cheb(Person.PlaceTile(list[i]), tile) <= 4) { list.RemoveAt(i); break; }
        list.Insert(0, place);
        if (list.Count > cap) list.RemoveAt(list.Count - 1);
    }

    private static void Forget(List<int> list, int place)
    {
        int i = list.IndexOf(place);
        if (i >= 0) list.RemoveAt(i);
    }

    private void Teach(Person p, int tile, bool water) =>
        Remember(water ? p.KnownWater : p.KnownFood, Person.Place(p.Cell, tile), water ? Person.WaterMemory : Person.FoodMemory);

    private bool TryDrink(Person p, Why why)
    {
        var life = Life(p.Cell);
        p.Position(Now, out int px, out int py);
        int hx = px / 1000, hy = py / 1000, here = hy * S + hx;
        int arg = p.Water / 1000;
        if (life.Drink[here]) { SetWhy(p, Act.Drink, why, arg); p.How = 0; p.Target = here; p.SearchMiss = 0; DoStill(p, 150); return true; }
        // 1) 보임
        int tile = life.NearestDrink(hx, hy, SightTiles);
        // (E4) 그릇에 담아 온 물이 있고 물가가 보이지 않으면 그것을 마신다 · 물이 멀면 그릇이 아쉽다
        if (tile < 0 && p.PotWater > 0) { SetWhy(p, Act.DrinkCarried, why, arg); p.Target = here; StandStill(p); DoStill(p, 100); return true; }
        if (tile < 0) AddWish(p, W_container, 8);
        byte how = 0; int howArg = -1;
        // 1-1) 경계 너머 보임 → 넘어간다
        if (tile < 0 && SeenAcross(p, life, px, py) is { } far)
        {
            int gi = NearestGate(far.B, life, here, far.B.SightX[far.K], far.B.SightY[far.K]);
            if (gi >= 0 && StartCross(p, far.B, gi, why, arg))
            {
                Stats.FindHow[0]++; p.How = 0; p.HowArg = Person.Place(far.B.Nb, far.B.SightTile[far.K]); p.SearchMiss = 0;
                return true;
            }
        }
        // 2) 기억(이 지방)
        if (tile < 0) tile = NearestKnown(p.KnownWater, p.Cell, here, life, t => life.Drink[t]);
        if (tile >= 0 && how == 0 && Cheb(tile, here) > SightTiles) how = 1;
        // 2-1) 기억(이웃 지방) → 넘어간다
        if (tile < 0 && CrossToKnown(p, life, here, p.KnownWater, why, arg)) return true;
        // 3) 남을 따라감
        if (tile < 0 && Followee(p, Act.Drink, here) is { } q)
        {
            if (q.Act == Act.Cross) return FollowAcross(p, q, why, arg);
            tile = q.Target; how = 2; howArg = q.Id; Teach(p, tile, true);
        }
        if (tile >= 0 && WalkTo(p, tile)) { Stats.FindHow[how]++; SetWhy(p, Act.Drink, why, arg); p.How = how; p.HowArg = howArg; p.Target = tile; p.SearchMiss = 0; return true; }
        // 4) 찾아다님(낮은 곳으로 — 이웃 지방도)
        return Search(p, Act.Drink, why, arg, water: true);
    }

    private bool TryEat(Person p, Why why, int skipTile = -1)
    {
        var life = Life(p.Cell);
        p.Position(Now, out int px, out int py);
        int hx = px / 1000, hy = py / 1000, here = hy * S + hx;
        p.PlanItem = -1;
        // (E4) 들고 있는 먹을 것이 있으면 그것부터(익힌 것 · 말린 것 · 바구니에 담아 둔 것) — 날것은 익히는 법을 알고 불이 가까우면 익혀서
        if (TryCook(p, why)) return true;
        int carried = BestCarriedFood(p, desperate: p.Food < UrgentFood);
        if (carried >= 0 && (p.Food < UrgentFood || life.NearestFood(hx, hy, Now, SightTiles) < 0 || Craft.IsCookedOrKept(carried)))
        {
            SetWhy(p, Act.EatCarried, why, p.Food / 1000); p.PlanItem = carried; p.Target = here; StandStill(p); DoStill(p, 250);
            return true;
        }
        if (p.Food <= 0 && p.HasTrait(T_cannibal))
        {
            Person? best = null; long bd = long.MaxValue;
            foreach (var b in life.People)
                if (b.Dead && !b.Eaten && b.Id != p.Id) { long d = ProvinceLife.D2(b.Tile(Now), hx, hy); if (d < bd && d < 40 * 40) { bd = d; best = b; } }
            if (best is not null && WalkTo(p, best.Tile(Now))) { SetWhy(p, Act.EatCorpse, Why.Starving); p.Target = best.Id; return true; }
        }
        _claims.Clear();
        foreach (var o in life.People)
            if (o.Act == Act.Eat && o.Walking && !o.Dead && o.Id != p.Id && o.How != 3) _claims[o.Target] = _claims.GetValueOrDefault(o.Target) + Math.Max(1, o.TargetB);
        life.Claims = _claims;
        int arg = p.Food / 1000;
        byte how = 0; int howArg = -1;
        // 1) 보임
        int tile = life.NearestFood(hx, hy, Now, SightTiles, skipTile);
        // 2) 기억(남은 몫이 있는 곳 — 가 보니 없으면 잊는다)
        if (tile < 0)
        {
            tile = NearestKnown(p.KnownFood, p.Cell, here, life, t => t != skipTile && life.StockAt(t, Now) > (_claims.TryGetValue(t, out int c) ? c : 0));
            if (tile >= 0) how = 1;
        }
        life.Claims = null;
        if (tile < 0) p.Scarcity++; // 둘레에도 기억에도 딸 것이 없다(누가 다 땄거나 원래 없음)
        // 2-1) 기억(이웃 지방) → 넘어간다
        if (tile < 0 && CrossToKnown(p, life, here, p.KnownFood, why, arg)) return true;
        // 3) 남을 따라감
        if (tile < 0 && Followee(p, Act.Eat, here) is { } fq)
        {
            if (fq.Act == Act.Cross) return FollowAcross(p, fq, why, arg);
            tile = fq.Target; how = 2; howArg = fq.Id; Teach(p, tile, false);
        }
        if (tile >= 0 && WalkTo(p, tile))
        {
            Stats.FindHow[how]++;
            SetWhy(p, Act.Eat, why, arg);
            p.How = how; p.HowArg = howArg;
            p.Target = tile;
            p.SearchMiss = 0;
            var hd = life.HarvestAt(tile);
            p.TargetB = hd is null ? 1 : (int)Math.Clamp((Person.Full - p.Food) / Math.Max(1, hd.Nutrition), 1, 5);
            // (E4) 익히는 법을 알고 불이 가까우면 들고 가서 익힌다 · 그릇이 있으면 남는 몫을 담아 둔다
            if (hd is not null && KnowsAnyRoast(p) && Craft.Prop(hd.ItemIndex, Craft.P_edible) > 0 && hd.ItemIndex != I_coconut && life.NearestFire(hx, hy, Now, 45) >= 0) p.PlanItem = 1;
            else if (hd is not null && HasContainer(p) && p.Scarcity == 0) p.PlanItem = 2;
            return true;
        }
        // 4) 찾아다님(초록이 짙은 쪽으로 — 이웃 지방도)
        return Search(p, Act.Eat, why, arg, water: false);
    }

    /// <summary>기억 중 이 지방(cell)의 자리에서 가장 가까운 것(타일). 없으면 −1.</summary>
    private int NearestKnown(List<int> known, int cell, int here, ProvinceLife life, Func<int, bool> ok)
    {
        int best = -1; long bd = long.MaxValue;
        foreach (int place in known)
        {
            if (Person.PlaceCell(place) != cell) continue;
            int t = Person.PlaceTile(place);
            if (!life.Paths.Reachable(here, t) || !ok(t)) continue;
            long d = ProvinceLife.D2(t, here % S, here / S);
            if (d < bd) { bd = d; best = t; }
        }
        return best;
    }

    /// <summary>기억 속 이웃 지방의 자리 — 가장 가까운 것으로 경계를 넘어간다(바로 붙은 지방만).</summary>
    private bool CrossToKnown(Person p, ProvinceLife life, int here, List<int> known, Why why, int arg)
    {
        Border? bestB = null; int bestGate = -1, bestPlace = -1; long bd = long.MaxValue;
        foreach (int place in known)
        {
            int cell = Person.PlaceCell(place);
            if (cell == p.Cell) continue;
            int slot = life.Map.SlotOf(cell);
            if (slot < 0 || BorderOf(p.Cell, slot) is not { } b || b.Gates.Length == 0) continue;
            int t = Person.PlaceTile(place);
            var (lx, ly) = b.Place.ToLayout(t % S + 0.5, t / S + 0.5);
            long ax = (long)Math.Round((lx + HexFrame.Center) * 1000), ay = (long)Math.Round((ly + HexFrame.Center) * 1000);
            int gi = NearestGate(b, life, here, ax, ay);
            if (gi < 0) continue;
            int g = b.Gates[gi];
            long dx = g % S * 1000 + 500 - ax, dy = g / S * 1000 + 500 - ay;
            long d = ProvinceLife.D2(g, here % S, here / S) * 1_000_000 + dx * dx + dy * dy;
            if (d < bd) { bd = d; bestB = b; bestGate = gi; bestPlace = place; }
        }
        if (bestB is null || !StartCross(p, bestB, bestGate, why, arg)) return false;
        Stats.FindHow[1]++; p.How = 1; p.HowArg = bestPlace; p.SearchMiss = 0;
        return true;
    }

    /// <summary>경계를 넘어 물 · 먹을 것을 찾아가는 사람을 따라간다(같은 자리로 넘어간다).</summary>
    private bool FollowAcross(Person p, Person q, Why why, int arg)
    {
        var b = BorderOf(q.Cell, q.TargetB);
        int gi = b is null ? -1 : Array.BinarySearch(b.Gates, q.Target);
        if (b is null || gi < 0 || !StartCross(p, b, gi, why, arg)) return false;
        Stats.FindHow[2]++; p.How = 2; p.HowArg = q.Id;
        return true;
    }

    /// <summary>같은 일을 하러 걸어가는 사람(가까운 순) — 따라가면 그곳을 알게 된다. 경계를 넘어 그 일을 하러 가는 사람도.</summary>
    private Person? Followee(Person p, Act act, int here)
    {
        var life = Life(p.Cell);
        Person? best = null; int bd = int.MaxValue;
        foreach (var q in life.People)
        {
            if (q.Id == p.Id || q.Dead || !q.Walking || q.How == 3 || q.Target < 0) continue;
            bool across = q.Act == Act.Cross && (act == Act.Drink ? q.Why is Why.Thirsty or Why.VeryThirsty : q.Why is Why.Hungry or Why.VeryHungry or Why.Starving);
            if (q.Act != act && !across) continue;
            int d = Cheb(q.Tile(Now), here);
            if (d > 40 || d >= bd || !life.Paths.Reachable(here, q.Target)) continue;
            if (act == Act.Eat && !across && life.StockAt(q.Target, Now) <= Math.Max(1, q.TargetB)) continue; // 그 사람이 다 딸 만큼만 남았으면 따라가도 소용없다
            bd = d; best = q;
        }
        return best;
    }

    /// <summary>
    /// 찾아다니기: 여덟 방향 중 — 물은 낮은 곳, 먹을 것은 풀 · 나무가 짙은 곳 — 가던 쪽을 조금 더 좋아한다.
    /// (E3b) 가장자리 가까이거나 여러 번 못 찾았으면 이웃 지방도 후보: 물은 더 낮은 땅 쪽, 먹을 것은 더 축축한 땅 쪽. 못 찾을수록 떠나고 싶어진다.
    /// 막 떠나온 지방으로 곧바로 되돌아가는 것은 덜 좋아한다.
    /// </summary>
    private bool Search(Person p, Act act, Why why, int arg, bool water)
    {
        var life = Life(p.Cell);
        var rng = Rng(p.Cell);
        p.Position(Now, out int px, out int py);
        int hx = px / 1000, hy = py / 1000, here = hy * S + hx;
        if (!life.Walk[here]) return false;
        int bestDir = -1, bestTile = -1, bestScore = int.MinValue;
        int dist = 16 + rng.NextInt(8);
        for (int d = 0; d < 8; d++)
        {
            int t = -1;
            for (int k = dist; k >= 5; k -= 3)
            {
                int x = hx + DX8[d] * k, y = hy + DY8[d] * k;
                if ((uint)x >= S || (uint)y >= S) continue;
                int c = y * S + x;
                if (life.Walk[c] && life.Region[c] == life.Region[here] && HexFrame.BorderDistance(c) >= 3) { t = c; break; }
            }
            if (t < 0) continue;
            int low = (life.Map.Height[here] - life.Map.Height[t]) * 3;
            int green = Greenness(life, t);
            int score = (water ? low * 2 + green : low / 2 + green * 3) + rng.NextInt(12);
            if (p.SearchDir >= 0) { if (d == p.SearchDir) score += 18; else if (d == (p.SearchDir + 4) % 8) score -= 25; }
            if (score > bestScore) { bestScore = score; bestDir = d; bestTile = t; }
        }
        // 이웃 지방
        Border? crossB = null; int crossGate = -1;
        if (HexFrame.BorderDistance(here) < 26 || p.SearchMiss >= 3)
        {
            var planet = _w.Planet;
            foreach (var b in OpenBorders(life, here))
            {
                int gi = NearestGate(b, life, here, px, py);
                if (gi < 0) continue;
                int g = b.Gates[gi];
                int score = water
                    ? Math.Clamp((planet.Elevation[p.Cell] - planet.Elevation[b.Nb]) / 4, -30, 30) + (int)(ProvinceGenerator.Moisture(planet, b.Nb) / 80)
                    : (int)(ProvinceGenerator.Moisture(planet, b.Nb) / 40) + Math.Clamp((planet.Elevation[p.Cell] - planet.Elevation[b.Nb]) / 10, -10, 10);
                score += p.SearchMiss * 8 - Cheb(g, here) / 2 + rng.NextInt(12);
                if (b.Nb == p.FromCell && Now - p.ArrivedTick < 3 * GameTime.TicksPerDay) score -= 30;
                int gd = Dir8(g % S - hx, g / S - hy);
                if (p.SearchDir >= 0 && gd == p.SearchDir) score += 18;
                if (score > bestScore) { bestScore = score; crossB = b; crossGate = gi; bestDir = gd; }
            }
        }
        p.SearchMiss++;
        if (crossB is not null)
        {
            if (!StartCross(p, crossB, crossGate, why, arg)) return false;
            p.SearchDir = bestDir;
            Stats.FindHow[3]++;
            p.How = 3; p.HowArg = -1;
            return true;
        }
        if (bestTile < 0 || !WalkTo(p, bestTile)) return false;
        p.SearchDir = bestDir;
        Stats.FindHow[3]++;
        SetWhy(p, act, why, arg);
        p.How = 3; p.HowArg = -1;
        p.Target = bestTile;
        return true;
    }

    /// <summary>(dx, dy) 와 가장 가까운 여덟 방향.</summary>
    private static int Dir8(int dx, int dy)
    {
        if (dx == 0 && dy == 0) return -1;
        int best = 0; long bd = long.MinValue;
        for (int d = 0; d < 8; d++)
        {
            // 대각선은 √2 로 나눠야 하지만 정수로: 축 방향 × 10, 대각선 × 7
            long dot = (long)(DX8[d] * dx + DY8[d] * dy) * (DX8[d] != 0 && DY8[d] != 0 ? 7 : 10);
            if (dot > bd) { bd = dot; best = d; }
        }
        return best;
    }

    /// <summary>둘레 5×5 의 풀 · 습지 · 진흙 땅과 나무 · 덤불 수.</summary>
    private static int Greenness(ProvinceLife life, int t)
    {
        int x0 = t % S, y0 = t / S, n = 0;
        for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
            {
                int x = x0 + dx, y = y0 + dy;
                if ((uint)x >= S || (uint)y >= S) continue;
                int i = y * S + x;
                if (life.Map.Ground[i] is Ground.Grass or Ground.Marsh or Ground.Mud) n++;
                if (life.Shelter[i] == 1 || life.HarvestAt(i) is { Food: true }) n += 2;
            }
        return n;
    }
}
