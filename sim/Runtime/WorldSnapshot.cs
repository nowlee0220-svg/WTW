using Wtw.Sim.Movers;
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

    // 점마다 Dot.History 칸
    public int[] ColorIndex = Array.Empty<int>();
    public int[] LegCount = Array.Empty<int>();
    public Leg[] Legs = Array.Empty<Leg>(); // [dot * History + k], k=0 이 가장 최근

    internal void Fill(GameWorld w, long epoch, double daysPerSecond, double load)
    {
        Epoch = epoch;
        Now = w.Now;
        Seed = w.Seed;
        MapUnits = w.Dots.MapUnits;
        EventsProcessed = w.EventsProcessed;
        SimDaysPerSecond = daysPerSecond;
        SimLoad = load;
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
