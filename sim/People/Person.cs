namespace Wtw.Sim.People;

public enum Sex : byte { Male = 0, Female = 1 }

public enum LifeStage : byte { Baby = 0, Child = 1, Adult = 2, Elder = 3 }

/// <summary>하는 일. 값은 저장 파일에 남으므로 뒤에만 더할 것.</summary>
public enum Act : byte
{
    Idle = 0, Wander, Eat, Drink, Sleep, Rest, Talk, Warm, Gather, Follow, Carried,
    BreakWander, BreakArgue, BreakLeave, Collapse, EatCorpse, Dead,
    /// <summary>(E3b) 지방 경계(Target = 경계 타일, TargetB = 변)로 걸어가 이웃 지방으로 넘어간다.</summary>
    Cross,
    // (E4) 재료와 발견
    /// <summary>실험: 바람에 맞을 것 같은 재료 · 행동을 해 본다(Target = 자리, Plan 에 행동 · 재료).</summary>
    Experiment,
    /// <summary>아는 법대로 만든다(Plan 에 규칙).</summary>
    Craft,
    /// <summary>불가에서 들고 있는 먹을 것을 익혀 먹는다(Target = 불 타일).</summary>
    Cook,
    /// <summary>불에 땔감을 넣는다(Target = 불 타일).</summary>
    TendFire,
    /// <summary>불씨를 얻으러 불이 있는 곳으로 간다(Target = 불 타일).</summary>
    TakeFire,
    /// <summary>재료를 얻으러 간다(Target = 오브젝트 타일, TargetB = 다루는 법 번호, PlanItem = 얻을 아이템).</summary>
    Material,
    /// <summary>바위를 캔다(Target = 바위 타일).</summary>
    Mine,
    /// <summary>들고 있는 것을 먹는다(제자리).</summary>
    EatCarried,
    /// <summary>그릇의 물을 마신다(제자리).</summary>
    DrinkCarried,
}

public enum DeathCause : byte { None = 0, Starvation, Thirst, Cold, Heat, OldAge, Illness }

/// <summary>"왜" — 지금 하는 일을 고른 까닭. 값은 저장 파일에 남는다.</summary>
public enum Why : byte
{
    None = 0, Spawned, Hungry, VeryHungry, Thirsty, VeryThirsty, Night, Exhausted, Cold, VeryCold, Hot, Lonely, Bored, Curious, Nothing,
    FollowParent, Carried, TalkedTo, WantsTalk, Break, Partner, FamilyNear, NoFood, NoWater, NoPath, Starving, Morning, Friend, WaitTalk,
    // (E3b) 옮겨 가는 까닭
    Scarce, SeekMate, Explore, GoBack, FollowFamily, FollowOther,
    // (E4) 바람(~가 있었으면) · 아는 법 · 단서 · 불 · 재료 · 캐기
    WantWarmth, WantSharp, WantHardTool, WantContainer, WantCooking, WantCarry, WantShiny, KnowHow, Clue, Fuel, TakeFire, Material, MineOre, MineStone, CookFood, Store, Carried2, FireNear, Rediscover,
}

/// <summary>생김새(코드로 그리는 몸 — 부모를 닮는다). 값은 0~255.</summary>
public readonly record struct Appearance(byte Skin, byte Hair, byte HairStyle, byte Build, byte Height, byte Face, byte Eyes, byte Beard);

public struct ThoughtInst
{
    public int Def;
    public long Until;
    public int Other;
}

public struct Relation
{
    public int Other;
    public int Opinion;  // −100 ~ 100
    public int Grudge;   // 0 ~ 100
    public int Favor;    // 0 ~ 100
    public int Talks;    // 이야기한 횟수
}

public enum BioKind : byte
{
    Born = 0, Spawned, Paired, ChildBorn, Died, FamilyDied, Break, CaveFound, Passion, Adult, Ill, PartnerDied, Starving, Argument, Named, LearnedSpeech, FoundWater, Moved, NamedLand,
    // (E4) Other = 규칙 번호(또는 사람), Param = 바람 번호 · 아이템 등
    Discovered, LearnedWatching, PartialSuccess, MadeThing, LitFire, FireDied, Mined, Nicknamed, NewWord, BorrowedWord, SawFire, BadStomach,
}

/// <summary>(E4) 아는 법 하나: 어느 규칙을 · 언제 · 어떤 바람에서 · 어떻게(주 재료 · 둘째 재료) · 누구에게서(−1 = 스스로 알아냄).</summary>
public struct Knowledge
{
    public int Rule;
    public long Tick;
    public int Wish;
    public int Input, With;
    public int LearnedFrom;
}

/// <summary>(E4) 부분 성공의 단서: 어느 규칙이 어느 조건(속성 이름 · heat · tool …)에서 모자랐나.</summary>
public struct Clue
{
    public int Rule;
    public string Key;
    public long Tick;
    public int Input;
}

public struct BioEntry
{
    public long Tick;
    public BioKind Kind;
    public int Other;
    public int Param;
}

