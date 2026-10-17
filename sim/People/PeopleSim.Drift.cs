using Wtw.Sim.Core;
using Wtw.Sim.Time;

namespace Wtw.Sim.People;

/// <summary>
/// (E3b) 말이 조금씩 달라짐 — 사투리의 시작(기획서 14-1). 한 해에 한 번(세계 하루 경계, 해의 첫날):
///   1) 무리(같은 말을 쓰는 사람들 중 한 지방에 사는 사람들)마다 가끔(해마다 SoundShiftPerMille/1000) 소리 버릇 하나가 한 칸 바뀐다
///      — 무리 모두의 말씨가 같이(유행처럼). 같은 무리 안에서는 이야기하며 다시 닮아 가므로 무리마다 따로 흘러간다.
///   2) 말을 쓰는 무리가 하나뿐이면 무리의 어긋남을 말에 굳힌다(말 전체가 바뀜). 여러 무리로 떨어져 살면,
///      어긋남이 SplitDrift 이상인 무리(어른 · 아이 3명 이상)가 새 말(사투리, Parent = 옛 말)로 갈라진다.
///   3) 말마다 가끔(해마다 WordChangePerMille/1000) 낱말 하나가 새것으로 바뀐다(말마다 정해진 순서).
///   4) 다시 만나 한 지방에서 살면 — 여러 번 이야기한 뒤 적은 쪽이 많은 쪽 말을 쓰게 된다(PeopleSim.Speech 의 Converge) → 다시 닮아 간다.
/// 숫자는 "떨어져 산 지 200년이면 알아듣기 60~85%, 500년이면 40% 아래"(기획서 목표)에 맞게 모형으로 골랐다(tools/bench people driftmodel).
/// 난수는 무리마다 그 지방의 ("people", 지방) 스트림, 낱말 바뀜은 ("language", 말 번호) 스트림.
/// </summary>
public sealed partial class PeopleSim
{
    public const int SoundShiftPerMille = 60, WordChangePerMille = 19, SplitDrift = 5, SplitMinSpeakers = 3;

    private void LanguageYear()
    {
        var comm = new SortedDictionary<(int Lang, int Cell), List<Person>>();
        foreach (var p in _all)
        {
            if (p.Dead || p.LangId < 0 || p.Stage(Now) == LifeStage.Baby) continue;
            if (!comm.TryGetValue((p.LangId, p.Cell), out var list)) comm[(p.LangId, p.Cell)] = list = new List<Person>();
            list.Add(p);
        }
        int n = Lang.SoundCount;
        // 1) 무리마다 소리 버릇이 가끔 한 칸
        foreach (var ((_, cell), list) in comm)
        {
            var rng = Rng(cell);
            if (rng.NextInt(1000) >= SoundShiftPerMille) continue;
            int i = rng.NextInt(n);
            int step = rng.NextInt(2) == 0 ? -1 : 1;
            foreach (var p in list) p.Accent[i] = (sbyte)Math.Clamp(p.Accent[i] + step, -Words.AccentLimit, Words.AccentLimit);
        }
        // 2) 굳히기 · 갈라짐
        var byLang = new SortedDictionary<int, List<(int Cell, List<Person> People)>>();
        foreach (var ((lang, cell), list) in comm)
        {
            if (!byLang.TryGetValue(lang, out var l)) byLang[lang] = l = new List<(int, List<Person>)>();
            l.Add((cell, list));
        }
        var mean = new int[n];
        foreach (var (langId, groups) in byLang)
        {
            if (!Languages.TryGetValue(langId, out var lang)) continue;
            foreach (var (cell, list) in groups)
            {
                int l1 = 0;
                for (int i = 0; i < n; i++)
                {
                    int sum = 0;
                    foreach (var p in list) sum += p.Accent[i];
                    mean[i] = sum / list.Count; // 0 쪽으로 자름(정수)
                    l1 += Math.Abs(mean[i]);
                }
                if (l1 == 0) continue;
                if (groups.Count == 1)
                {
                    for (int i = 0; i < n; i++) lang.Sounds[i] = Math.Max(0, lang.Sounds[i] + mean[i]);
                    lang.Version++;
                    Shift(list, mean);
                }
                else if (l1 >= SplitDrift && list.Count >= SplitMinSpeakers)
                {
                    var child = new Language { Id = NextLanguageId++, Seed = lang.Seed, Parent = lang.Id, Born = Now, Length = lang.Length };
                    child.Sounds = new int[n];
                    for (int i = 0; i < n; i++) child.Sounds[i] = Math.Max(0, lang.Sounds[i] + mean[i]);
                    foreach (int k in lang.Known) child.Known.Add(k);
                    foreach (var (k, v) in lang.Overrides) child.Overrides[k] = v;
                    Languages.Add(child.Id, child);
                    Shift(list, mean);
                    foreach (var p in list) p.LangId = child.Id;
                    foreach (var p in _all) if (!p.Dead && p.Cell == cell && p.LangId == lang.Id && p.Stage(Now) == LifeStage.Baby) p.LangId = child.Id; // 그 무리의 아기도
                    Stats.LanguageSplits++;
                    Notify(WorldEventKind.Language, list[0], -1,
                        $"{LandLabelOr(cell, "한 땅")}에 사는 무리의 말이 조금씩 달라져 {Hangul.Ro(LanguageLabel(child.Id).Split('(')[0])} 갈라졌습니다 — 사투리의 시작(아직은 거의 알아듣는다)");
                }
            }
        }
        // 3) 낱말 하나가 새것으로
        var live = new SortedSet<int>(comm.Keys.Select(k => k.Lang));
        foreach (int id in live)
        {
            if (!Languages.TryGetValue(id, out var lang)) continue;
            var rng = _w.Rng.Get("language", id);
            if (rng.NextInt(1000) < WordChangePerMille) { lang.ReplaceNextWord(BaseConcepts); Stats.WordChanges++; }
        }
    }

    private static void Shift(List<Person> list, int[] mean)
    {
        foreach (var p in list)
            for (int i = 0; i < mean.Length; i++)
                if (mean[i] != 0) p.Accent[i] = (sbyte)Math.Clamp(p.Accent[i] - mean[i], -Words.AccentLimit, Words.AccentLimit);
    }

    private List<ConceptDef>? _baseConcepts;
    private List<ConceptDef> BaseConcepts => _baseConcepts ??= Lang.Concepts.Where(c => c.Base).ToList();

    /// <summary>두 말이 얼마나 통하나(0~100): 말씨 없이 그 말의 낱말끼리.</summary>
    public int LanguageUnderstanding(int a, int b)
    {
        if (a == b) return 100;
        if (!Languages.TryGetValue(a, out var la) || !Languages.TryGetValue(b, out var lb)) return 0;
        int sum = 0, k = 0;
        foreach (var c in BaseConcepts)
        {
            sum += Words.Similarity(Words.Word(la, null, c, Lang), Words.Word(lb, null, c, Lang));
            k++;
        }
        return k == 0 ? 0 : sum / k;
    }

    /// <summary>말의 뿌리(가장 먼 조상).</summary>
    public int RootLanguage(int id)
    {
        int guard = 0;
        while (Languages.TryGetValue(id, out var l) && l.Parent >= 0 && guard++ < 1000) id = l.Parent;
        return id;
    }
}
