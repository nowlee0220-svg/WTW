using System.Runtime.CompilerServices;
using Wtw.Sim.Core;
using Wtw.Sim.Data;
using Wtw.Sim.Planet;

namespace Wtw.Sim.Provinces;

/// <summary>
/// 지방 지도 만들기: 행성 칸 정보(지형 · 높이 · 생물군계 · 기온 · 강수 · 강 · 해안 · 자원) + 지방 시드 → 150×150 타일.
///
/// 이웃과 이어지게 하는 방법:
///   1. 타일 하나하나를 행성 위의 한 점에 놓는다. 육각형을 여섯 쐐기(중심, 꼭짓점 H[s−1], H[s])로 나누고,
///      쐐기 안의 무게 좌표로 "칸 중심 · 행성 꼭짓점 두 개" 를 섞는다. 맞댄 변 위의 점은 양쪽 지방에서 행성 위의 같은 점이다.
///   2. 그 점에서 "칸의 무게"를 정한다: 중심 칸 λc + 꼭짓점마다 만나는 세 칸에 λ/3 씩. 변 위에서는 양쪽 칸이 같은 무게를 받는다.
///      높이 · 기온 · 강수 · 거칠기 · 자원 단계는 이 무게로 섞은 값이고, 생물군계는 무게 + 잡음으로 고른다 → 경계에서 저절로 이어진다.
///   3. 잡음은 지방마다가 아니라 행성 전체에 하나(행성 위의 점에서 뽑는다) → 어느 쪽 지방에서 봐도 같은 무늬.
///   4. 강이 변을 건너는 자리는 두 칸 번호로 정한 공유 시드에서 → 어느 쪽을 먼저 만들어도 같은 자리, 변에 수직으로 건넌다.
///   5. 바다 · 호수 쪽 변은 물. 물 띠는 옆 변도 물이면 이어지고, 옆 변이 땅이면 꼭짓점에서 0 으로 줄어 이웃의 물 띠와 만난다.
/// 결정론: 사칙연산 · 제곱근 · Floor 만 쓰고(삼각함수 없음), 결과는 정수(땅 종류 · 높이 m · 오브젝트 번호)로 저장한다.
/// 난수는 지방 시드(타일 해시)와 (세계 시드, "province.…") 스트림에서만.
/// </summary>
public static class ProvinceGenerator
{
    /// <summary>타일 하나가 행성 위에서 차지하는 각(라디안): 이웃 칸 사이(정이십면체 모서리 ÷ 72) ÷ 지도의 중심 사이 거리(2A). 행성 전체에 하나.</summary>
    public static readonly double TileAngle = 1.1071487177940904 / GeoGrid.DefaultFrequency / (2 * HexFrame.A);

    private sealed class Noises
    {
        public readonly Noise3 Height, Ridge, Rough, Biome, Coast, Forest, GroundA, GroundB, Marsh, Edge;
        public Noises(ulong seed)
        {
            Noise3 N(string name) => new(RngStreams.Create(seed, "province.noise." + name, RngStreams.GlobalProvince));
            Height = N("height"); Ridge = N("ridge"); Rough = N("rough"); Biome = N("biome"); Coast = N("coast");
            Forest = N("forest"); GroundA = N("ground.a"); GroundB = N("ground.b"); Marsh = N("marsh"); Edge = N("edge");
        }
    }

    private static readonly ConditionalWeakTable<Planet.Planet, Noises> NoiseTable = new();
    private static Noises NoisesFor(Planet.Planet p) => NoiseTable.GetValue(p, pl => new Noises(pl.Seed));

    /// <summary>변(자리) 짝짓기: 가장 북쪽 이웃이 위(자리 0). 오각형은 이웃 다섯 다음 자리를 막는다.</summary>
    public static (int Rotation, int Blocked, int[] SlotCell, int[] SlotCornerTri) Slots(Planet.Planet p, int cell)
    {
        var g = p.Grid;
        var nbs = g.Neighbors(cell);
        var corners = g.Corners(cell);
        int k = nbs.Length;
        int north = 0;
        for (int i = 1; i < k; i++) if (p.Z[nbs[i]] > p.Z[nbs[north]]) north = i;
        int rot = (6 - north) % 6;
        var slotCell = new int[6];
        var slotTri = new int[6];
        for (int s = 0; s < 6; s++)
        {
            int j = (s - rot + 6) % 6;
            slotCell[s] = j < k ? nbs[j] : -1;
            slotTri[s] = corners[Math.Min(j, k - 1)];
        }
        int blocked = k == 5 ? (5 + rot) % 6 : -1;
        return (rot, blocked, slotCell, slotTri);
    }

    // 지형별 거칠기(0~1)
    private static double Roughness(Terrain t) => t switch
    {
        Terrain.Plain => 0.12,
        Terrain.Hill => 0.45,
        Terrain.Plateau => 0.32,
        Terrain.Mountain => 0.8,
        Terrain.HighMountain => 1.0,
        _ => 0.1,
    };

