using Wtw.Sim.Provinces;

namespace Wtw.Sim.People;

/// <summary>
/// 물 · 먹을 것 찾기(E3 고침). 사람은 다 알지 못한다:
///   1) 둘레 SightTiles 타일 안에 보이는 것 → 그리로(보이는 것은 기억해 둔다)
///   2) 한 번 본 물가 · 열매 자리(기억) → 가장 가까운 곳으로
///   3) 다른 사람이 마시러 · 따러 가고 있으면 → 따라간다(가 보면 그곳을 알게 된다)
///   4) 그래도 없으면 찾아다닌다 — 물은 낮은 곳으로, 먹을 것은 초록이 짙은 쪽으로(가던 쪽을 조금 더 좋아함). 닿으면 둘러보고 다시 판단.
/// 카드의 "지금 하는 일"은 이 넷 중 실제로 한 것을 말한다.
/// </summary>
public sealed partial class PeopleSim
{
    public const int SightTiles = 14;

    /// <summary>둘레를 둘러보고 보이는 물가 · 열매 자리를 기억한다.</summary>
    private void Look(Person p)
    {
        var life = Life(p.Cell);
        p.Position(Now, out int px, out int py);
        int hx = px / 1000, hy = py / 1000;
        int w = life.NearestDrink(hx, hy, SightTiles);
        if (w >= 0)
        {
            if (p.KnownWater.Count == 0) AddBio(p, BioKind.FoundWater);
            Remember(p.KnownWater, w, Person.WaterMemory, life);
        }
        int f = life.NearestFood(hx, hy, Now, SightTiles);
        if (f >= 0) Remember(p.KnownFood, f, Person.FoodMemory, life);
    }

    /// <summary>기억 목록 맨 앞에 넣는다(가까운 자리 — 4타일 안 — 는 하나로 친다).</summary>
    private static void Remember(List<int> list, int tile, int cap, ProvinceLife life)
    {
        for (int i = 0; i < list.Count; i++)
            if (Cheb(list[i], tile) <= 4) { list.RemoveAt(i); break; }
        list.Insert(0, tile);
        if (list.Count > cap) list.RemoveAt(list.Count - 1);
    }

    private void Teach(Person p, int tile, bool water)
    {
        var life = Life(p.Cell);
        Remember(water ? p.KnownWater : p.KnownFood, tile, water ? Person.WaterMemory : Person.FoodMemory, life);
    }

    private bool TryDrink(Person p, Why why)
    {
        var life = Life(p.Cell);
        p.Position(Now, out int px, out int py);
        int hx = px / 1000, hy = py / 1000, here = hy * S + hx;
        int arg = p.Water / 1000;
        if (life.Drink[here]) { SetWhy(p, Act.Drink, why, arg); p.How = 0; p.Target = here; DoStill(p, 150); return true; }
        // 1) 보임
        int tile = life.NearestDrink(hx, hy, SightTiles);
        byte how = 0; int howArg = -1;
        // 2) 기억
        if (tile < 0) tile = NearestKnown(p.KnownWater, here, life, t => life.Drink[t]);
        if (tile >= 0 && how == 0 && Cheb(tile, here) > SightTiles) how = 1;
        // 3) 남을 따라감
        if (tile < 0 && Followee(p, Act.Drink, here) is { } q) { tile = q.Target; how = 2; howArg = q.Id; Teach(p, tile, true); }
        if (tile >= 0 && WalkTo(p, tile)) { Stats.FindHow[how]++; SetWhy(p, Act.Drink, why, arg); p.How = how; p.HowArg = howArg; p.Target = tile; return true; }
        // 4) 찾아다님(낮은 곳으로)
        return Search(p, Act.Drink, why, arg, water: true);
    }

    private bool TryEat(Person p, Why why, int skipTile = -1)
    {
        var life = Life(p.Cell);
        p.Position(Now, out int px, out int py);
        int hx = px / 1000, hy = py / 1000, here = hy * S + hx;
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
            tile = NearestKnown(p.KnownFood, here, life, t => t != skipTile && life.StockAt(t, Now) > (_claims.TryGetValue(t, out int c) ? c : 0));
            if (tile >= 0) how = 1;
        }
        life.Claims = null;
        // 3) 남을 따라감
        if (tile < 0 && Followee(p, Act.Eat, here) is { } fq) { tile = fq.Target; how = 2; howArg = fq.Id; Teach(p, tile, false); }
        if (tile >= 0 && WalkTo(p, tile))
        {
            Stats.FindHow[how]++;
            SetWhy(p, Act.Eat, why, arg);
            p.How = how; p.HowArg = howArg;
            p.Target = tile;
            var hd = life.HarvestAt(tile);
            p.TargetB = hd is null ? 1 : (int)Math.Clamp((Person.Full - p.Food) / Math.Max(1, hd.Nutrition), 1, 5);
            return true;
        }
        // 4) 찾아다님(초록이 짙은 쪽으로)
        return Search(p, Act.Eat, why, arg, water: false);
    }

    private int NearestKnown(List<int> known, int here, ProvinceLife life, Func<int, bool> ok)
    {
        int best = -1; long bd = long.MaxValue;
        foreach (int t in known)
        {
            if (!life.Paths.Reachable(here, t) || !ok(t)) continue;
            long d = ProvinceLife.D2(t, here % S, here / S);
            if (d < bd) { bd = d; best = t; }
        }
        return best;
    }

    /// <summary>같은 일을 하러 걸어가는 사람(가까운 순) — 따라가면 그곳을 알게 된다.</summary>
    private Person? Followee(Person p, Act act, int here)
    {
        var life = Life(p.Cell);
        Person? best = null; int bd = int.MaxValue;
        foreach (var q in life.People)
        {
            if (q.Id == p.Id || q.Dead || q.Act != act || !q.Walking || q.How == 3 || q.Target < 0) continue;
            int d = Cheb(q.Tile(Now), here);
            if (d > 40 || d >= bd || !life.Paths.Reachable(here, q.Target)) continue;
            if (act == Act.Eat && life.StockAt(q.Target, Now) <= Math.Max(1, q.TargetB)) continue; // 그 사람이 다 딸 만큼만 남았으면 따라가도 소용없다
            bd = d; best = q;
        }
        return best;
    }

    /// <summary>찾아다니기: 여덟 방향 중 — 물은 낮은 곳, 먹을 것은 풀 · 나무가 짙은 곳 — 가던 쪽을 조금 더 좋아한다.</summary>
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
        if (bestTile < 0 || !WalkTo(p, bestTile)) return false;
        p.SearchDir = bestDir;
        Stats.FindHow[3]++;
        SetWhy(p, act, why, arg);
        p.How = 3; p.HowArg = -1;
        p.Target = bestTile;
        return true;
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
