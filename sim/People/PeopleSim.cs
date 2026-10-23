using Wtw.Sim.Core;
using Wtw.Sim.Data;
using Wtw.Sim.Provinces;
using Wtw.Sim.Time;
using Wtw.Sim.World;

namespace Wtw.Sim.People;

/// <summary>알림(아이가 태어남 · 죽음 · 짝 · 정신 붕괴 …). Seq 는 1 부터 차례로.</summary>
public sealed record WorldEvent(long Seq, long Tick, WorldEventKind Kind, int Person, int Other, int Cell, string Text);

/// <summary>세계의 일. 값은 저장 파일에 남으므로 뒤에만 더할 것. Discovery = 세계에서 처음, DiscoveryGroup = 그 무리에서 처음, Rediscovered = 잊혔다 다시.</summary>
public enum WorldEventKind : byte { Spawn = 0, Birth, Death, Pair, Break, LandNamed, Moved, Language, Discovery, DiscoveryLost, Wildfire, DiscoveryGroup, Rediscovered,
    /// <summary>(E5) 한 무리에서 끊김 · 되찾음.</summary>
    GroupLost, GroupRefound,
    /// <summary>(E5b) 세계에서 처음 · 무리에서 처음 큰 짐승을 잡음 · 짐승에게 덮쳐짐 · 새끼 짐승이 따라다님.</summary>
    HuntFirst, HuntGroup, BeastAttack, Tamed,
    /// <summary>(E5c) 흉년 · 씨앗까지 먹어 버림 · 무리에서 처음 거둠 · 짐승이 밭을 먹음.</summary>
    BadHarvest, SeedsEaten, HarvestFirst, FieldEaten,
    /// <summary>(E6) 무리에서 처음 지은 건물(종류마다) · 다 지음 · 무너짐 · 무리에서 처음 무덤 · 묻음 · 사연 있는 물건이 생김.</summary>
    BuiltFirst, Built, Collapsed, GraveFirst, Buried, NamedThing }

