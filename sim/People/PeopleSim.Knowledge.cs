using Wtw.Sim.Core;
using Wtw.Sim.Data;
using Wtw.Sim.Provinces;
using Wtw.Sim.Time;

namespace Wtw.Sim.People;

/// <summary>(E5) 그림 하나: 어느 법을(−1 = 알아볼 수 없는 무늬) · 누가 · 언제 · 어디에(칸 · 벽 타일 · 서서 보는 타일) · 무엇에(0 나무껍질 · 1 바위 면 · 2 동굴 벽) · 언제까지 남나.</summary>
public sealed class Painting
{
    public int Id, Cell, Tile, Stand, Rule = -1, Painter = -1;
    public byte Medium;
    public long Tick, Until;
    /// <summary>들여다본 횟수 · 이 그림으로 다 배운 사람 수.</summary>
    public int Seen, Taught;
    public bool Alive(long now) => now < Until;
    public static string MediumName(byte m) => m switch { 2 => "동굴 벽", 1 => "바위 면", _ => "나무껍질" };
}

/// <summary>(E5) 한 무리(같은 지방에 함께 사는 사람들)가 한 법을 아나: 지금 아는 사람 수 · 끊긴 적 · 되찾은 적.</summary>
public sealed class GroupKnow
{
    public bool Known = true;
    public int Knowers;
    public long Since, LostTick = -1;
    /// <summary>아는 사람이 무리에서 없어진 때(−1 = 있음). 잠깐 이웃 땅에 다녀오는 것은 끊김이 아니다 — LoseAfter 동안 없어야 끊긴 것.</summary>
    public long EmptySince = -1;
    public int LostCount, RefoundCount;
}

/// <summary>(E5) 지식의 행방 — 한 법의 역사 한 줄. Kind: 0 처음 · 1 무리에서 끊김 · 2 무리에서 되찾음 · 3 세계에서 사라짐 · 4 세계에서 다시 · 5 그림으로 남김 · 6 그림이 바램.</summary>
public readonly record struct KnowEvent(long Tick, byte Kind, int Cell, string Text);

/// <summary>
/// (E5) 지식(기획서 9절). 지식은 사람 머릿속에 있다: 아는 사람이 일부러 가르치고(조금씩 쌓임, 말이 다르면 느림), 곁에서 보고 따라 하고(더 느리고 덜 정확),
/// 오래 안 쓰면 흐려져 잊고(늙으면 조금씩), 그림(동굴 벽 · 바위 면 · 나무껍질)으로 남긴다. 그림은 말과 상관없이 누구나 조금은 알아본다.
/// 무리(같은 지방에 함께 사는 사람들 — E7 이 바꿀 수 있게 한 곳에서만 정한다) 단위로 끊김과 되찾음을 센다.
/// </summary>
public sealed partial class PeopleSim
{
    public const long TeachCooldown = GameTime.TicksPerDay;
    public const int ForgetBelow = 15;
    /// <summary>안 쓴 지 이만큼 지나면 흐려지기 시작한다(2년).</summary>
    public const long FadeAfter = 2 * GameTime.TicksPerYear;
    public readonly SortedDictionary<int, Painting> Paintings = new();
    public int NextPaintingId { get; internal set; } = 1;
    public readonly SortedDictionary<long, GroupKnow> GroupKnows = new();
    public readonly SortedDictionary<int, List<KnowEvent>> KnowHistory = new();
    public const int HistoryKept = 40;
    /// <summary>무리에 아는 사람이 이만큼 없으면 끊긴 것으로 본다(잠깐 떠났다 돌아오는 것은 빼려고 — 20일).</summary>
    public const long LoseAfter = 20 * GameTime.TicksPerDay;

    private int W_record = -1, A_draw = -1, I_painting = -1, I_ash = -1;
    private CraftRule? R_paint;

    private void InitKnowledge()
    {
        foreach (var w in Craft.Wishes) if (w.Id == "record") W_record = w.Index;
        A_draw = Craft.ActionIndex("draw");
        I_painting = Craft.ItemIndex("painting");
        I_ash = Craft.ItemIndex("ash");
        R_paint = Craft.Rule("paint");
    }

