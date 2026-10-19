using Wtw.Sim.Data;
using Wtw.Sim;
using Wtw.Sim.People;
using Wtw.Sim.Persistence;
using Wtw.Sim.Time;
using Wtw.Sim.World;
using Xunit;

namespace Wtw.Sim.Tests;

/// <summary>E5 지식: 가르치기 · 잊기 · 그림 · 무리 단위 끊김과 되찾음.</summary>
[Trait("Category", "Fast")]
public class KnowledgeTests
{
    private const ulong Seed = 20261016;
    private static readonly WorldConfig Cfg = new() { DotCount = 0 };
    private static CraftData C => CraftData.Default;
    private static int I(string id) => C.ItemIndex(id);

    private static (GameWorld W, int Cell, List<(int X, int Y)> Spots) NewWorld()
    {
        var w = GameWorld.Create(Seed, Cfg);
        int cell = PeopleSamples.Home(w.Planet);
        return (w, cell, PeopleSamples.SpawnSpots(w.People.Life(cell), 8));
    }

    private static Person Spawn(GameWorld w, int cell, (int X, int Y) at)
    {
        w.Apply(new Intervention(w.Now, Intervention.Kinds.SpawnPerson, new long[] { cell, at.X, at.Y }));
        return w.People.All[^1];
    }

    private static void Same(Person p)
    {
        p.Traits.Clear();
        for (int a = 0; a < p.BaseAbility.Length; a++) p.BaseAbility[a] = 5;
        p.BirthTick = -30 * GameTime.TicksPerYear;
    }

    private static int Lesson(PeopleSim ps, GameWorld w, Person teacher, Person learner, CraftRule rule)
    {
        int before = learner.LearnProgress(rule.Index);
        teacher.Target = learner.Id; teacher.TargetB = rule.Index;
        teacher.PlanHeat = (int)(w.Now / 60); teacher.ActUntil = w.Now + GameTime.TicksPerHour;
        ps.FinishLesson(teacher, done: true);
        return (learner.Knows(rule.Index) ? 100 : learner.LearnProgress(rule.Index)) - before;
    }

    [Fact]
    public void TeachingAccumulatesAndIsSlowerAcrossLanguages()
    {
        var (w, cell, spots) = NewWorld();
        var ps = w.People;
        w.Apply(new Intervention(0, Intervention.Kinds.SpawnFamily, new long[] { cell, spots[0].X, spots[0].Y }));
        var teacher = ps.All[0]; var same = ps.All[1];
        var other = Spawn(w, cell, spots[0]);
        Assert.Equal(teacher.LangId, same.LangId);
        Assert.NotEqual(teacher.LangId, other.LangId);
        foreach (var q in new[] { teacher, same, other }) Same(q);
        var rule = C.Rule("cord")!;
        ps.Learn(teacher, rule, 0, I("grass"), -1, -1);
        // 한 번에 다 배우지 않는다 — 조금씩 쌓인다
        int a1 = Lesson(ps, w, teacher, same, rule);
        Assert.InRange(a1, 1, 99);
        Assert.False(same.Knows(rule.Index));
        // 말이 다르면 느리다(막지는 않는다)
        int b1 = Lesson(ps, w, teacher, other, rule);
        Assert.True(b1 > 0 && b1 < a1, $"같은 말 {a1} · 다른 말 {b1} (알아듣기 {ps.Understanding(teacher, other)}%)");
        // 거듭 가르치면 결국 안다 — 누구에게 배웠는지 남는다
        for (int k = 0; k < 10 && !same.Knows(rule.Index); k++) Lesson(ps, w, teacher, same, rule);
        Assert.True(same.Knows(rule.Index));
        var kn = same.Known[same.KnownIndex(rule.Index)];
        Assert.Equal(Via.Teach, kn.Via);
        Assert.Equal(teacher.Id, kn.LearnedFrom);
        Assert.Equal(PeopleSim.StartLevel(Via.Teach), kn.Level);
        Assert.Empty(same.Learning);
        Assert.Contains(ps.Card(same.Id)!.Story, s => s.Contains("배웠다"));
        Assert.True(ps.Stats.Lessons == 0 || ps.Stats.LessonAmount > 0);
    }