/// <summary>측정용 셈(저장하지 않는다 — 결과에 영향 없음).</summary>
public sealed class PeopleStats
{
    public long Decisions, Events, Paths, PathFails, Talks, Arguments, Pairs, Births, Breaks, Conceptions, Illnesses;
    public readonly long[] Deaths = new long[12];
    /// <summary>(E4) 정신 붕괴: 나이 단계별 횟수 · 무너질 때 기분을 끌어내린 생각(생각 번호별 기분 합, 음수만).</summary>
    public readonly long[] BreaksByStage = new long[4];
    public readonly long[] RuleFail = new long[64]; public readonly SortedDictionary<string, long> FailKeys = new();
    public readonly long[] BreakDrag = new long[256];
    public long GatherXpPassion, GatherXpPlain, GatherActsPassion, GatherActsPlain;
    /// <summary>목마름으로 죽었는데 그 사람이 닿을 수 있는 곳(지방 안 이어진 땅)에 민물이 없었다 · 굶어 죽었는데 먹을 것이 없었다.</summary>
    public long NoWaterDeaths, NoFoodDeaths;
    /// <summary>(E3b) 경계를 넘은 횟수 · 잠든 지방이 깨어난 횟수 · 옮겨 간 까닭별 횟수(Why 값).</summary>
    public long Crossings, Wakes, CrossBatches;
    /// <summary>(E3b) 말이 갈라진 횟수 · 낱말이 새로 바뀐 횟수.</summary>
    public long LanguageSplits, WordChanges;
    public readonly long[] MoveWhy = new long[64];
    /// <summary>(E4) 실험 · 성공 · 부분 성공 · 아는 법대로 만든 횟수 · 불 피움 · 자연의 불에서 얻음 · 캐기 · 익힌 끼니 · 배탈 · 보고 배움 · 사라진 발견 · 되찾은 발견.</summary>
    /// <summary>(E4 측정) 계획이 어디서 막혔나: [0 시도, 1 행동 없음, 2 재료 없음(구하러 감), 3 재료를 못 구함, 4 불 없음, 5 둘째 재료, 6 도구, 7 시작함, 8 실험 점수 없음, 9 고른 일이 실험].</summary>
    public readonly long[] PlanWhy = new long[20];
    /// <summary>(E4 측정) 규칙마다 성공한 횟수.</summary>
    public readonly long[] RuleDone = new long[64];
    public long Experiments, Discoveries, Partials, Crafts, FiresLit, FiresTaken, Mined, CookedMeals, BadStomachs, Watched, Lost, Refound, Wildfires, CraftFails, PartialRepeats;
    /// <summary>(E5) 지식: 수업 · 수업으로 쌓인 양 합 · 알아듣기 합 · 수업으로 다 배움 · 보고 다 배움 · 단서 나눔 · 되살아남 · 잊음 · 그림 · 바랜 그림 · 그림 보기 · 그림으로 다 배움 · 아는 법의 흐려진 실패.</summary>
    public long Lessons, LessonAmount, LessonUnderstand, TaughtFull, WatchedFull, CluesShared, Refreshed, Forgot, Paintings, PaintingsFaded, Studies, LearnedFromPainting, FadedFails, ClueAvoid, LostByForget;
    /// <summary>(E5) 무리 단위: 끊김 · 되찾음 · 되찾기까지 걸린 틱 · 되찾은 길별(0 다시 발견 · 1 이웃 · 2 그림 · 3 옮겨 옴) · 아는 법 표본(날마다 무리 × 법) · 그중 한 사람만 앎.</summary>
    public long GroupLost, GroupRefound, GroupKnownSamples, GroupSoleSamples;
    public readonly List<long> RefoundDelays = new();
    public readonly long[] RefoundHow = new long[4];
    /// <summary>(E5 측정) 그림을 그린 까닭(0 그 밖 · 1 늙은 장인 · 2 혼자 앎 · 3 처음 알아냄 · 4 그리기를 처음 알아낸 실험)과 매체(0 나무껍질 · 1 바위 면 · 2 동굴 벽).</summary>
    public readonly long[] PaintWhy = new long[5], PaintMedium = new long[3];
    /// <summary>물 · 먹을 것을 찾아간 방법별 횟수: [0 보임, 1 기억, 2 따라감, 3 찾아다님].</summary>
    public readonly long[] FindHow = new long[4];
    /// <summary>(E5b) 짐승: 무리의 판단 · 달아남 · 태어남 · 이웃에서 들어온 무리 · 포식자가 잡은 짐승 · 사람을 노림 · 덮침 · 불에 물러남 · 막아 내며 쓰러뜨린 포식자.</summary>
    public long HerdEvents, HerdFlees, AnimalsBorn, ImmigrantHerds, PreyKilled, Stalks, BeastAttacks, FireRepelled, BeastsKilledDefending;
    /// <summary>(E5b) 사냥: 나섬 · 함께한 사람 · 몰아 봄 · 잡음 · 놓침 · 다침(사냥) · 사냥하다 죽음 · 손질 · 다침(모두) · 물고기 잡으러 · 잡은 물고기 · 길들임 · 해진 옷 · 썩어 버린 것 · 짐승이 줄어 떠남.</summary>
    public long ShortDiscoveries;
    public long HuntsStarted, HuntHelpers, HuntsTried, HuntSuccess, HuntsLost, HuntInjuries, HuntDeaths, Butchered, Injuries, Fishings, FishCaught, Tamed, ClothesWornOut, Rotted, LeftForGame;
    public readonly long[] HuntBySpecies = new long[16], KillsBySpecies = new long[16];
    /// <summary>(E5b 고침 측정) 썩어 버린 것: 들고 있던 날고기 · 날생선 / 날가죽 / 그 밖(열매 등) · 땅에 둔 짐승 아이템 · 손질로 얻은 고기 · 들 수 없어 그 자리에 둔 것.</summary>
    public long RotMeat, RotHide, RotOther, RotGround, MeatGot, ButcherDropped, MeatShared, MeatDried, StashEaten;
    /// <summary>(E5c) 농사: 저절로 난 싹 · 싹을 본 번 · 보고 알아냄 · 일군 타일 · 심은 타일 · 거둔 타일 · 거둔 수 · 김맨 타일 · 짐승이 먹은 타일(번) · 다 먹혀 망친 밭 ·
    /// 지킨 번 · 쫓아낸 번 · 떨어져 버려진 밭 · 잡풀이 덮어 사라진 밭 · 싹이 안 난 씨 · 씨까지 먹은 번 · 흉년 · 모아 둔 수 · 모아 둔 데서 가져감 · 모아 둔 것을 잃음 · 씨앗을 가지러 · 씨앗 먹을 거리로 주움.</summary>
    public long WildSprouts, SproutSights, SproutLearned, Tilled, Sown, HarvestTiles, HarvestItems, Weeded, FieldEaten, FieldsRuined,
        Guarded, ChasedOff, FieldsFallen, FieldsWeededOver, SeedsFailed, SeedEaten, BadHarvests, Stored, StashTaken, StashLost, SeedFetch, SeedGathered,
        StayFarmDays, StayFarmN, StayOtherDays, StayOtherN, RotBerries,
        /// <summary>(E5c 고침 측정) 아이를 가질 만했는데(짝 곁 · 나이 · 상한 아래) 배를 곯아(영양 부족) 갖지 않은 번.</summary>
        ConceiveHungry,
        /// <summary>(E6) 건물: 자리를 정함 · 날라 온 재료 · 쌓은 일(분) · 다 지음 · 버려진 자리 · 무너짐 · 고침 · 집 안에서 잔 번 · 바람막이 곁에서 잔 번 · 작업대에서 만든 번 ·
        /// 작업대를 알아냄 · 묻음(흙 · 돌) · 그대로 둠 · 사연 있는 물건 · 물려받음 · 잃음 · 다시 찾음 · 저녁 불을 챙김 · 걷은 덤불.</summary>
        SitesStarted, BuildDelivered, BuildWorkMin, BuildingsDone, SitesAbandoned, BuildingsCollapsed, Repairs, SleptHome, SleptWindbreak, BenchCrafts,
        BenchLearned, BuriedEarth, BuriedCairn, BodiesLeft, NamedMade, NamedInherited, NamedLost, NamedFound, NamedGone, EveningFires, FireKits, EmbersKept, ClearedBrush,
        /// <summary>(E6) 만든 물건의 품질별 수(0 나쁨 ~ 3 훌륭함).</summary>
        Q0, Q1, Q2, Q3;
    /// <summary>(T1 측정) 먹은 배부름(1000 = 1)을 갈래마다: [0 들에서 바로 딴 열매 · 버섯 · 이끼, 1 들고 먹은 열매 · 버섯 · 이끼, 2 낟알 · 콩 · 덩이뿌리 · 도토리(익힌 것 포함), 3 고기 · 물고기(익힌 것 포함), 4 그 밖, 5 시신].</summary>
    public readonly long[] Eaten = new long[6];
    /// <summary>(E5c 측정) 계획 · 재료 구하기를 바람마다(바람 × 2 + 아는 법이면 1).</summary>
    public readonly long[] PlanByWish = new long[32], MatByWish = new long[32], PlanKnownRule = new long[64];
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
        "by_fire", "firelight", "cooked_meal", "bad_stomach", "discovered", "half_success", "failed_try", "fire_died", "made_thing", "taught", "learned",
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
    /// <summary>측정 셈(세계 전체). 지방 계산 중의 셈은 레인에 모았다가 읽을 때 여기에 더한다(시뮬 스레드에서 읽을 것).</summary>
    public PeopleStats Stats { get { FlushLaneStats(); return _stats; } }
    private readonly PeopleStats _stats = new();

