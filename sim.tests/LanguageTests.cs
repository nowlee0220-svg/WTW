using Wtw.Sim.People;
using Wtw.Sim.Provinces;
using Wtw.Sim.Time;
using Wtw.Sim.World;
using Xunit;

namespace Wtw.Sim.Tests;

/// <summary>E3 고침: 말(소리 · 낱말)의 뼈대 · 이름 · 조사 · 물 찾기.</summary>
[Trait("Category", "Fast")]
public class LanguageTests
{
    private const ulong Seed = 20261016;
    private static readonly WorldConfig Cfg = new() { DotCount = 0 };

    [Fact]
    public void WordsAreDeterministicAndFiltered()
    {
        var d = LanguageData.Default;
        Assert.True(d.Concepts.Count(c => c.Base) <= 30, "처음 낱말은 아주 적게");
        int words = 0;
        for (int id = 1; id <= 200; id++)
        {
            var a = Language.Create(id, (ulong)id * 7919, 0, d);
            var b = Language.Create(id, (ulong)id * 7919, 0, d);
            foreach (var c in d.Concepts)
            {
                string w = Words.Word(a, null, c, d);
                Assert.Equal(w, Words.Word(b, null, c, d));
                Assert.False(d.IsBanned(w), $"걸러야 할 낱말이 나왔다: {w}");
                for (int i = 1; i < w.Length; i++) Assert.NotEqual(w[i - 1], w[i]);
                words++;
            }
            var name = Words.Name(a, null, new[] { d.Concepts[d.ConceptIndex("red")], d.Concepts[d.ConceptIndex("stone")] }, d);
            Assert.InRange(name.Length, 2, 4);
        }
        Assert.True(words > 1000);
        Assert.Equal(100, Words.Similarity("타무", "타무"));
        Assert.True(Words.Similarity("타무", "타문") > Words.Similarity("타무", "고레"));
    }

    [Fact]
    public void JosaFollowsFinalConsonant()
    {
        Assert.Equal("타문이", Hangul.I("타문"));
        Assert.Equal("타무가", Hangul.I("타무"));
        Assert.Equal("키 큰 여자 (2)가", Hangul.I("키 큰 여자 (2)"));   // 둘 → "이" 받침 없음
        Assert.Equal("키 큰 여자 (3)이", Hangul.I("키 큰 여자 (3)"));   // 셋 → "삼"
        Assert.Equal("'노보'를", Hangul.Eul("'노보'"));
        Assert.Equal("돌과", Hangul.Gwa("돌"));
        Assert.Equal("타무와", Hangul.Gwa("타무"));
        Assert.Equal("돌은", Hangul.Eun("돌"));
    }

    private static (GameWorld W, int Cell, List<(int X, int Y)> Spots) NewWorld(int n)
    {
        var w = GameWorld.Create(Seed, Cfg);
        int cell = PeopleSamples.Home(w.Planet);
        return (w, cell, PeopleSamples.SpawnSpots(w.People.Life(cell), n));
    }

    [Fact]
    public void SpawnedPeopleHaveNoNameAndDistinctDescriptions()
    {
        var (w, cell, spots) = NewWorld(12);
        foreach (var (x, y) in spots) w.Apply(new Intervention(0, Intervention.Kinds.SpawnPerson, new long[] { cell, x, y }));
        var ps = w.People;
        Assert.All(ps.All, p => Assert.Equal("", p.Name));
        var names = ps.All.Select(ps.DisplayName).ToList();
        Assert.Equal(names.Count, names.Distinct().Count()); // 비슷하면 번호를 붙여 서로 다르게
        Assert.All(names, n => Assert.Matches("(남자|여자)", n));
        Assert.Equal(ps.All.Count, ps.All.Select(p => p.LangId).Distinct().Count()); // 뿌린 사람마다 제 말
    }

