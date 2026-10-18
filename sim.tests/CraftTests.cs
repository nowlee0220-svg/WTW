using Wtw.Sim.Data;
using Wtw.Sim.People;
using Wtw.Sim.Persistence;
using Wtw.Sim.Provinces;
using Wtw.Sim.Time;
using Wtw.Sim.World;
using Xunit;

namespace Wtw.Sim.Tests;

/// <summary>E4 재료와 발견.</summary>
[Trait("Category", "Fast")]
public class CraftTests
{
    private const ulong Seed = 20261016;
    private static readonly WorldConfig Cfg = new() { DotCount = 0 };
    private static CraftData C => CraftData.Default;
    private static int I(string id) => C.ItemIndex(id);
    private static int A(string id) => C.ActionIndex(id);
    private static int[] P(string item) => (int[])C.ItemProps[I(item)].Clone();

    private static (GameWorld W, int Cell, List<(int X, int Y)> Spots) NewWorld(int spots = 8, string where = "home")
    {
        var w = GameWorld.Create(Seed, Cfg);
        int cell = where == "cold" ? PeopleSamples.Cold(w.Planet) : where == "cool" ? PeopleSamples.Cool(w.Planet) : PeopleSamples.Home(w.Planet);
        return (w, cell, PeopleSamples.SpawnSpots(w.People.Life(cell), spots));
    }

    private static Person Spawn(GameWorld w, int cell, (int X, int Y) at)
    {
        w.Apply(new Intervention(w.Now, Intervention.Kinds.SpawnPerson, new long[] { cell, at.X, at.Y }));
        return w.People.All[^1];
    }

    [Fact]
    public void DataIsComplete()
    {
        Assert.InRange(C.Catalog.Items.Count(i => i.Kind is "made" or "place"), 30, 40);
        Assert.True(C.Properties.Count >= 22);
        foreach (var id in new[] { "strike", "break", "grind", "cut", "tear", "twist", "weave", "shape", "dry", "roast", "burn", "melt", "mix", "soak", "boil", "bury" })
            Assert.True(A(id) >= 0, id);
        // 돌의 성질 메모(E2)가 숫자로 옮겨졌다: 흑요석 · 부싯돌은 날이 잘 서고 흔한 돌은 아니다
        Assert.Equal(5, C.Prop(I("obsidian"), "sharpens"));
        Assert.True(C.Prop(I("stone"), "sharpens") <= 1);
    }

    [Fact]
    public void JudgmentComesFromPropertiesNotATable()
    {
        int strike = A("strike");
        // 부싯돌을 단단한 돌로 친다 → 깬 돌 날
        var ok = C.Judge(strike, P("flint"), null, P("stone"), 0, 0, 0);
        Assert.True(ok.Success);
        Assert.Equal(I("flake"), ok.Rule!.Result);
        // 같은 행동이라도 속성이 다르면 결과가 다르다: 부싯돌의 "날이 선다"를 낮추면 부분 성공(깨진 돌)
        var dull = P("flint");
        dull[C.P_sharpens] = 1;
        var part = C.Judge(strike, dull, null, P("stone"), 0, 0, 0);
        Assert.False(part.Success);
        Assert.True(part.IsPartial);
        Assert.Equal(I("broken_stone"), part.Partial!.Result);
        // 흔한 돌의 속성을 부싯돌처럼 바꾸면 흔한 돌도 날이 선다(아이템 이름이 아니라 속성으로 판정)
        var stoneLikeFlint = P("stone");
        stoneLikeFlint[C.P_splits] = 4; stoneLikeFlint[C.P_sharpens] = 5;
        Assert.Equal(I("flake"), C.Judge(strike, stoneLikeFlint, null, P("stone"), 0, 0, 0).Rule!.Result);
        // 도구가 없으면 "손으로는" 부분 성공
        var noTool = C.Judge(strike, P("flint"), null, null, 0, 0, 0);
        Assert.Equal("tool", noTool.FailedKey);
        // 실이 되는 것을 꼬면 끈, 실이 안 되는 돌은 아무 일도 없다
        Assert.Equal(I("cord"), C.Judge(A("twist"), P("grass"), null, null, 0, 0, 0).Rule!.Result);
        Assert.Null(C.Judge(A("twist"), P("stone"), null, null, 0, 0, 0).Rule);
    }

