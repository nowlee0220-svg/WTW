namespace Wtw.Sim.Planet;

/// <summary>
/// 측지 육각 격자(골드버그 다면체). 정이십면체의 면을 frequency 등분한 삼각 격자의 꼭짓점 하나 = 칸 하나.
/// 칸 수 = 10·n² + 2 (n = 72 → 51,842 = 육각형 51,830 + 오각형 12). 삼각형 하나 = 칸의 꼭짓점(모서리) 하나.
///
/// 결정론: 칸 번호 · 이웃 순서 · 꼭짓점 순서는 모두 정수 규칙으로만 정한다.
///   - 0~11 = 정이십면체 꼭짓점(오각형), 그다음 모서리 30개(정렬 순) 위의 점, 그다음 면 20개 안쪽의 점.
///   - 이웃은 바깥에서 볼 때 시계 방향, 가장 작은 번호의 이웃에서 시작.
///   - Corner[k] = 이웃 k 와 이웃 k+1 사이의 꼭짓점(삼각형).
/// 좌표는 실수(사칙연산 + 제곱근만 — 기계마다 같은 값)이며 시드와 무관하다. 한 번 만들어 공유한다.
/// </summary>
public sealed class GeoGrid
{
    public const int DefaultFrequency = 72;

    public int Frequency { get; }
    public int CellCount { get; }
    public int TriangleCount { get; }

    /// <summary>칸 중심(단위 구).</summary>
    public readonly double[] Cx, Cy, Cz;
    /// <summary>칸 c 의 이웃·꼭짓점은 [NStart[c], NStart[c+1]).</summary>
    public readonly int[] NStart;
    public readonly int[] Nbr;
    public readonly int[] Corner;
    /// <summary>삼각형(= 칸의 꼭짓점) 세 칸, 바깥에서 볼 때 반시계 방향.</summary>
    public readonly int[] TriA, TriB, TriC;
    /// <summary>삼각형 무게중심을 구에 올린 점(칸 모양의 꼭짓점).</summary>
    public readonly double[] Tx, Ty, Tz;

    private static readonly Lazy<GeoGrid> SharedGrid = new(() => Build(DefaultFrequency), LazyThreadSafetyMode.ExecutionAndPublication);
    /// <summary>n = 72 격자(처음 부를 때 만든다, 이후 공유).</summary>
    public static GeoGrid Shared => SharedGrid.Value;
    /// <summary>공유 격자를 만드는 데 걸린 시간(초). 아직 안 만들었으면 0.</summary>
    public static double SharedBuildSeconds { get; private set; }

    public bool IsPentagon(int cell) => cell < 12;
    public int NeighborCount(int cell) => NStart[cell + 1] - NStart[cell];
    public ReadOnlySpan<int> Neighbors(int cell) => Nbr.AsSpan(NStart[cell], NStart[cell + 1] - NStart[cell]);
    public ReadOnlySpan<int> Corners(int cell) => Corner.AsSpan(NStart[cell], NStart[cell + 1] - NStart[cell]);

    // 정이십면체: 꼭짓점 12 · 면 20 (바깥에서 볼 때 반시계 방향)
    public static readonly int[,] IcoFaces =
    {
        { 0, 11, 5 }, { 0, 5, 1 }, { 0, 1, 7 }, { 0, 7, 10 }, { 0, 10, 11 },
        { 1, 5, 9 }, { 5, 11, 4 }, { 11, 10, 2 }, { 10, 7, 6 }, { 7, 1, 8 },
        { 3, 9, 4 }, { 3, 4, 2 }, { 3, 2, 6 }, { 3, 6, 8 }, { 3, 8, 9 },
        { 4, 9, 5 }, { 2, 4, 11 }, { 6, 2, 10 }, { 8, 6, 7 }, { 9, 8, 1 },
    };

    public static (double X, double Y, double Z)[] IcoVertices()
    {
        double p = (1 + Math.Sqrt(5)) / 2;
        var v = new (double, double, double)[]
        {
            (-1, p, 0), (1, p, 0), (-1, -p, 0), (1, -p, 0),
            (0, -1, p), (0, 1, p), (0, -1, -p), (0, 1, -p),
            (p, 0, -1), (p, 0, 1), (-p, 0, -1), (-p, 0, 1),
        };
        for (int i = 0; i < v.Length; i++) v[i] = Normalize(v[i].Item1, v[i].Item2, v[i].Item3);
        return v;
    }