    // ───────── (S1) 레인: 지방마다 CPU ─────────

    [ThreadStatic] private static SimLane? t_lane;
    private readonly SimLane _serial;
    private readonly List<SimLane> _lanes = new();
    /// <summary>지방 계산 중인가(이 스레드가 이 세계의 지방 하나를 계산하는 중).</summary>
    private bool InProvince { get { var l = t_lane; return l is not null && l.Sim == this; } }
    private SimLane L { get { var l = t_lane; return l is not null && l.Sim == this ? l : _serial; } }
    /// <summary>측정 셈(지방 계산 중이면 그 레인의 것).</summary>
    internal PeopleStats St { get { var l = t_lane; return l is not null && l.Sim == this ? l.Stats : _stats; } }
    /// <summary>맞추는 시각에 미뤄 둔 일을 적용하는 동안의 시각(−1 = 세계 시각).</summary>
    private long _opTick = -1;

    // 자주 쓰는 번호
    private readonly int T_industrious, T_lazy, T_tough, T_frail, T_handy, T_clumsy, T_curious, T_stubborn, T_eloquent, T_blunt, T_brave, T_coward,
        T_bloodlust, T_kind, T_abrasive, T_optimist, T_pessimist, T_iron, T_nervous, T_fast, T_cannibal;
    internal readonly int H_hungry, H_starving, H_thirsty, H_cold, H_freezing, H_hot, H_tired, H_exhausted, H_lonely, H_bored, H_sick, H_ate, H_slept, H_rough,
        H_sleptCold, H_dark, H_talked, H_goodTalk, H_argument, H_withPartner, H_newPartner, H_childBorn, H_familyDied, H_partnerDied, H_friendDied, H_cave,
        H_catharsis, H_passion, H_lazyRest, H_alone, H_byFire, H_firelight, H_cookedMeal, H_badStomach, H_discovered, H_halfSuccess, H_failedTry, H_fireDied, H_madeThing, H_taught, H_learned;