    [Fact]
    public void UnusedKnowledgeFadesAndUseRevivesIt()
    {
        var (w, cell, spots) = NewWorld();
        var ps = w.People;
        var p = Spawn(w, cell, spots[0]);
        var rule = C.Rule("cord")!;
        ps.Learn(p, rule, 0, I("grass"), -1, -1);
        int i = p.KnownIndex(rule.Index);
        var k = p.Known[i]; k.LastUse = w.Now - 6 * GameTime.TicksPerYear; p.Known[i] = k;
        int start = k.Level;
        // 날마다(하루 경계와 같은 셈): 안 쓴 지 오래면 흐려진다
        for (int d = 1; d <= 12; d++) { w.AdvanceTo(d * GameTime.TicksPerDay); KeepOld(p, rule.Index, w.Now); }
        int faded = p.KnowLevel(rule.Index);
        Assert.True(faded < start, $"처음 {start} → {faded}");
        // 다시 쓰면 굳는다
        ps.UseKnowledge(p, rule.Index);
        Assert.True(p.KnowLevel(rule.Index) > faded);
        // 아주 흐려지면 잊는다
        i = p.KnownIndex(rule.Index); k = p.Known[i]; k.Level = PeopleSim.ForgetBelow; k.LastUse = w.Now - 6 * GameTime.TicksPerYear; p.Known[i] = k;
        for (int d = 13; d <= 30 && p.Knows(rule.Index); d++) { w.AdvanceTo(d * GameTime.TicksPerDay); KeepOld(p, rule.Index, w.Now); }
        Assert.False(p.Knows(rule.Index));
        Assert.Contains(p.Bio, b => b.Kind == BioKind.Forgot && b.Param == rule.Index);
        // 흐려진 사람도 남에게 배우면 되살아난다
        var q = Spawn(w, cell, spots[0]);
        ps.Learn(q, rule, 0, I("grass"), -1, -1);
        ps.Learn(p, rule, 0, I("grass"), -1, q.Id, Via.Teach);
        i = p.KnownIndex(rule.Index); k = p.Known[i]; k.Level = 30; p.Known[i] = k;
        ps.AddLearning(p, rule, 50, q.Id, Via.Teach, 0, -1, -1);
        Assert.True(p.KnowLevel(rule.Index) >= 50);
        Assert.Contains(p.Bio, b => b.Kind == BioKind.Refreshed);
    }

    /// <summary>사람이 그사이 그 법을 썼으면(시뮬이 돌았으니) 다시 오래된 것으로 돌려 놓는다 — 흐려짐만 본다.</summary>
    private static void KeepOld(Person p, int rule, long now)
    {
        int i = p.KnownIndex(rule);
        if (i < 0) return;
        var k = p.Known[i];
        if (now - k.LastUse < 5 * GameTime.TicksPerYear) { k.LastUse = now - 6 * GameTime.TicksPerYear; p.Known[i] = k; }
    }

    [Fact]
    public void EasyRulesFadeSlowerThanHardOnes()
    {
        // 단계가 적은 법(불씨 · 마른 풀 · 장작 · 끈)은 쉬움, 토기 굽기 · 숯은 보통, 구리는 어려움
        foreach (var id in new[] { "sparks", "dry_grass", "split_wood", "cord" }) Assert.True(PeopleSim.RuleSteps(C.Rule(id)!) <= 2, id);
        foreach (var id in new[] { "pot", "charcoal" }) Assert.Equal(3, PeopleSim.RuleSteps(C.Rule(id)!));
        Assert.True(PeopleSim.RuleSteps(C.Rule("copper")!) >= 4);
        var easy = PeopleSim.FadeOf(C.Rule("dry_grass")!); var normal = PeopleSim.FadeOf(C.Rule("pot")!); var hard = PeopleSim.FadeOf(C.Rule("copper")!);
        Assert.True(easy.After > normal.After && normal.After > hard.After);
        Assert.True(easy.Every > normal.Every && normal.Every > hard.Every);
        // 실제로: 같은 사람이 6년 안 쓴 쉬운 법(구리 날 두드리기 — 2단계)과 어려운 법(구리 녹이기) — 30일 뒤 어려운 법이 더 흐려진다(둘 다 재료가 없어 쓸 수 없다)
        var (w, cell, spots) = NewWorld();
        var ps = w.People;
        var p = Spawn(w, cell, spots[0]);
        p.Traits.Clear(); p.BirthTick = -30 * GameTime.TicksPerYear;
        ps.Learn(p, C.Rule("copper_blade")!, 0, I("copper_lump"), -1, -1);
        ps.Learn(p, C.Rule("copper")!, 0, I("copper_ore"), -1, -1);
        Array.Clear(p.Wish); p.RecordRule = -1; // 세계에서 처음 알아낸 것을 그림으로 남기러 가지 않게(그리면 그 법을 "쓴" 것이 된다)
        foreach (int r in new[] { C.Rule("copper_blade")!.Index, C.Rule("copper")!.Index }) { int i = p.KnownIndex(r); var k = p.Known[i]; k.LastUse = -6 * GameTime.TicksPerYear; p.Known[i] = k; }
        for (int d = 1; d <= 30; d++) { w.AdvanceTo(d * GameTime.TicksPerDay); KeepOld(p, C.Rule("copper_blade")!.Index, w.Now); KeepOld(p, C.Rule("copper")!.Index, w.Now); }
        int lostEasy = 80 - p.KnowLevel(C.Rule("copper_blade")!.Index), lostHard = 80 - p.KnowLevel(C.Rule("copper")!.Index);
        Assert.True(lostHard > lostEasy && lostEasy >= 0, $"쉬운 법 −{lostEasy} · 어려운 법 −{lostHard}");
    }

