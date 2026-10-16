namespace Wtw.Sim.Movers;

/// <summary>
/// 움직임 시험용 "점". 출발 → 경로를 따라 걷기 → 도착 시각에 쉼 → 다음 출발을 예약.
/// E3 의 사람 걷기가 이 구조(일정 + 예약)를 그대로 쓴다.
/// </summary>
public sealed class Dot
{
    /// <summary>화면이 고를 수 있게 남겨 두는 최근 구간 수. 시뮬이 화면보다 조금 앞서 가도 화면이 지난 구간을 그릴 수 있다.</summary>
    public const int History = 4;

    public int Id;
    public int Province;
    public int TicksPerTile;   // 걷는 빠르기 (작을수록 빠름)
    public int ColorIndex;
    public bool Walking;
    public long PendingHandle; // 다음 예약 (0 = 없음)

    private readonly Leg[] _legs = { new(), new(), new(), new() };
    private int _head = -1;      // 가장 최근 구간 위치
    private int _legCount;

    public int LegCount => _legCount;
    /// <summary>가장 최근 구간 (k=0), 그 전 (k=1) …</summary>
    public Leg LegAgo(int k) => _legs[(_head - k + History) % History];
    public Leg Current => _legs[_head];

    /// <summary>새 구간을 기록할 칸을 받는다(가장 오래된 구간을 덮어쓴다).</summary>
    public Leg NewLeg()
    {
        _head = (_head + 1) % History;
        if (_legCount < History) _legCount++;
        return _legs[_head];
    }

    public void IntPositionAt(long tick, out int x, out int y) => Current.IntPositionAt(tick, out x, out y);
}