    public PeopleSim(GameWorld w, PeopleData data)
    {
        _w = w; Data = data;
        _serial = new SimLane(this, serial: true) { Stats = _stats };
        _solo = new SimLane(this, serial: false);
        _lanes.Add(_solo);
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
        H_byFire = H("by_fire"); H_firelight = H("firelight"); H_cookedMeal = H("cooked_meal"); H_badStomach = H("bad_stomach"); H_discovered = H("discovered");
        H_halfSuccess = H("half_success"); H_failedTry = H("failed_try"); H_fireDied = H("fire_died"); H_madeThing = H("made_thing"); H_taught = H("taught"); H_learned = H("learned");
        InitCraft();
        InitKnowledge();
        InitHunt();
        InitFarm();
        InitBuild();
    }

    public IReadOnlyList<Person> All => _all;
    public Person? Get(int id) => _byId.TryGetValue(id, out var p) ? p : null;
    /// <summary>(S1) 같은 지방에 있는 그 사람(다른 지방이면 null) — 지방 계산 중에는 다른 지방 사람을 보지 않는다.</summary>
    private Person? Here(int id, Person p) => _byId.TryGetValue(id, out var q) && q.Cell == p.Cell ? q : null;
    public int AliveCount { get; private set; }
    /// <summary>측정 도구용: 지금 시각(지방 계산 중이면 그 지방의 시각).</summary>
    public long NowTick => Now;
    public Wtw.Sim.Planet.Planet PlanetForBench => _w.Planet;
    private long Now
    {
        get
        {
            var l = t_lane;
            if (l is not null && l.Sim == this) return l.Now;
            return _opTick >= 0 ? _opTick : _w.Now;
        }
    }

