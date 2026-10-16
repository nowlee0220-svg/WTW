using System.Diagnostics;
using Wtw.Sim.Core;

namespace Wtw.Sim.Planet;

/// <summary>
/// 행성 만들기. 순서:
///  1) 모양 — 땅은 한쪽 면에 모인다: 남북으로 길쭉한 대륙 + 해안 앞의 섬 (PlaceShapes 설명).
///  2) 방향 — 격자를 여러 방향으로 돌려 보며 오각형이 땅에 가장 적게 걸리는 방향을 고른다(땅의 양과 무관한 기준).
///  3) 땅 — 값이 높은 칸부터 정한 땅 칸 수만큼(같은 행성에서 땅 비율만 바뀜). 아주 작은 땅 조각은 바다로, 대양과 떨어진 작은 물은 호수로.
///  4) 높이 — 내륙일수록 높게 + 산맥(능선 잡음) + 고원 + 언덕. 바다는 해안에서 멀수록 깊게.
///  5) 물길 — 낮은 곳부터 채워 가며(우선순위 채우기) 웅덩이는 호수로, 나머지는 바다로 흘러가는 길을 정한다.
///  6) 기후 — 위도 · 높이 · 내륙 · 바람(위도대별 탁월풍, 산맥 뒤 비그늘) → 연평균 기온 · 강수.
///  7) 강 — 강수에서 증발을 뺀 물을 물길을 따라 모아, 많이 모인 칸이 강.
///  8) 생물군계 · 자원.
/// 실수 계산은 사칙연산 + 제곱근 + Floor 만 쓰고(삼각함수 없음) 결과는 정수로 바꿔 저장한다.
/// 난수는 (세계 시드, "planet.…") 스트림에서만 뽑는다.
/// </summary>
public static class PlanetGenerator
{
    // ── 모양 ──
    private const double WarpFreq = 1.9, WarpAmp = 0.55;     // 큰 굴곡(만 · 반도) — 중심에서의 거리에 비례
    private const double CoastFreq = 3.0, CoastAmp = 0.30;   // 들쭉날쭉한 해안선 — 거리에 곱한다
    /// <summary>이보다 작은 땅 조각은 바다로. 땅이 적으면 섬도 작으므로 땅 칸 수에 맞춰(4~25칸).</summary>
    private static int MinIsletCells(int land) => Math.Clamp(land / 300, 3, 40);
    private const int MaxLakeFromSeaCells = 80;              // 대양과 떨어진 이보다 작은 물은 호수
    private const int OrientationCandidates = 3000;
    private static readonly int[] OrientationLands = { 2_000, 8_000, 16_000, 24_000 };

    public static Planet Generate(ulong seed, PlanetSettings settings, Action<double, string>? progress = null)
    {
        if (settings.LandCells < PlanetSettings.MinLand || settings.LandCells > PlanetSettings.MaxLand)
            throw new ArgumentOutOfRangeException(nameof(settings), $"땅 칸 수는 {PlanetSettings.MinLand:N0} ~ {PlanetSettings.MaxLand:N0}");
        var sw = Stopwatch.StartNew();
        progress?.Invoke(0.02, "격자 준비");
        var grid = GeoGrid.Shared;
        var g = new Gen(grid, seed, settings, progress);
        var p = g.Run();
        p.GenerationSeconds = sw.Elapsed.TotalSeconds;
        progress?.Invoke(1.0, "끝");
        return p;
    }

    /// <summary>
    /// 자연로그 — 사칙연산만(Math.Log 는 기계마다 끝자리가 다를 수 있다). x = m·2^e 로 나눈 뒤(비트 그대로라 정확)
    /// ln m = 2·atanh((m−1)/(m+1)) 급수.
    /// </summary>
    public static double DetLog(double x)
    {
        long bits = BitConverter.DoubleToInt64Bits(x);
        int e = (int)((bits >> 52) & 0x7FF) - 1023;
        double m = BitConverter.Int64BitsToDouble((bits & 0x000FFFFFFFFFFFFFL) | 0x3FF0000000000000L); // [1, 2)
        if (m > 1.4142135623730951) { m *= 0.5; e++; }
        double t = (m - 1) / (m + 1), t2 = t * t, term = t, sum = 0;
        for (int k = 1; k < 40; k += 2) { sum += term / k; term *= t2; }
        return 2 * sum + e * 0.6931471805599453;
    }

    private sealed class Gen
    {
        private readonly GeoGrid _g;
        private readonly ulong _seed;
        private readonly PlanetSettings _s;
        private readonly Action<double, string>? _progress;
        private readonly int _n;
        private readonly Planet _p;

        // 모양
        private Noise3 _warpX = null!, _warpY = null!, _warpZ = null!, _coast = null!;
        private double[] _field = null!;
        private bool[] _ocean = null!, _lake = null!, _land = null!;

        public Gen(GeoGrid g, ulong seed, PlanetSettings s, Action<double, string>? progress)
        {
            _g = g; _seed = seed; _s = s; _progress = progress; _n = g.CellCount;
            _p = new Planet(g, seed, s);
        }

        private Pcg32 Stream(string name) => RngStreams.Create(_seed, "planet." + name, RngStreams.GlobalProvince);
        private void Report(double f, string what) => _progress?.Invoke(f, what);

