namespace Wtw.Sim.People;

public enum Sex : byte { Male = 0, Female = 1 }

public enum LifeStage : byte { Baby = 0, Child = 1, Adult = 2, Elder = 3 }

/// <summary>하는 일. 값은 저장 파일에 남으므로 뒤에만 더할 것.</summary>
public enum Act : byte
{
    Idle = 0, Wander, Eat, Drink, Sleep, Rest, Talk, Warm, Gather, Follow, Carried,
    BreakWander, BreakArgue, BreakLeave, Collapse, EatCorpse, Dead,
}

public enum DeathCause : byte { None = 0, Starvation, Thirst, Cold, Heat, OldAge, Illness }

/// <summary>"왜" — 지금 하는 일을 고른 까닭. 값은 저장 파일에 남는다.</summary>
public enum Why : byte
{
    None = 0, Spawned, Hungry, VeryHungry, Thirsty, VeryThirsty, Night, Exhausted, Cold, VeryCold, Hot, Lonely, Bored, Curious, Nothing,
    FollowParent, Carried, TalkedTo, WantsTalk, Break, Partner, FamilyNear, NoFood, NoWater, NoPath, Starving, Morning, Friend, WaitTalk,
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

public enum BioKind : byte { Born = 0, Spawned, Paired, ChildBorn, Died, FamilyDied, Break, CaveFound, Passion, Adult, Ill, PartnerDied, Starving, Argument }

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
    public string Name = "";
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

    public LifeStage Stage(long now)
    {
        int y = AgeYears(now);
        return y < 3 ? LifeStage.Baby : y < 14 ? LifeStage.Child : y < 60 ? LifeStage.Adult : LifeStage.Elder;
    }

    public bool HasTrait(int t) => Traits.Contains(t);

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