    /// <summary>무리 = 같은 지방에 함께 사는 사람들(E7 이 정식 집단으로 바꿀 자리 — 여기 한 곳).</summary>
    public static long GroupKey(int cell, int rule) => (long)cell * 1000 + rule;
    public static int GroupCell(long key) => (int)(key / 1000);
    public static int GroupRule(long key) => (int)(key % 1000);

    /// <summary>같은 무리(지방)에서 살아 있는 사람 가운데 그 법을 아는 사람 수.</summary>
    public int GroupKnowers(int cell, int rule)
    {
        int n = 0;
        foreach (var q in PeopleIn(cell)) if (!q.Dead && q.Knows(rule)) n++;
        return n;
    }

    // ───────── 아는 정도 ─────────

    /// <summary>처음 알게 될 때의 아는 정도: 스스로 알아냄 80 · 배움 65 · 보고 따라 함 50 · 그림 40.</summary>
    public static int StartLevel(Via v) => v switch { Via.Self => 80, Via.Teach => 65, Via.Watch => 50, _ => 40 };

    /// <summary>그 법을 썼다: 굳는다(아는 정도 +8, 마지막으로 쓴 때).</summary>
    public void UseKnowledge(Person p, int rule)
    {
        int i = p.KnownIndex(rule);
        if (i < 0) return;
        var k = p.Known[i];
        k.LastUse = Now; k.Level = Math.Min(100, k.Level + 8);
        p.Known[i] = k;
    }

    /// <summary>
    /// 배움이 쌓인다(가르침 · 보고 따라 함 · 그림). 이미 알면 흐려진 것이 되살아난다. 100 이 되면 안다.
    /// from = 가르친(본) 사람(그림이면 그린 사람), wish · input · with = 알게 될 때 남길 "왜 · 어떻게".
    /// </summary>
    public void AddLearning(Person q, CraftRule rule, int amount, int from, Via via, int wish, int input, int with)
    {
        if (amount <= 0) return;
        int ki = q.KnownIndex(rule.Index);
        if (ki >= 0)
        {
            var k = q.Known[ki];
            int before = k.Level;
            k.Level = Math.Min(100, k.Level + amount / 2); k.LastUse = Now;
            q.Known[ki] = k;
            if (before < 50 && k.Level >= 50) { AddBio(q, BioKind.Refreshed, from, rule.Index); Stats.Refreshed++; }
            return;
        }
        int li = q.Learning.FindIndex(l => l.Rule == rule.Index);
        var e = li >= 0 ? q.Learning[li] : new Learning { Rule = rule.Index };
        e.Progress = Math.Min(100, e.Progress + amount); e.From = from; e.Via = via; e.Tick = Now;
        if (e.Progress >= 100)
        {
            if (li >= 0) q.Learning.RemoveAt(li);
            Learn(q, rule, wish, input, with, from, via);
            return;
        }
        if (li >= 0) q.Learning[li] = e; else q.Learning.Add(e);
    }

    /// <summary>하루 경계(사람마다): 안 쓴 법은 흐려지고, 늙으면 조금씩 잊고, 아주 흐려지면 잊는다. 오래 멈춘 배움도 흐려진다.</summary>
    internal void KnowledgeDaily(Person p)
    {
        long day = Now / GameTime.TicksPerDay + p.Id;
        bool elder = p.Stage(Now) == LifeStage.Elder, stubborn = p.HasTrait(T_stubborn);
        for (int i = p.Known.Count - 1; i >= 0; i--)
        {
            var k = p.Known[i];
            int dec = 0;
            if (Now - k.LastUse > FadeAfter && day % 3 == 0) dec++;
            if (elder && day % 8 == 0) dec++;
            if (stubborn && day % 2 == 0) dec = 0; // 고지식함: 아는 법을 덜 잊는다
            if (dec == 0) continue;
            k.Level -= dec;
            if (k.Level < ForgetBelow)
            {
                p.Known.RemoveAt(i);
                Stats.Forgot++;
                AddBio(p, BioKind.Forgot, -1, k.Rule);
                continue;
            }
            p.Known[i] = k;
        }
        for (int i = p.Learning.Count - 1; i >= 0; i--)
        {
            var l = p.Learning[i];
            if (Now - l.Tick < GameTime.TicksPerYear || day % 4 != 0) continue;
            l.Progress--;
            if (l.Progress <= 0) p.Learning.RemoveAt(i); else p.Learning[i] = l;
        }
    }

