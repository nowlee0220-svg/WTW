namespace Wtw.Sim.Movers;

/// <summary>
/// 걷기 한 구간의 일정: 출발 시각 · 도착 시각 · 경로(꺾인 선, 점 2~4개).
/// 위치는 정수 "밀리타일"(1타일 = 1000) 로 다룬다 — 시뮬 안의 계산은 정수만 써서 플랫폼마다 같다.
/// 화면은 같은 일정으로 실수 보간을 한다(<see cref="PositionAt(double, out double, out double)"/>).
/// </summary>
public sealed class Leg
{
    public const int MaxPoints = 4;

    public long Depart;
    public long Arrive;
    public int PointCount;
    public readonly int[] X = new int[MaxPoints];
    public readonly int[] Y = new int[MaxPoints];
    /// <summary>Cum[i] = 점 0 에서 점 i 까지의 길이(정수, 밀리타일).</summary>
    public readonly long[] Cum = new long[MaxPoints];

    public long Length => PointCount == 0 ? 0 : Cum[PointCount - 1];
    public int EndX => X[PointCount - 1];
    public int EndY => Y[PointCount - 1];

    public void Set(long depart, long arrive, ReadOnlySpan<int> xs, ReadOnlySpan<int> ys)
    {
        if (xs.Length < 1 || xs.Length > MaxPoints || xs.Length != ys.Length) throw new ArgumentException("점 개수가 틀렸다");
        if (arrive < depart) throw new ArgumentException("도착이 출발보다 이르다");
        Depart = depart;
        Arrive = arrive;
        PointCount = xs.Length;
        Cum[0] = 0;
        for (int i = 0; i < xs.Length; i++)
        {
            X[i] = xs[i];
            Y[i] = ys[i];
            if (i > 0) Cum[i] = Cum[i - 1] + SegLength(X[i - 1], Y[i - 1], X[i], Y[i]);
        }
    }

    public void CopyFrom(Leg o)
    {
        Depart = o.Depart;
        Arrive = o.Arrive;
        PointCount = o.PointCount;
        Array.Copy(o.X, X, MaxPoints);
        Array.Copy(o.Y, Y, MaxPoints);
        Array.Copy(o.Cum, Cum, MaxPoints);
    }

    public static long SegLength(int x0, int y0, int x1, int y1)
    {
        long dx = x1 - x0, dy = y1 - y0;
        return ISqrt(dx * dx + dy * dy);
    }

    /// <summary>정수 제곱근(내림).</summary>
    public static long ISqrt(long v)
    {
        if (v < 0) throw new ArgumentOutOfRangeException(nameof(v));
        if (v < 2) return v;
        long r = (long)Math.Sqrt(v); // 근사 후 정수로 바로잡는다 → 결과는 플랫폼과 무관
        while (r * r > v) r--;
        while ((r + 1) * (r + 1) <= v) r++;
        return r;
    }

    /// <summary>시뮬용 정수 위치. tick ≤ 출발이면 시작점, tick ≥ 도착이면 끝점(정확히).</summary>
    public void IntPositionAt(long tick, out int x, out int y)
    {
        if (PointCount == 1 || tick <= Depart) { x = X[0]; y = Y[0]; return; }
        if (tick >= Arrive) { x = EndX; y = EndY; return; }
        long total = Length;
        long d = total * (tick - Depart) / (Arrive - Depart);
        int i = 1;
        while (i < PointCount - 1 && Cum[i] < d) i++;
        long segStart = Cum[i - 1], seg = Cum[i] - segStart;
        if (seg <= 0) { x = X[i]; y = Y[i]; return; }
        long t = d - segStart;
        x = (int)(X[i - 1] + (X[i] - X[i - 1]) * t / seg);
        y = (int)(Y[i - 1] + (Y[i] - Y[i - 1]) * t / seg);
    }

    /// <summary>화면용 실수 보간 (tick 은 소수 틱도 된다).</summary>
    public void PositionAt(double tick, out double x, out double y)
    {
        if (PointCount == 1 || tick <= Depart) { x = X[0]; y = Y[0]; return; }
        if (tick >= Arrive) { x = EndX; y = EndY; return; }
        double d = Length * ((tick - Depart) / (Arrive - Depart));
        int i = 1;
        while (i < PointCount - 1 && Cum[i] < d) i++;
        double segStart = Cum[i - 1], seg = Cum[i] - segStart;
        if (seg <= 0) { x = X[i]; y = Y[i]; return; }
        double t = (d - segStart) / seg;
        x = X[i - 1] + (X[i] - X[i - 1]) * t;
        y = Y[i - 1] + (Y[i] - Y[i - 1]) * t;
    }
}