    public ProvinceLife Life(int cell)
    {
        var lane = L;
        if (lane.LastLife is { } last && last.Cell == cell) return last;
        if (!lane.Serial)
        {
            // (S1) 지방 계산 중에는 그 지방만 만진다(다른 지방을 읽거나 새로 만들면 CPU 수에 따라 결과가 달라진다)
            if (cell != lane.Life!.Cell) throw new InvalidOperationException($"지방 {lane.Life.Cell} 계산 중에 다른 지방 {cell} 을 만졌다");
            return lane.LastLife = lane.Life;
        }
        if (!Provinces.TryGetValue(cell, out var life))
        {
            life = new ProvinceLife(_w.Planet, _w.Province(cell), Data) { Perish = PerishItems };
            Provinces.Add(cell, life);
            _livesDirty = true;
            if (Dormant.Remove(cell, out var d))
            {
                // 깨어남: 바뀐 것을 되살린다. 딴 몫은 딴 시각부터 흐른 시간만큼 저절로 다시 열려 있다.
                foreach (var (tile, st) in d.Stocks) life.Stocks[tile] = st;
                foreach (int t in d.Revealed) life.Revealed.Add(t);
                foreach (var (t, f) in d.Fires) life.Fires[t] = f;
                foreach (var (t, item, n) in d.Ground) life.Drop(t, item, n);
                life.RestoreDug(d.Dug);
                life.People.AddRange(d.People);
                life.Sched.Import(d.Events, d.SchedHandle, d.SchedOrder);
                St.Wakes++;
                WakeAnimals(life, d);
                WakeFarm(life, d);
                WakeBuild(life, d);
            }
            else InitAnimals(life); // (E5b) 처음 열린 지방: 먹이만큼 짐승
            FillNamedGround(life);
        }
        return lane.LastLife = life;
    }

    /// <summary>지방 목록이 바뀌었다(깨어남 · 잠듦) — 레인의 "마지막 지방" 기억을 지운다.</summary>
    private void ForgetLastLife() { _serial.LastLife = null; foreach (var l in _lanes) l.LastLife = null; }

    /// <summary>("people", 지방) 스트림. (S1) 깨어 있는 지방은 처음 쓸 때 지방에 기억해 둔다(같은 스트림 — 사전 찾기만 줄인다).</summary>
    internal Pcg32 Rng(int cell)
    {
        var lane = L;
        var life = lane.LastLife is { } l && l.Cell == cell ? l : !lane.Serial ? Life(cell) : Provinces.GetValueOrDefault(cell);
        if (life is not null) return life.PeopleRng ??= _w.Rng.Get(Stream, cell);
        return _w.Rng.Get(Stream, cell);
    }

    internal void AddLoaded(Person p)
    {
        _all.Add(p);
        _byId.Add(p.Id, p);
        if (!p.Dead) AliveCount++;
        if (!p.Dead && p.Conceived >= 0) _pregnant++;
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
        if (p.Injury > 40_000 && a is PeopleData.Str or PeopleData.Dex) v -= 2; // (E5b) 크게 다쳤다
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
        if (p.Injury > 30_000) t += 5; // (E5b) 다쳐서 절뚝인다
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
        if (p.Injury > 30_000) pct += 25;
        return Math.Clamp(pct, 40, 200);
    }

    // ───────── 기분 ─────────