    // ───────── 가르치기 ─────────

    /// <summary>가르칠 수 있나(빠른 확인 — 판단마다 부른다): 아는 법이 있는 어른, 하루에 한 번, 낮에.</summary>
    private int TeachScore(Person p, ProvinceLife life)
    {
        if (p.Known.Count == 0 || p.Stage(Now) < LifeStage.Adult || Now - p.LastTeach < TeachCooldown || life.IsNight(Now)) return int.MinValue;
        int alive = 0; foreach (var q in life.People) if (!q.Dead) { alive++; if (alive >= 2) break; }
        if (alive < 2) return int.MinValue;
        return 10 + p.SkillLevel(PeopleData.Teaching) + (p.HasTrait(T_eloquent) ? 6 : 0) - (p.HasTrait(T_blunt) ? 4 : 0) + (Ability(p, PeopleData.Soc) - 5);
    }

    /// <summary>
    /// 누구에게 무엇을 가르칠까: 곁(40타일)의 깨어 있는 아이 이상 가운데 모르는(또는 흐려진) 법.
    /// 까닭: 바람이 있어 물어 옴(그 바람에 맞는 법) · 무리에 아는 사람이 적음 · 자라는 아이(가족이면 더) · 흐려진 사람.
    /// </summary>
    private bool TryTeach(Person p)
    {
        var life = Life(p.Cell);
        var rng = Rng(p.Cell);
        int here = p.Tile(Now);
        p.LastTeach = Now - TeachCooldown + 6 * GameTime.TicksPerHour; // 못 찾으면 여섯 시간 뒤에 다시
        Person? best = null; int bestRule = -1, bestScore = 0; Why bestWhy = Why.None;
        var counts = new Dictionary<int, int>();
        int Count(int rule) { if (!counts.TryGetValue(rule, out int n)) counts[rule] = n = GroupKnowers(p.Cell, rule); return n; }
        foreach (var q in life.People)
        {
            if (q.Id == p.Id || q.Stage(Now) < LifeStage.Child || !CanBeTalkedTo(q)) continue;
            int qt = q.Tile(Now), d = Cheb(qt, here);
            if (d > 40 || !life.Paths.Reachable(here, qt)) continue;
            bool family = IsFamily(p, q), child = q.Stage(Now) == LifeStage.Child;
            foreach (var k in p.Known)
            {
                if (k.Level < 40) continue;
                var r = Craft.Rules[k.Rule];
                int ql = q.KnowLevel(k.Rule);
                if (ql >= 50) continue;
                int sc = 0; Why why = Why.None;
                if (ql > 0) { sc = 18; why = Why.TeachFaded; }
                int asked = 0;
                for (int w = 0; w < Craft.Wishes.Count; w++) if (q.Wish[w] >= 20 && ServesWish(r, Craft.Wishes[w])) asked = Math.Max(asked, q.Wish[w]);
                if (asked > 0 && 20 + asked / 2 > sc) { sc = 20 + asked / 2; why = Why.TeachAsked; }
                int kn = Count(k.Rule);
                if (kn <= 2 && 25 - kn * 5 > sc) { sc = 25 - kn * 5; why = Why.TeachFew; }
                if (child && 22 > sc) { sc = 22; why = Why.TeachChild; }
                if (sc == 0) continue;
                if (family) sc += 12;
                sc += q.LearnProgress(k.Rule) / 5 + rng.NextInt(8) - d / 4;
                if (sc > bestScore) { bestScore = sc; best = q; bestRule = k.Rule; bestWhy = why; }
            }
        }
        if (best is null) return false;
        int goal = Beside(p, best.Tile(Now));
        p.LastTeach = Now;
        if (Cheb(goal, here) <= 1) { SetWhy(p, Act.Teach, bestWhy, best.Id); p.Target = best.Id; p.TargetB = bestRule; StandStill(p); BeginLesson(p); return true; }
        if (!WalkTo(p, goal)) return false;
        SetWhy(p, Act.Teach, bestWhy, best.Id);
        p.Target = best.Id; p.TargetB = bestRule;
        return true;
    }