        public Planet Run()
        {
            Report(0.05, "대륙과 섬 놓기");
            PlaceShapes();
            Report(0.15, "행성 방향 고르기");
            var rot = ChooseOrientation();
            ApplyRotation(rot);
            // 땅 칸 수 = 마른 땅(호수 제외). 호수와 지운 조각만큼 모자라면 그만큼 더해서 한 번 다시 나눈다.
            int target = _s.LandCells;
            int[] elev = null!, order = null!, parent = null!;
            for (int pass = 0; pass < 2; pass++)
            {
                Report(0.30 + pass * 0.15, "땅과 바다 나누기");
                MakeLandMask(target);
                elev = MakeElevation();
                (order, parent) = FloodAndLakes(elev);
                int dry = 0;
                for (int i = 0; i < _n; i++) if (!_ocean[i] && !_lake[i]) dry++;
                if (dry == _s.LandCells) break;
                target += _s.LandCells - dry;
            }
            Report(0.60, "지형");
            ClassifyTerrain(elev);
            Report(0.65, "기후");
            MakeClimate();
            Report(0.80, "강");
            MakeRivers(order, parent);
            Report(0.88, "생물군계");
            MakeBiomes();
            Report(0.93, "자원");
            MakeResources();
            Stats();
            return _p;
        }

        // ───────── 1) 모양 ─────────

        private static (double X, double Y, double Z) RandomUnit(Pcg32 r)
        {
            while (true)
            {
                double x = r.NextDouble() * 2 - 1, y = r.NextDouble() * 2 - 1, z = r.NextDouble() * 2 - 1;
                double l2 = x * x + y * y + z * z;
                if (l2 > 1e-6 && l2 <= 1) { double l = Math.Sqrt(l2); return (x / l, y / l, z / l); }
            }
        }

        private static (double X, double Y, double Z) Tangent((double X, double Y, double Z) p, (double X, double Y, double Z) v)
        {
            double d = p.X * v.X + p.Y * v.Y + p.Z * v.Z;
            return GeoGrid.Normalize(v.X - d * p.X, v.Y - d * p.Y, v.Z - d * p.Z);
        }

        // 땅의 중심(행성의 "앞면")과 대륙 모양
        private (double X, double Y, double Z) _center, _major, _minor;
        private double _aspect;
        private Noise3 _isle = null!;

        /// <summary>
        /// 땅은 행성의 한쪽 면(중심 L)에 모인다. 땅 값 = −(L 에서의 거리), 그래서 땅이 적으면 L 둘레의 작은 대륙,
        /// 많으면 같은 대륙이 둘레로 넓어진다(같은 값을 더 낮은 경계에서 자르는 것뿐이라 작은 땅은 늘 큰 땅 안에 들어간다).
        ///  - 대륙은 남북으로 길쭉한 타원(가로세로 1.8~2.4배) → 가장 작은 땅에서도 더운 곳(남쪽 끝)부터 추운 곳(북쪽 끝)까지 걸친다.
        ///  - 거리에 비례하는 굴곡·잡음 → 대륙이 크든 작든 해안선의 들쭉날쭉함이 비슷하다.
        ///  - 섬: "방향 × 거리의 로그" 위의 드문 혹(잡음). 거리의 로그를 쓰므로 무늬가 크기와 무관하게 되풀이된다 →
        ///    땅이 적든 많든 해안 앞(대륙 크기의 1~1.6배 거리)에 비슷한 비율의 섬이 몇 개 있다. 대륙이 커지면 섬은 대륙에 붙고 더 바깥의 섬이 떠오른다.
        /// </summary>
        private void PlaceShapes()
        {
            var r = Stream("shape");
            _warpX = new Noise3(Stream("noise.warp.x"));
            _warpY = new Noise3(Stream("noise.warp.y"));
            _warpZ = new Noise3(Stream("noise.warp.z"));
            _coast = new Noise3(Stream("noise.coast"));

            // 중심: 위도 33~46°(북·남반구 중 하나), 경도는 아무 데나
            double z0 = 0.55 + 0.17 * r.NextDouble();
            if (r.NextInt(2) == 1) z0 = -z0;
            double ux, uy, u2;
            do { ux = r.NextDouble() * 2 - 1; uy = r.NextDouble() * 2 - 1; u2 = ux * ux + uy * uy; } while (u2 < 1e-6 || u2 > 1);
            double rr = Math.Sqrt((1 - z0 * z0) / u2);
            _center = (ux * rr, uy * rr, z0);
            // 남북 방향(조금 기울여서)과 동서 방향
            var north = Tangent(_center, (0, 0, 1));
            var east = Cross(north, _center);
            double tilt = (r.NextDouble() * 2 - 1) * 0.35;
            _major = GeoGrid.Normalize(north.X + tilt * east.X, north.Y + tilt * east.Y, north.Z + tilt * east.Z);
            _minor = Cross(_major, _center);
            _aspect = 1.8 + 0.6 * r.NextDouble();

            _isle = new Noise3(Stream("noise.islands"));
        }