/// <summary>
/// 사람 한 명(기획서 7절). 시뮬 스레드만 만진다. 욕구는 정수(0 ~ 100,000 = 0 ~ 100%), 마지막으로 계산한 시각(LastUpdate)부터 몰아서 계산한다.
/// </summary>
public sealed class Person
{
    public const int Full = 100_000;
    public const int History = 4;

    public int Id;
    /// <summary>지은 이름(무리의 말로). 빈칸 = 아직 이름이 없다(뿌린 사람) — 화면엔 생김새로.</summary>
    public string Name = "";
    /// <summary>이름의 뜻(뜻 번호들), 물려받은 이름이면 그 사람, 지어 준 사람.</summary>
    public int[] NameConcepts = Array.Empty<int>();
    public int NameFrom = -1, NamedBy = -1;
    /// <summary>쓰는 말(Language 번호)과 사람마다 조금 다른 말씨(소리 무게에 더하는 작은 값).</summary>
    public int LangId = -1;
    public sbyte[] Accent = new sbyte[LanguageData.Default.SoundCount];
    /// <summary>이야기할 때 말풍선에 띄울 뜻(−1 = 없음).</summary>
    public int TalkConcept = -1;
    /// <summary>
    /// 한 번 본 물가 · 열매 자리(최근 것부터, 몇 개만 기억). (E3b) 값 = 칸 × 타일 수 + 타일(Place) — 이웃 지방의 물가도 기억한다.
    /// </summary>
    public readonly List<int> KnownWater = new();
    public readonly List<int> KnownFood = new();
    public const int WaterMemory = 6, FoodMemory = 10;
    /// <summary>찾아다니기: 마지막으로 고른 방향(0~7, −1 = 없음).</summary>
    public int SearchDir = -1;
    /// <summary>물 · 먹을 것을 어떻게 찾아가나: 0 보임 · 1 기억 · 2 남을 따라감(HowArg = 그 사람) · 3 찾아다님(낮은 곳 · 초록 쪽).</summary>
    public byte How;
    public int HowArg = -1;
    /// <summary>(E3b) 바로 전에 살던 지방(−1 = 없음) · 지금 지방에 들어온 시각 · 찾아다녀도 못 찾은 횟수(찾으면 0) · 먹을 것이 모자란 날 셈 · 마지막으로 떠나기로 한 시각.</summary>
    public int FromCell = -1;
    public long ArrivedTick;
    public int SearchMiss;
    public int Scarcity;
    public long LastMove = long.MinValue / 2;
    /// <summary>(E3b) 옮겨 가려는 마음(하루 경계에서 정함): 까닭(None = 없음)과 가려는 칸(−1 = 아무 데나 — 갈 때 고른다).</summary>
    public Why MoveWhy;
    public int MoveTo = -1;
    public Sex Sex;
    public long BirthTick;
    public int Cell;
    public Appearance Look;
    public readonly int[] BaseAbility = new int[PeopleData.AbilityCount];
    public readonly List<int> Traits = new();
    public readonly int[] SkillXp = new int[PeopleData.SkillCount];
    public readonly byte[] Passion = new byte[PeopleData.SkillCount];
    public readonly int[] PassionPoints = new int[PeopleData.SkillCount];

    // 욕구 · 몸
    public int Food = Full, Water = Full, Rest = Full, Joy = Full, Social = Full;
    public int Malnutrition, Dehydration, Hypothermia, Heatstroke;
    public int Health = Full;
    public long IllUntil;
    public int IllDamage; // 하루에 깎이는 건강
    public long LastUpdate;
    public long LastTalkTick;
    public long LastGather = long.MinValue / 2;

    // 하는 일
    public Act Act;
    public bool Walking;     // 지금 구간이 걷기인가
    public long ActUntil;    // 하는 일(걷기 다음)이 끝나는 시각
    public int Target = -1;  // 타일 번호 또는 사람 번호(일에 따라)
    public int TargetB = -1;
    public Why Why;
    public int WhyArg;
    public long PendingHandle;
    public bool Asleep;
    public int SleepShelter; // 잠든 자리의 그늘 종류
    public bool SleptCold;
    public bool Huddle;      // 다른 사람 곁에서 잔다(덜 춥다)

    // 가족
    public int Partner = -1, Mother = -1, Father = -1, Carrier = -1;
    public readonly List<int> Children = new();
    public long Conceived = -1;
    public int ChildFather = -1;

    // 들고 다니는 것: (아이템 번호, 개수)
    public readonly List<(int Item, int Count)> Carry = new();

