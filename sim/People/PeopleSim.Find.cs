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
            int k = b.NearestSight(px, py, ref bd); // (S1) 색인으로 — 같은 거리면 앞 변 · 작은 번호(모두 훑을 때와 같다)
            if (k >= 0) best = (b, k);
        }
        return best;
    }

    /// <summary>기억 목록 맨 앞에 넣는다(같은 지방의 가까운 자리 — 4타일 안 — 는 하나로 친다).</summary>
    private static void Remember(List<int> list, int place, int cap)
    {
        // (S1) 같은 지방인지는 나눗셈 없이(칸 × 타일 수 ≤ 값 < (칸 + 1) × 타일 수) — 결과는 같다
        int cell = Person.PlaceCell(place), tile = Person.PlaceTile(place);
        int lo = cell * HexFrame.TileCount, hi = lo + HexFrame.TileCount, tx = tile % S, ty = tile / S;
        for (int i = 0; i < list.Count; i++)
        {
            int v = list[i];
            if (v < lo || v >= hi) continue;
            int t = v - lo;
            if (Math.Abs(t % S - tx) <= 4 && Math.Abs(t / S - ty) <= 4) { list.RemoveAt(i); break; }
        }
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
        if (tile < 0 && !HasContainer(p)) AddWish(p, W_container, 8); // (E5b 고침) 그릇을 이미 들었으면 더 아쉽지 않다(빈 그릇을 들고도 그릇을 거듭 만들던 것)
        byte how = 0; int howArg = -1;
        // 1-1) 경계 너머 보임 → 넘어간다
        if (tile < 0 && SeenAcross(p, life, px, py) is { } far && !(far.B.Nb == p.FromCell && Now - p.ArrivedTick < 2 * GameTime.TicksPerHour)) // (E5b 고침) 막 떠나온 쪽의 물로 곧바로 되돌아가지 않는다(그쪽 물은 넘어선 자리에서 닿지 않았다)
        {
            int gi = NearestGate(far.B, life, here, far.B.SightX[far.K], far.B.SightY[far.K]);
            if (gi >= 0 && StartCross(p, far.B, gi, why, arg))
            {
                St.FindHow[0]++; p.How = 0; p.HowArg = Person.Place(far.B.Nb, far.B.SightTile[far.K]); p.SearchMiss = 0;
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
        if (tile >= 0 && WalkTo(p, tile)) { St.FindHow[how]++; SetWhy(p, Act.Drink, why, arg); p.How = how; p.HowArg = howArg; p.Target = tile; p.SearchMiss = 0; return true; }
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
        // (E5b 고침) 익힐 수 없으면 날고기 · 날생선을 썩기 전에 먹는다(배탈이 날 수 있다 — 그래서 익히고 싶어진다)
        int rawMeat = p.CarryCount(I_rawMeat) > 0 ? I_rawMeat : p.CarryCount(I_rawFish) > 0 ? I_rawFish : -1;
        bool eatRaw = rawMeat >= 0 && (p.Food < 50_000 || p.CarryCount(rawMeat) >= 3) && !(carried >= 0 && Craft.IsCookedOrKept(carried)); // 꽤 배고프거나 셋 넘게 들었을 때 — 익힌 · 말린 것이 있으면 그것부터
        if (eatRaw) carried = rawMeat; else rawMeat = -1;
        // (E5b 고침) 곧 썩을 것(날것이 아니고 썩는다 4 이상 — 열매 등)은 새로 따기 전에 먼저 먹는다
        if (carried >= 0 && (p.Food < UrgentFood || life.NearestFood(hx, hy, Now, SightTiles) < 0 || Craft.IsCookedOrKept(carried) || carried == rawMeat || (Prop(carried, Craft.P_rots) >= 4 && Prop(carried, Craft.P_poison) == 0)))
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
        var claims = L.Claims;
        claims.Clear();
        foreach (var o in life.People)
            if (o.Act == Act.Eat && o.Walking && !o.Dead && o.Id != p.Id && o.How != 3) claims[o.Target] = claims.GetValueOrDefault(o.Target) + Math.Max(1, o.TargetB);
        life.Claims = claims;
        int arg = p.Food / 1000;
        byte how = 0; int howArg = -1;
        // 1) 보임
        int tile = life.NearestFood(hx, hy, Now, SightTiles, skipTile);
        // 2) 기억(남은 몫이 있는 곳 — 가 보니 없으면 잊는다)
        if (tile < 0)
        {
            tile = NearestKnown(p.KnownFood, p.Cell, here, life, t => t != skipTile && life.StockAt(t, Now) > (claims.TryGetValue(t, out int c) ? c : 0));
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
            St.FindHow[how]++;
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
        // (E5b 고침) 둘레에 말려 둔 고기 같은 먹을 것이 놓여 있으면 가져온다
        if (p.Food < 40_000 && TryFetchStash(p, life, hx, hy, why)) return true;
        // (E5b) 딸 것이 없다 → 보이는 짐승을 잡거나 물고기를 잡는다
        if (TryHuntForFood(p)) return true;
        // 4) 찾아다님(초록이 짙은 쪽으로 — 이웃 지방도)
        return Search(p, Act.Eat, why, arg, water: false);
    }

    /// <summary>(E5b 고침) 땅에 놓인 오래가는 먹을 것(말린 고기 · 그을린 고기 · 말린 열매 …, 25타일 안 · 같은 이어진 땅)을 주우러 간다.</summary>
    private bool TryFetchStash(Person p, ProvinceLife life, int hx, int hy, Why why)
    {
        if (life.Items.Count == 0) return false;
        int here = hy * S + hx, reg = life.Region[here], best = -1, bestItem = -1; long bd = 25L * 25 + 1;
        void Check(int t, List<(int Item, int Count)> list)
        {
            long d = ProvinceLife.D2(t, hx, hy);
            if (d >= bd || (life.Region[t] != reg && !life.BesideRegion(t, reg))) return;
            foreach (var (item, n) in list)
                if (n > 0 && Craft.IsCookedOrKept(item) && Prop(item, Craft.P_edible) >= 3 && Prop(item, Craft.P_rots) <= 2) { bd = d; best = t; bestItem = item; return; }
        }
        if (life.Items.Count <= 64) { foreach (var (t, list) in life.Items) Check(t, list); }
        else
            for (int y = Math.Max(0, hy - 25); y <= Math.Min(S - 1, hy + 25); y++)
                for (int x = Math.Max(0, hx - 25); x <= Math.Min(S - 1, hx + 25); x++)
                {
                    int t = y * S + x;
                    if (life.HasItems(t)) Check(t, life.Items[t]);
                }
        if (best < 0 || !WalkTo(p, best)) return false;
        SetWhy(p, Act.Material, why); p.Target = best; p.TargetB = -2; p.PlanItem = bestItem;
        St.StashEaten++;
        return true;
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
            // (E5b 고침) 막 떠나온 지방의 자리로 곧바로 되돌아가지 않는다 — 거기서 닿지 못해 넘어왔다(두 지방 사이를 끝없이 오가던 것)
            if (cell == p.FromCell && Now - p.ArrivedTick < 2 * GameTime.TicksPerHour) continue;
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
        St.FindHow[1]++; p.How = 1; p.HowArg = bestPlace; p.SearchMiss = 0;
        return true;
    }

    /// <summary>경계를 넘어 물 · 먹을 것을 찾아가는 사람을 따라간다(같은 자리로 넘어간다).</summary>
    private bool FollowAcross(Person p, Person q, Why why, int arg)
    {
        var b = BorderOf(q.Cell, q.TargetB);
        int gi = b is null ? -1 : Array.BinarySearch(b.Gates, q.Target);
        if (b is null || gi < 0 || !StartCross(p, b, gi, why, arg)) return false;
        St.FindHow[2]++; p.How = 2; p.HowArg = q.Id;
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
            // (E5b 고침) 막 떠나온 지방으로 넘어가는 사람은 따라가지 않는다 — 서로 따라 두 지방 사이를 끝없이 오가던 것
            if (across && Now - p.ArrivedTick < 2 * GameTime.TicksPerHour && BorderOf(q.Cell, q.TargetB) is { } qb && qb.Nb == p.FromCell) continue;
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
        // (E5b 고침) 막 넘어온 뒤 두 시간은 이 지방 안부터 찾는다 — 셋 이상의 지방 모서리를 빙빙 돌며 목말라 죽던 것
        if (Now - p.ArrivedTick >= 2 * GameTime.TicksPerHour && (HexFrame.BorderDistance(here) < 26 || p.SearchMiss >= 3))
        {
            var planet = _w.Planet;
            foreach (var b in OpenBorders(life, here))
            {
                if (b.Nb == p.FromCell && Now - p.ArrivedTick < 2 * GameTime.TicksPerHour) continue; // (E5b 고침) 막 넘어온 곳으로 곧바로는 되돌아가지 않는다(먼저 이 지방 안을 찾는다)
                int gi = NearestGate(b, life, here, px, py);
                if (gi < 0) continue;
                int g = b.Gates[gi];
                int score = water
                    ? Math.Clamp((planet.Elevation[p.Cell] - planet.Elevation[b.Nb]) / 4, -30, 30) + (int)(ProvinceGenerator.Moisture(planet, b.Nb) / 80)
                    : (int)(ProvinceGenerator.Moisture(planet, b.Nb) / 40) + Math.Clamp((planet.Elevation[p.Cell] - planet.Elevation[b.Nb]) / 10, -10, 10);
                score += Math.Min(p.SearchMiss, 6) * 8 - Cheb(g, here) / 2 + rng.NextInt(12); // (E5b 고침) 못 찾은 수에 한도 — 없으면 몇백 번 못 찾은 사람은 지방 안을 찾지 않고 경계만 오갔다
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
            St.FindHow[3]++;
            p.How = 3; p.HowArg = -1;
            return true;
        }
        if (bestTile < 0 || !WalkTo(p, bestTile)) return false;
        p.SearchDir = bestDir;
        St.FindHow[3]++;
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