    [Fact]
    public void TalkingMakesOneSpeechAndNames()
    {
        var (w, cell, spots) = NewWorld(6);
        foreach (var (x, y) in spots) w.Apply(new Intervention(0, Intervention.Kinds.SpawnPerson, new long[] { cell, x, y }));
        var ps = w.People;
        int before = ps.Understanding(ps.All[0], ps.All[1]);
        w.AdvanceTo(GameTime.TicksPerDay * 30);
        var alive = ps.All.Where(p => !p.Dead).ToList();
        int langs = alive.Select(p => p.LangId).Distinct().Count();
        Assert.True(langs <= 2, $"30일 뒤에도 말이 {langs}개");
        var big = alive.GroupBy(p => p.LangId).OrderByDescending(g => g.Count()).First().ToList();
        Assert.True(big.Count >= 3);
        Assert.All(big, p => Assert.NotEqual("", p.Name));                      // 같은 무리에서 이름을 얻었다
        Assert.All(big, p => Assert.Contains(p.Bio, b => b.Kind == BioKind.Named));
        Assert.All(big, p => Assert.False(string.IsNullOrEmpty(ps.NameMeaning(p))));
        int after = ps.Understanding(big[0], big[1]);
        Assert.True(before < 40 && after > 85, $"알아듣기 {before}% → {after}%");
    }

    [Fact]
    public void FamilyShareSpeechAndChildrenGetMeaningfulNames()
    {
        var (w, cell, spots) = NewWorld(2);
        w.Apply(new Intervention(0, Intervention.Kinds.SpawnFamily, new long[] { cell, spots[0].X, spots[0].Y }));
        var ps = w.People;
        Assert.Single(ps.All.Select(p => p.LangId).Distinct());
        Assert.All(ps.All, p => Assert.NotEqual("", p.Name));
        var mom = ps.All.First(p => p.Female && p.Stage(w.Now) >= LifeStage.Adult);
        var dad = ps.All.First(p => p.Id == mom.Partner);
        var baby = ps.Make(cell, mom.Tile(w.Now), 0, Sex.Male, mom, dad, ps.Rng(cell), newborn: true);
        Assert.Equal(mom.LangId, baby.LangId);
        Assert.Equal("", baby.Name); // Make 자체는 이름을 짓지 않는다 — 태어남(GiveBirth)에서 부모가 짓는다
        w.AdvanceTo(GameTime.TicksPerDay * 2);
        var card = ps.Card(mom.Id)!;
        Assert.False(card.Unnamed);
        Assert.False(string.IsNullOrEmpty(card.NameMeaning));
        Assert.NotEmpty(card.Vocabulary);
        Assert.All(card.Vocabulary, v => Assert.False(string.IsNullOrEmpty(v.Meaning)));
    }

    [Fact]
    public void WithoutKnownWaterPeopleSearchDownhillAndRemember()
    {
        var w = GameWorld.Create(Seed, Cfg);
        int cell = PeopleSamples.Home(w.Planet);
        var life = w.People.Life(cell);
        // 물에서 가장 먼(보이지 않는) 걸을 수 있는 타일
        int far = -1; long bd = -1;
        for (int t = 0; t < HexFrame.TileCount; t++)
        {
            if (!life.Walk[t] || HexFrame.BorderDistance(t) < 8 || !life.HasDrinkFrom(t)) continue;
            int d = life.NearestDrink(t % 150, t / 150, PeopleSim.SightTiles + 2);
            if (d >= 0) continue;
            long c = ProvinceLife.D2(t, 75, 75);
            if (bd < 0 || c < bd) { bd = c; far = t; }
        }
        Assert.True(far >= 0);
        w.Apply(new Intervention(0, Intervention.Kinds.SpawnPerson, new long[] { cell, far % 150, far / 150 }));
        var p = w.People.All[0];
        p.Water = 20_000; p.Food = 90_000;
        bool searched = false, found = false;
        for (long t = 100; t < GameTime.TicksPerDay * 2 && !found; t += 200)
        {
            w.AdvanceTo(t);
            if (p.Act == Act.Drink && p.Walking && p.How == 3)
            {
                searched = true;
                Assert.Contains("찾아", w.People.ActivityText(p));
                Assert.Contains("찾아다닌다", w.People.WhyText(p));
            }
            if (p.KnownWater.Count > 0) found = true;
        }
        Assert.True(searched, "아는 물가가 없으면 찾아다녀야 한다");
        Assert.True(found, "찾아다니다 물가를 보면 기억해야 한다");
        Assert.False(p.Dead);
    }
}
