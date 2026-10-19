using System.Collections.Concurrent;
using Wtw.Sim.Scheduling;
using Wtw.Sim.World;

namespace Wtw.Sim.People;

/// <summary>
/// (S1) 지방마다 CPU. 시간을 "맞추는 시각"(넘겨주기 간격, 6분)으로 자른다:
///   1) 맞추는 시각까지 깨어 있는 지방마다 그 지방의 예약을 처리한다 — 지방끼리는 서로 만지지 않으므로 여러 CPU 가 나눠 해도 된다.
///      세계 전체에 남는 일(알림 · 발견 목록 · 말의 낱말 · 그림 · 지식의 역사)은 바로 바꾸지 않고 그 지방의 Ops 에 모은다.
///   2) 맞추는 시각에 한 줄기로: 모은 일을 (시각, 칸, 차례) 순으로 적용 → 넘겨주기 → (하루 경계면) 하루 일.
/// CPU 를 몇 개 쓰든 지방마다 같은 순서로 같은 일을 하고, 나눠 쓰는 것은 맞추는 시각에만 정해진 순서로 바뀌므로 결과가 같다.
/// </summary>
public sealed partial class PeopleSim
{
    /// <summary>(S1) 지방 계산을 나눌 때 세워 둔 일꾼(ProvincePool)을 쓰나, 부를 때마다 Parallel.ForEach 를 쓰나(빠르기 비교용 — 결과는 같다).</summary>
    public static bool UsePool = true;

    /// <summary>상한 셈의 기준(지난 맞추는 시각의 산 사람 + 아이 밴 사람). 저장한다.</summary>
    internal int CapBase;

    private readonly ConcurrentBag<SimLane> _freeLanes = new();
    /// <summary>CPU 하나로 돌 때의 레인(빌리고 돌려줄 것 없이).</summary>
    private readonly SimLane _solo;

    /// <summary>깨어 있는 지방(칸 번호 순) — 맞추는 시각마다 SortedDictionary 를 훑지 않게 지방 목록이 바뀔 때만 다시 만든다.</summary>
    private readonly List<ProvinceLife> _lives = new();
    private bool _livesDirty = true;
    private List<ProvinceLife> Lives()
    {
        if (_livesDirty) { _lives.Clear(); _lives.AddRange(Provinces.Values); _livesDirty = false; }
        return _lives;
    }
    private readonly List<ProvinceLife> _work = new();

    private SimLane TakeLane()
    {
        if (_freeLanes.TryTake(out var lane)) return lane;
        lane = new SimLane(this, serial: false);
        lock (_lanes) _lanes.Add(lane);
        return lane;
    }

    /// <summary>지방 계산 중 생긴 세계 전체의 일을 맞추는 시각까지 미룬다.</summary>
    private void Defer(PendingOp op)
    {
        var lane = L;
        op.Tick = lane.Now;
        op.Cell = lane.Life!.Cell;
        lane.Life.Ops.Add(op);
    }

    /// <summary>
    /// 깨어 있는 지방마다 upTo 까지의 예약을 처리한다(threads 개 CPU — 결과는 CPU 수와 상관없다).
    /// 같은 지방 안에서는 (시각, 예약 순번) 순. 지방 사이에 오가는 일은 없다(맞추는 시각에만).
    /// </summary>
    internal void RunProvinces(long upTo, int threads)
    {
        var work = _work;
        work.Clear();
        foreach (var life in Lives())
            if (life.Sched.TryPeek(out var e) && e.Tick <= upTo) work.Add(life);
        if (work.Count == 0) return;
        if (threads <= 1 || work.Count == 1)
        {
            foreach (var life in work) RunOne(_solo, life, upTo);
        }
        else
        {
            // 큰 지방부터(예약이 많은 곳) — 나눠 맡는 순서는 결과와 상관없고 빠르기에만
            work.Sort((a, b) => b.Sched.Count != a.Sched.Count ? b.Sched.Count.CompareTo(a.Sched.Count) : a.Cell.CompareTo(b.Cell));
            if (UsePool)
            {
                var pool = ProvincePool.Rent(threads - 1);
                try
                {
                    pool.Run(work.Count, i =>
                    {
                        var lane = TakeLane();
                        try { RunOne(lane, work[i], upTo); }
                        finally { _freeLanes.Add(lane); }
                    });
                }
                finally { ProvincePool.Return(pool); }
            }
            else
                Parallel.ForEach(Partitioner.Create(0, work.Count, 1), new ParallelOptions { MaxDegreeOfParallelism = threads }, range =>
                {
                    var lane = TakeLane();
                    try { for (int i = range.Item1; i < range.Item2; i++) RunOne(lane, work[i], upTo); }
                    finally { _freeLanes.Add(lane); }
                });
        }
        MergeLanes();
    }

    private void RunOne(SimLane lane, ProvinceLife life, long upTo)
    {
        var prev = t_lane;
        t_lane = lane;
        lane.Life = life; lane.LastLife = life;
        try
        {
            var sched = life.Sched;
            while (sched.TryPeek(out var ev) && ev.Tick <= upTo)
            {
                sched.Pop();
                lane.Now = ev.Tick;
                if (ev.Kind != EventKinds.Person) throw new InvalidOperationException($"지방 예약표에 모르는 예약 종류: {ev.Kind}");
                OnEvent(ev.Target);
                lane.EventsDone++;
            }
        }
        finally
        {
            lane.Life = null; lane.LastLife = null;
            t_lane = prev;
        }
    }

