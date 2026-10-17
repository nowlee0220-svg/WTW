using Wtw.Sim.Core;
using Wtw.Sim.Data;
using Wtw.Sim.Provinces;
using Wtw.Sim.Time;
using Wtw.Sim.World;

namespace Wtw.Sim.People;

/// <summary>알림(아이가 태어남 · 죽음 · 짝 · 정신 붕괴 …). Seq 는 1 부터 차례로.</summary>
public sealed record WorldEvent(long Seq, long Tick, WorldEventKind Kind, int Person, int Other, int Cell, string Text);

public enum WorldEventKind : byte { Spawn = 0, Birth, Death, Pair, Break, LandNamed, Moved, Language }

/// <summary>측정용 셈(저장하지 않는다 — 결과에 영향 없음).</summary>
public sealed class PeopleStats
{
    public long Decisions, Events, Paths, PathFails, Talks, Arguments, Pairs, Births, Breaks, Conceptions, Illnesses;
    public readonly long[] Deaths = new long[8];
    public long GatherXpPassion, GatherXpPlain, GatherActsPassion, GatherActsPlain;
    /// <summary>목마름으로 죽었는데 그 사람이 닿을 수 있는 곳(지방 안 이어진 땅)에 민물이 없었다 · 굶어 죽었는데 먹을 것이 없었다.</summary>
    public long NoWaterDeaths, NoFoodDeaths;
    /// <summary>(E3b) 경계를 넘은 횟수 · 잠든 지방이 깨어난 횟수 · 옮겨 간 까닭별 횟수(Why 값).</summary>
    public long Crossings, Wakes;
    /// <summary>(E3b) 말이 갈라진 횟수 · 낱말이 새로 바뀐 횟수.</summary>
    public long LanguageSplits, WordChanges;
    public readonly long[] MoveWhy = new long[64];
    /// <summary>물 · 먹을 것을 찾아간 방법별 횟수: [0 보임, 1 기억, 2 따라감, 3 찾아다님].</summary>
    public readonly long[] FindHow = new long[4];
}

/// <summary>
/// 사람들(기획서 6-1 · 7절). 예약 방식: 사람은 "언제까지 무엇을 한다"를 정하고 다음 판단 시각을 예약한다. 조용한 사람은 비용 0.
/// 급한 일(심한 배고픔 · 목마름 · 추위 · 지침)이 그 사이에 닥칠 시각을 미리 셈해 그 시각으로 예약을 당긴다. 누가 말을 걸면 예약을 끊는다.
/// 난수는 ("people", 지방) 스트림에서만, 사람은 번호 순 목록으로만 돈다.
/// </summary>
public sealed partial class PeopleSim
{
    public const string Stream = "people";
    public static readonly string[] RequiredThoughts =
    {
        "hungry", "starving", "thirsty", "cold", "freezing", "hot", "tired", "exhausted", "lonely", "bored", "sick", "ate_well", "slept_well", "slept_rough",
        "slept_cold", "dark_fear", "talked", "good_talk", "argument", "with_partner", "new_partner", "child_born", "family_died", "partner_died", "friend_died",
        "cave_found", "catharsis", "passion_work", "lazy_rest", "alone_long",
    };
    public static readonly string[] RequiredTraits =
    {
        "industrious", "lazy", "tough", "frail", "handy", "clumsy", "curious", "stubborn", "eloquent", "blunt", "brave", "coward", "bloodlust", "kind",
        "abrasive", "ambitious", "optimist", "pessimist", "iron_will", "nervous", "fast_walker", "cannibal",
    };

    // 욕구 셈(시간당, 100,000 = 100%)
    public const int FoodAwake = 5_000, FoodAsleep = 2_000, WaterAwake = 6_000, WaterAsleep = 1_500;
    public const int RestAwake = 5_500, RestAsleep = 12_500, JoyAwake = 3_000, SocialAwake = 2_500;
    public const int UrgentFood = 15_000, UrgentWater = 15_000, UrgentRest = 3_000, UrgentCold = 25_000;
    public const long PregnancyTicks = 30 * GameTime.TicksPerDay;