    /// <summary>다가갔다: 배울 사람이 곁에 있고 말을 받을 수 있으면 그 사람의 일을 끊고 함께 한 시간 남짓 가르친다.</summary>
    internal void BeginLesson(Person p)
    {
        var q = Get(p.Target);
        int here = p.Tile(Now);
        if (q is null || !CanBeTalkedTo(q) || Cheb(q.Tile(Now), here) > 3) { if (!TryWander(p, Why.Nothing)) StartRest(p, Why.Nothing); return; }
        var rng = Rng(p.Cell);
        long dur = GameTime.TicksPerHour + rng.NextInt((int)(GameTime.TicksPerHour / 2));
        Integrate(q, Now);
        q.Position(Now, out int qx, out int qy);
        q.NewMotion().SetStill(Now, qx, qy);
        q.Walking = false;
        SetWhy(q, Act.Learn, Why.BeingTaught, p.Id);
        q.Target = p.Id; q.TargetB = p.TargetB;
        p.ActUntil = q.ActUntil = Now + dur;
        p.Walking = false;
        p.PlanHeat = (int)(Now / 60); // 수업 시작(분 단위 — 끝에 얼마나 했나를 셈)
        Schedule(p, PredictUrgent(p, Now, p.ActUntil, here));
        Schedule(q, PredictUrgent(q, Now, q.ActUntil, q.Tile(Now)));
        Stats.Lessons++;
    }

    /// <summary>
    /// 수업이 끝났다(가르친 사람이 정한다): 배움이 쌓인다. 빠르기 = 가르치는 사람의 말재주 · 가르치기 기술 + 배우는 사람의 영리함 · 나이(아이가 빠름),
    /// × 알아듣기(25% + 75% × 알아듣기%) — 말이 다르면 느리다(막지 않는다). 끝까지 못 했으면 한 만큼.
    /// </summary>
    internal void FinishLesson(Person p, bool done)
    {
        var q = Get(p.Target);
        int rule = p.TargetB;
        if (q is null || q.Dead || (uint)rule >= (uint)Craft.Rules.Count || !p.Knows(rule) || Cheb(q.Tile(Now), p.Tile(Now)) > 4) return;
        long start = (long)p.PlanHeat * 60;
        long full = Math.Max(1, p.ActUntil - start);
        int pct = done ? 100 : (int)Math.Clamp((Now - start) * 100 / full, 0, 100);
        if (pct < 20) return;
        int und = Understanding(p, q);
        int amount = 20 + Ability(p, PeopleData.Soc) * 2 + p.SkillLevel(PeopleData.Teaching) * 3 + (Ability(q, PeopleData.Int) - 5) * 3
                     + (q.Stage(Now) == LifeStage.Child ? 10 : q.Stage(Now) == LifeStage.Elder ? -6 : 0) + (p.HasTrait(T_eloquent) ? 5 : 0);
        amount = Math.Max(4, amount) * (25 + 75 * und / 100) / 100 * pct / 100;
        Stats.LessonAmount += amount; Stats.LessonUnderstand += und;
        var k = p.Known[p.KnownIndex(rule)];
        bool knewBefore = q.Knows(rule);
        AddLearning(q, Craft.Rules[rule], Math.Max(1, amount), p.Id, Via.Teach, k.Wish, k.Input, k.With);
        UseKnowledge(p, rule);
        GainSkill(p, PeopleData.Teaching, 12);
        p.LastTalkTick = q.LastTalkTick = Now;
        var rp = p.GetRelation(q.Id); rp.Opinion = Math.Min(100, rp.Opinion + 2); rp.Talks++; p.SetRelation(rp);
        var rq = q.GetRelation(p.Id); rq.Opinion = Math.Min(100, rq.Opinion + 3); rq.Favor = Math.Min(100, rq.Favor + 2); rq.Talks++; q.SetRelation(rq);
        AddThought(p, H_taught, q.Id);
        if (!knewBefore && q.Knows(rule)) { AddBio(p, BioKind.Taught, q.Id, rule); Stats.TaughtFull++; }
        ShareClue(p, q, Rng(p.Cell));
    }