    private GeoGrid(int n)
    {
        Frequency = n;
        CellCount = 10 * n * n + 2;
        TriangleCount = 20 * n * n;
        Cx = new double[CellCount]; Cy = new double[CellCount]; Cz = new double[CellCount];
        NStart = new int[CellCount + 1];
        Nbr = new int[6 * CellCount - 12];
        Corner = new int[6 * CellCount - 12];
        TriA = new int[TriangleCount]; TriB = new int[TriangleCount]; TriC = new int[TriangleCount];
        Tx = new double[TriangleCount]; Ty = new double[TriangleCount]; Tz = new double[TriangleCount];
    }

    public static GeoGrid Build(int n)
    {
        if (n < 2) throw new ArgumentOutOfRangeException(nameof(n));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var g = new GeoGrid(n);
        var ico = IcoVertices();

        // 모서리 30개: (작은 번호, 큰 번호) 정렬 순
        var edgeIndex = new int[12, 12];
        var edges = new List<(int A, int B)>();
        for (int f = 0; f < 20; f++)
            for (int k = 0; k < 3; k++)
            {
                int a = IcoFaces[f, k], b = IcoFaces[f, (k + 1) % 3];
                var e = a < b ? (a, b) : (b, a);
                if (!edges.Contains(e)) edges.Add(e);
            }
        edges.Sort();
        for (int i = 0; i < edges.Count; i++) { edgeIndex[edges[i].A, edges[i].B] = i; edgeIndex[edges[i].B, edges[i].A] = i; }

        int edgeBase = 12, faceBase = 12 + 30 * (n - 1), perFace = (n - 1) * (n - 2) / 2;

        int EdgeVertex(int u, int v, int k)
        {
            if (k == 0) return u;
            if (k == n) return v;
            int e = edgeIndex[u, v];
            return u < v ? edgeBase + e * (n - 1) + (k - 1) : edgeBase + e * (n - 1) + (n - k - 1);
        }
        int Interior(int i, int j) => (j - 1) * (n - 1) - (j - 1) * j / 2 + (i - 1);
        int V(int f, int i, int j)
        {
            int a = IcoFaces[f, 0], b = IcoFaces[f, 1], c = IcoFaces[f, 2];
            if (j == 0) return EdgeVertex(a, b, i);
            if (i == 0) return EdgeVertex(a, c, j);
            if (i + j == n) return EdgeVertex(b, c, j);
            return faceBase + f * perFace + Interior(i, j);
        }

        // 좌표: 점마다 한 번, 정해진 식으로만 계산 (어느 면에서 보든 같은 값)
        for (int i = 0; i < 12; i++) g.Set(i, ico[i]);
        for (int e = 0; e < 30; e++)
        {
            var (a, b) = edges[e];
            for (int k = 1; k < n; k++)
                g.Set(edgeBase + e * (n - 1) + (k - 1), Normalize(
                    ico[a].X * (n - k) + ico[b].X * k, ico[a].Y * (n - k) + ico[b].Y * k, ico[a].Z * (n - k) + ico[b].Z * k));
        }
        for (int f = 0; f < 20; f++)
        {
            var A = ico[IcoFaces[f, 0]]; var B = ico[IcoFaces[f, 1]]; var C = ico[IcoFaces[f, 2]];
            for (int j = 1; j <= n - 2; j++)
                for (int i = 1; i <= n - 1 - j; i++)
                {
                    int w = n - i - j;
                    g.Set(faceBase + f * perFace + Interior(i, j), Normalize(
                        A.X * w + B.X * i + C.X * j, A.Y * w + B.Y * i + C.Y * j, A.Z * w + B.Z * i + C.Z * j));
                }
        }

        // 삼각형: 면마다 (j, i) 순으로 위 삼각형 → 아래 삼각형
        int t = 0;
        for (int f = 0; f < 20; f++)
            for (int j = 0; j < n; j++)
                for (int i = 0; i + j < n; i++)
                {
                    g.AddTri(t++, V(f, i, j), V(f, i + 1, j), V(f, i, j + 1));
                    if (i + j <= n - 2) g.AddTri(t++, V(f, i + 1, j), V(f, i + 1, j + 1), V(f, i, j + 1));
                }
        if (t != g.TriangleCount) throw new InvalidOperationException("삼각형 수가 맞지 않는다");

        g.BuildAdjacency();
        sw.Stop();
        if (n == DefaultFrequency && SharedBuildSeconds == 0) SharedBuildSeconds = sw.Elapsed.TotalSeconds;
        return g;
    }