    [Fact]
    public void WordMeaningIsTheSameEverywhere()
    {
        var (w, cell, spots) = NewWorld();
        var ps = w.People;
        w.Apply(new Intervention(0, Intervention.Kinds.SpawnFamily, new long[] { cell, spots[0].X, spots[0].Y }));
        var a = ps.All[0]; var b = ps.All[1];
        var fire = C.Rule("campfire")!;
        ps.Learn(a, fire, 0, I("twigs"), I("ember"), -1);
        ps.Learn(b, C.Rule("pot")!, 0, I("dry_pot"), -1, -1);
        var snap = new Runtime.WorldSnapshot();
        snap.Fill(w, 1, 0, 0);
        int c = ps.Lang.ConceptIndex("fire");
        string word = ps.WordOf(a, c), meaning = ps.Lang.Concepts[c].Meaning;
        Assert.Equal("불", meaning);
        // [지식의 행방] 창(위 줄 · 펼친 낱말 줄은 같은 값을 쓴다)
        var d = snap.Discoveries.Single(x => x.Rule == fire.Index);
        Assert.Equal(word, d.Word);
        Assert.Equal(meaning, d.WordMeaning);
        // 카드의 새 낱말 · 가르칠 때의 말풍선
        var card = ps.Card(a.Id)!;
        Assert.Contains(card.NewWords, x => x.Word == word && x.Meaning == meaning);
        a.TargetB = fire.Index;
        Assert.Equal((word, meaning), ps.TeachWord(a));
        // 토기도: 낱말마다 뜻이 하나다(다른 뜻과 섞이지 않는다)
        int pc = ps.Lang.ConceptIndex("pot");
        var dp = snap.Discoveries.Single(x => x.Rule == C.Rule("pot")!.Index);
        Assert.Equal(ps.Lang.Concepts[pc].Meaning, dp.WordMeaning);
        Assert.NotEqual(d.WordMeaning, dp.WordMeaning);
    }

    [Fact]
    public void PaintingOutlivesTheLastKnowerAndCanBeRelearned()
    {
        var (w, cell, spots) = NewWorld();
        var ps = w.People;
        var a = Spawn(w, cell, spots[0]);
        var b = Spawn(w, cell, spots[1]);
        var rule = C.Rule("cord")!;
        ps.Learn(a, rule, 0, I("grass"), -1, -1);
        a.RecordRule = rule.Index;
        ps.MakePainting(a, a.Tile(w.Now), new Core.Pcg32(1, 1));
        var g = Assert.Single(ps.Paintings.Values);
        Assert.Equal(rule.Index, g.Rule);
        Assert.True(g.Until > w.Now + GameTime.TicksPerYear, "그림은 적어도 몇 해는 남는다");
        // 마지막으로 아는 사람이 죽는다 → 세계에서 사라짐(그림이 남았다고 알린다)
        ps.Kill(a, DeathCause.Illness);
        ps.CheckLostDiscoveries();
        Assert.True(ps.Discoveries[rule.Index].Lost);
        Assert.Contains(ps.Events, e => e.Kind == WorldEventKind.DiscoveryLost && e.Text.Contains("그림"));
        // 그림을 본 사람은 조금씩 배운다(가르치기보다 훨씬 느리다) — 결국 되찾는다
        int looks = 0;
        for (; looks < 60 && !b.Knows(rule.Index); looks++) { b.Target = g.Id; ps.FinishStudy(b); }
        Assert.True(b.Knows(rule.Index));
        Assert.True(looks >= 4, $"그림은 느리게 가르친다 ({looks}번 만에)");
        Assert.Equal(Via.Painting, b.Known[b.KnownIndex(rule.Index)].Via);
        Assert.False(ps.Discoveries[rule.Index].Lost);
        Assert.Equal(1, g.Taught);
        // 그림은 말과 상관없다: 다른 말을 쓰는 사람도 같은 빠르기로 배운다
        Assert.NotEqual(a.LangId, b.LangId);
    }