    /// <summary>"그건 이미 해 봤는데 안 됐다" — 이야기 · 수업 끝에 아는 단서 하나를 나눈다(상대가 모르는 법의, 상대에게 없는 단서).</summary>
    internal void ShareClue(Person a, Person b, Pcg32 rng)
    {
        for (int pass = 0; pass < 2; pass++)
        {
            var (from, to) = pass == 0 ? (a, b) : (b, a);
            for (int i = from.Clues.Count - 1; i >= 0; i--)
            {
                var c = from.Clues[i];
                if (to.Knows(c.Rule)) continue;
                bool has = false; foreach (var x in to.Clues) if (x.Rule == c.Rule && x.Key == c.Key) { has = true; break; }
                if (has) continue;
                if (rng.NextInt(100) < 50) { AddClue(to, c); Stats.CluesShared++; }
                break;
            }
        }
    }

    // ───────── 그림 ─────────

    /// <summary>그릴 자리: 동굴 안(40타일) → 바위 면(25) → 나무(10). 서서 그릴 타일 · 벽 타일 · 매체. 없으면 false.</summary>
    private bool FindSurface(Person p, ProvinceLife life, int here, out int stand, out int wall, out byte medium)
    {
        stand = wall = -1; medium = 0;
        int hx = here % S, hy = here / S, reg = life.Region[here];
        long bd = long.MaxValue; int bestMed = -1;
        for (int dy = -40; dy <= 40; dy++)
        {
            int y = hy + dy; if ((uint)y >= S) continue;
            for (int dx = -40; dx <= 40; dx++)
            {
                int x = hx + dx; if ((uint)x >= S) continue;
                int t = y * S + x;
                int sh = life.Shelter[t];
                if (sh == 0 || !life.Walk[t] || life.Region[t] != reg) continue;
                int m = sh == 3 ? 2 : sh == 2 ? 1 : 0;
                int lim = m == 2 ? 40 : m == 1 ? 25 : 10;
                int cd = Math.Max(Math.Abs(dx), Math.Abs(dy));
                if (cd > lim || m < bestMed) continue;
                long d = (long)dx * dx + dy * dy;
                if (m == bestMed && d >= bd) continue;
                if (PaintingAt(p.Cell, t) is not null) continue;
                bestMed = m; bd = d; stand = t;
            }
        }
        if (stand < 0) return false;
        medium = (byte)bestMed;
        wall = stand;
        int sx = stand % S, sy = stand / S;
        for (int k = 0; k < 8; k++)
        {
            int nx = sx + DX8[k], ny = sy + DY8[k];
            if ((uint)nx >= S || (uint)ny >= S) continue;
            int n = ny * S + nx;
            bool ok = medium >= 1 ? life.Map.Ground[n] == Ground.Stone : life.Map.ObjectAt(n) is int o && o >= 0 && ObjectCatalog.Default.Objects[o].IsTree;
            if (ok) { wall = n; break; }
        }
        if (medium == 0 && wall == stand && life.Map.ObjectAt(stand) is int so && so >= 0 && ObjectCatalog.Default.Objects[so].IsTree) wall = stand;
        return true;
    }

    public Painting? PaintingAt(int cell, int stand)
    {
        foreach (var (_, g) in Paintings) if (g.Cell == cell && g.Stand == stand && g.Alive(Now)) return g;
        return null;
    }

    /// <summary>그림이 오래가는 해: 동굴 벽 400 · 바위 면 40 · 나무껍질 3(비 · 바람 · 썩음). 조금씩 다르게.</summary>
    private long PaintingLife(byte medium, Pcg32 rng)
    {
        int years = medium == 2 ? 400 : medium == 1 ? 40 : 3;
        return years * GameTime.TicksPerYear * (80 + rng.NextInt(41)) / 100;
    }