    /// <summary>레인에 모인 셈(죽음 · 아이 밴 사람 · 처리한 예약)을 세계에 더한다. 측정 셈은 읽을 때(FlushLaneStats).</summary>
    private void MergeLanes()
    {
        lock (_lanes)
            foreach (var lane in _lanes)
            {
                AliveCount += lane.AliveDelta; lane.AliveDelta = 0;
                _pregnant += lane.PregnantDelta; lane.PregnantDelta = 0;
                if (lane.EventsDone > 0) { _w.AddEventsProcessed(lane.EventsDone); lane.EventsDone = 0; lane.StatsDirty = true; }
            }
    }

    /// <summary>레인의 측정 셈을 세계 셈에 더하고 비운다(지방 계산 밖에서 — Stats 를 읽을 때).</summary>
    private void FlushLaneStats()
    {
        if (InProvince) return;
        lock (_lanes)
            foreach (var lane in _lanes)
            {
                if (!lane.StatsDirty) continue;
                StatsMerge.AddInto(_stats, lane.Stats);
                StatsMerge.ClearSecond(lane.Stats, lane.Stats);
                lane.StatsDirty = false;
            }
    }

    /// <summary>불러온 미룬 일(잠든 지방의 것도 — 저장할 때의 차례 그대로, 지방 것보다 앞).</summary>
    private readonly List<PendingOp> _loadedOps = new();
    internal void LoadPendingOps(List<PendingOp> ops) { _loadedOps.Clear(); _loadedOps.AddRange(ops); }

    /// <summary>저장용: 다음 맞추는 시각에 적용할 미룬 일(ApplyOps 가 모으는 차례 그대로).</summary>
    internal List<PendingOp> PendingOpsForSave()
    {
        var all = new List<PendingOp>(_loadedOps);
        foreach (var (_, life) in Provinces) all.AddRange(life.Ops);
        return all;
    }

    /// <summary>맞추는 시각: 지방 계산 중에 미뤄 둔 일을 (시각, 칸, 차례) 순으로 적용한다.</summary>
    internal void ApplyOps()
    {
        List<PendingOp>? all = null;
        if (_loadedOps.Count > 0) { all = new List<PendingOp>(_loadedOps); _loadedOps.Clear(); }
        foreach (var life in Lives())
        {
            if (life.Ops.Count == 0) continue;
            (all ??= new()).AddRange(life.Ops); // 지방 안에서는 이미 시각 순, 칸 번호 순으로 돈다 → 안정 정렬로 차례가 남는다
            life.Ops.Clear();
        }
        if (all is null) return;
        var sorted = all.Select((op, i) => (op, i)).OrderBy(x => x.op.Tick).ThenBy(x => x.op.Cell).ThenBy(x => x.i).Select(x => x.op);
        foreach (var op in sorted) Apply(op);
    }

    private void Apply(PendingOp op)
    {
        _opTick = op.Tick;
        try
        {
            switch (op.Kind)
            {
                case PendingOp.Kinds.Notify:
                    Events.Add(new WorldEvent(NextEventSeq++, op.Tick, (WorldEventKind)op.A, op.B, op.C, op.D, op.S));
                    if (Events.Count > EventsKept) Events.RemoveRange(0, Events.Count - EventsKept);
                    break;
                case PendingOp.Kinds.Learned:
                    if (Get(op.A) is { } p) LearnWorld(p, Craft.Rules[op.B], (Via)op.C, op.D, op.E);
                    break;
                case PendingOp.Kinds.Partial:
                    PartialWorld(op.A, op.S);
                    break;
                case PendingOp.Kinds.Painting:
                    if (Get(op.A) is { } painter) PaintingWorld(painter, op.Cell, op.B, op.C, op.D, (byte)op.E, op.L);
                    break;
                case PendingOp.Kinds.History:
                    History(op.A, (byte)op.B, op.C, op.S);
                    break;
                default:
                    throw new InvalidOperationException($"모르는 미룬 일: {op.Kind}");
            }
        }
        finally { _opTick = -1; }
    }

    /// <summary>맞추는 시각이 끝났다: 다음 구간의 상한 셈 기준.</summary>
    internal void AfterSync() => CapBase = AliveCount + Pregnant;

    /// <summary>저장용: 지방마다의 예약표 · 미룬 일(칸 번호 순).</summary>
    internal IEnumerable<ProvinceLife> AwakeLives() => Provinces.Values;

    /// <summary>불러오기(버전 8 이하): 세계 예약표의 사람 예약을 그 사람의 지방 예약표로 옮긴다(시각 · 순번 · 번호 그대로).</summary>
    internal void TakePersonEvents(Scheduler world)
    {
        var all = world.ExportSorted();
        var keep = new List<ScheduledEvent>();
        var by = new SortedDictionary<int, List<ScheduledEvent>>();
        foreach (var e in all)
        {
            if (e.Kind != EventKinds.Person) { keep.Add(e); continue; }
            var p = _byId[e.Target];
            if (!by.TryGetValue(p.Cell, out var list)) by[p.Cell] = list = new();
            list.Add(e);
        }
        foreach (var (cell, list) in by) Life(cell).Sched.Import(list, world.NextHandle, world.NextOrder);
        world.Import(keep, world.NextHandle, world.NextOrder);
    }
}
