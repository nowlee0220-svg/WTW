using Wtw.Sim.Movers;
using Wtw.Sim.People;
using Wtw.Sim.World;

namespace Wtw.Sim.Runtime;

/// <summary>
/// 화면이 읽는 "지금 상태" 의 사본. 시뮬 스레드가 채우고 화면 스레드는 읽기만 한다.
/// 점마다 최근 구간 몇 개(일정 + 경로)를 담아, 화면이 원하는 소수 틱에서 보간할 수 있다.
/// </summary>
public sealed class WorldSnapshot
{
    public long Epoch;          // 새로 시작/불러오기 때마다 바뀜
    public long Now;            // 시뮬이 계산을 마친 시각 (이 시각까지는 일정이 확정)
    public ulong Seed;
    public int MapUnits;
    public int DotCount;
    public long EventsProcessed;
    public double SimDaysPerSecond; // 최근 1초 동안 실제로 흐른 게임 하루 수
    public double SimLoad;          // 시뮬 스레드가 계산에 쓴 시간 비율 (0~1)
    public WorldConfig Config = new();
    /// <summary>행성(읽기 전용 — 시뮬이 만든 뒤 바꾸지 않으므로 복사하지 않고 같이 본다). 아직 없으면 null.</summary>
    public Planet.Planet? Planet;

    // 점마다 Dot.History 칸
    public int[] ColorIndex = Array.Empty<int>();
    public int[] LegCount = Array.Empty<int>();
    public Leg[] Legs = Array.Empty<Leg>(); // [dot * History + k], k=0 이 가장 최근

    // ── 사람(E3) ──
    public int PeopleCount;
    public PersonView[] People = Array.Empty<PersonView>();
    /// <summary>사람이 사는 칸과 살아 있는 사람 수(칸 번호 순).</summary>
    public (int Cell, int Alive)[] CellPeople = Array.Empty<(int, int)>();
    /// <summary>한 번이라도 사람이 산 칸(열린 칸).</summary>
    public int[] OpenedCells = Array.Empty<int>();
    public WorldEvent[] RecentEvents = Array.Empty<WorldEvent>();
    public int AliveCount;
    public int PeopleCap;
    /// <summary>사람이 들어가 걷힌 타일(칸 → 타일 번호들). 바뀔 때만 새 배열.</summary>
    public Dictionary<int, int[]> Revealed = new();

    internal void Fill(GameWorld w, long epoch, double daysPerSecond, double load)
    {
        if (epoch != Epoch) Revealed = new();
        FillPeople(w);
        Epoch = epoch;
        Now = w.Now;
        Seed = w.Seed;
        MapUnits = w.Dots.MapUnits;
        EventsProcessed = w.EventsProcessed;
        SimDaysPerSecond = daysPerSecond;
        SimLoad = load;
        Config = w.Config;
        Planet = w.HasPlanet ? w.Planet : null;
        var dots = w.Dots.Dots;
        DotCount = dots.Count;
        if (ColorIndex.Length < DotCount)
        {
            int cap = Math.Max(DotCount, ColorIndex.Length * 2);
            ColorIndex = new int[cap];
            LegCount = new int[cap];
            var legs = new Leg[cap * Dot.History];
            for (int i = 0; i < legs.Length; i++) legs[i] = i < Legs.Length ? Legs[i] : new Leg();
            Legs = legs;
        }
        for (int i = 0; i < DotCount; i++)
        {
            var d = dots[i];
            ColorIndex[i] = d.ColorIndex;
            LegCount[i] = d.LegCount;
            for (int k = 0; k < d.LegCount; k++) Legs[i * Dot.History + k].CopyFrom(d.LegAgo(k));
        }
    }

    private void FillPeople(GameWorld w)
    {
        var all = w.People.All;
        PeopleCount = all.Count;
        AliveCount = w.People.AliveCount;
        PeopleCap = w.Config.PeopleCap;
        if (People.Length < PeopleCount)
        {
            var arr = new PersonView[Math.Max(PeopleCount, People.Length * 2)];
            for (int i = 0; i < arr.Length; i++) arr[i] = i < People.Length ? People[i] : new PersonView();
            People = arr;
        }
        for (int i = 0; i < PeopleCount; i++)
        {
            var p = all[i];
            var v = People[i];
            v.Fill(p, w.Now);
            v.Name = w.People.DisplayName(p);
            v.Named = p.Name.Length > 0;
            if (v.Talking && p.TalkConcept >= 0) { v.Say = w.People.WordOf(p, p.TalkConcept); v.SayMeaning = w.People.Lang.Concepts[p.TalkConcept].Meaning; }
            else { v.Say = ""; v.SayMeaning = ""; }
        }
        var counts = new List<(int, int)>();
        var opened = new List<int>();
        foreach (var (cell, life) in w.People.Provinces)
        {
            int alive = 0, ever = 0;
            foreach (var p in life.People) { ever++; if (!p.Dead) alive++; }
            if (ever > 0) opened.Add(cell);
            if (alive > 0) counts.Add((cell, alive));
        }
        foreach (var (cell, life) in w.People.Provinces)
            if (life.Revealed.Count > 0 && (!Revealed.TryGetValue(cell, out var old) || old.Length != life.Revealed.Count)) Revealed[cell] = life.Revealed.ToArray();
        CellPeople = counts.ToArray();
        OpenedCells = opened.ToArray();
        var ev = w.People.Events;
        int n = Math.Min(30, ev.Count);
        if (RecentEvents.Length != n || (n > 0 && RecentEvents[^1].Seq != ev[^1].Seq)) RecentEvents = ev.Skip(ev.Count - n).ToArray();
    }