    [Fact]
    public void CopperNeedsChargedFire()
    {
        int melt = A("melt");
        foreach (int heat in new[] { 0, 1, 2 })
        {
            var o = C.Judge(melt, P("copper_ore"), null, null, heat, 0, 0);
            Assert.False(o.Success);
            Assert.Equal("heat", o.FailedKey);
            Assert.Contains("녹지 않았다", o.Partial!.Text);
        }
        var good = C.Judge(melt, P("copper_ore"), null, null, 3, 0, 0);
        Assert.True(good.Success);
        Assert.Equal(I("copper_lump"), good.Rule!.Result);
        // 굽기: 덜 마른 그릇은 금이 간다, 마른 그릇은 토기가 된다
        Assert.Equal(I("cracked_pot"), C.Judge(A("roast"), P("wet_pot"), null, null, 1, 0, 0).Partial!.Result);
        Assert.Equal(I("pot"), C.Judge(A("roast"), P("dry_pot"), null, null, 1, 0, 0).Rule!.Result);
    }

    [Fact]
    public void PartialSuccessLeavesClueAndNextExperimentFollowsIt()
    {
        var (w, cell, spots) = NewWorld();
        var ps = w.People;
        var p = Spawn(w, cell, spots[0]);
        var life = ps.Life(cell);
        int here = p.Tile(w.Now);
        life.LightFire(here, 1, 10 * GameTime.TicksPerHour, w.Now, -1);
        // 구리 광석을 맨불에 녹여 본다 → 부분 성공(불이 모자람) → 단서
        p.Carry.Add((I("copper_ore"), 1));
        p.PlanAction = A("melt"); p.PlanInput = I("copper_ore"); p.PlanWith = p.PlanTool = p.PlanRule = -1;
        p.PlanWish = C.Wishes.First(x => x.Id == "shiny").Index;
        ps.FinishWork(p);
        Assert.True(p.Clues.Count == 1, $"단서 {p.Clues.Count}개 · 불 세기 {life.HeatAt(p.Tile(w.Now), w.Now)} · 들고 있는 것 {p.Carry.Count} · 실험 {ps.Stats.Experiments} · 부분 {ps.Stats.Partials}");
        var clue = p.Clues[0];
        Assert.Equal("heat", clue.Key);
        Assert.Equal(C.Rule("copper")!.Index, clue.Rule);
        Assert.True(ps.Partials.ContainsKey(clue.Rule), "발견 전의 부분 성공이 세계에 남는다");
        // 다음 실험은 그 단서를 따라간다: 불을 더 세게 — 무거운 돌이 있으면 모닥불을 돌로 둘러 불을 가둔다
        p.Carry.Clear();
        p.Carry.Add((I("stone"), 4));
        p.BaseAbility[PeopleData.Str] = 10;
        p.Wish[clue.Rule >= 0 ? C.Wishes.First(x => x.Id == "shiny").Index : 0] = 80;
        bool followed = false;
        var seen = new List<string>();
        for (int k = 0; k < 40 && !followed; k++)
        {
            var rng = new Core.Pcg32((ulong)k, 3);
            bool ok = ps.Plan(p, C.Wishes.First(x => x.Id == "shiny").Index, known: false, rng);
            seen.Add($"{ok}:{p.Act}:{p.PlanAction}:{p.Why}:{p.Target}:fires={life.Fires.Count}:n={life.NearestFire(p.Tile(w.Now) % 150, p.Tile(w.Now) / 150, w.Now, 60)}");
            if (ok && p.PlanAction == A("pile")) followed = true;
        }
        Assert.True(followed, "단서(불이 모자람)를 따라 불을 가두는 실험을 해야 한다 — " + string.Join(" ", seen.Take(10)));
    }

    [Fact]
    public void FireKeepsColdNightsWarmAndCookingFeedsMore()
    {
        var (w, cell, spots) = NewWorld(4, "cold");
        var life = w.People.Life(cell);
        int t = spots[0].Y * HexFrame.Size + spots[0].X;
        long night = w.Now + (long)((24 - life.LocalHour(w.Now) + 2) % 24) * GameTime.TicksPerHour;
        int cold = life.FeltTemp10(night, t, false);
        life.LightFire(t + 1, 2, 30 * GameTime.TicksPerHour, w.Now, -1);
        int warm = life.FeltTemp10(night, t, false);
        Assert.True(warm >= cold + 80, $"불 곁 {warm} · 없을 때 {cold} · 불 {life.Fires.Count} · 켜짐 {life.Fires.First().Value.Lit(night)} · 그늘 {life.Shelter[t]}");
        // 툰드라의 밤: 불이 있으면 둘째 날을 넘긴다
        var (a, ca, sa) = NewWorld(4, "cold");
        var (b, _, _) = NewWorld(4, "cold");
        foreach (var (wd, withFire) in new[] { (a, false), (b, true) })
        {
            foreach (var s in sa.Take(3)) Spawn(wd, ca, s);
            if (withFire)
                foreach (var s in sa.Take(3))
                    wd.People.Life(ca).LightFire(s.Y * HexFrame.Size + s.X + 1, 2, 200 * GameTime.TicksPerHour, wd.Now, -1);
            wd.AdvanceTo(GameTime.TicksPerDay * 2);
        }
        var lc = a.People.Life(ca);
        Assert.True(b.People.AliveCount > 0, $"평균 {lc.Mean10} · 하루 폭 {lc.Range10} · 새벽 바깥 {lc.OutsideTemp10(GameTime.TicksPerDay + 6 * GameTime.TicksPerHour - lc.OffsetTicks)} · 불 곁 새벽 {b.People.Life(ca).FeltTemp10(GameTime.TicksPerDay + 6 * GameTime.TicksPerHour - lc.OffsetTicks, sa[0].Y * 150 + sa[0].X, false)} · 죽음 {string.Join(",", b.People.All.Select(q => q.Cause))} · 저체온 {string.Join(",", b.People.All.Select(q => q.Hypothermia / 1000))}");
        Assert.True(b.People.AliveCount > a.People.AliveCount || b.People.All.Sum(q => q.Hypothermia) < a.People.All.Sum(q => q.Hypothermia),
            $"불 있음 {b.People.AliveCount} · 없음 {a.People.AliveCount}");
        // 익히면 영양이 오른다(속성): 익힌 열매 > 열매 · 익힌 버섯은 독이 없다
        Assert.True(C.Prop(I("cooked_fruit"), C.P_edible) > C.Prop(I("berries"), C.P_edible));
        Assert.Equal(0, C.Prop(I("roasted_mushroom"), C.P_poison));
        Assert.True(C.Prop(I("mushroom"), C.P_poison) > 0);
    }

