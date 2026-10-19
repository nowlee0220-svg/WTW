using Wtw.Sim.Core;
using Wtw.Sim.Movers;
using Wtw.Sim.People;
using Wtw.Sim.Planet;
using Wtw.Sim.Provinces;
using Wtw.Sim.Scheduling;
using Wtw.Sim.Time;

namespace Wtw.Sim.World;

/// <summary>
/// 세계 껍데기: 시드 · 시계 · 지방 목록 · 엔티티 저장소 · 난수 스트림 · 예약 계산기 · 개입 기록 · (시험용) 점.
/// </summary>
public sealed class GameWorld
{
    public ulong Seed { get; }
    public WorldConfig Config { get; }
    public long Now { get; private set; }
    public long NextDayBoundary { get; private set; } = GameTime.TicksPerDay;
    public long DaysElapsed { get; private set; }
    public long EventsProcessed { get; private set; }

    public List<Province> Provinces { get; } = new();
    public EntityStore Entities { get; } = new();
    public RngStreams Rng { get; }
    public Scheduler Scheduler { get; } = new();
    public DotField Dots { get; }

    private Planet.Planet? _planet;

    /// <summary>
    /// 행성. 시드 + 설정으로 다시 만들 수 있어서 저장하지 않는다. 처음 부를 때 만든다(약 0.5초) —
    /// 화면은 EnsurePlanet 으로 미리 만들어 둔다. 같은 시드 + 설정의 행성은 한 벌을 나눠 쓴다(읽기 전용).
    /// </summary>
    public Planet.Planet Planet => _planet ??= PlanetCache.Get(Seed, Config.PlanetSettings, null);
    public bool HasPlanet => _planet is not null;
    public Planet.Planet EnsurePlanet(Action<double, string>? progress) => _planet ??= PlanetCache.Get(Seed, Config.PlanetSettings, progress);

    /// <summary>
    /// 지방 지도. 행성 + 칸 번호로 언제나 똑같이 다시 만들어지므로(결정론) 저장하지 않고, 최근 것만 기억해 둔다.
    /// 지도가 바뀌는 것(딴 열매 · 주운 돌 · 걷힌 동굴)은 People.Provinces 의 "바뀐 것"으로 따로 쌓아 저장한다.
    /// </summary>
    public ProvinceMap Province(int cell) => ProvinceCache.Get(Planet, cell);

    /// <summary>사람들(E3)과 사람이 사는 지방의 바뀐 것.</summary>
    public PeopleSim People { get; }

    private readonly List<Intervention> _interventions = new();
    public IReadOnlyList<Intervention> Interventions => _interventions;

    /// <summary>하루 경계 훅(지방끼리 주고받기 자리). 등록한 순서대로 부른다. 인자 = 새로 시작한 날 번호.</summary>
    public event Action<GameWorld, long>? DayBoundary;

    /// <summary>
    /// (S1) 사람 계산에 쓸 CPU 수(1 = 한 줄기). 결과와 상관없다 — 저장하지 않는다. 게임은 코어 수 − 1(화면 몫), 측정 도구는 --threads.
    /// </summary>
    public int Threads { get; set; } = DefaultThreads;

    /// <summary>(S1) 새로 만들거나 불러온 세계의 처음 CPU 수(측정 도구의 --threads).</summary>
    public static int DefaultThreads { get; set; } = 1;

    /// <summary>(S1) 맞추는 시각의 간격(= 넘겨주기 간격). 지방끼리는 이 시각에만 주고받는다.</summary>
    public const long SyncInterval = PeopleSim.CrossInterval;

    internal void AddEventsProcessed(long n) => EventsProcessed += n;

    private GameWorld(ulong seed, WorldConfig config)
    {
        Seed = seed;
        Config = config;
        Rng = new RngStreams(seed);
        Dots = new DotField(config.MapTiles);
        People = new PeopleSim(this, PeopleData.Default);
    }

    public static GameWorld Create(ulong seed, WorldConfig config)
    {
        var w = new GameWorld(seed, config);
        w.SpawnDots(config.DotCount);
        return w;
    }

    /// <summary>불러오기용: 빈 세계(점 없음). 상태는 SaveGame 이 채운다.</summary>
    internal static GameWorld CreateEmpty(ulong seed, WorldConfig config, long now, long nextDayBoundary, long daysElapsed, long eventsProcessed)
    {
        var w = new GameWorld(seed, config) { Now = now, NextDayBoundary = nextDayBoundary, DaysElapsed = daysElapsed, EventsProcessed = eventsProcessed };
        return w;
    }

    internal void AddLoadedIntervention(Intervention iv) => _interventions.Add(iv);

    public StartRecord ToStartRecord() => new(Seed, Config, _interventions.ToArray());