    // (E4) 재료와 발견
    /// <summary>바람의 세기(0~100, crafting.json 의 wishes 순서)와 생긴 시각.</summary>
    public int[] Wish = new int[8];
    public long[] WishSince = new long[8];
    public readonly List<Knowledge> Known = new();
    public readonly List<Clue> Clues = new();
    public const int ClueMemory = 12;
    /// <summary>지금 하려는 실험 · 만들기: 행동 · 주 재료 · 둘째 재료 · 도구 · 바람 · 규칙(아는 법이면) · 쓸 불 세기.</summary>
    public int PlanAction = -1, PlanInput = -1, PlanWith = -1, PlanTool = -1, PlanWish = -1, PlanRule = -1, PlanHeat, PlanItem = -1;
    public long LastExperiment = long.MinValue / 2;
    /// <summary>(E4) 아는 법으로 만들려다 재료가 없어 못 한 때 — 잠깐은 다시 계획하지 않는다.</summary>
    public long LastCraftFail = long.MinValue / 2;
    public int Experiments;
    /// <summary>들고 있는 불씨가 생긴 시각(−1 = 없음) — 두 시간이면 꺼진다.</summary>
    public long EmberTick = -1;
    /// <summary>그릇에 담은 물(몫 수).</summary>
    public int PotWater;
    /// <summary>별명(그 무리의 말) · 뜻 · 까닭(쉬운 말).</summary>
    public string Nickname = "";
    public int[] NicknameConcepts = Array.Empty<int>();
    public string NicknameReason = "";
    public int Mined;

    public readonly List<ThoughtInst> Thoughts = new();
    public readonly List<Relation> Relations = new(); // Other 순으로 정렬
    public readonly List<BioEntry> Bio = new();

    public long BreakUntil = -1;
    public int Breaks;
    public long LastBreak = long.MinValue / 2;
    public bool FoundCave;

    public bool Dead;
    public long DeathTick;
    public DeathCause Cause;
    public bool Eaten;
    /// <summary>잠자리에 드는 시각 · 깨는 시각의 사람마다 다른 어긋남(틱).</summary>
    public int BedShift, WakeShift;

    // 움직임 기록(화면이 조금 지난 시각을 그릴 수 있게 최근 몇 구간)
    private readonly Motion[] _motions = { new(), new(), new(), new() };
    private int _head = -1, _count;
    public int MotionCount => _count;
    public Motion MotionAgo(int k) => _motions[(_head - k + History * 4) % History];
    public Motion Current => _motions[_head];
    public Motion NewMotion()
    {
        _head = (_head + 1) % History;
        if (_count < History) _count++;
        return _motions[_head];
    }

    public bool Female => Sex == Sex.Female;
    public long AgeTicks(long now) => now - BirthTick;
    public int AgeYears(long now) => (int)(AgeTicks(now) / Time.GameTime.TicksPerYear);

    /// <summary>(E3b) 칸 + 타일 → 기억에 쓰는 값 하나(칸 51,842개 × 22,500 타일 &lt; 2³¹).</summary>
    public static int Place(int cell, int tile) => cell * Provinces.HexFrame.TileCount + tile;
    public static int PlaceCell(int place) => place / Provinces.HexFrame.TileCount;
    public static int PlaceTile(int place) => place % Provinces.HexFrame.TileCount;

    public LifeStage Stage(long now)
    {
        int y = AgeYears(now);
        return y < 3 ? LifeStage.Baby : y < 14 ? LifeStage.Child : y < 60 ? LifeStage.Adult : LifeStage.Elder;
    }

    public bool HasTrait(int t) => Traits.Contains(t);

    public bool Knows(int rule) { foreach (var k in Known) if (k.Rule == rule) return true; return false; }
    public int CarryCount(int item) { foreach (var c in Carry) if (c.Item == item) return c.Count; return 0; }
    public void AddCarry(int item, int n)
    {
        if (n <= 0) return;
        int k = Carry.FindIndex(c => c.Item == item);
        if (k >= 0) Carry[k] = (item, Carry[k].Count + n); else Carry.Add((item, n));
    }
    public bool RemoveCarry(int item, int n)
    {
        int k = Carry.FindIndex(c => c.Item == item);
        if (k < 0 || Carry[k].Count < n) return false;
        if (Carry[k].Count == n) Carry.RemoveAt(k); else Carry[k] = (item, Carry[k].Count - n);
        return true;
    }

    public void Position(long tick, out int x, out int y) => Current.IntPositionAt(tick, out x, out y);
    public int Tile(long tick) { Position(tick, out int x, out int y); return (y / 1000) * Provinces.HexFrame.Size + x / 1000; }

    public int RelationIndex(int other)
    {
        int lo = 0, hi = Relations.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            int c = Relations[mid].Other.CompareTo(other);
            if (c == 0) return mid;
            if (c < 0) lo = mid + 1; else hi = mid - 1;
        }
        return ~lo;
    }

    public Relation GetRelation(int other) { int i = RelationIndex(other); return i >= 0 ? Relations[i] : new Relation { Other = other }; }

    public void SetRelation(Relation r)
    {
        int i = RelationIndex(r.Other);
        if (i >= 0) Relations[i] = r; else Relations.Insert(~i, r);
    }

    public int SkillLevel(int s) => LevelOf(SkillXp[s]);

    /// <summary>경험 → 단계: 단계 L 에서 L+1 로 가는 데 1,000 × (L+1).</summary>
    public static int LevelOf(int xp)
    {
        int l = 0;
        while (l < 10 && xp >= 500 * (l + 1) * (l + 2)) l++;
        return l;
    }

    public static int XpFor(int level) => 500 * level * (level + 1);
}
