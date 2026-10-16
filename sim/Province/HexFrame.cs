namespace Wtw.Sim.Provinces;

/// <summary>
/// 지방 지도의 틀(시드·칸과 무관한 기하). 150×150 네모 타일 안의 정육각형만 쓴다.
///
/// 좌표: 타일 (x, y), x 는 오른쪽, y 는 아래쪽, 타일 중심 = (x + 0.5, y + 0.5). 육각형 중심 = (75, 75).
/// 육각형은 위·아래 변이 평평하다(꼭짓점이 왼쪽·오른쪽 끝). 꼭짓점 반지름 R = 75, 변까지 거리 A = R·√3/2.
/// 변(자리) s = 0..5: 바깥 방향 각도 = −90° + 60°·s (화면에서 y 가 아래이므로 각도가 커지면 시계 방향).
///   s = 0 위 · 1 오른쪽 위 · 2 오른쪽 아래 · 3 아래 · 4 왼쪽 아래 · 5 왼쪽 위.
/// 꼭짓점 H[s] = 변 s 와 변 s+1 사이(각도 −60° + 60°·s).
/// 변 s 의 쐐기(중심, H[s−1], H[s]) 안의 무게 좌표 (λc, λ1, λ2): λc = 변까지 거리 / A.
/// </summary>
public static class HexFrame
{
    public const int Size = 150;
    public const int TileCount = Size * Size;
    public const double Center = 75.0;
    public const double R = 75.0;
    public static readonly double A = R * Math.Sqrt(3) / 2; // 변까지 거리(약 64.95)

    /// <summary>변 s 의 바깥 방향(단위 벡터).</summary>
    public static readonly double[] NX = new double[6], NY = new double[6];
    /// <summary>꼭짓점 H[s] (타일 좌표).</summary>
    public static readonly double[] HX = new double[6], HY = new double[6];

    /// <summary>타일마다: 육각형 안인가, 쐐기(변) 번호, 무게 좌표 λ1 · λ2 (λc = 1 − λ1 − λ2), 가장 가까운 변까지 거리.</summary>
    public static readonly bool[] Inside = new bool[TileCount];
    public static readonly byte[] Wedge = new byte[TileCount];
    public static readonly float[] L1 = new float[TileCount], L2 = new float[TileCount];
    public static readonly int InsideCount;

    static HexFrame()
    {
        // 각도는 고정된 표에서(삼각함수 없이): cos/sin(−90 + 60s)
        double h = Math.Sqrt(3) / 2;
        double[] cx = { 0, h, h, 0, -h, -h }, cy = { -1, -0.5, 0.5, 1, 0.5, -0.5 };
        double[] vx = { 0.5, 1, 0.5, -0.5, -1, -0.5 }, vy = { -h, 0, h, h, 0, -h };
        for (int s = 0; s < 6; s++)
        {
            NX[s] = cx[s]; NY[s] = cy[s];
            HX[s] = Center + R * vx[s]; HY[s] = Center + R * vy[s];
        }
        int inside = 0;
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                int i = y * Size + x;
                double px = x + 0.5 - Center, py = y + 0.5 - Center;
                int best = 0; double bd = double.NegativeInfinity;
                for (int s = 0; s < 6; s++)
                {
                    double d = px * NX[s] + py * NY[s];
                    if (d > bd) { bd = d; best = s; }
                }
                Wedge[i] = (byte)best;
                Inside[i] = bd <= A;
                if (Inside[i]) inside++;
                // p = λ1·H[s−1] + λ2·H[s] (중심 기준)
                int sp = (best + 5) % 6;
                double ax = HX[sp] - Center, ay = HY[sp] - Center, bx = HX[best] - Center, by = HY[best] - Center;
                double det = ax * by - ay * bx;
                double l1 = (px * by - py * bx) / det, l2 = (ax * py - ay * px) / det;
                L1[i] = (float)l1; L2[i] = (float)l2;
            }
        InsideCount = inside;
    }

    /// <summary>타일 i 에서 변 s 까지 거리(타일). 육각형 안이면 0 이상.</summary>
    public static double EdgeDistance(int i, int s)
    {
        double px = i % Size + 0.5 - Center, py = i / Size + 0.5 - Center;
        return A - (px * NX[s] + py * NY[s]);
    }

    /// <summary>타일 i 에서 가장 가까운 변까지 거리.</summary>
    public static double BorderDistance(int i)
    {
        double best = double.MaxValue;
        for (int s = 0; s < 6; s++) best = Math.Min(best, EdgeDistance(i, s));
        return best;
    }

    /// <summary>변 s 위의 점(t = 0 이면 H[s−1], 1 이면 H[s]).</summary>
    public static (double X, double Y) EdgePoint(int s, double t)
    {
        int sp = (s + 5) % 6;
        return (HX[sp] + (HX[s] - HX[sp]) * t, HY[sp] + (HY[s] - HY[sp]) * t);
    }

    /// <summary>점이 변 s 의 어디쯤인가(0 = H[s−1], 1 = H[s]) — 변에 내린 수선의 발.</summary>
    public static double EdgeParam(int s, double x, double y)
    {
        int sp = (s + 5) % 6;
        double ex = HX[s] - HX[sp], ey = HY[s] - HY[sp];
        return ((x - HX[sp]) * ex + (y - HY[sp]) * ey) / (ex * ex + ey * ey);
    }
}