    /// <summary>그림을 남긴다(그리기를 해냈다). 무엇을: 남기려던 법 → 아는 법 가운데 무리에서 아는 사람이 가장 적은 것 → 없으면 무늬.</summary>
    internal void MakePainting(Person p, int here, Pcg32 rng)
    {
        var life = Life(p.Cell);
        if (!FindSurface(p, life, here, out int stand, out int wall, out byte med) || Cheb(stand, here) > 2) { stand = here; wall = here; med = (byte)Math.Min(2, life.Shelter[here] == 3 ? 2 : life.Shelter[here] == 2 ? 1 : 0); }
        int rule = p.RecordRule >= 0 && p.Knows(p.RecordRule) ? p.RecordRule : -1;
        if (rule < 0)
        {
            int bestN = int.MaxValue;
            foreach (var k in p.Known)
            {
                if (k.Rule == R_paint?.Index) continue;
                int n = GroupKnowers(p.Cell, k.Rule);
                if (n < bestN) { bestN = n; rule = k.Rule; }
            }
        }
        var g = new Painting { Id = NextPaintingId++, Cell = p.Cell, Tile = wall, Stand = stand, Rule = rule, Painter = p.Id, Medium = med, Tick = Now, Until = Now + PaintingLife(med, rng) };
        Paintings[g.Id] = g;
        p.RecordRule = -1;
        p.LastPaint = Now;
        Stats.Paintings++;
        AddBio(p, BioKind.Painted, g.Id, rule);
        if (rule >= 0)
        {
            UseKnowledge(p, rule);
            History(rule, 5, p.Cell, $"{Hangul.I(DisplayName(p))} {LandLabelOr(p.Cell, "한 땅")}의 {Painting.MediumName(med)}에 그려 남겼다");
        }
    }

    /// <summary>그림을 들여다볼 만한가(점수): 곁(30타일)에 모르는 법을 그린 그림이 있고, 하루에 한 번, 낮에.</summary>
    private int StudyScore(Person p, ProvinceLife life, out Painting? target)
    {
        target = null;
        if (Paintings.Count == 0 || p.Stage(Now) < LifeStage.Child || Now - p.LastStudy < 3 * GameTime.TicksPerDay || life.IsNight(Now)) return int.MinValue;
        int here = p.Tile(Now);
        long bd = 31L * 31;
        foreach (var (_, g) in Paintings)
        {
            if (g.Cell != p.Cell || g.Rule < 0 || !g.Alive(Now) || p.KnowLevel(g.Rule) >= 50) continue;
            long d = ProvinceLife.D2(g.Stand, here % S, here / S);
            if (d >= bd || !life.Paths.Reachable(here, g.Stand)) continue;
            bd = d; target = g;
        }
        if (target is null) { p.LastStudy = Now - GameTime.TicksPerDay / 2; return int.MinValue; }
        var r = Craft.Rules[target.Rule];
        int wish = 0;
        for (int w = 0; w < Craft.Wishes.Count; w++) if (ServesWish(r, Craft.Wishes[w])) wish = Math.Max(wish, p.Wish[w]);
        bool lost = GroupKnows.TryGetValue(GroupKey(p.Cell, target.Rule), out var gk) && !gk.Known;
        return 6 + (p.HasTrait(T_curious) ? 8 : 0) + wish / 3 + (lost ? 10 : 0) + (p.LearnProgress(target.Rule) > 0 ? 6 : 0);
    }

    private bool TryStudy(Person p, Painting g)
    {
        p.LastStudy = Now;
        int here = p.Tile(Now);
        int spot = g.Stand;
        if (spot != here && !WalkTo(p, spot)) return false;
        SetWhy(p, Act.Study, Why.StudyPainting, g.Rule);
        p.Target = g.Id;
        if (spot == here) DoStill(p, GameTime.TicksPerHour);
        return true;
    }

    /// <summary>그림을 다 봤다: 조금 배운다(가르치기보다 훨씬 느림 — 말과 상관없다).</summary>
    internal void FinishStudy(Person p)
    {
        if (!Paintings.TryGetValue(p.Target, out var g) || !g.Alive(Now) || g.Rule < 0) return;
        g.Seen++;
        Stats.Studies++;
        int amount = Math.Max(3, 6 + (Ability(p, PeopleData.Int) - 5) * 2 + (p.HasTrait(T_curious) ? 2 : 0));
        bool knew = p.Knows(g.Rule);
        AddLearning(p, Craft.Rules[g.Rule], amount, g.Painter, Via.Painting, -1, -1, -1);
        if (!knew && p.Knows(g.Rule)) { g.Taught++; Stats.LearnedFromPainting++; }
    }

    // ───────── 남기고 싶은 마음 ─────────

