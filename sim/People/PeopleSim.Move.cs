using Wtw.Sim.Planet;
using Wtw.Sim.Provinces;
using Wtw.Sim.Time;

namespace Wtw.Sim.People;

/// <summary>
/// (E3b) 왜 옮겨 가나. 하루 경계마다 사람마다 "떠나고 싶은 까닭"을 정하고(MoveWhy), 다음 판단에서 급한 일이 없으면 떠난다.
///   - 먹을 것이 모자람: 며칠째 둘레에도 기억에도 딸 것이 없다(Scarcity — 사람이 늘어 딴 몫이 빨리 줄면 쌓인다) → 집안(짝 · 아이)과 함께 새 땅으로.
///     한 무리 안에서도 모자란 집부터 떠나므로, 무리 일부가 떨어져 나간다.
///   - 짝을 찾아: 짝이 될 만한 사람이 이 땅에 없다(가끔).
///   - 호기심: 호기심 많은 어른이 드물게 이웃 땅을 둘러보러.
///   - 돌아가기: 혼자 남았고 쓸쓸하면 살던 땅(사람이 있던 곳)으로.
///   - 가족 따라: 짝 · 부모가 이웃 땅에 있으면 그리로.
/// 물 · 먹을 것을 찾아 넘어가는 일(보임 · 기억 · 따라감 · 찾아다님)은 PeopleSim.Find 가 한다. 정신 붕괴의 "혼자 떠나기"도 가끔 넘어간다.
/// </summary>
public sealed partial class PeopleSim
{
    public const int ScarcityToMove = 6;

    /// <summary>하루 경계: 떠나고 싶은 까닭을 정한다(사람 번호 순으로 불린다).</summary>
    private void MoveIntent(Person p, Core.Pcg32 rng)
    {
        p.Scarcity = p.Scarcity * 3 / 4;
        var st = p.Stage(Now);
        if (st == LifeStage.Baby || p.Carrier >= 0) { p.MoveWhy = Why.None; return; }
        var life = Life(p.Cell);
        // 가족 따라: 짝 · (아이면) 부모가 바로 이웃 땅에 있다
        foreach (int id in st == LifeStage.Child ? new[] { p.Mother, p.Father, p.Partner } : new[] { p.Partner })
            if (id >= 0 && Get(id) is { Dead: false } f && f.Cell != p.Cell && life.Map.SlotOf(f.Cell) >= 0 && f.Act != Act.Cross)
            {
                if (st == LifeStage.Child || rng.NextInt(100) < 50) { p.MoveWhy = Why.FollowFamily; p.MoveTo = f.Cell; return; }
            }
        if (st == LifeStage.Child) { p.MoveWhy = Why.None; return; }
        // 아이는 어른 따라, 아이 둔 부모는 아이를 데리고만
        if (p.Scarcity >= ScarcityToMove) { p.MoveWhy = Why.Scarce; p.MoveTo = -1; return; }
        if (p.MoveWhy is Why.Scarce or Why.FollowFamily) p.MoveWhy = Why.None; // 그 까닭이 없어졌다
        // 스스로 떠나는 일(돌아가기 · 짝 찾기 · 호기심)은 한 번 떠난 뒤 한동안 쉰다(이리저리 오가기만 하지 않게)
        if (Now - p.LastMove < 15 * GameTime.TicksPerDay) { if (p.MoveWhy is not (Why.Scarce or Why.FollowFamily)) p.MoveWhy = Why.None; return; }
        bool alone = true;
        foreach (var q in life.People) if (!q.Dead && q.Id != p.Id && q.Stage(Now) != LifeStage.Baby) { alone = false; break; }
        long here = Now - p.ArrivedTick;
        if (alone && p.FromCell >= 0 && here > 3 * GameTime.TicksPerDay && p.Social < 40_000 && life.Map.SlotOf(p.FromCell) >= 0 && rng.NextInt(100) < 20)
        { p.MoveWhy = Why.GoBack; p.MoveTo = p.FromCell; return; }
        int age = p.AgeYears(Now);
        if (p.Partner < 0 && age is >= 17 and <= 45 && here > 20 * GameTime.TicksPerDay && !HasMateCandidate(p, life) && rng.NextInt(1000) < 15)
        { p.MoveWhy = Why.SeekMate; p.MoveTo = -1; return; }
        if (p.HasTrait(T_curious) && rng.NextInt(1000) < 5 && !HasLittleChild(p)) { p.MoveWhy = Why.Explore; p.MoveTo = -1; return; }
    }

    private bool HasMateCandidate(Person p, ProvinceLife life)
    {
        int age = p.AgeYears(Now);
        foreach (var q in life.People)
        {
            if (q.Dead || q.Sex == p.Sex || IsFamily(p, q)) continue;
            int qa = q.AgeYears(Now);
            if (qa < 16 || Math.Abs(qa - age) > 15) continue;
            if (q.Partner < 0) return true;
        }
        return false;
    }

    private bool HasLittleChild(Person p)
    {
        foreach (int c in p.Children) if (Get(c) is { Dead: false } k && k.Stage(Now) <= LifeStage.Child) return true;
        return false;
    }