        private static (double X, double Y, double Z) Cross((double X, double Y, double Z) a, (double X, double Y, double Z) b) =>
            GeoGrid.Normalize(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

        /// <summary>중심에서의 거리(현 길이)를 남북으로 길쭉하게: 동서 방향 거리는 aspect 배로 센다.</summary>
        private double ContinentDistance(double x, double y, double z)
        {
            double dz = x * _center.X + y * _center.Y + z * _center.Z;
            double a = x * _major.X + y * _major.Y + z * _major.Z;
            double b = x * _minor.X + y * _minor.Y + z * _minor.Z;
            double s2 = a * a + b * b;
            double f = s2 < 1e-12 ? 1 : (a * a + b * b * _aspect * _aspect) / s2;
            return Math.Sqrt(Math.Max(0, 2 - 2 * dz) * f);
        }

        /// <summary>굴곡(만·반도): 중심에서 멀수록 크게 비튼다 → 크기와 무관하게 비슷한 모양.</summary>
        private (double X, double Y, double Z) Warp(double x, double y, double z)
        {
            double dx = x - _center.X, dy = y - _center.Y, dz = z - _center.Z;
            double dist = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            double amp = WarpAmp * dist;
            double wx = _warpX.Fbm(x * WarpFreq, y * WarpFreq, z * WarpFreq, 4);
            double wy = _warpY.Fbm(x * WarpFreq, y * WarpFreq, z * WarpFreq, 4);
            double wz = _warpZ.Fbm(x * WarpFreq, y * WarpFreq, z * WarpFreq, 4);
            return GeoGrid.Normalize(x + amp * wx, y + amp * wy, z + amp * wz);
        }

        /// <summary>대륙만의 땅 값(잡음 포함, 늘 0 이하): −(거리) × (1 + 잡음). 거리에 곱하므로 대륙 크기와 무관하게 같은 비율로 흔든다.</summary>
        private double ContinentField(double x, double y, double z, (double X, double Y, double Z) q)
        {
            double d = ContinentDistance(q.X, q.Y, q.Z);
            double n = CoastAmp * _coast.Fbm(x * CoastFreq, y * CoastFreq, z * CoastFreq, 3)
                     + FineNoise(x, y, z);
            return -d * (1 + n);
        }

        /// <summary>잔 톱니(중간 + 작은 굴곡).</summary>
        private double FineNoise(double x, double y, double z) =>
            0.06 * _coast.Sample(x * 13 + 5.1, y * 13 + 1.7, z * 13 + 9.3) + 0.035 * _coast.Sample(x * 29 + 2.9, y * 29 + 7.7, z * 29 + 4.1);

        // 섬 혹: 높이(로그 거리, 0.65 ≈ 대륙 크기의 1.9배 거리까지) · 혹이 되는 잡음 문턱 · 방향 무늬 · 거리(로그) 무늬
        private const double IsleLift = 0.65, IsleCut = 0.20, IsleDirFreq = 4.5, IsleScaleFreq = 7.5;

        /// <summary>
        /// 땅 값: 높을수록 땅. 행성 기준 좌표. = −ln(대륙 거리 × (1+잡음)) + 섬 혹.
        /// 로그는 단조라서 대륙만 보면 순서가 같고(같은 땅), 섬 혹은 로그 거리에서 정해진 높이만큼 땅을 바깥으로 들어 올린다.
        /// </summary>
        private double Field(double x, double y, double z)
        {
            var q = Warp(x, y, z);
            double f = -DetLog(Math.Max(-ContinentField(x, y, z, q), 1e-9));
            // 중심에서 본 방향(2차원 단위 벡터)과 거리의 로그 → 섬 무늬
            double a = q.X * _major.X + q.Y * _major.Y + q.Z * _major.Z;
            double b = q.X * _minor.X + q.Y * _minor.Y + q.Z * _minor.Z;
            double s2 = Math.Sqrt(a * a + b * b);
            if (s2 > 1e-9)
            {
                double u = DetLog(ContinentDistance(q.X, q.Y, q.Z));
                double v = _isle.Fbm(a / s2 * IsleDirFreq, b / s2 * IsleDirFreq, u * IsleScaleFreq, 2) * 1.6;
                if (v > IsleCut) f += IsleLift * (v - IsleCut) / (1 - IsleCut);
            }
            return f;
        }


        // ───────── 2) 방향 ─────────

        /// <summary>임의 회전(단위 사원수 → 행렬). 사칙연산 + 제곱근만.</summary>
        private static double[] RandomRotation(Pcg32 r)
        {
            double a, b, c, d, l2;
            do
            {
                a = r.NextDouble() * 2 - 1; b = r.NextDouble() * 2 - 1; c = r.NextDouble() * 2 - 1; d = r.NextDouble() * 2 - 1;
                l2 = a * a + b * b + c * c + d * d;
            } while (l2 < 1e-6 || l2 > 1);
            double l = Math.Sqrt(l2);
            a /= l; b /= l; c /= l; d /= l;
            return new[]
            {
                1 - 2 * (c * c + d * d), 2 * (b * c - a * d), 2 * (b * d + a * c),
                2 * (b * c + a * d), 1 - 2 * (b * b + d * d), 2 * (c * d - a * b),
                2 * (b * d - a * c), 2 * (c * d + a * b), 1 - 2 * (b * b + c * c),
            };
        }

        private static (double X, double Y, double Z) Rotate(double[] m, double x, double y, double z) =>
            (m[0] * x + m[1] * y + m[2] * z, m[3] * x + m[4] * y + m[5] * z, m[6] * x + m[7] * y + m[8] * z);

        private double[] ChooseOrientation()
        {
            // 땅 2,000 · 8,000 · 16,000 · 24,000 의 경계값을 회전 없이 어림한다 — 값의 분포는 회전과 거의 무관.
            // 고르는 기준이 땅의 양과 무관하므로 땅의 양을 바꿔도 행성 방향은 같다.
            var vals = new double[_n];
            for (int i = 0; i < _n; i++) vals[i] = Field(_g.Cx[i], _g.Cy[i], _g.Cz[i]);
            Array.Sort(vals);
            var lands = OrientationLands;
            var th = new double[lands.Length];
            for (int k = 0; k < lands.Length; k++) th[k] = vals[_n - lands[k]];
            const double margin = 0.02;

            // 점수: 작은 땅 설정부터 오각형이 땅에 걸린 수 (사전식으로 작을수록 좋음)
            var r = Stream("orient");
            double[] best = null!;
            long bestScore = long.MaxValue;
            int tries = 0;
            var fv = new double[12];
            for (int cand = 0; cand < OrientationCandidates; cand++)
            {
                var m = RandomRotation(r);
                tries++;
                for (int v = 0; v < 12; v++)
                {
                    var q = Rotate(m, _g.Cx[v], _g.Cy[v], _g.Cz[v]);
                    fv[v] = Field(q.X, q.Y, q.Z);
                }
                long score = 0;
                for (int k = 0; k < th.Length; k++)
                {
                    int bad = 0;
                    for (int v = 0; v < 12; v++) if (fv[v] >= th[k] - margin) bad++;
                    score = score * 16 + bad;
                }
                if (score < bestScore) { bestScore = score; best = m; }
                if (score == 0) break;
            }
            _p.OrientationTries = tries;
            return best;
        }

        private void ApplyRotation(double[] m)
        {
            for (int i = 0; i < _n; i++)
            {
                var q = Rotate(m, _g.Cx[i], _g.Cy[i], _g.Cz[i]);
                _p.X[i] = q.X; _p.Y[i] = q.Y; _p.Z[i] = q.Z;
            }
            for (int t = 0; t < _g.TriangleCount; t++)
            {
                var q = Rotate(m, _g.Tx[t], _g.Ty[t], _g.Tz[t]);
                _p.TX[t] = q.X; _p.TY[t] = q.Y; _p.TZ[t] = q.Z;
            }
        }

        // ───────── 3) 땅과 바다 ─────────

        private void MakeLandMask(int target)
        {
            _field = new double[_n];
            for (int i = 0; i < _n; i++) _field[i] = Field(_p.X[i], _p.Y[i], _p.Z[i]);
            // 값이 높은 칸부터 땅 칸 수만큼 (같은 값이면 번호 순)
            var idx = new int[_n];
            for (int i = 0; i < _n; i++) idx[i] = i;
            var f = _field;
            Array.Sort(idx, (a, b) => f[a] != f[b] ? f[b].CompareTo(f[a]) : a.CompareTo(b));
            _land = new bool[_n];
            for (int k = 0; k < target; k++) _land[idx[k]] = true;
            _threshold = _field[idx[target - 1]];

            // 아주 작은 땅 조각 → 바다
            var comp = Components(_land, out var sizes);
            int removed = 0;
            var removedComp = new bool[sizes.Count];
            for (int c = 0; c < sizes.Count; c++) if (sizes[c] < MinIsletCells(_s.LandCells)) { removedComp[c] = true; removed++; }
            for (int i = 0; i < _n; i++) if (_land[i] && removedComp[comp[i]]) _land[i] = false;
            _p.RemovedIslets = removed;

            // 물: 가장 큰 물 덩어리 = 대양. 대양과 떨어진 작은 물 → 호수, 큰 것은 안쪽 바다로 둔다.
            var water = new bool[_n];
            for (int i = 0; i < _n; i++) water[i] = !_land[i];
            var wcomp = Components(water, out var wsizes);
            int oceanComp = 0;
            for (int c = 1; c < wsizes.Count; c++) if (wsizes[c] > wsizes[oceanComp]) oceanComp = c;
            _ocean = new bool[_n];
            _lake = new bool[_n];
            for (int i = 0; i < _n; i++)
            {
                if (!water[i]) continue;
                if (wcomp[i] == oceanComp || wsizes[wcomp[i]] > MaxLakeFromSeaCells) _ocean[i] = true;
                else _lake[i] = true;
            }
        }

        private double _threshold;

        /// <summary>mask 가 참인 칸들의 연결 덩어리. 덩어리 번호는 가장 작은 칸 번호 순.</summary>
        private int[] Components(bool[] mask, out List<int> sizes)
        {
            var comp = new int[_n];
            Array.Fill(comp, -1);
            sizes = new List<int>();
            var stack = new Stack<int>();
            for (int s = 0; s < _n; s++)
            {
                if (!mask[s] || comp[s] >= 0) continue;
                int id = sizes.Count, size = 0;
                comp[s] = id; stack.Push(s);
                while (stack.Count > 0)
                {
                    int c = stack.Pop(); size++;
                    foreach (int nb in _g.Neighbors(c))
                        if (mask[nb] && comp[nb] < 0) { comp[nb] = id; stack.Push(nb); }
                }
                sizes.Add(size);
            }
            return comp;
        }

        // ───────── 4) 높이 ─────────

        private int[] MakeElevation()
        {
            var ridge = new Noise3(Stream("noise.ridge"));
            var ridgeMask = new Noise3(Stream("noise.ridge.mask"));
            var plateau = new Noise3(Stream("noise.plateau"));
            var hill = new Noise3(Stream("noise.hill"));
            var basin = new Noise3(Stream("noise.basin"));
            var elev = new int[_n];

            // 바다까지 칸 수(땅 쪽)와 땅까지 칸 수(바다 쪽)
            var seaDist = Bfs(_ocean);
            var landSrc = new bool[_n];
            for (int i = 0; i < _n; i++) landSrc[i] = !_ocean[i];
            var landDist = Bfs(landSrc);

            for (int i = 0; i < _n; i++)
            {
                double x = _p.X[i], y = _p.Y[i], z = _p.Z[i];
                if (_ocean[i])
                {
                    int d = landDist[i];
                    double depth = 20 + 70 * (d - 1) + 22.0 * (d - 1) * (d - 1);
                    if (d >= 4) depth += 400 * hill.Fbm(x * 4, y * 4, z * 4, 2);
                    elev[i] = -(int)Math.Round(Math.Clamp(depth, 15, 5500));
                    continue;
                }
                // 내륙일수록 높다: 바다까지 거리 + 땅 값의 여유
                // 대륙이 작으면 "내륙" 도 짧다
                double inland = Math.Min(1.0, seaDist[i] / Math.Max(4.0, Math.Sqrt(_s.LandCells) / 9));
                double q = Math.Clamp((_field[i] - _threshold) / 0.9, 0, 1); // 땅 값은 로그 거리: 0.9 ≈ 해안에서 중심까지의 60%
                double h = 15 + 260 * inland + 420 * q;

                // 산맥: 능선 잡음(0 근처 = 능선)을 덩어리 가면으로 끊어 줄기로
                double rn = ridge.Fbm(x * 2.4, y * 2.4, z * 2.4, 4);
                double rv = 1 - Math.Abs(rn);
                rv = rv * rv; rv = rv * rv * rv; // ^6: 가는 줄기
                double mk = Smooth(-0.05, 0.30, ridgeMask.Fbm(x * 1.4, y * 1.4, z * 1.4, 2));
                double coastFade = Smooth(0, 4, seaDist[i]);
                h += 3900 * rv * mk * (0.35 + 0.65 * coastFade);

                // 고원: 넓고 평평하게 솟은 땅 (해안에서 떨어진 곳)
                double pl = plateau.Fbm(x * 1.7, y * 1.7, z * 1.7, 2);
                h += 1050 * Smooth(0.22, 0.34, pl) * Smooth(2, 7, seaDist[i]);

                // 언덕: 잔잔한 오르내림
                h += 330 * Math.Max(0, hill.Fbm(x * 7, y * 7, z * 7, 3) + 0.05);

                // 분지(호수 자리): 드물게 오목하게
                double bs = basin.Fbm(x * 3.2, y * 3.2, z * 3.2, 2);
                h -= 300 * Smooth(0.52, 0.64, bs) * Smooth(3, 6, seaDist[i]);

                elev[i] = (int)Math.Round(Math.Clamp(h, 1, 6800));
            }
            for (int i = 0; i < _n; i++) if (_lake[i]) elev[i] = 1; // 대양과 떨어진 물: 바닥을 낮게 → 채우기에서 호수가 된다
            return elev;
        }

        private static double Smooth(double a, double b, double v)
        {
            double t = Math.Clamp((v - a) / (b - a), 0, 1);
            return t * t * (3 - 2 * t);
        }

        /// <summary>src 칸들에서의 칸 수 거리(너비 우선, 정수).</summary>
        private int[] Bfs(bool[] src)
        {
            var dist = new int[_n];
            Array.Fill(dist, int.MaxValue);
            var q = new Queue<int>();
            for (int i = 0; i < _n; i++) if (src[i]) { dist[i] = 0; q.Enqueue(i); }
            while (q.Count > 0)
            {
                int c = q.Dequeue();
                foreach (int nb in _g.Neighbors(c))
                    if (dist[nb] == int.MaxValue) { dist[nb] = dist[c] + 1; q.Enqueue(nb); }
            }
            for (int i = 0; i < _n; i++) if (dist[i] == int.MaxValue) dist[i] = 999;
            return dist;
        }

        // ───────── 5) 물길과 호수 ─────────

        private const int LakeMinDepth = 150, MaxLakeCells = 45;

        /// <summary>
        /// 우선순위 채우기: 대양에서 시작해 낮은 칸부터 넓혀 간다. 각 칸의 "부모" = 물이 흘러갈 칸.
        /// 웅덩이(채운 높이 > 원래 높이)는 깊으면 호수, 얕으면 메워서 평평하게.
        /// 돌려주는 order 는 하류 → 상류 순서(부모가 항상 먼저).
        /// </summary>
        private (int[] Order, int[] Parent) FloodAndLakes(int[] elev)
        {
            var filled = (int[])elev.Clone();
            var parent = new int[_n];
            Array.Fill(parent, -1);
            var visited = new bool[_n];
            // 같은 높이(평평한 곳)에서 넓혀 가는 순서: 칸 번호 대신 고정된 난수 열쇠 → 평지의 물길이 자로 그은 직선이 되지 않는다
            var key = new int[_n];
            var kr = Stream("flats");
            for (int i = 0; i < _n; i++) key[i] = (int)(kr.NextUInt() >> 1);
            var pq = new PriorityQueue<int, (int, int, int)>();
            var order = new List<int>(_n);
            for (int i = 0; i < _n; i++) if (_ocean[i]) visited[i] = true;
            for (int i = 0; i < _n; i++)
            {
                if (_ocean[i]) continue;
                int best = -1;
                foreach (int nb in _g.Neighbors(i)) if (_ocean[nb] && (best < 0 || nb < best)) best = nb;
                if (best >= 0) { visited[i] = true; parent[i] = best; pq.Enqueue(i, (filled[i], key[i], i)); }
            }
            while (pq.TryDequeue(out int c, out _))
            {
                order.Add(c);
                foreach (int nb in _g.Neighbors(c))
                {
                    if (visited[nb]) continue;
                    visited[nb] = true;
                    parent[nb] = c;
                    filled[nb] = Math.Max(elev[nb], filled[c]);
                    pq.Enqueue(nb, (filled[nb], key[nb], nb));
                }
            }

            // 웅덩이: 채운 높이보다 LakeMinDepth 이상 낮은 칸이 호수 후보. 한 웅덩이에서 호수는 가장 깊은 MaxLakeCells 칸까지
            // (산맥에 둘러싸인 큰 분지가 통째로 물에 잠기지 않게 — 실제로는 물이 넘쳐 출구를 깎는다). 나머지는 메워 평평하게.
            var pit = new bool[_n];
            for (int i = 0; i < _n; i++) pit[i] = !_ocean[i] && filled[i] - elev[i] >= LakeMinDepth;
            var comp = Components(pit, out var sizes);
            var members = new List<int>[sizes.Count];
            for (int c = 0; c < sizes.Count; c++) members[c] = new List<int>();
            for (int i = 0; i < _n; i++) if (pit[i]) members[comp[i]].Add(i);
            Array.Fill(_lake, false);
            foreach (var list in members)
            {
                list.Sort((a, b) => (filled[b] - elev[b]) != (filled[a] - elev[a]) ? (filled[b] - elev[b]).CompareTo(filled[a] - elev[a]) : a.CompareTo(b));
                for (int k = 0; k < list.Count && k < MaxLakeCells; k++) _lake[list[k]] = true;
            }
            for (int i = 0; i < _n; i++) if (!_ocean[i]) elev[i] = filled[i];
            return (order.ToArray(), parent);
        }

        private void ClassifyTerrain(int[] elev)
        {
            for (int i = 0; i < _n; i++)
            {
                _p.Elevation[i] = elev[i];
                if (_ocean[i]) { _p.Terrain[i] = elev[i] > -200 ? Terrain.ShallowSea : Terrain.DeepSea; continue; }
                if (_lake[i]) { _p.Terrain[i] = Terrain.Lake; continue; }
                int relief = 0;
                foreach (int nb in _g.Neighbors(i))
                    if (!_ocean[nb] && !_lake[nb]) relief = Math.Max(relief, Math.Abs(elev[nb] - elev[i]));
                int e = elev[i];
                _p.Terrain[i] =
                    e >= 3000 ? Terrain.HighMountain :
                    e >= 1700 ? (relief >= 350 ? Terrain.Mountain : Terrain.Plateau) :
                    e >= 850 ? (relief >= 300 ? Terrain.Hill : Terrain.Plateau) :
                    relief >= 180 || e >= 600 ? Terrain.Hill :
                    Terrain.Plain;
            }
            // 해안 · 호숫가 표시, 바다까지 거리
            var seaDist = Bfs(_ocean);
            for (int i = 0; i < _n; i++)
            {
                _p.SeaDistance[i] = (short)Math.Min(seaDist[i], short.MaxValue);
                if (_ocean[i] || _lake[i]) continue;
                foreach (int nb in _g.Neighbors(i))
                {
                    if (_ocean[nb]) _p.Flags[i] |= CellFlags.Coast;
                    else if (_lake[nb]) _p.Flags[i] |= CellFlags.LakeShore;
                }
            }
        }

        // ───────── 6) 기후 ─────────

        // 위도(|z| = sin 위도) → 기본 기온 °C
        private static readonly double[] TempZ = { 0, 0.26, 0.42, 0.50, 0.64, 0.707, 0.77, 0.82, 0.866, 0.906, 0.94, 0.97, 1.0 };
        private static readonly double[] TempV = { 27, 26, 23, 20, 14, 10, 6.5, 3, 0, -4, -9, -15, -24 };
        // 위도 → 기본 강수 mm (적도 비구름 띠 · 아열대 건조 띠 · 편서풍 띠 · 극지)
        private static readonly double[] RainZ = { 0, 0.17, 0.26, 0.34, 0.42, 0.50, 0.57, 0.64, 0.71, 0.77, 0.82, 0.866, 0.94, 1.0 };
        private static readonly double[] RainV = { 2500, 2200, 1500, 750, 380, 380, 620, 820, 930, 930, 830, 640, 360, 180 };

        /// <summary>정수 거듭제곱(곱셈만 — Math.Pow 는 기계마다 끝자리가 다를 수 있다).</summary>
        private static double PowInt(double b, int e)
        {
            double r = 1;
            for (int i = 0; i < e; i++) r *= b;
            return r;
        }

        private static double Table(double[] xs, double[] ys, double x)
        {
            if (x <= xs[0]) return ys[0];
            for (int i = 1; i < xs.Length; i++)
                if (x <= xs[i]) return ys[i - 1] + (ys[i] - ys[i - 1]) * (x - xs[i - 1]) / (xs[i] - xs[i - 1]);
            return ys[^1];
        }

        public static Wind WindAt(double absZ) => absZ < 0.5 ? Wind.Trade : absZ < 0.866 ? Wind.Westerly : Wind.Polar;

        private void MakeClimate()
        {
            double bias = _s.Climate switch { ClimateBias.Cold => -4, ClimateBias.Warm => 4, _ => 0 };
            double rainBias = _s.Climate switch { ClimateBias.Cold => 0.9, ClimateBias.Warm => 1.1, _ => 1.0 };

            // 바람을 거슬러 가는 이웃(바람이 불어오는 쪽)
            var upwind = new int[_n];
            for (int i = 0; i < _n; i++)
            {
                double x = _p.X[i], y = _p.Y[i], z = _p.Z[i];
                var east = GeoGrid.Normalize(-y, x, 1e-12);
                var w = WindAt(Math.Abs(z));
                double sgn = w == Wind.Westerly ? -1 : 1; // 무역풍·극동풍은 동쪽에서, 편서풍은 서쪽에서 불어온다
                int best = -1; double bestDot = double.NegativeInfinity;
                foreach (int nb in _g.Neighbors(i))
                {
                    double d = sgn * ((_p.X[nb] - x) * east.X + (_p.Y[nb] - y) * east.Y + (_p.Z[nb] - z) * east.Z);
                    if (d > bestDot) { bestDot = d; best = nb; }
                }
                upwind[i] = best;
                _p.Wind[i] = w;
            }

            // 기후 띠의 경계를 구불구불하게: 위도를 잡음으로 조금 밀어서 읽는다(띠가 자로 그은 듯한 직선이 되지 않게)
            // 큰 물결 + 중간 + 작은 물결 세 겹으로 밀고(작은 대륙 안에서도 경계가 구불구불하게), 강수의 띠 자체도 ±35% 안팎 출렁이게 한다.
            var bandNoise = new Noise3(Stream("noise.climate.band"));
            var rainNoise = new Noise3(Stream("noise.climate.rain"));
            for (int i = 0; i < _n; i++)
            {
                double x = _p.X[i], y = _p.Y[i], z = _p.Z[i];
                double shift = 0.10 * bandNoise.Fbm(x * 2.2, y * 2.2, z * 2.2, 2)
                             + 0.07 * bandNoise.Sample(x * 6 + 3.3, y * 6 + 8.1, z * 6 + 1.9)
                             + 0.04 * bandNoise.Sample(x * 13 + 7.7, y * 13 + 2.2, z * 13 + 5.5);
                double az = Math.Clamp(Math.Abs(z) + shift, 0, 1);
                double tLat = Table(TempZ, TempV, az);
                double belt = Table(RainZ, RainV, az) * (1 + 0.35 * rainNoise.Fbm(x * 5, y * 5, z * 5, 3) * 1.6);
                int e = _p.Elevation[i];
                bool water = _ocean[i] || _lake[i];
                double tAlt = !_ocean[i] && e > 0 ? -6.5 * e / 1000.0 : 0;
                int sd = _p.SeaDistance[i];
                double tIn = 0;
                if (!water)
                {
                    double steps = Math.Min(Math.Max(sd - 2, 0), 25);
                    tIn = az > 0.5 ? -0.13 * steps : 0.05 * steps; // 내륙: 높은 위도는 더 춥고, 열대는 조금 덥다
                }
                double t = tLat + tAlt + tIn + bias;
                _p.TempLatitude10[i] = (short)Math.Round(tLat * 10);
                _p.TempAltitude10[i] = (short)Math.Round(tAlt * 10);
                _p.TempInland10[i] = (short)Math.Round(tIn * 10);
                _p.Temp10[i] = (short)Math.Round(t * 10);
                _p.PrecipBelt[i] = (short)Math.Round(belt);
                _p.ShadowFrom[i] = -1;
                _p.WindSeaSteps[i] = -1;

                if (_ocean[i]) { _p.PrecipMm[i] = (short)Math.Round(belt * rainBias); _p.MoisturePct[i] = 100; continue; }

                // 바람을 거슬러 바다를 찾는다: 땅을 지날수록 습기가 줄고, 높은 산을 넘으면 비그늘
                int cur = i, maxElev = 0, maxCell = -1, steps2 = 0;
                bool found = false;
                for (int s = 1; s <= 40; s++)
                {
                    cur = upwind[cur];
                    if (_ocean[cur]) { found = true; steps2 = s - 1; break; }
                    int ce = _p.Elevation[cur];
                    if (ce > maxElev) { maxElev = ce; maxCell = cur; }
                }
                double moist = PowInt(0.965, found ? steps2 : 40);
                if (found) _p.WindSeaSteps[i] = (short)steps2;
                if (sd <= 1) moist = Math.Max(moist, 0.6); // 해안: 어느 쪽에서든 바닷바람
                double shadow = 0;
                int ownE = Math.Max(e, 0);
                if (maxElev >= 1200 && maxElev - ownE >= 500)
                {
                    shadow = Math.Clamp((maxElev - ownE) / 3500.0, 0, 0.7);
                    _p.ShadowFrom[i] = maxCell;
                }
                moist *= 1 - shadow;
                double oro = 0;
                int rise = e - Math.Max(_p.Elevation[upwind[i]], 0);
                if (rise > 150 && moist > 0.3) oro = Math.Clamp(rise / 1500.0, 0, 0.6);
                double precip = belt * (0.25 + 0.95 * moist) * (1 + oro) * rainBias;
                _p.PrecipMm[i] = (short)Math.Round(Math.Clamp(precip, 20, 6000));
                _p.MoisturePct[i] = (byte)Math.Round(Math.Clamp(moist / (1 - shadow + 1e-9), 0, 1) * 100);
                _p.ShadowPct[i] = (byte)Math.Round(shadow * 100);
                _p.OroPct[i] = (byte)Math.Round(oro * 100);
            }
        }

        // ───────── 7) 강 ─────────

        private const int StreamFlow = 9_000, RiverFlow = 30_000, BigRiverFlow = 110_000;

        private void MakeRivers(int[] order, int[] parent)
        {
            var flow = new long[_n];
            for (int i = 0; i < _n; i++)
            {
                _p.Downstream[i] = _ocean[i] ? -1 : parent[i];
                if (_ocean[i] || _lake[i]) continue;
                double evap = 300 + 14 * Math.Max(_p.Temp10[i] / 10.0, 0);
                flow[i] = Math.Max(0, _p.PrecipMm[i] - (long)Math.Round(evap));
            }
            for (int k = order.Length - 1; k >= 0; k--)
            {
                int c = order[k];
                int d = parent[c];
                if (d >= 0) flow[d] += flow[c];
            }
            int rivers = 0;
            for (int i = 0; i < _n; i++)
            {
                if (_ocean[i] || _lake[i]) continue;
                long f = flow[i];
                byte size = f >= BigRiverFlow ? (byte)3 : f >= RiverFlow ? (byte)2 : f >= StreamFlow ? (byte)1 : (byte)0;
                _p.RiverSize[i] = size;
                if (size > 0) { _p.Flags[i] |= CellFlags.River; rivers++; }
            }
            _p.RiverCells = rivers;
        }

        // ───────── 8) 생물군계 · 자원 ─────────

        private void MakeBiomes()
        {
            for (int i = 0; i < _n; i++)
            {
                if (_ocean[i] || _lake[i]) { _p.Biome[i] = Biome.None; continue; }
                double t = _p.Temp10[i] / 10.0;
                int pr = _p.PrecipMm[i];
                var ter = _p.Terrain[i];
                double desert = 200 + 6 * Math.Max(t, 0);
                bool wet = ter == Terrain.Plain && _p.Elevation[i] < 400 && t >= 0 &&
                           ((_p.RiverSize[i] >= 2 && pr >= 900) || ((_p.Flags[i] & CellFlags.LakeShore) != 0 && pr >= 800));
                _p.Biome[i] =
                    ter == Terrain.HighMountain || (ter == Terrain.Mountain && t < 3) ? Biome.Alpine :
                    t < -8 ? Biome.Ice :
                    t < -1 ? Biome.Tundra :
                    wet ? Biome.Wetland :
                    pr < desert ? Biome.Desert :
                    t < 5 ? (pr >= 350 ? Biome.Boreal : Biome.Grassland) :
                    t < 18 ? (pr >= 650 ? Biome.Temperate : Biome.Grassland) :
                    pr >= 1500 ? Biome.Rainforest : Biome.Savanna;
            }
        }

        private void MakeResources()
        {
            int R = ResourceKinds.Count;
            var noise = new Noise3[R];
            for (int r = 0; r < R; r++) noise[r] = new Noise3(Stream("res." + ResourceKinds.Ids[r]));
            for (int i = 0; i < _n; i++)
            {
                double x = _p.X[i] * 9, y = _p.Y[i] * 9, z = _p.Z[i] * 9;
                double F(int r) => noise[r].Fbm(x, y, z, 2) * 1.6; // 대략 −1 ~ 1 로 넓힘
                var ter = _p.Terrain[i];
                var bio = _p.Biome[i];
                var fl = _p.Flags[i];
                int o = i * R;
                double t = _p.Temp10[i] / 10.0;
                if (_ocean[i] || _lake[i])
                {
                    _p.Resources[o + ResourceKinds.Fish] = (byte)(ter == Terrain.DeepSea ? 1 : 2);
                    continue;
                }
                bool high = ter is Terrain.Hill or Terrain.Plateau or Terrain.Mountain or Terrain.HighMountain;
                bool rugged = ter is Terrain.Hill or Terrain.Mountain or Terrain.HighMountain;
                bool coast = (fl & CellFlags.Coast) != 0, shore = (fl & CellFlags.LakeShore) != 0;
                int river = _p.RiverSize[i];

                double f = F(ResourceKinds.Stone);
                _p.Resources[o + ResourceKinds.Stone] = (byte)(ter is Terrain.Mountain or Terrain.HighMountain ? 2 : high ? (f > 0.3 ? 2 : 1) : f > 0.35 ? 1 : 0);

                f = F(ResourceKinds.Flint);
                _p.Resources[o + ResourceKinds.Flint] = (byte)(ter is Terrain.Mountain or Terrain.HighMountain ? 0 : f > 0.55 ? 2 : f > 0.25 || (coast && f > 0.05) ? 1 : 0);

                f = F(ResourceKinds.Clay);
                _p.Resources[o + ResourceKinds.Clay] = (byte)(bio == Biome.Wetland || river >= 2 ? 2 : river == 1 || shore || (ter == Terrain.Plain && _p.PrecipMm[i] > 600 && f > 0.4) ? 1 : 0);

                f = F(ResourceKinds.Copper);
                _p.Resources[o + ResourceKinds.Copper] = (byte)(!high ? 0 : f > 0.6 ? 2 : f > 0.3 ? 1 : 0);

                f = F(ResourceKinds.Tin);
                _p.Resources[o + ResourceKinds.Tin] = (byte)(!rugged ? 0 : f > 0.72 ? 2 : f > 0.55 ? 1 : 0);

                f = F(ResourceKinds.Iron);
                _p.Resources[o + ResourceKinds.Iron] = (byte)(f > 0.55 ? 2 : f > 0.25 + (high ? 0 : 0.1) || (bio == Biome.Wetland && f > -0.1) ? 1 : 0);

                f = F(ResourceKinds.Salt);
                _p.Resources[o + ResourceKinds.Salt] = (byte)(bio == Biome.Desert && f > 0.3 ? 2 : (coast && _p.PrecipMm[i] < 600 && t > 10) || f > 0.62 ? 1 : 0);

                f = F(ResourceKinds.Wood);
                _p.Resources[o + ResourceKinds.Wood] = (byte)(bio is Biome.Rainforest or Biome.Temperate or Biome.Boreal ? 2 : bio is Biome.Savanna or Biome.Wetland || (bio == Biome.Grassland && f > 0.3) ? 1 : 0);

                f = F(ResourceKinds.Game);
                _p.Resources[o + ResourceKinds.Game] = (byte)(bio is Biome.Grassland or Biome.Savanna or Biome.Temperate ? 2 :
                    bio is Biome.Boreal or Biome.Rainforest or Biome.Wetland or Biome.Tundra || (bio is Biome.Desert or Biome.Alpine && f > 0.3) ? 1 : 0);

                _p.Resources[o + ResourceKinds.Fish] = (byte)(river >= 3 || (coast && t > 0) ? 2 : coast || shore || river >= 2 ? 1 : 0);
            }
        }

        private void Stats()
        {
            int land = 0, lakes = 0;
            var dry = new bool[_n];
            for (int i = 0; i < _n; i++)
            {
                if (_lake[i]) lakes++;
                else if (!_ocean[i]) { land++; dry[i] = true; }
            }
            _p.LandCount = land;
            _p.LakeCount = lakes;
            var notOcean = new bool[_n];
            for (int i = 0; i < _n; i++) notOcean[i] = !_ocean[i];
            var comp = Components(notOcean, out var allSizes);
            var sizes = new List<int>(allSizes.Count);
            var drySize = new int[allSizes.Count];
            for (int i = 0; i < _n; i++) if (dry[i]) drySize[comp[i]]++;
            sizes.AddRange(drySize.Where(x => x > 0)); // 땅덩어리(호수 포함해 이어진 것)마다 마른 땅 칸 수
            var sorted = sizes.OrderByDescending(s => s).ToArray();
            _p.ContinentCells = sorted.Length > 0 ? sorted[0] : 0;
            _p.IslandCount = Math.Max(0, sorted.Length - 1);
            _p.IslandSizes = sorted.Skip(1).ToArray();
            int pent = 0;
            for (int v = 0; v < 12; v++) if (!_ocean[v]) pent++;
            _p.PentagonsOnLand = pent;
        }
    }
}