    private static double HalfWidth(int size) => size switch { 1 => 0.85, 2 => 1.6, _ => 2.6 };

    public static ProvinceMap Generate(Planet.Planet p, int cell) => Generate(p, cell, ObjectCatalog.Default);

    public static ProvinceMap Generate(Planet.Planet p, int cell, ObjectCatalog catalog)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        if ((uint)cell >= (uint)p.CellCount) throw new ArgumentOutOfRangeException(nameof(cell));
        var (rot, blocked, slotCell, slotTri) = Slots(p, cell);
        ulong seed = p.ProvinceSeed(cell);
        var m = new ProvinceMap(cell, seed, rot, blocked, slotCell, slotTri);
        var gen = new Builder(p, cell, m, NoisesFor(p), catalog);
        gen.Run();
        m.GenerationSeconds = sw.Elapsed.TotalSeconds;
        return m;
    }

    private sealed class Builder
    {
        private const int N = HexFrame.TileCount, S = HexFrame.Size;
        private readonly Planet.Planet _p;
        private readonly int _c;
        private readonly ProvinceMap _m;
        private readonly Noises _n;
        private readonly ObjectCatalog _cat;
        private readonly Pcg32 _rng;
        private readonly bool _waterCell;

        // 타일마다 (육각형 안만)
        private readonly double[] _px = new double[N], _py = new double[N], _pz = new double[N]; // 행성 위의 점 ÷ TileAngle (타일 단위 좌표)
        private readonly float[] _elev = new float[N], _temp = new float[N], _precip = new float[N], _rough = new float[N];
        private readonly float[] _res; // [r * N + i] 자원 단계(0~2)를 섞은 값
        private readonly float[] _h = new float[N];
        private readonly float[] _riverD = new float[N]; // 강 가장자리까지 부호 거리(음수 = 강물 안)
        private readonly byte[] _riverSize = new byte[N];
        private readonly float[] _waterDepthTiles = new float[N];
        private readonly bool[] _water = new bool[N], _sea = new bool[N];

        public Builder(Planet.Planet p, int c, ProvinceMap m, Noises n, ObjectCatalog cat)
        {
            _p = p; _c = c; _m = m; _n = n; _cat = cat;
            _rng = new Pcg32(m.Seed, 0x9E3779B97F4A7C15UL);
            _waterCell = p.IsWater(c);
            _res = new float[ResourceKinds.Count * N];
        }

        public void Run()
        {
            Fields();
            if (_waterCell) WaterCell();
            else
            {
                Heights();
                Water();
                Rivers();
                Blocked();
                Shore();
                Grounds();
                Clusters();
                Scatter();
            }
            for (int i = 0; i < N; i++) if (!HexFrame.Inside[i]) { _m.Ground[i] = Ground.None; _m.Biome[i] = Biome.None; }
        }

        private ulong TileHash(int i, ulong salt) => StableHash.Combine(_m.Seed, (ulong)i, salt);
        private static double Unit(ulong h) => (h >> 11) * (1.0 / (1UL << 53));
        private static double Smooth(double e0, double e1, double x)
        {
            double t = Math.Clamp((x - e0) / (e1 - e0), 0, 1);
            return t * t * (3 - 2 * t);
        }

        private double Noise(Noise3 nz, int i, double scaleTiles, int octaves = 1, double ox = 0)
        {
            double f = 1.0 / scaleTiles;
            return octaves == 1 ? nz.Sample(_px[i] * f + ox, _py[i] * f + ox * 0.7, _pz[i] * f + ox * 1.3)
                                : nz.Fbm(_px[i] * f + ox, _py[i] * f + ox * 0.7, _pz[i] * f + ox * 1.3, octaves);
        }

        // ───────── 1. 행성 위의 점과 섞은 값 ─────────

        private void Fields()
        {
            var p = _p;
            var g = p.Grid;
            Span<int> cells = stackalloc int[8];
            Span<double> wts = stackalloc double[8];
            Span<double> bw = stackalloc double[BiomeIds.Count];
            double inv = 1.0 / TileAngle;
            int rk = ResourceKinds.Count;
            for (int i = 0; i < N; i++)
            {
                if (!HexFrame.Inside[i]) continue;
                int s = HexFrame.Wedge[i];
                double l1 = HexFrame.L1[i], l2 = HexFrame.L2[i], lc = 1 - l1 - l2;
                int ta = _m.SlotCornerTri[(s + 5) % 6], tb = _m.SlotCornerTri[s];
                double x = lc * p.X[_c] + l1 * p.TX[ta] + l2 * p.TX[tb];
                double y = lc * p.Y[_c] + l1 * p.TY[ta] + l2 * p.TY[tb];
                double z = lc * p.Z[_c] + l1 * p.TZ[ta] + l2 * p.TZ[tb];
                if (s == _m.BlockedSlot)
                {
                    // 오각형의 막힌 쐐기: 두 꼭짓점이 행성의 같은 점이라 그대로 두면 무늬가 줄무늬로 늘어진다.
                    // 쐐기 안쪽만 옆으로 부풀린다(변두리 t = 0, 1 에서는 0 → 옆 쐐기와 이어짐). 이 쐐기는 이웃과 맞닿지 않는다.
                    double tw = l1 + l2 > 1e-9 ? l2 / (l1 + l2) : 0.5, r = l1 + l2;
                    double ux = p.TX[ta] - p.X[_c], uy = p.TY[ta] - p.Y[_c], uz = p.TZ[ta] - p.Z[_c];
                    double vx = p.TY[ta] * uz - p.TZ[ta] * uy, vy = p.TZ[ta] * ux - p.TX[ta] * uz, vz = p.TX[ta] * uy - p.TY[ta] * ux;
                    double a = 2.2 * tw * (1 - tw) * r, b = 4.0 * tw * (1 - tw) * (tw - 0.5) * r;
                    x += vx * a + ux * b; y += vy * a + uy * b; z += vz * a + uz * b;
                }
                double len = Math.Sqrt(x * x + y * y + z * z);
                _px[i] = x / len * inv; _py[i] = y / len * inv; _pz[i] = z / len * inv;

                int n = 0;
                void Add(int cc, double w, Span<int> cs, Span<double> ws, ref int count)
                {
                    for (int q = 0; q < count; q++) if (cs[q] == cc) { ws[q] += w; return; }
                    cs[count] = cc; ws[count] = w; count++;
                }
                Add(_c, lc, cells, wts, ref n);
                Add(g.TriA[ta], l1 / 3, cells, wts, ref n); Add(g.TriB[ta], l1 / 3, cells, wts, ref n); Add(g.TriC[ta], l1 / 3, cells, wts, ref n);
                Add(g.TriA[tb], l2 / 3, cells, wts, ref n); Add(g.TriB[tb], l2 / 3, cells, wts, ref n); Add(g.TriC[tb], l2 / 3, cells, wts, ref n);

                double lw = 0, e = 0, t = 0, pr = 0, ro = 0;
                for (int q = 0; q < n; q++)
                {
                    int cc = cells[q];
                    if (p.IsWater(cc)) continue;
                    double w = wts[q];
                    lw += w;
                    e += w * Math.Max(p.Elevation[cc], 5);
                    t += w * p.Temp10[cc] * 0.1;
                    pr += w * p.PrecipMm[cc];
                    ro += w * Roughness(p.Terrain[cc]);
                    for (int r = 0; r < rk; r++) _res[r * N + i] += (float)(w * p.Resource(cc, r));
                }
                if (lw <= 0)
                {
                    lw = 1; e = Math.Max(p.Elevation[_c], 5); t = p.Temp10[_c] * 0.1; pr = p.PrecipMm[_c]; ro = 0.1;
                    for (int r = 0; r < rk; r++) _res[r * N + i] = p.Resource(_c, r);
                }
                else for (int r = 0; r < rk; r++) _res[r * N + i] /= (float)lw;
                _elev[i] = (float)(e / lw); _temp[i] = (float)(t / lw); _precip[i] = (float)(pr / lw); _rough[i] = (float)(ro / lw);

                // 생물군계: 생물군계마다 무게를 모으고(같은 생물군계의 칸은 더한다), 생물군계마다 다른 잡음을 더해 가장 큰 것
                bw.Clear();
                bool any = false;
                for (int q = 0; q < n; q++)
                {
                    int cc = cells[q];
                    if (p.IsWater(cc)) continue;
                    bw[(int)p.Biome[cc]] += wts[q];
                    any = true;
                }
                Biome best = Biome.None; double bs = double.NegativeInfinity;
                if (any)
                    for (int b = 1; b < bw.Length; b++)
                    {
                        if (bw[b] <= 0) continue;
                        double sc = bw[b] + 0.6 * Noise(_n.Biome, i, 14, 3, b * 37.618);
                        if (sc > bs) { bs = sc; best = (Biome)b; }
                    }
                _m.Biome[i] = best;
            }
        }

        // ───────── 물 칸(호수) — 지방이 아니지만 이웃 보기에서 그린다 ─────────

        private void WaterCell()
        {
            bool sea = _p.IsOcean(_c);
            for (int i = 0; i < N; i++)
            {
                if (!HexFrame.Inside[i]) continue;
                double shore = double.MaxValue;
                for (int s = 0; s < 6; s++)
                {
                    int nb = _m.SlotCell[s];
                    if (nb >= 0 && _p.IsLand(nb)) shore = Math.Min(shore, HexFrame.EdgeDistance(i, s));
                }
                double depth = shore + 2 + 3 * Noise(_n.Coast, i, 14, 2);
                _water[i] = true; _sea[i] = sea;
                _m.Ground[i] = depth > (sea ? 9 : 7) ? Ground.DeepWater : Ground.ShallowWater;
                _m.WaterDepth[i] = (byte)Math.Clamp(depth, 1, 250);
                _m.Height[i] = (short)Math.Clamp(_elev[i], -32000, 32000);
                _m.Biome[i] = Biome.None;
                if (sea) _m.Flags[i] |= TileFlags.Sea;
            }
        }

        // ───────── 2. 높이 ─────────

        private void Heights()
        {
            for (int i = 0; i < N; i++)
            {
                if (!HexFrame.Inside[i]) continue;
                double ro = _rough[i];
                double amp = 5 + ro * 300;
                double baseN = Noise(_n.Height, i, 38, 4);
                double ridge = 1 - Math.Abs(Noise(_n.Ridge, i, 30, 3));
                ridge *= ridge;
                double hills = Noise(_n.Rough, i, 11, 2);
                double h = _elev[i] + amp * (0.75 * baseN + ro * (0.9 * ridge - 0.35) + 0.25 * ro * hills);
                _h[i] = (float)Math.Max(h, 2);
            }
        }

        // ───────── 3. 바다 · 호수 쪽 변의 물 띠 ─────────

        private bool SlotWater(int s)
        {
            int nb = _m.SlotCell[(s + 6) % 6];
            return nb >= 0 && _p.IsWater(nb);
        }

        private void Water()
        {
            for (int s = 0; s < 6; s++)
            {
                int nb = _m.SlotCell[s];
                if (nb < 0 || !_p.IsWater(nb)) continue;
                bool sea = _p.IsOcean(nb);
                bool waterBefore = SlotWater(s + 5), waterAfter = SlotWater(s + 1);
                double frac = sea ? 0.30 : 0.24;
                for (int i = 0; i < N; i++)
                {
                    if (!HexFrame.Inside[i]) continue;
                    double d = HexFrame.EdgeDistance(i, s);
                    if (d > HexFrame.A * 0.75) continue;
                    double t = Math.Clamp(HexFrame.EdgeParam(s, i % S + 0.5, i / S + 0.5), 0, 1);
                    double taper = (waterBefore ? 1 : Smooth(0, 0.38, t)) * (waterAfter ? 1 : Smooth(0, 0.38, 1 - t));
                    double band = HexFrame.A * frac * taper * (1 + 0.45 * Noise(_n.Coast, i, 22, 3)) + 3 * Noise(_n.Coast, i, 5, 1, 50);
                    double depth = band - d;
                    if (depth <= 0) continue;
                    if (!_water[i] || depth > _waterDepthTiles[i]) _waterDepthTiles[i] = (float)depth;
                    _water[i] = true;
                    if (sea) _sea[i] = true;
                }
            }
        }

        // ───────── 4. 강 ─────────

        private (double X, double Y) Crossing(int s, int nb)
        {
            int lo = Math.Min(_c, nb), hi = Math.Max(_c, nb);
            ulong h = StableHash.Combine(_p.Seed, (ulong)lo, (ulong)hi);
            double u = 0.32 + 0.36 * ((h >> 20) & 0xFFFF) / 65535.0;
            int ta = _m.SlotCornerTri[(s + 5) % 6], tb = _m.SlotCornerTri[s];
            double t = ta < tb ? u : 1 - u; // 행성 꼭짓점 번호가 작은 쪽에서 잰다 → 이웃 지방과 같은 점
            return HexFrame.EdgePoint(s, t);
        }

        private readonly List<(double X, double Y)> _crossings = new();

        private double NearCrossing(int i)
        {
            double px = i % S + 0.5, py = i / S + 0.5, best = 0;
            foreach (var (x, y) in _crossings)
            {
                double d = Math.Sqrt((px - x) * (px - x) + (py - y) * (py - y));
                best = Math.Max(best, Smooth(16, 9, d));
            }
            return best;
        }

        private void Rivers()
        {
            var p = _p;
            int size = p.RiverSize[_c];
            var inflow = new List<(int Slot, int Size)>();
            int outSlot = -1;
            for (int s = 0; s < 6; s++)
            {
                int nb = _m.SlotCell[s];
                if (nb < 0) continue;
                if (p.IsLand(nb) && p.RiverSize[nb] > 0 && p.Downstream[nb] == _c) inflow.Add((s, p.RiverSize[nb]));
                if (size > 0 && nb == p.Downstream[_c]) outSlot = s;
            }
            if (size == 0 && inflow.Count == 0) return;
            if (size == 0) size = inflow.Max(f => f.Size);

            for (int i = 0; i < N; i++) _riverD[i] = float.MaxValue;
            double cx = HexFrame.Center, cy = HexFrame.Center;
            double ang = _rng.NextDouble(), rad = 6 + 10 * _rng.NextDouble();
            var (mx, my) = Around(cx, cy, ang, rad);

            var paths = new List<(List<(double X, double Y)> Pts, double W0, double W1, int Size)>();
            foreach (var (s, sz) in inflow)
            {
                var pts = EdgePath(s, _m.SlotCell[s], mx, my);
                pts.Reverse();
                paths.Add((pts, HalfWidth(sz), HalfWidth(size), size));
            }
            if (outSlot >= 0)
                paths.Add((EdgePath(outSlot, _m.SlotCell[outSlot], mx, my), HalfWidth(size), HalfWidth(size), size));
            if (inflow.Count == 0)
            {
                // 샘: 지방 안에서 시작
                var (sx, sy) = Around(cx, cy, _rng.NextDouble(), 18 + 12 * _rng.NextDouble());
                var pts = new List<(double, double)> { (sx, sy), Lerp((sx, sy), (mx, my), 0.5, Jitter(sx, sy, mx, my)), (mx, my) };
                paths.Add((Chaikin(pts, 0, 3), HalfWidth(1) * 0.5, HalfWidth(size), size));
            }

            foreach (var (pts, w0, w1, sz) in paths)
            {
                double total = 0;
                for (int k = 1; k < pts.Count; k++) total += Dist(pts[k - 1], pts[k]);
                double acc = 0;
                for (int k = 1; k < pts.Count; k++)
                {
                    double segLen = Dist(pts[k - 1], pts[k]);
                    double fa = total > 0 ? acc / total : 0, fb = total > 0 ? (acc + segLen) / total : 1;
                    acc += segLen;
                    Stamp(pts[k - 1], pts[k], w0 + (w1 - w0) * fa, w0 + (w1 - w0) * fb, sz);
                }
            }
        }

        /// <summary>강의 한 갈래(합류점 → 변): 변을 수직으로 건너고(변 밖 4타일까지), 안쪽은 구불구불.</summary>
        private List<(double X, double Y)> EdgePath(int s, int nb, double mx, double my)
        {
            var (qx, qy) = Crossing(s, nb);
            _crossings.Add((qx, qy));
            double nx = HexFrame.NX[s], ny = HexFrame.NY[s];
            var inner = (qx - nx * 12, qy - ny * 12);
            var pts = new List<(double X, double Y)> { (mx, my) };
            pts.Add(Inset(Lerp((mx, my), inner, 0.33, Jitter(mx, my, inner.Item1, inner.Item2)), 14));
            pts.Add(Inset(Lerp((mx, my), inner, 0.66, Jitter(mx, my, inner.Item1, inner.Item2)), 14));
            pts.Add(inner);
            pts.Add((qx - nx * 5, qy - ny * 5));
            pts.Add((qx, qy));
            pts.Add((qx + nx * 4, qy + ny * 4));
            return Chaikin(pts, 3, 3);
        }

        private (double, double) Jitter(double ax, double ay, double bx, double by)
        {
            double dx = bx - ax, dy = by - ay, len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-9) return (0, 0);
            double off = (_rng.NextDouble() * 2 - 1) * len * 0.22;
            return (-dy / len * off, dx / len * off);
        }

        /// <summary>변에서 minDist 타일 안쪽으로 들인 점(가운데 쪽으로 당긴다). 강이 건너지 않는 변에 붙지 않게.</summary>
        private static (double, double) Inset((double X, double Y) q, double minDist)
        {
            double x = q.X, y = q.Y;
            for (int k = 0; k < 20; k++)
            {
                double worst = double.MaxValue;
                for (int s = 0; s < 6; s++) worst = Math.Min(worst, HexFrame.A - ((x - HexFrame.Center) * HexFrame.NX[s] + (y - HexFrame.Center) * HexFrame.NY[s]));
                if (worst >= minDist) break;
                x = HexFrame.Center + (x - HexFrame.Center) * 0.9; y = HexFrame.Center + (y - HexFrame.Center) * 0.9;
            }
            return (x, y);
        }

        private static (double, double) Lerp((double X, double Y) a, (double X, double Y) b, double t, (double, double) off) =>
            (a.X + (b.X - a.X) * t + off.Item1, a.Y + (b.Y - a.Y) * t + off.Item2);

        private static (double, double) Around(double cx, double cy, double u, double r)
        {
            // 각도 대신 정사각형 둘레의 점을 원에 투영(삼각함수 없이)
            double a = u * 8;
            int side = (int)Math.Floor(a);
            double f = a - side;
            double x, y;
            switch (side & 7)
            {
                case 0: x = 1; y = -1 + 2 * f * 0.5; break;
                case 1: x = 1; y = f; break;
                case 2: x = 1 - f; y = 1; break;
                case 3: x = -f; y = 1; break;
                case 4: x = -1; y = 1 - f; break;
                case 5: x = -1; y = -f; break;
                case 6: x = -1 + f; y = -1; break;
                default: x = f; y = -1; break;
            }
            double l = Math.Sqrt(x * x + y * y);
            return (cx + x / l * r, cy + y / l * r);
        }

        private static double Dist((double X, double Y) a, (double X, double Y) b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        /// <summary>모서리 깎기(Chaikin). 끝의 keepTail 개 점(변을 수직으로 건너는 곧은 부분)은 그대로 둔다.</summary>
        private static List<(double X, double Y)> Chaikin(List<(double X, double Y)> pts, int keepTail, int iterations)
        {
            for (int it = 0; it < iterations; it++)
            {
                int bend = pts.Count - keepTail; // [0, bend] 까지만 깎는다
                var o = new List<(double X, double Y)> { pts[0] };
                for (int k = 0; k + 1 < pts.Count; k++)
                {
                    var a = pts[k]; var b = pts[k + 1];
                    if (k + 1 >= bend) { o.Add(b); continue; }
                    o.Add((0.75 * a.X + 0.25 * b.X, 0.75 * a.Y + 0.25 * b.Y));
                    o.Add((0.25 * a.X + 0.75 * b.X, 0.25 * a.Y + 0.75 * b.Y));
                }
                if (bend >= pts.Count) o.Add(pts[^1]);
                pts = o;
            }
            return pts;
        }

        private void Stamp((double X, double Y) a, (double X, double Y) b, double wa, double wb, int size)
        {
            double reach = Math.Max(wa, wb) + 7;
            int x0 = Math.Max(0, (int)Math.Floor(Math.Min(a.X, b.X) - reach)), x1 = Math.Min(S - 1, (int)Math.Ceiling(Math.Max(a.X, b.X) + reach));
            int y0 = Math.Max(0, (int)Math.Floor(Math.Min(a.Y, b.Y) - reach)), y1 = Math.Min(S - 1, (int)Math.Ceiling(Math.Max(a.Y, b.Y) + reach));
            double dx = b.X - a.X, dy = b.Y - a.Y, l2 = dx * dx + dy * dy;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    int i = y * S + x;
                    if (!HexFrame.Inside[i]) continue;
                    double px = x + 0.5, py = y + 0.5;
                    double t = l2 > 0 ? Math.Clamp(((px - a.X) * dx + (py - a.Y) * dy) / l2, 0, 1) : 0;
                    double ex = a.X + dx * t - px, ey = a.Y + dy * t - py;
                    double d = Math.Sqrt(ex * ex + ey * ey) - (wa + (wb - wa) * t);
                    if (d < _riverD[i]) { _riverD[i] = (float)d; _riverSize[i] = (byte)size; }
                }
        }

        // ───────── 5. 오각형의 막힌 변 ─────────

        private void Blocked()
        {
            int b = _m.BlockedSlot;
            if (b < 0) return;
            for (int i = 0; i < N; i++)
            {
                if (!HexFrame.Inside[i]) continue;
                double d = HexFrame.EdgeDistance(i, b);
                double cliff = 4.5 + 2.0 * Noise(_n.Edge, i, 6, 2);
                if (d < cliff)
                {
                    _m.Ground[i] = Ground.Cliff;
                    _water[i] = false; _riverD[i] = float.MaxValue;
                    _h[i] += (float)(60 + 120 * (1 - d / cliff));
                }
            }
        }

        // ───────── 6. 물가 · 땅 종류 ─────────

        private readonly float[] _shoreDist = new float[N];

        private void Shore()
        {
            // 강 · 물 띠를 물로 확정하고, 물까지 거리(타일, 8방향 근사)를 잰다
            for (int i = 0; i < N; i++)
            {
                if (!HexFrame.Inside[i] || _m.Ground[i] == Ground.Cliff) continue;
                if (_riverD[i] < 0) { _water[i] = true; _m.Flags[i] |= TileFlags.River; }
                // 강가는 골짜기로 낮게 (변 근처에서도 양쪽이 같은 모양)
                if (_riverD[i] < 8)
                {
                    // 골짜기는 변 근처에서 사라진다(이웃 지도는 이 강을 모른다). 강이 변을 건너는 곳은 양쪽이 같은 모양이라 그대로.
                    double fade = Math.Max(Smooth(1, 10, HexFrame.BorderDistance(i)), NearCrossing(i));
                    double depth = (4 + 9 * _riverSize[i] + 50 * _rough[i]) * fade;
                    _h[i] -= (float)(depth * Smooth(8, -1, _riverD[i]));
                    if (_h[i] < 1) _h[i] = 1;
                }
            }
            const float Big = 1e6f;
            for (int i = 0; i < N; i++) _shoreDist[i] = _water[i] ? 0 : Big;
            const float D1 = 1f, D2 = 1.4142f;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    int i = y * S + x;
                    float v = _shoreDist[i];
                    if (x > 0) v = Math.Min(v, _shoreDist[i - 1] + D1);
                    if (y > 0) v = Math.Min(v, _shoreDist[i - S] + D1);
                    if (x > 0 && y > 0) v = Math.Min(v, _shoreDist[i - S - 1] + D2);
                    if (x < S - 1 && y > 0) v = Math.Min(v, _shoreDist[i - S + 1] + D2);
                    _shoreDist[i] = v;
                }
            for (int y = S - 1; y >= 0; y--)
                for (int x = S - 1; x >= 0; x--)
                {
                    int i = y * S + x;
                    float v = _shoreDist[i];
                    if (x < S - 1) v = Math.Min(v, _shoreDist[i + 1] + D1);
                    if (y < S - 1) v = Math.Min(v, _shoreDist[i + S] + D1);
                    if (x < S - 1 && y < S - 1) v = Math.Min(v, _shoreDist[i + S + 1] + D2);
                    if (x > 0 && y < S - 1) v = Math.Min(v, _shoreDist[i + S - 1] + D2);
                    _shoreDist[i] = v;
                }
            // 바닷가 · 호숫가는 물 높이 쪽으로 낮아진다
            for (int i = 0; i < N; i++)
            {
                if (!HexFrame.Inside[i] || _water[i] || !NearStill(i)) continue;
                double f = Smooth(0, 16, _shoreDist[i]);
                _h[i] = (float)(3 + (_h[i] - 3) * f);
            }
        }

        // 물가 낮추기는 바다 · 호수 띠 근처만(강은 골짜기로 따로)
        private bool NearStill(int i) => _shoreDist[i] < 16 && _waterDepthTiles.Length > 0 && StillWaterNear(i);

        private bool StillWaterNear(int i)
        {
            for (int s = 0; s < 6; s++)
            {
                int nb = _m.SlotCell[s];
                if (nb >= 0 && _p.IsWater(nb) && HexFrame.EdgeDistance(i, s) < HexFrame.A * 0.6) return true;
            }
            return false;
        }

        private void Grounds()
        {
            for (int i = 0; i < N; i++)
            {
                if (!HexFrame.Inside[i]) continue;
                _m.Height[i] = (short)Math.Clamp(Math.Round(_h[i]), -32000, 32000);
                if (_m.Ground[i] == Ground.Cliff) continue;
                if (_water[i])
                {
                    bool river = _riverD[i] < 0 && _waterDepthTiles[i] <= 0;
                    double depth = river ? -_riverD[i] : _waterDepthTiles[i];
                    bool deep = river ? (_riverSize[i] >= 3 && depth > 1.4) : depth > (_sea[i] ? 9 : 7);
                    _m.Ground[i] = deep ? Ground.DeepWater : Ground.ShallowWater;
                    _m.WaterDepth[i] = (byte)Math.Clamp(Math.Round(river ? 1 + depth * 2 : 1 + depth), 1, 250);
                    if (_sea[i] && !river) _m.Flags[i] |= TileFlags.Sea;
                    // 얼어붙은 물
                    if (_temp[i] < -14 && !deep) _m.Ground[i] = Ground.Ice;
                    continue;
                }
                _m.Ground[i] = LandGround(i);
            }
        }

        /// <summary>기울기(m / 타일) — 두 타일 떨어진 높이로 재서 타일 하나짜리 들쭉날쭉함은 무시한다.</summary>
        private double Slope(int i)
        {
            int x = i % S, y = i / S;
            int xa = Math.Max(0, x - 2), xb = Math.Min(S - 1, x + 2), ya = Math.Max(0, y - 2), yb = Math.Min(S - 1, y + 2);
            double hx = (_h[y * S + xb] - _h[y * S + xa]) / Math.Max(1, xb - xa);
            double hy = (_h[yb * S + x] - _h[ya * S + x]) / Math.Max(1, yb - ya);
            return Math.Sqrt(hx * hx + hy * hy);
        }

        private Ground LandGround(int i)
        {
            var b = _m.Biome[i];
            double t = _temp[i] - 0.0065 * (_h[i] - _elev[i]);
            double g1 = Noise(_n.GroundA, i, 16, 3), g2 = Noise(_n.GroundB, i, 8, 2);
            double slope = Slope(i);
            double shore = _shoreDist[i];
            bool nearSea = shore < 4 && SeaNear(i);

            if (b == Biome.Ice) return slope > 40 || g2 > 0.55 ? Ground.Rock : g1 > -0.25 ? Ground.Ice : Ground.Snow;
            if (t < -14) return slope > 16 || g2 > 0.35 ? Ground.Rock : Ground.Snow;
            // 눈은 완만한 곳에 쌓이고, 가파른 곳은 바위가 드러난다
            if (t < -2 && slope < 25 && g1 * 0.5 + 0.5 > (t + 14) / 12) return Ground.Snow;
            double rocky = _rough[i] * 0.85 + slope / 55 + 0.22 * g2 + 0.12 * g1;
            if (rocky > 0.82) return Ground.Rock;
            if (shore <= 1.5)
            {
                if (nearSea) return t < 2 ? Ground.Rock : Ground.Sand;
                if (b == Biome.Desert) return Ground.Sand;
                if (b == Biome.Wetland) return Ground.Marsh;
                return t < 0 ? Ground.Soil : Ground.Mud;
            }
            if (nearSea && _h[i] < 18 && g2 > -0.4 && t >= 2) return Ground.Sand;
            switch (b)
            {
                case Biome.Desert:
                    return g1 > -0.3 ? Ground.Sand : g2 > 0.2 ? Ground.Rock : Ground.Soil;
                case Biome.Wetland:
                {
                    double mz = Noise(_n.Marsh, i, 9, 3);
                    if (mz < -0.42) { _water[i] = true; _m.WaterDepth[i] = 1; return Ground.ShallowWater; }
                    if (mz < 0.05) return Ground.Marsh;
                    return g2 > 0.35 ? Ground.Mud : Ground.Grass;
                }
                case Biome.Alpine:
                    return g1 > 0.1 ? Ground.Rock : g2 > 0.2 ? Ground.Soil : Ground.Grass;
                case Biome.Tundra:
                    return g1 > 0.45 ? Ground.Soil : Ground.Grass;
                case Biome.Savanna:
                    return g1 > 0.35 ? Ground.Soil : g1 < -0.7 ? Ground.Sand : Ground.Grass;
                default:
                {
                    double grass = Math.Clamp((_precip[i] - 150) / 600, 0.45, 0.92);
                    if (b is Biome.Boreal or Biome.Temperate or Biome.Rainforest) grass -= 0.15; // 숲 바닥은 흙이 드러난 곳이 많다
                    return g1 * 0.5 + 0.5 > grass ? Ground.Soil : Ground.Grass;
                }
            }
        }

        private bool SeaNear(int i)
        {
            for (int s = 0; s < 6; s++)
            {
                int nb = _m.SlotCell[s];
                if (nb >= 0 && _p.IsOcean(nb) && HexFrame.EdgeDistance(i, s) < HexFrame.A * 0.6) return true;
            }
            return false;
        }

        // ───────── 7. 오브젝트 ─────────

        private void Clusters()
        {
            var objs = _cat.Objects;
            for (int k = 0; k < objs.Count; k++)
            {
                var o = objs[k];
                if (!o.IsCluster) continue;
                int level = _p.Resource(_c, o.Resource);
                if (level == 0) continue;
                int count = level * o.ClusterCount + _rng.NextInt(-1, 2);
                count = Math.Max(1, count);
                for (int q = 0; q < count; q++)
                {
                    int start = -1;
                    for (int tries = 0; tries < 40 && start < 0; tries++)
                    {
                        int i = _rng.NextInt(N);
                        if (HexFrame.Inside[i] && _m.Object[i] == 0 && o.GroundWeight[(int)_m.Ground[i]] > 0 && HexFrame.BorderDistance(i) > 3) start = i;
                    }
                    if (start < 0) continue;
                    int size = Math.Max(1, (int)Math.Round(o.ClusterSize * (0.6 + 0.8 * _rng.NextDouble())));
                    var frontier = new List<int> { start };
                    int placed = 0;
                    for (int guard = 0; guard < size * 12 && placed < size && frontier.Count > 0; guard++)
                    {
                        int fi = _rng.NextInt(frontier.Count);
                        int i = frontier[fi];
                        if (_m.Object[i] == 0 && _rng.NextDouble() < Math.Min(1, o.GroundWeight[(int)_m.Ground[i]]))
                        {
                            _m.Object[i] = (ushort)(k + 1);
                            _m.Variant[i] = (byte)(TileHash(i, 3) >> 56);
                            placed++;
                            int x = i % S, y = i / S;
                            if (x > 0) frontier.Add(i - 1);
                            if (x < S - 1) frontier.Add(i + 1);
                            if (y > 0) frontier.Add(i - S);
                            if (y < S - 1) frontier.Add(i + S);
                        }
                        frontier.RemoveAt(fi);
                        frontier.RemoveAll(j => !HexFrame.Inside[j] || _m.Object[j] != 0);
                    }
                }
            }
        }

        private void Scatter()
        {
            var objs = _cat.Objects;
            Span<double> pr = stackalloc double[objs.Count];
            for (int i = 0; i < N; i++)
            {
                if (!HexFrame.Inside[i] || _m.Object[i] != 0) continue;
                var gnd = _m.Ground[i];
                if (gnd is Ground.Cliff or Ground.DeepWater or Ground.None) continue;
                int b = (int)_m.Biome[i];
                double forest = -1, sum = 0;
                for (int k = 0; k < objs.Count; k++)
                {
                    var o = objs[k];
                    pr[k] = 0;
                    if (o.IsCluster || o.Density <= 0) continue;
                    double w = o.Density * o.BiomeWeight[b] * o.GroundWeight[(int)gnd];
                    if (w <= 0) continue;
                    if (o.Resource >= 0) w *= _res[o.Resource * N + i] * 0.5; // 많음 = density, 있음 = 절반, 없음 = 0
                    if (o.IsTree)
                    {
                        if (forest < 0) forest = Math.Clamp(1 + 0.9 * Noise(_n.Forest, i, 20, 3), 0.1, 1.9); // 숲과 빈터
                        w *= forest;
                    }
                    pr[k] = w; sum += w;
                }
                if (sum <= 0) continue;
                double scale = sum > 0.9 ? 0.9 / sum : 1;
                double r = Unit(TileHash(i, 1));
                if (r >= sum * scale) continue;
                double acc = 0;
                for (int k = 0; k < objs.Count; k++)
                {
                    acc += pr[k] * scale;
                    if (r < acc)
                    {
                        _m.Object[i] = (ushort)(k + 1);
                        _m.Variant[i] = (byte)(TileHash(i, 3) >> 56);
                        break;
                    }
                }
            }
        }
    }
}