    private void Set(int id, (double X, double Y, double Z) p) { Cx[id] = p.X; Cy[id] = p.Y; Cz[id] = p.Z; }

    private void AddTri(int t, int a, int b, int c)
    {
        TriA[t] = a; TriB[t] = b; TriC[t] = c;
        var p = Normalize(Cx[a] + Cx[b] + Cx[c], Cy[a] + Cy[b] + Cy[c], Cz[a] + Cz[b] + Cz[c]);
        Tx[t] = p.X; Ty[t] = p.Y; Tz[t] = p.Z;
    }

    private void BuildAdjacency()
    {
        // 칸마다 붙은 삼각형 (정수만)
        var count = new int[CellCount + 1];
        for (int t = 0; t < TriangleCount; t++) { count[TriA[t]]++; count[TriB[t]]++; count[TriC[t]]++; }
        for (int c = 0; c < CellCount; c++) NStart[c + 1] = NStart[c] + count[c];
        if (NStart[CellCount] != Nbr.Length) throw new InvalidOperationException("이웃 수 합계가 맞지 않는다");
        var fill = new int[CellCount];
        var incTri = new int[Nbr.Length];
        for (int t = 0; t < TriangleCount; t++)
        {
            incTri[NStart[TriA[t]] + fill[TriA[t]]++] = t;
            incTri[NStart[TriB[t]] + fill[TriB[t]]++] = t;
            incTri[NStart[TriC[t]] + fill[TriC[t]]++] = t;
        }

        Span<int> nx = stackalloc int[6], ny = stackalloc int[6], nt = stackalloc int[6], ccw = stackalloc int[6], ccwTri = stackalloc int[6];
        for (int c = 0; c < CellCount; c++)
        {
            int deg = NStart[c + 1] - NStart[c];
            if (deg != 5 && deg != 6) throw new InvalidOperationException($"칸 {c} 의 이웃 수 {deg}");
            for (int k = 0; k < deg; k++)
            {
                int t = incTri[NStart[c] + k];
                // 삼각형 (c, x, y) 를 반시계로: c 가 맨 앞에 오게 돌린다
                int a = TriA[t], b = TriB[t], d = TriC[t];
                if (a == c) { nx[k] = b; ny[k] = d; }
                else if (b == c) { nx[k] = d; ny[k] = a; }
                else { nx[k] = a; ny[k] = b; }
                nt[k] = t;
            }
            // 반시계 순서: x → y (같은 삼각형), 가장 작은 번호에서 시작
            int start = int.MaxValue;
            for (int k = 0; k < deg; k++) start = Math.Min(start, nx[k]);
            int cur = start;
            for (int s = 0; s < deg; s++)
            {
                int k = 0;
                while (nx[k] != cur) k++;
                ccw[s] = cur; ccwTri[s] = nt[k];
                cur = ny[k];
            }
            if (cur != start) throw new InvalidOperationException($"칸 {c} 의 이웃 고리가 닫히지 않는다");
            // 시계 방향: start, ccw[deg-1], …, ccw[1]. 꼭짓점 k = 이웃 k 와 k+1 사이 = 반시계로 (이웃 k+1 → 이웃 k) 인 삼각형
            int o = NStart[c];
            for (int k = 0; k < deg; k++)
            {
                int idx = k == 0 ? 0 : deg - k;
                Nbr[o + k] = ccw[idx];
            }
            for (int k = 0; k < deg; k++)
            {
                int next = Nbr[o + (k + 1) % deg];
                int s = 0;
                while (ccw[s] != next) s++;
                Corner[o + k] = ccwTri[s];
            }
        }
    }

    public static (double X, double Y, double Z) Normalize(double x, double y, double z)
    {
        double l = Math.Sqrt(x * x + y * y + z * z);
        return (x / l, y / l, z / l);
    }
}
