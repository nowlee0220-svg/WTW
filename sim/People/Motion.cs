namespace Wtw.Sim.People;

/// <summary>
/// 사람 한 명의 한 구간 일정: 출발 · 도착 시각과 꺾인 길(점 여러 개, 밀리타일). E0 의 Leg 와 같은 방식이지만 점 개수에 제한이 없다
/// (길 찾기로 나온 길을 줄인 꺾임점들). 서 있을 때는 점 하나, 출발 = 도착.
/// 시뮬은 정수 위치(IntPositionAt), 화면은 같은 일정으로 실수 보간(PositionAt).
/// </summary>
public sealed class Motion
{
    public long Depart;
    public long Arrive;
    public int Count;
    public int[] X = new int[4];
    public int[] Y = new int[4];
    public long[] Cum = new long[4];

    public long Length => Count == 0 ? 0 : Cum[Count - 1];
    public int EndX => X[Count - 1];
    public int EndY => Y[Count - 1];
    public int StartX => X[0];
    public int StartY => Y[0];

    private void Ensure(int n)
    {
        if (X.Length >= n) return;
        int cap = Math.Max(n, X.Length * 2);
        Array.Resize(ref X, cap); Array.Resize(ref Y, cap); Array.Resize(ref Cum, cap);
    }

    public void SetStill(long tick, int x, int y)
    {
        Depart = Arrive = tick;
        Count = 1;
        X[0] = x; Y[0] = y; Cum[0] = 0;
    }

    /// <summary>점들을 지나 걷는다. 걸리는 시간 = 길이 × 타일당 틱.</summary>
    public void SetPath(long depart, IReadOnlyList<(int X, int Y)> pts, int ticksPerTile)
    {
        Ensure(pts.Count);
        Count = pts.Count;
        for (int i = 0; i < pts.Count; i++)
        {
            X[i] = pts[i].X; Y[i] = pts[i].Y;
            Cum[i] = i == 0 ? 0 : Cum[i - 1] + Movers.Leg.SegLength(X[i - 1], Y[i - 1], X[i], Y[i]);
        }
        Depart = depart;
        Arrive = depart + Math.Max(1, (Length * ticksPerTile + 999) / 1000);
    }

    public void Set(long depart, long arrive, int[] xs, int[] ys, int n)
    {
        Ensure(n);
        Count = n;
        for (int i = 0; i < n; i++)
        {
            X[i] = xs[i]; Y[i] = ys[i];
            Cum[i] = i == 0 ? 0 : Cum[i - 1] + Movers.Leg.SegLength(X[i - 1], Y[i - 1], X[i], Y[i]);
        }
        Depart = depart; Arrive = arrive;
    }

    public void CopyFrom(Motion o)
    {
        Ensure(o.Count);
        Depart = o.Depart; Arrive = o.Arrive; Count = o.Count;
        Array.Copy(o.X, X, o.Count); Array.Copy(o.Y, Y, o.Count); Array.Copy(o.Cum, Cum, o.Count);
    }

    public void IntPositionAt(long tick, out int x, out int y)
    {
        if (Count == 1 || tick <= Depart) { x = X[0]; y = Y[0]; return; }
        if (tick >= Arrive) { x = EndX; y = EndY; return; }
        long d = Length * (tick - Depart) / (Arrive - Depart);
        int i = 1;
        while (i < Count - 1 && Cum[i] < d) i++;
        long s0 = Cum[i - 1], seg = Cum[i] - s0;
        if (seg <= 0) { x = X[i]; y = Y[i]; return; }
        long t = d - s0;
        x = (int)(X[i - 1] + (X[i] - X[i - 1]) * t / seg);
        y = (int)(Y[i - 1] + (Y[i] - Y[i - 1]) * t / seg);
    }

    /// <summary>화면용: 위치와 걷는 방향(dx, dy — 서 있으면 0).</summary>
    public void PositionAt(double tick, out double x, out double y, out double dx, out double dy)
    {
        dx = dy = 0;
        if (Count == 1 || tick <= Depart) { x = X[0]; y = Y[0]; if (Count > 1 && tick > Depart - 1) { dx = X[1] - X[0]; dy = Y[1] - Y[0]; } return; }
        if (tick >= Arrive) { x = EndX; y = EndY; return; }
        double d = Length * ((tick - Depart) / (Arrive - Depart));
        int i = 1;
        while (i < Count - 1 && Cum[i] < d) i++;
        double s0 = Cum[i - 1], seg = Cum[i] - s0;
        dx = X[i] - X[i - 1]; dy = Y[i] - Y[i - 1];
        if (seg <= 0) { x = X[i]; y = Y[i]; return; }
        double t = (d - s0) / seg;
        x = X[i - 1] + dx * t;
        y = Y[i - 1] + dy * t;
    }
}