    /// <summary>(E4) 사건으로 생긴 슬픈 생각(죽음 · 말다툼 · 노숙 …)을 모두 더해도 기분을 이만큼까지만 끌어내린다.</summary>
    public const int SadCap = 35;

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
        // (E5b) 다침 · 따뜻하게 입음(추울 때) · 따르는 짐승
        if (p.Injury > 15_000) into.Add(H_injured);
        if (p.Worn >= 0 && Prop(p.Worn, Craft.P_warm) >= 3 && Life(p.Cell).OutsideTemp10(Now) < 120) into.Add(H_warmClothes);
        if (CompanionNear(Life(p.Cell), p, 8)) into.Add(H_petNear);
    }

    public int Mood(Person p)
    {
        int m = MoodBase(p);
        var tmp = L.TmpThoughts;
        tmp.Clear();
        StateThoughts(p, tmp);
        foreach (int t in tmp) m += Data.Thoughts[t].Mood;
        int sad = 0;
        // (E5b 고침) ActiveThoughtCounts 와 같은 셈을 목록 없이(기분은 판단마다 셈한다)
        Span<int> counts = stackalloc int[Data.Thoughts.Count];
        long now = Now;
        foreach (var t in p.Thoughts) if (t.Until > now) counts[t.Def]++;
        for (int d = 0; d < counts.Length; d++)
        {
            if (counts[d] == 0) continue;
            int v = Data.Thoughts[d].Mood * Math.Min(counts[d], Data.Thoughts[d].Stack);
            if (v < 0) sad += v; else m += v;
        }
        m += Math.Max(sad, -SadCap); // (E4) 슬픈 일이 겹쳐도 한꺼번에 끌어내리는 데는 한도가 있다
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

    private void PurgeThoughts(Person p)
    {
        // (E5b 고침) RemoveAll(람다)처럼 차례를 지키되 판단마다 메모리를 잡지 않게
        long now = Now; var l = p.Thoughts; int j = 0;
        for (int i = 0; i < l.Count; i++) if (l[i].Until > now) { if (i != j) l[j] = l[i]; j++; }
        if (j < l.Count) l.RemoveRange(j, l.Count - j);
    }

    // ───────── 기록 · 알림 ─────────

    private void AddBio(Person p, BioKind kind, int other = -1, int param = 0)
    {
        // (S1) 맞추는 시각에 미뤄 둔 일을 적용할 때는 그때의 시각으로 — 기록은 늘 시각 순(뒤에서 자리를 찾는다)
        long now = Now;
        var e = new BioEntry { Tick = now, Kind = kind, Other = other, Param = param };
        int i = p.Bio.Count;
        while (i > 0 && p.Bio[i - 1].Tick > now) i--;
        if (i == p.Bio.Count) p.Bio.Add(e); else p.Bio.Insert(i, e);
    }

    private void Notify(WorldEventKind kind, Person p, int other, string text)
    {
        if (InProvince) { Defer(new PendingOp { Kind = PendingOp.Kinds.Notify, A = (int)kind, B = p.Id, C = other, D = p.Cell, S = text }); return; }
        Events.Add(new WorldEvent(NextEventSeq++, Now, kind, p.Id, other, p.Cell, text));
        if (Events.Count > EventsKept) Events.RemoveRange(0, Events.Count - EventsKept);
    }

    internal void AddLoadedEvent(WorldEvent e) => Events.Add(e);

    // ───────── 예약 ─────────

    // (S1) 예약은 지방마다(ProvinceLife.Sched) — 사람은 늘 자기 지방의 예약표에 있다(넘겨줄 때는 예약이 없다).
    private void Schedule(Person p, long tick)
    {
        var sched = Life(p.Cell).Sched;
        if (p.PendingHandle != 0) sched.Cancel(p.PendingHandle);
        p.PendingHandle = sched.Schedule(Math.Max(tick, Now + 1), p.Id, EventKinds.Person);
    }

    private void Unschedule(Person p)
    {
        if (p.PendingHandle != 0) Life(p.Cell).Sched.Cancel(p.PendingHandle);
        p.PendingHandle = 0;
    }

    /// <summary>예약된 시각이 되었다.</summary>
    internal void OnEvent(int id)
    {
        var p = _byId[id];
        p.PendingHandle = 0;
        if (p.Dead) return;
        St.Events++;
        Integrate(p, Now);
        if (CheckDeath(p)) return;
        if (p.Walking) { p.Walking = false; Arrive(p); }
        else Finish(p);
    }
}