    /// <summary>하루 경계(무리마다): 그림으로 남기고 싶은 마음 — 나이 든 장인 · 무리에 아는 사람이 하나뿐. 이미 그 지방에 그 그림이 있으면 생기지 않는다.</summary>
    private void RecordWishes(int cell, ProvinceLife life, SortedDictionary<int, int> counts)
    {
        if (W_record < 0) return;
        int alive = 0; foreach (var q in life.People) if (!q.Dead) alive++;
        foreach (var q in life.People)
        {
            if (q.Dead || q.Stage(Now) < LifeStage.Adult || q.Known.Count == 0) continue;
            bool old = q.AgeYears(Now) >= 55;
            int pick = -1; Why why = Why.None; int amount = 0;
            foreach (var k in q.Known)
            {
                if (k.Level < 40 || k.Rule == R_paint?.Index) continue;
                int n = counts.GetValueOrDefault(k.Rule);
                if (old && n <= 2 && 8 > amount) { pick = k.Rule; why = Why.RecordOld; amount = 8; }
                else if (n == 1 && alive >= 3 && 4 > amount) { pick = k.Rule; why = Why.RecordOnly; amount = 4; }
            }
            if (pick < 0 || HasPainting(cell, pick)) continue;
            AddWish(q, W_record, amount);
            if (q.RecordRule < 0 || !q.Knows(q.RecordRule)) { q.RecordRule = pick; q.RecordWhy = why; }
        }
    }

    public bool HasPainting(int cell, int rule)
    {
        foreach (var (_, g) in Paintings) if (g.Cell == cell && g.Rule == rule && g.Alive(Now)) return true;
        return false;
    }

    public int PaintingsOf(int rule)
    {
        int n = 0; foreach (var (_, g) in Paintings) if (g.Rule == rule && g.Alive(Now)) n++;
        return n;
    }

    // ───────── 무리 단위 끊김 · 되찾음 ─────────

    /// <summary>
    /// 하루 경계: 무리(지방)마다 아는 법을 센다. 알던 법을 아는 사람이 무리에 없게 되면 끊김(★★), 끊겼던 법을 다시 알게 되면 되찾음(★★, 어느 길로).
    /// 사람이 모두 떠나거나 죽은 지방의 무리는 사라진 것으로 본다(끊김이 아니다). 그림은 바래면 없어진다.
    /// </summary>
    internal void GroupKnowledgeDaily()
    {
        var cellCounts = new SortedDictionary<int, SortedDictionary<int, int>>();
        foreach (var (cell, life) in Provinces)
        {
            SortedDictionary<int, int>? counts = null;
            foreach (var q in life.People)
            {
                if (q.Dead) continue;
                counts ??= new SortedDictionary<int, int>();
                foreach (var k in q.Known) counts[k.Rule] = counts.GetValueOrDefault(k.Rule) + 1;
            }
            if (counts is not null) cellCounts[cell] = counts;
        }
        List<long>? gone = null;
        foreach (var (key, gk) in GroupKnows)
        {
            int cell = GroupCell(key), rule = GroupRule(key);
            if (!cellCounts.TryGetValue(cell, out var counts)) { (gone ??= new()).Add(key); continue; }
            int n = counts.GetValueOrDefault(rule);
            gk.Knowers = n;
            if (n > 0) gk.EmptySince = -1;
            else if (gk.Known && gk.EmptySince < 0) gk.EmptySince = Now;
            if (gk.Known && n == 0 && Now - gk.EmptySince >= LoseAfter)
            {
                gk.Known = false; gk.LostTick = gk.EmptySince; gk.LostCount++; gk.EmptySince = -1;
                Stats.GroupLost++;
                bool painted = HasPainting(cell, rule);
                var who = FirstAlive(cell);
                string text = $"{LandLabelOr(cell, "한 땅")}의 무리에서 {Hangul.I(Craft.ItemName(Craft.Rules[rule].Result))} 끊겼습니다 — 아는 사람이 없습니다" + (painted ? " (그림은 남아 있다)" : "");
                if (who is not null) Notify(WorldEventKind.GroupLost, who, rule, text);
                History(rule, 1, cell, text);
            }
            else if (!gk.Known && n > 0)
            {
                gk.Known = true; gk.RefoundCount++; gk.Knowers = n;
                Stats.GroupRefound++;
                long delay = Now - gk.LostTick;
                Stats.RefoundDelays.Add(delay);
                var (how, person) = RegainHow(cell, rule, gk.LostTick);
                Stats.RefoundHow[(int)how]++;
                string text = $"{LandLabelOr(cell, "한 땅")}의 무리가 {Hangul.Eul(Craft.ItemName(Craft.Rules[rule].Result))} 되찾았습니다 — {RegainName(how, person)} ({delay / (double)GameTime.TicksPerYear:0.#}년 만에)";
                if (person is not null) Notify(WorldEventKind.GroupRefound, person, rule, text);
                History(rule, 2, cell, text);
            }
        }
        if (gone is not null) foreach (long k in gone) GroupKnows.Remove(k);
        foreach (var (cell, counts) in cellCounts)
        {
            foreach (var (rule, n) in counts)
            {
                long key = GroupKey(cell, rule);
                if (!GroupKnows.ContainsKey(key)) GroupKnows[key] = new GroupKnow { Since = Now, Knowers = n };
            }
            // 한 사람만 아는 법(측정)
            foreach (var (_, n) in counts) { Stats.GroupKnownSamples++; if (n == 1) Stats.GroupSoleSamples++; }
            if (Provinces.TryGetValue(cell, out var life)) RecordWishes(cell, life, counts);
        }
        // 바랜 그림
        List<int>? faded = null;
        foreach (var (id, g) in Paintings) if (!g.Alive(Now)) (faded ??= new()).Add(id);
        if (faded is not null)
            foreach (int id in faded)
            {
                var g = Paintings[id];
                Paintings.Remove(id);
                Stats.PaintingsFaded++;
                if (g.Rule >= 0) History(g.Rule, 6, g.Cell, $"{LandLabelOr(g.Cell, "한 땅")}의 {Painting.MediumName(g.Medium)} 그림이 바래 사라졌다");
            }
    }