    /// <summary>시작 기록으로 세계를 처음부터 다시 만들어 untilTick 까지 돌린다.</summary>
    public static GameWorld Replay(StartRecord record, long untilTick)
    {
        var w = Create(record.Seed, record.Config);
        foreach (var iv in record.Interventions)
        {
            if (iv.Tick > untilTick) break;
            w.AdvanceTo(iv.Tick);
            w.Apply(iv);
        }
        w.AdvanceTo(untilTick);
        return w;
    }

    /// <summary>
    /// targetTick 까지 계산한다. 시각이 targetTick 이하인 예약을 모두 처리하고 Now = targetTick.
    /// (S1) 시간을 맞추는 시각(SyncInterval)으로 자른다: 맞추는 시각까지 지방마다 예약을 처리하고(Threads 개 CPU),
    /// 맞추는 시각에 한 줄기로 — 미뤄 둔 세계의 일 → 지방 넘겨주기 → (하루 경계면) 하루 일.
    /// 맞추는 시각 ≤ Now 는 모두 처리된 상태다(그 사이 어디서 멈추고 저장해도 이어서 같은 결과).
    /// </summary>
    public void AdvanceTo(long targetTick)
    {
        if (targetTick < Now) throw new ArgumentOutOfRangeException(nameof(targetTick), "시간은 뒤로 가지 않는다");
        while (Now < targetTick)
        {
            long boundary = (Now / SyncInterval + 1) * SyncInterval;
            long t = Math.Min(boundary, targetTick);
            // 시험용 점(E0)은 사람과 상관없이 세계 예약표에서 한 줄기로
            while (Scheduler.TryPeek(out var next) && next.Tick <= t)
            {
                var ev = Scheduler.Pop();
                Now = ev.Tick;
                Dispatch(ev);
                EventsProcessed++;
            }
            People.RunProvinces(t, Threads);
            Now = t;
            if (t != boundary) break;
            People.ApplyOps();
            People.DoCrossings();
            if (NextDayBoundary <= t)
            {
                NextDayBoundary += GameTime.TicksPerDay;
                DaysElapsed++;
                People.OnDay();
                DayBoundary?.Invoke(this, GameTime.DayIndex(Now));
            }
            People.AfterSync();
        }
    }

    private void Dispatch(in ScheduledEvent ev)
    {
        switch (ev.Kind)
        {
            case EventKinds.DotDepart:
            {
                var d = Dots.Get(ev.Target);
                d.PendingHandle = 0;
                Dots.OnDepart(d, Now, Rng.Get(DotField.StreamName, d.Province), Scheduler);
                break;
            }
            case EventKinds.DotArrive:
            {
                var d = Dots.Get(ev.Target);
                d.PendingHandle = 0;
                Dots.OnArrive(d, Now, Rng.Get(DotField.StreamName, d.Province), Scheduler);
                break;
            }
            default:
                throw new InvalidOperationException($"모르는 예약 종류: {ev.Kind}");
        }
    }

    /// <summary>개입을 지금 시각에 적용하고 기록한다. 지금까지 처리한 예약 바로 뒤에 일어난 일로 친다.</summary>
    public void Apply(Intervention iv)
    {
        if (iv.Tick != Now) throw new InvalidOperationException($"개입 시각({iv.Tick})이 지금({Now})과 다르다");
        switch (iv.Kind)
        {
            case Intervention.Kinds.AddDots:
                SpawnDots((int)Math.Clamp(iv.Values[0], 0, 10_000));
                break;
            case Intervention.Kinds.Gather:
            {
                var rng = Rng.Get("gather");
                int x = (int)Math.Clamp(iv.Values[0], 0, Dots.MapUnits - 1);
                int y = (int)Math.Clamp(iv.Values[1], 0, Dots.MapUnits - 1);
                foreach (var d in Dots.Dots) Dots.Interrupt(d, Now, x, y, rng, Scheduler);
                break;
            }
            case Intervention.Kinds.SpawnPerson:
                if (iv.Values.Length >= 3) People.SpawnOne((int)iv.Values[0], (int)iv.Values[1], (int)iv.Values[2]);
                break;
            case Intervention.Kinds.SpawnFamily:
                if (iv.Values.Length >= 3) People.SpawnFamily((int)iv.Values[0], (int)iv.Values[1], (int)iv.Values[2]);
                break;
            default:
                throw new InvalidOperationException($"모르는 개입 종류: {iv.Kind}");
        }
        _interventions.Add(iv);
    }

    private void SpawnDots(int count)
    {
        var rng = Rng.Get(DotField.StreamName, 0);
        for (int i = 0; i < count; i++) Dots.Spawn(Entities.Allocate(), Now, rng, Scheduler);
    }
}