    /// <summary>판단 중: 떠나고 싶은 마음이 있으면 집안과 함께 떠난다. 떠났으면 true.</summary>
    private bool TryMove(Person p)
    {
        var why = p.MoveWhy;
        if (why == Why.None) return false;
        var life = Life(p.Cell);
        int here = p.Tile(Now);
        Border? b = null;
        if (p.MoveTo >= 0)
        {
            int slot = life.Map.SlotOf(p.MoveTo);
            b = slot >= 0 ? BorderOf(p.Cell, slot) : null;
            if (b is not null && NearestGate(b, life, here, here % S * 1000 + 500, here / S * 1000 + 500) < 0) b = null;
        }
        else b = PickNewLand(p, life, here);
        p.MoveWhy = Why.None; p.MoveTo = -1;
        if (b is null) return false;
        // 함께 갈 집안: 짝과 아이(같은 지방, 깨어 있고 급하지 않음) — 자는 사람이 있으면 다음에
        var group = new List<Person>();
        if (why is Why.Scarce or Why.Explore or Why.SeekMate)
        {
            if (why != Why.SeekMate && Get(p.Partner) is { Dead: false } mate && mate.Cell == p.Cell) group.Add(mate);
            foreach (int c in p.Children) if (Get(c) is { Dead: false } k && k.Cell == p.Cell && k.Carrier < 0 && k.Stage(Now) == LifeStage.Child) group.Add(k);
            if (group.Exists(g => g.Asleep || g.Act is Act.Cross)) { p.MoveWhy = why; return false; }
        }
        int gi = NearestGate(b, life, here, here % S * 1000 + 500, here / S * 1000 + 500);
        if (gi < 0 || !StartCross(p, b, gi, why)) return false;
        p.LastMove = Now;
        Stats.MoveWhy[(int)why]++;
        if (why == Why.Scarce)
        {
            p.Scarcity = 0;
            p.KnownFood.RemoveAll(k => Person.PlaceCell(k) == life.Cell); // 다 따 간 땅의 열매 자리는 잊는다(되돌아오지 않게)
        }
        foreach (var g in group)
        {
            Integrate(g, Now);
            if (g.Act is Act.Talk or Act.BreakArgue) EndTalkEarly(g);
            int gh = g.Tile(Now);
            int ggi = NearestGate(b, life, gh, b.Gates[gi] % S * 1000 + 500, b.Gates[gi] / S * 1000 + 500);
            if (ggi < 0 || !StartCross(g, b, ggi, Why.FollowFamily, p.Id)) continue;
            g.LastMove = Now; g.MoveWhy = Why.None;
            if (why == Why.Scarce) { g.Scarcity = 0; g.KnownFood.RemoveAll(k => Person.PlaceCell(k) == life.Cell); }
        }
        return true;
    }

    /// <summary>이야기 중인 사람을 데려간다: 상대는 혼자 남는다.</summary>
    private void EndTalkEarly(Person g)
    {
        if (Get(g.Target) is { } q && q.Target == g.Id && q.Act is Act.Talk or Act.BreakArgue && !q.Dead)
        {
            Integrate(q, Now);
            q.TargetB = -1;
            SetWhy(q, Act.Idle, Why.Nothing);
            Schedule(q, Now + 1);
        }
        g.TargetB = -1;
    }

    /// <summary>
    /// 새 땅 고르기(아무 데나 갈 때): 걸어갈 수 있는 변 중 — 막 떠나온 땅은 피하고, 축축한(풀 · 나무 · 물이 많을 듯한) 땅을 조금 더 좋아한다.
    /// 사람은 경계 너머를 모르므로 보이는 것(땅이 낮고 푸른가)만으로 고른다.
    /// </summary>
    private Border? PickNewLand(Person p, ProvinceLife life, int here)
    {
        var rng = Rng(p.Cell);
        var planet = _w.Planet;
        Border? best = null; int bs = int.MinValue;
        foreach (var b in OpenBorders(life, here))
        {
            int sc = (int)Math.Min(40, ProvinceGenerator.Moisture(planet, b.Nb) / 40) + rng.NextInt(30);
            if (b.Nb == p.FromCell) sc -= 40;
            int gi = NearestGate(b, life, here, here % S * 1000 + 500, here / S * 1000 + 500);
            if (gi >= 0) sc -= Cheb(b.Gates[gi], here) / 4;
            if (sc > bs) { bs = sc; best = b; }
        }
        return best;
    }

    /// <summary>아이 · 짝이 이웃 땅에 있는 가족에게 간다(판단 중). 갔으면 true.</summary>
    private bool CrossToward(Person p, int cell, Why why, int arg)
    {
        var life = Life(p.Cell);
        int slot = life.Map.SlotOf(cell);
        if (slot < 0 || BorderOf(p.Cell, slot) is not { } b) return false;
        int here = p.Tile(Now);
        int gi = NearestGate(b, life, here, here % S * 1000 + 500, here / S * 1000 + 500);
        return gi >= 0 && StartCross(p, b, gi, why, arg);
    }
}