    /// <summary>점 i 의 tick 시점 위치(밀리타일, 실수). 출발이 tick 이하인 가장 최근 구간을 쓴다.</summary>
    public void DotPosition(int i, double tick, out double x, out double y)
    {
        int n = LegCount[i];
        int baseIdx = i * Dot.History;
        for (int k = 0; k < n; k++)
        {
            var leg = Legs[baseIdx + k];
            if (leg.Depart <= tick || k == n - 1)
            {
                leg.PositionAt(tick, out x, out y);
                return;
            }
        }
        x = y = 0;
    }
}

/// <summary>화면이 사람 하나를 그리는 데 필요한 것(사본).</summary>
public sealed class PersonView
{
    public int Id, Cell, Carrier, AgeYears;
    public long DeathTick;
    public string Name = "";
    public bool Dead, Female, Asleep, Walking, Breaking, Talking, Eaten, Named;
    /// <summary>말풍선: 그 사람의 말로 된 낱말과 뜻(이야기할 때만).</summary>
    public string Say = "", SayMeaning = "";
    public LifeStage Stage;
    public Act Act;
    public Appearance Look;
    public int MotionCount;
    public readonly Motion[] Motions = { new(), new(), new(), new() }; // k = 0 이 가장 최근

    internal void Fill(Person p, long now)
    {
        Id = p.Id; Cell = p.Cell; Carrier = p.Carrier;
        DeathTick = p.DeathTick;
        Dead = p.Dead; Female = p.Female; Asleep = p.Asleep; Walking = p.Walking; Eaten = p.Eaten;
        long at = p.Dead ? p.DeathTick : now;
        AgeYears = p.AgeYears(at); Stage = p.Stage(at);
        Act = p.Act; Look = p.Look;
        Breaking = p.Act is Act.BreakArgue or Act.BreakLeave or Act.BreakWander;
        Talking = (p.Act == Act.Talk || p.Act == Act.BreakArgue) && !p.Walking;
        MotionCount = p.MotionCount;
        for (int k = 0; k < MotionCount; k++) Motions[k].CopyFrom(p.MotionAgo(k));
    }

    /// <summary>tick 시점 위치(밀리타일)와 걷는 방향. 출발이 tick 이하인 가장 최근 구간.</summary>
    public void PositionAt(double tick, out double x, out double y, out double dx, out double dy)
    {
        for (int k = 0; k < MotionCount; k++)
        {
            var m = Motions[k];
            if (m.Depart <= tick || k == MotionCount - 1) { m.PositionAt(tick, out x, out y, out dx, out dy); return; }
        }
        x = y = dx = dy = 0;
    }
}

/// <summary>
/// 세 칸 버퍼(triple buffer). 시뮬은 뒤 칸에 쓰고 바꿔 놓기만, 화면은 최신 칸을 가져가기만 한다.
/// 잠금이 없어 어느 쪽도 상대를 기다리지 않는다.
/// </summary>
public sealed class SnapshotBuffer
{
    private readonly WorldSnapshot[] _slots = { new(), new(), new() };
    private int _back = 0, _front = 1;
    private int _middle = 2;          // 하위 2비트 = 칸 번호, 4 = 새것 표시
    private const int FreshBit = 4;

    /// <summary>시뮬 스레드: 쓸 칸.</summary>
    public WorldSnapshot Back => _slots[_back];

    /// <summary>시뮬 스레드: 다 쓴 칸을 내놓는다.</summary>
    public void Publish()
    {
        int old = Interlocked.Exchange(ref _middle, _back | FreshBit);
        _back = old & 3;
    }

    /// <summary>화면 스레드: 최신 칸 (새것이 없으면 지난번 것).</summary>
    public WorldSnapshot Acquire()
    {
        if ((Volatile.Read(ref _middle) & FreshBit) != 0)
        {
            int old = Interlocked.Exchange(ref _middle, _front);
            _front = old & 3;
        }
        return _slots[_front];
    }
}