    [Fact]
    public void StoneToolMinesRockAndRevealsHiddenOre()
    {
        var w = GameWorld.Create(Seed, Cfg);
        var ps = w.People;
        foreach (int cell in PeopleSamples.Ranked(w.Planet, (pl, c) => pl.Terrain[c] is Planet.Terrain.Hill or Planet.Terrain.Plateau ? pl.Resource(c, Planet.ResourceKinds.Copper) : int.MinValue).Take(40))
        {
            var life = ps.Life(cell);
            var m = life.Map;
            // 드러난 바위 옆, 그 바위 너머에 숨은 바위가 있는 자리
            int target = -1;
            for (int i = 0; i < HexFrame.TileCount && target < 0; i++)
            {
                if (m.Ground[i] != Ground.Stone || m.IsHidden(i)) continue;
                int x = i % HexFrame.Size, y = i / HexFrame.Size;
                bool walkNear = false, hiddenNear = false;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if ((uint)nx >= HexFrame.Size || (uint)ny >= HexFrame.Size) continue;
                        int j = ny * HexFrame.Size + nx;
                        if (life.Walk[j]) walkNear = true;
                        if (life.IsHidden(j)) hiddenNear = true;
                    }
                if (walkNear && hiddenNear) target = i;
            }
            if (target < 0) continue;
            // 맨손으로는 캐지 않는다(돌 도구가 없으면 TryMine 이 안 됨)
            var p = ps.Make(cell, life.NearestStone(target % 150, target / 150, 5, false) >= 0 ? NearWalk(life, target) : NearWalk(life, target), 25, Sex.Male, null, null, ps.Rng(cell));
            Assert.False(ps.HasHardTool(p));
            p.Carry.Add((I("flake"), 1));
            Assert.True(ps.HasHardTool(p));
            int hiddenBefore = Enumerable.Range(0, HexFrame.TileCount).Count(life.IsHidden);
            Assert.True(life.Mine(target), $"캐기 실패 {target} {m.Ground[target]} dug={life.Dug.Contains(target)}");
            Assert.True(life.Walk[target], "캔 자리는 걸을 수 있다");
            Assert.False(life.Mine(target));
            life.RevealAround(target, 2);
            Assert.True(Enumerable.Range(0, HexFrame.TileCount).Count(life.IsHidden) < hiddenBefore, "캐면 숨은 바위 속이 드러난다");
            return;
        }
        Assert.Fail("캘 바위가 있는 지방을 찾지 못했다");
    }

    private static int NearWalk(ProvinceLife life, int t)
    {
        int x = t % 150, y = t / 150;
        for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) { int j = (y + dy) * 150 + x + dx; if ((uint)j < HexFrame.TileCount && life.Walk[j]) return j; }
        return t;
    }

    [Fact]
    public void DiscoveryIsLostWhenAllKnowersDie_WithNewWordBorrowingAndNickname()
    {
        var (w, cell, spots) = NewWorld();
        var ps = w.People;
        var a = Spawn(w, cell, spots[0]);
        var b = Spawn(w, cell, spots[1]); // 다른 말을 쓰는 사람(뿌린 사람마다 말이 다르다)
        Assert.NotEqual(a.LangId, b.LangId);
        var rule = C.Rule("campfire")!;
        ps.Learn(a, rule, 0, I("twigs"), I("ember"), -1);
        var d = ps.Discoveries[rule.Index];
        Assert.Equal(a.Id, d.Person);
        Assert.Contains(ps.Events, e => e.Kind == WorldEventKind.Discovery && e.Text.Contains("세계에서 처음"));
        // 새 낱말: 발견한 무리의 말에 "불"이 생긴다(지어냄)
        int fire = ps.Lang.ConceptIndex("fire");
        var la = ps.Languages[a.LangId];
        Assert.Contains(fire, la.Known);
        Assert.Equal(-1, la.Origins[fire].FromLang);
        // 별명: 처음 불을 피운 사람 — 그 무리의 말로, 뜻과 함께(조사는 받침에 맞게)
        Assert.NotEmpty(a.Nickname);
        Assert.Contains(ps.Lang.ConceptIndex("maker"), a.NicknameConcepts);
        Assert.Contains(a.Nickname, ps.Card(a.Id)!.Nickname);
        string line = ps.Card(a.Id)!.Story.Last(s => s.Contains("불리게"));
        Assert.Contains(Hangul.Rago($"'{a.Nickname}'"), line);
        // 빌려 오기: 다른 말을 쓰는 b 가 a 에게서 보고 배우면 낱말도 따라온다 — 출처가 남고 소리가 같다
        ps.Learn(b, rule, 0, I("twigs"), I("ember"), a.Id);
        var lb = ps.Languages[b.LangId];
        Assert.Equal(a.LangId, lb.Origins[fire].FromLang);
        Assert.True(ps.WordOf(a, fire).Length > 0);
        Assert.Equal(Words.Word(la, null, ps.Lang.Concepts[fire], ps.Lang), Words.Word(lb, null, ps.Lang.Concepts[fire], ps.Lang));
        Assert.Equal(2, ps.Knowers(rule.Index));
        // 아는 사람이 다 죽으면 사라진다
        ps.Kill(a, DeathCause.Illness);
        ps.CheckLostDiscoveries();
        Assert.False(d.Lost);
        ps.Kill(b, DeathCause.Illness);
        ps.CheckLostDiscoveries();
        Assert.True(d.Lost);
        Assert.Contains(ps.Events, e => e.Kind == WorldEventKind.DiscoveryLost);
        Assert.Equal(1, ps.Stats.Lost);
    }

    [Fact]
    public void SameSeedSameExperimentsAndSaveLoadContinues()
    {
        GameWorld Run(out long mid, out byte[] bytes)
        {
            var (w, cell, spots) = NewWorld();
            foreach (var s in spots.Take(3)) w.Apply(new Intervention(0, Intervention.Kinds.SpawnFamily, new long[] { cell, s.X, s.Y }));
            var life = w.People.Life(cell);
            // 불 · 땅의 물건 · 캔 바위 · 아는 것 · 바람이 모두 저장에 들어가게
            life.LightFire(spots[4].Y * 150 + spots[4].X, 1, 6 * GameTime.TicksPerHour, 0, -1);
            life.Drop(spots[5].Y * 150 + spots[5].X, I("flake"), 2);
            w.People.Learn(w.People.All[0], C.Rule("flake")!, 1, I("flint"), -1, -1);
            w.People.All[1].Wish[0] = 60;
            w.People.All[1].Carry.Add((I("ember"), 1)); w.People.All[1].EmberTick = 0;
            mid = GameTime.TicksPerDay * 20 + 777;
            w.AdvanceTo(mid);
            bytes = SaveGame.ToBytes(w);
            w.AdvanceTo(GameTime.TicksPerDay * 30);
            return w;
        }
        var a = Run(out long mid, out var bytesA);
        var b = Run(out _, out var bytesB);
        Assert.Equal(bytesA, bytesB);
        Assert.True(a.People.Stats.Experiments + a.People.Stats.Crafts > 0, "실험이 일어나야 한다");
        Assert.Equal(SaveGame.Fingerprint(a), SaveGame.Fingerprint(b));
        var loaded = SaveGame.FromBytes(bytesA);
        Assert.Equal(bytesA, SaveGame.ToBytes(loaded));
        loaded.AdvanceTo(GameTime.TicksPerDay * 30);
        Assert.Equal(SaveGame.ToBytes(a), SaveGame.ToBytes(loaded));
        _ = mid;
    }

    [Fact]
    public void DeadLeaveTheirThingsWhereTheyFell()
    {
        var (w, cell, spots) = NewWorld();
        var p = Spawn(w, cell, spots[0]);
        p.Carry.Add((I("stone_knife"), 1));
        int t = p.Tile(w.Now);
        w.People.Kill(p, DeathCause.Illness);
        var life = w.People.Life(cell);
        Assert.True(life.Items.TryGetValue(t, out var list) && list.Exists(c => c.Item == I("stone_knife")));
        Assert.Empty(p.Carry);
    }
}