    private readonly GameWorld _w;
    public PeopleData Data { get; }
    public readonly SortedDictionary<int, ProvinceLife> Provinces = new();
    private readonly List<Person> _all = new();
    private readonly Dictionary<int, Person> _byId = new(); // 조회 전용
    public readonly List<WorldEvent> Events = new();
    public long NextEventSeq { get; internal set; } = 1;
    public const int EventsKept = 200;
    public PeopleStats Stats { get; } = new();
    private readonly List<(int X, int Y)> _path = new();

    // 자주 쓰는 번호
    private readonly int T_industrious, T_lazy, T_tough, T_frail, T_handy, T_clumsy, T_curious, T_stubborn, T_eloquent, T_blunt, T_brave, T_coward,
        T_bloodlust, T_kind, T_abrasive, T_optimist, T_pessimist, T_iron, T_nervous, T_fast, T_cannibal;
    internal readonly int H_hungry, H_starving, H_thirsty, H_cold, H_freezing, H_hot, H_tired, H_exhausted, H_lonely, H_bored, H_sick, H_ate, H_slept, H_rough,
        H_sleptCold, H_dark, H_talked, H_goodTalk, H_argument, H_withPartner, H_newPartner, H_childBorn, H_familyDied, H_partnerDied, H_friendDied, H_cave,
        H_catharsis, H_passion, H_lazyRest, H_alone;

    public PeopleSim(GameWorld w, PeopleData data)
    {
        _w = w; Data = data;
        int T(string id) => data.TraitIndex(id);
        int H(string id) => data.ThoughtIndex(id);
        T_industrious = T("industrious"); T_lazy = T("lazy"); T_tough = T("tough"); T_frail = T("frail"); T_handy = T("handy"); T_clumsy = T("clumsy");
        T_curious = T("curious"); T_stubborn = T("stubborn"); T_eloquent = T("eloquent"); T_blunt = T("blunt"); T_brave = T("brave"); T_coward = T("coward");
        T_bloodlust = T("bloodlust"); T_kind = T("kind"); T_abrasive = T("abrasive"); T_optimist = T("optimist"); T_pessimist = T("pessimist");
        T_iron = T("iron_will"); T_nervous = T("nervous"); T_fast = T("fast_walker"); T_cannibal = T("cannibal");
        H_hungry = H("hungry"); H_starving = H("starving"); H_thirsty = H("thirsty"); H_cold = H("cold"); H_freezing = H("freezing"); H_hot = H("hot");
        H_tired = H("tired"); H_exhausted = H("exhausted"); H_lonely = H("lonely"); H_bored = H("bored"); H_sick = H("sick"); H_ate = H("ate_well");
        H_slept = H("slept_well"); H_rough = H("slept_rough"); H_sleptCold = H("slept_cold"); H_dark = H("dark_fear"); H_talked = H("talked");
        H_goodTalk = H("good_talk"); H_argument = H("argument"); H_withPartner = H("with_partner"); H_newPartner = H("new_partner"); H_childBorn = H("child_born");
        H_familyDied = H("family_died"); H_partnerDied = H("partner_died"); H_friendDied = H("friend_died"); H_cave = H("cave_found"); H_catharsis = H("catharsis");
        H_passion = H("passion_work"); H_lazyRest = H("lazy_rest"); H_alone = H("alone_long");
    }

    public IReadOnlyList<Person> All => _all;
    public Person? Get(int id) => _byId.TryGetValue(id, out var p) ? p : null;
    public int AliveCount { get; private set; }
    private long Now => _w.Now;

    private ProvinceLife? _lastLife;

