namespace Wtw.Sim.Scheduling;

/// <summary>예약 하나: "Target 을 Tick 에 Kind 로 다시 계산".</summary>
public readonly record struct ScheduledEvent(long Handle, long Order, long Tick, int Target, int Kind);

/// <summary>
/// 예약 계산기. (시각, 예약 순번) 으로 정렬한 최소 힙. 같은 시각이면 먼저 예약한 것이 먼저(순번은 결정론적 카운터).
/// 취소와 당김(급한 일로 다른 시각으로 옮김)을 지원한다. 힙 내부 배치는 결과에 영향을 주지 않는다
/// (꺼내는 순서는 (Tick, Order) 의 전순서로만 정해진다).
/// </summary>
public sealed class Scheduler
{
    private ScheduledEvent[] _heap = new ScheduledEvent[64];
    private int _count;
    private readonly Dictionary<long, int> _position = new(); // handle → 힙 위치 (조회 전용, 순회하지 않음)
    private long _nextHandle = 1;
    private long _nextOrder = 1;

    public int Count => _count;
    public long NextHandle => _nextHandle;
    public long NextOrder => _nextOrder;

    public long Schedule(long tick, int target, int kind)
    {
        long handle = _nextHandle++;
        Push(new ScheduledEvent(handle, _nextOrder++, tick, target, kind));
        return handle;
    }

    public bool Contains(long handle) => _position.ContainsKey(handle);

    public bool TryGet(long handle, out ScheduledEvent ev)
    {
        if (_position.TryGetValue(handle, out int i)) { ev = _heap[i]; return true; }
        ev = default;
        return false;
    }

    public bool Cancel(long handle)
    {
        if (!_position.TryGetValue(handle, out int i)) return false;
        RemoveAt(i);
        return true;
    }

    /// <summary>
    /// 예약을 다른 시각으로 옮긴다(당김). 순번은 새로 받는다 — "이 시각에 다시 예약한 것" 으로 취급.
    /// </summary>
    public bool Reschedule(long handle, long newTick)
    {
        if (!_position.TryGetValue(handle, out int i)) return false;
        var old = _heap[i];
        RemoveAt(i);
        Push(old with { Tick = newTick, Order = _nextOrder++ });
        return true;
    }

    public bool TryPeek(out ScheduledEvent ev)
    {
        if (_count == 0) { ev = default; return false; }
        ev = _heap[0];
        return true;
    }

    public ScheduledEvent Pop()
    {
        if (_count == 0) throw new InvalidOperationException("예약이 비었다");
        var top = _heap[0];
        RemoveAt(0);
        return top;
    }

    /// <summary>저장용: (Tick, Order) 순으로 정렬한 사본.</summary>
    public ScheduledEvent[] ExportSorted()
    {
        var arr = new ScheduledEvent[_count];
        Array.Copy(_heap, arr, _count);
        Array.Sort(arr, static (a, b) => Compare(a, b));
        return arr;
    }

    public void Import(IEnumerable<ScheduledEvent> events, long nextHandle, long nextOrder)
    {
        _count = 0;
        _position.Clear();
        foreach (var e in events) Push(e);
        _nextHandle = nextHandle;
        _nextOrder = nextOrder;
    }

    private static int Compare(in ScheduledEvent a, in ScheduledEvent b)
    {
        int c = a.Tick.CompareTo(b.Tick);
        return c != 0 ? c : a.Order.CompareTo(b.Order);
    }

    private void Push(ScheduledEvent e)
    {
        if (_count == _heap.Length) Array.Resize(ref _heap, _heap.Length * 2);
        _heap[_count] = e;
        _position[e.Handle] = _count;
        _count++;
        SiftUp(_count - 1);
    }

    private void RemoveAt(int i)
    {
        _position.Remove(_heap[i].Handle);
        _count--;
        if (i == _count) return;
        _heap[i] = _heap[_count];
        _position[_heap[i].Handle] = i;
        if (!SiftUp(i)) SiftDown(i);
    }

    private bool SiftUp(int i)
    {
        bool moved = false;
        while (i > 0)
        {
            int p = (i - 1) >> 1;
            if (Compare(_heap[i], _heap[p]) >= 0) break;
            Swap(i, p);
            i = p;
            moved = true;
        }
        return moved;
    }

    private void SiftDown(int i)
    {
        while (true)
        {
            int l = 2 * i + 1;
            if (l >= _count) break;
            int r = l + 1;
            int m = (r < _count && Compare(_heap[r], _heap[l]) < 0) ? r : l;
            if (Compare(_heap[m], _heap[i]) >= 0) break;
            Swap(i, m);
            i = m;
        }
    }

    private void Swap(int a, int b)
    {
        (_heap[a], _heap[b]) = (_heap[b], _heap[a]);
        _position[_heap[a].Handle] = a;
        _position[_heap[b].Handle] = b;
    }
}