    [Fact]
    public void GroupLossAndRecoveryAreRecorded()
    {
        var (w, cell, spots) = NewWorld();
        var ps = w.People;
        var a = Spawn(w, cell, spots[0]);
        var b = Spawn(w, cell, spots[1]);
        var c = Spawn(w, cell, spots[2]);
        var rule = C.Rule("cord")!;
        ps.Learn(a, rule, 0, I("grass"), -1, -1);
        ps.GroupKnowledgeDaily();
        var gk = ps.GroupKnows[PeopleSim.GroupKey(cell, rule.Index)];
        Assert.True(gk.Known);
        Assert.Equal(1, gk.Knowers);
        ps.Kill(a, DeathCause.Illness);
        // 잠깐 없는 것은 끊김이 아니다 — 20일 동안 아무도 모르면 끊김
        ps.GroupKnowledgeDaily();
        Assert.True(gk.Known);
        w.AdvanceTo(PeopleSim.LoseAfter + GameTime.TicksPerDay);
        Assert.False(gk.Known);
        Assert.Equal(1, gk.LostCount);
        Assert.Contains(ps.Events, e => e.Kind == WorldEventKind.GroupLost);
        // 다른 무리 사람(옮겨 온 사람)에게 배워 되찾는다
        var stranger = Spawn(w, cell, spots[3]);
        ps.Learn(stranger, rule, 0, I("grass"), -1, -1); // 아는 사람이 들어왔다
        ps.Learn(b, rule, 0, I("grass"), -1, stranger.Id, Via.Teach);
        ps.GroupKnowledgeDaily();
        Assert.True(gk.Known);
        Assert.Equal(1, gk.RefoundCount);
        Assert.Contains(ps.Events, e => e.Kind == WorldEventKind.GroupRefound);
        Assert.True(ps.KnowHistory[rule.Index].Count >= 3, "처음 · 끊김 · 되찾음이 역사에 남는다");
        _ = c;
    }

    [Fact]
    public void SameSeedSameResultAndSaveLoadKeepsLearningFadingPaintings()
    {
        GameWorld Run(out byte[] bytes)
        {
            var (w, cell, spots) = NewWorld();
            foreach (var s in spots.Take(3)) w.Apply(new Intervention(0, Intervention.Kinds.SpawnFamily, new long[] { cell, s.X, s.Y }));
            var ps = w.People;
            w.AdvanceTo(1);
            // 아는 법(흐려진 것 포함) · 배우는 중 · 그림이 저장에 들어가게
            foreach (var q in ps.All.Where(q => q.Stage(w.Now) >= LifeStage.Adult))
            {
                ps.Learn(q, C.Rule("cord")!, 0, I("grass"), -1, -1);
                ps.Learn(q, C.Rule("flake")!, 1, I("flint"), -1, -1);
            }
            var p0 = ps.All[0];
            var k = p0.Known[0]; k.Level = 40; k.LastUse = -6 * GameTime.TicksPerYear; p0.Known[0] = k;
            ps.AddLearning(ps.All[^1], C.Rule("leaf_cup")!, 30, -1, Via.Watch, 3, -1, -1);
            p0.RecordRule = C.Rule("cord")!.Index;
            ps.MakePainting(p0, p0.Tile(w.Now), new Core.Pcg32(5, 5));
            w.AdvanceTo(GameTime.TicksPerDay * 25 + 777);
            bytes = SaveGame.ToBytes(w);
            w.AdvanceTo(GameTime.TicksPerDay * 40);
            return w;
        }
        var a = Run(out var bytesA);
        var b = Run(out var bytesB);
        Assert.Equal(bytesA, bytesB);
        Assert.Equal(SaveGame.Fingerprint(a), SaveGame.Fingerprint(b));
        Assert.NotEmpty(a.People.Paintings);
        var loaded = SaveGame.FromBytes(bytesA);
        Assert.Equal(bytesA, SaveGame.ToBytes(loaded));
        loaded.AdvanceTo(GameTime.TicksPerDay * 40);
        Assert.Equal(SaveGame.ToBytes(a), SaveGame.ToBytes(loaded));
        Assert.True(a.People.Stats.Lessons > 0, "40일이면 누군가 가르친다");
    }
}