    public ProvinceLife Life(int cell)
    {
        if (_lastLife is not null && _lastLife.Cell == cell) return _lastLife;
        if (!Provinces.TryGetValue(cell, out var life))
        {
            life = new ProvinceLife(_w.Planet, _w.Province(cell), Data);
            Provinces.Add(cell, life);
            if (Dormant.Remove(cell, out var d))
            {
                // 깨어남: 바뀐 것을 되살린다. 딴 몫은 딴 시각부터 흐른 시간만큼 저절로 다시 열려 있다.
                foreach (var (tile, st) in d.Stocks) life.Stocks[tile] = st;
                foreach (int t in d.Revealed) life.Revealed.Add(t);
                life.People.AddRange(d.People);
                Stats.Wakes++;
            }
        }
        return _lastLife = life;
    }

    internal Pcg32 Rng(int cell) => _w.Rng.Get(Stream, cell);

    internal void AddLoaded(Person p)
    {
        _all.Add(p);
        _byId.Add(p.Id, p);
        if (!p.Dead) AliveCount++;
        if (!p.Dead || Provinces.ContainsKey(p.Cell)) Life(p.Cell).People.Add(p);
        else if (Dormant.TryGetValue(p.Cell, out var d)) d.People.Add(p);
        else Dormant[p.Cell] = new DormantProvince(p.Cell, Array.Empty<(int, TileStock)>(), Array.Empty<int>(), new List<Person> { p });
    }

    // ───────── 능력 ─────────

    public int Ability(Person p, int a)
    {
        int v = p.BaseAbility[a];
        foreach (int t in p.Traits) v += Data.Traits[t].Abilities[a];
        var st = p.Stage(Now);
        if (st == LifeStage.Child) v -= a == PeopleData.Str ? 3 : a == PeopleData.Dex ? 2 : 1;
        else if (st == LifeStage.Elder && a is PeopleData.Str or PeopleData.Dex) v -= 2;
        if (p.IllUntil > Now) v -= 1;
        return Math.Clamp(v, 1, 10);
    }

    public int TicksPerTile(Person p)
    {
        int t = p.HasTrait(T_fast) ? 10 : 13;
        var st = p.Stage(Now);
        if (st == LifeStage.Child) t += p.AgeYears(Now) < 8 ? 5 : 2;
        else if (st == LifeStage.Elder) t += 4;
        if (p.IllUntil > Now) t += 5;
        if (p.Malnutrition > 50_000) t += 4;
        if (p.Hypothermia > 50_000) t += 3;
        return t;
    }

    /// <summary>일 속도 배율(×100): 근면 · 게으름 · 손재주 · 채집 기술.</summary>
    private int WorkPercent(Person p)
    {
        int pct = 100;
        if (p.HasTrait(T_industrious)) pct -= 20;
        if (p.HasTrait(T_lazy)) pct += 20;
        pct += (5 - Ability(p, PeopleData.Dex)) * 5;
        pct -= p.SkillLevel(PeopleData.Gathering) * 4;
        return Math.Clamp(pct, 40, 200);
    }

    // ───────── 기분 ─────────

    public int MoodBase(Person p) => 44 + (p.HasTrait(T_optimist) ? 8 : 0) - (p.HasTrait(T_pessimist) ? 8 : 0);

    public int BreakThreshold(Person p)
    {
        int t = 25;
        if (p.HasTrait(T_iron)) t = 10;
        if (p.HasTrait(T_nervous)) t = 35;
        if (p.HasTrait(T_optimist)) t -= 5;
        return t;
    }

    /// <summary>지금 상태로 정해지는 생각들(배고픔 · 추위 …).</summary>
    public void StateThoughts(Person p, List<int> into)
    {
        if (p.Dead || p.Stage(Now) == LifeStage.Baby) return;
        if (p.Malnutrition > 0 || p.Food <= 0) into.Add(H_starving); else if (p.Food < 25_000) into.Add(H_hungry);
        if (p.Water < 25_000) into.Add(H_thirsty);
        int felt = FeltNow(p);
        if (felt < 40) into.Add(H_freezing); else if (felt < 110) into.Add(H_cold); else if (felt > 330) into.Add(H_hot);
        if (p.Rest < 8_000) into.Add(H_exhausted); else if (p.Rest < 25_000) into.Add(H_tired);
        if (p.Social <= 0 && Now - p.LastTalkTick > 3 * GameTime.TicksPerDay) into.Add(H_alone); else if (p.Social < 20_000) into.Add(H_lonely);
        if (p.Joy < 15_000) into.Add(H_bored);
        if (p.IllUntil > Now) into.Add(H_sick);
    }

