using Wtw.Sim.Core;
using Wtw.Sim.Movers;
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
    /// 지도가 바뀌는 일(나무 베기 등)은 E3~ 에서 ProvinceChanges 에 "바뀐 타일"로 쌓는다.
    /// </summary>
    public ProvinceMap Province(int cell) => ProvinceCache.Get(Planet, cell);

    /// <summary>바뀐 지방 목록(칸 번호 → 바뀐 타일들). 지금은 늘 비어 있다 — 저장 파일의 자리만 있다.</summary>
    public SortedDictionary<int, int> ProvinceChanges { get; } = new();

    private readonly List<Intervention> _interventions = new();
    public IReadOnlyList<Intervention> Interventions => _interventions;

    /// <summary>하루 경계 훅(지방끼리 주고받기 자리). 등록한 순서대로 부른다. 인자 = 새로 시작한 날 번호.</summary>
    public event Action<GameWorld, long>? DayBoundary;

    private GameWorld(ulong seed, WorldConfig config)
    {
        Seed = seed;
        Config = config;
        Rng = new RngStreams(seed);
        Dots = new DotField(config.MapTiles);
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
    /// 하루 경계는 그 시각의 예약보다 먼저 처리한다.
    /// </summary>
    public void AdvanceTo(long targetTick)
    {
        if (targetTick < Now) throw new ArgumentOutOfRangeException(nameof(targetTick), "시간은 뒤로 가지 않는다");
        while (true)
        {
            bool hasEvent = Scheduler.TryPeek(out var next);
            long eventTick = hasEvent ? next.Tick : long.MaxValue;
            if (NextDayBoundary <= targetTick && NextDayBoundary <= eventTick)
            {
                Now = NextDayBoundary;
                NextDayBoundary += GameTime.TicksPerDay;
                DaysElapsed++;
                DayBoundary?.Invoke(this, GameTime.DayIndex(Now));
                continue;
            }
            if (eventTick <= targetTick)
            {
                var ev = Scheduler.Pop();
                Now = ev.Tick;
                Dispatch(ev);
                EventsProcessed++;
                continue;
            }
            break;
        }
        Now = targetTick;
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