    /// <summary>되찾은 길: 0 다시 발견 · 1 이웃(다른 무리 사람)에게 배움 · 2 그림 · 3 옮겨 온 사람이 가져옴.</summary>
    public enum RegainPath : byte { Rediscover = 0, Neighbor, Painting, Brought }

    private (RegainPath, Person?) RegainHow(int cell, int rule, long lostTick)
    {
        Person? first = null; Knowledge fk = default;
        foreach (var q in PeopleIn(cell))
        {
            if (q.Dead) continue;
            int i = q.KnownIndex(rule);
            if (i < 0) continue;
            var k = q.Known[i];
            if (first is null || k.Tick < fk.Tick) { first = q; fk = k; }
        }
        if (first is null) return (RegainPath.Rediscover, null);
        if (fk.Tick < lostTick) return (RegainPath.Brought, first);
        return (fk.Via switch { Via.Self => RegainPath.Rediscover, Via.Painting => RegainPath.Painting, _ => RegainPath.Neighbor }, first);
    }

    private string RegainName(RegainPath how, Person? p) => how switch
    {
        RegainPath.Brought => $"아는 {Hangul.I(DisplayName(p))} 옮겨 왔다",
        RegainPath.Neighbor => $"{Hangul.I(DisplayName(p))} 다른 무리 사람에게 배웠다",
        RegainPath.Painting => $"{Hangul.I(DisplayName(p))} 남은 그림을 보고 알아냈다",
        _ => $"{Hangul.I(DisplayName(p))} 다시 알아냈다",
    };

    private Person? FirstAlive(int cell) { foreach (var q in PeopleIn(cell)) if (!q.Dead) return q; return null; }

    /// <summary>(E5) 가르치는 사람의 말풍선: 가르치는 물건의 낱말(그 말로)과 뜻. 낱말이 없으면 null.</summary>
    public (string Word, string Meaning)? TeachWord(Person p)
    {
        if ((uint)p.TargetB >= (uint)Craft.Rules.Count || !Languages.TryGetValue(p.LangId, out var lang)) return null;
        if (ConceptForItem(Craft.Catalog.Items[Craft.Rules[p.TargetB].Result].Id) is not { } cid || Lang.ConceptIndex(cid) is not (int c and >= 0) || !lang.Known.Contains(c)) return null;
        return (WordOf(p, c), Lang.Concepts[c].Meaning);
    }

    internal void History(int rule, byte kind, int cell, string text)
    {
        if (!KnowHistory.TryGetValue(rule, out var list)) KnowHistory[rule] = list = new List<KnowEvent>();
        list.Add(new KnowEvent(Now, kind, cell, text));
        if (list.Count > HistoryKept) list.RemoveAt(1); // 맨 처음(첫 발견)은 남긴다
    }
}