    private readonly List<int> _tmpThoughts = new();

    public int Mood(Person p)
    {
        int m = MoodBase(p);
        _tmpThoughts.Clear();
        StateThoughts(p, _tmpThoughts);
        foreach (int t in _tmpThoughts) m += Data.Thoughts[t].Mood;
        foreach (var (def, count) in ActiveThoughtCounts(p)) m += Data.Thoughts[def].Mood * count;
        return Math.Clamp(m, 0, 100);
    }

    /// <summary>남아 있는 사건 생각(같은 생각은 겹치는 최대 수까지).</summary>
    public IEnumerable<(int Def, int Count)> ActiveThoughtCounts(Person p)
    {
        Span<int> counts = stackalloc int[Data.Thoughts.Count];
        foreach (var t in p.Thoughts) if (t.Until > Now) counts[t.Def]++;
        var list = new List<(int, int)>();
        for (int d = 0; d < counts.Length; d++) if (counts[d] > 0) list.Add((d, Math.Min(counts[d], Data.Thoughts[d].Stack)));
        return list;
    }

    public void AddThought(Person p, int def, int other = -1)
    {
        var d = Data.Thoughts[def];
        long until = Now + d.Hours * GameTime.TicksPerHour;
        // 같은 생각이 쌓일 수 있는 수보다 많으면 가장 오래된 것을 새로 고친다
        int same = 0, oldest = -1;
        for (int i = 0; i < p.Thoughts.Count; i++)
            if (p.Thoughts[i].Def == def && p.Thoughts[i].Until > Now) { same++; if (oldest < 0 || p.Thoughts[i].Until < p.Thoughts[oldest].Until) oldest = i; }
        if (same >= d.Stack && oldest >= 0) { p.Thoughts[oldest] = new ThoughtInst { Def = def, Until = until, Other = other }; return; }
        p.Thoughts.Add(new ThoughtInst { Def = def, Until = until, Other = other });
    }

    private void PurgeThoughts(Person p) => p.Thoughts.RemoveAll(t => t.Until <= Now);

    // ───────── 기록 · 알림 ─────────

    private void AddBio(Person p, BioKind kind, int other = -1, int param = 0) => p.Bio.Add(new BioEntry { Tick = Now, Kind = kind, Other = other, Param = param });

    private void Notify(WorldEventKind kind, Person p, int other, string text)
    {
        Events.Add(new WorldEvent(NextEventSeq++, Now, kind, p.Id, other, p.Cell, text));
        if (Events.Count > EventsKept) Events.RemoveRange(0, Events.Count - EventsKept);
    }

    internal void AddLoadedEvent(WorldEvent e) => Events.Add(e);

    // ───────── 예약 ─────────

    private void Schedule(Person p, long tick)
    {
        if (p.PendingHandle != 0) _w.Scheduler.Cancel(p.PendingHandle);
        p.PendingHandle = _w.Scheduler.Schedule(Math.Max(tick, Now + 1), p.Id, EventKinds.Person);
    }

    private void Unschedule(Person p)
    {
        if (p.PendingHandle != 0) _w.Scheduler.Cancel(p.PendingHandle);
        p.PendingHandle = 0;
    }

    /// <summary>예약된 시각이 되었다.</summary>
    internal void OnEvent(int id)
    {
        var p = _byId[id];
        p.PendingHandle = 0;
        if (p.Dead) return;
        Stats.Events++;
        Integrate(p, Now);
        if (CheckDeath(p)) return;
        if (p.Walking) { p.Walking = false; Arrive(p); }
        else Finish(p);
    }
}
