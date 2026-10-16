using Wtw.Sim.Core;

namespace Wtw.Sim.Planet;

/// <summary>
/// 만들어진 행성(읽기 전용). 시드 + 설정으로 언제든 똑같이 다시 만들 수 있어서 저장하지 않는다.
/// 시뮬이 쓰는 값은 모두 정수 배열이다. 실수 좌표(X/Y/Z, TX/TY/TZ)는 화면과 "좌표" 표시용.
/// 좌표는 행성 기준(+Z = 북극). 격자를 돌려 놓은 뒤의 값이다.
/// </summary>
public sealed class Planet
{
    public GeoGrid Grid { get; }
    public ulong Seed { get; }
    public PlanetSettings Settings { get; }
    public int CellCount => Grid.CellCount;

    // 화면용 실수 좌표 (행성 기준, 단위 구)
    public readonly double[] X, Y, Z;
    public readonly double[] TX, TY, TZ;

    // 시뮬 값 (정수)
    public readonly int[] Elevation;       // m (바다는 음수 = 깊이)
    public readonly Terrain[] Terrain;
    public readonly short[] Temp10;        // 연평균 기온 × 10 (°C)
    public readonly short[] PrecipMm;      // 연 강수량 mm
    public readonly Biome[] Biome;
    public readonly CellFlags[] Flags;
    public readonly int[] Downstream;      // 물이 흘러가는 칸 (바다는 −1)
    public readonly byte[] RiverSize;      // 0 없음 · 1 개울 · 2 강 · 3 큰 강
    public readonly short[] SeaDistance;   // 바다(대양)까지 칸 수 (바다 = 0)
    public readonly byte[] Resources;      // [cell * ResourceKinds.Count + r] = 0 없음 · 1 있음 · 2 많음

    // "왜" 설명용 (정수)
    public readonly short[] TempLatitude10, TempAltitude10, TempInland10;
    public readonly short[] PrecipBelt;    // 위도대 기본 강수 mm
    public readonly byte[] MoisturePct;    // 바람이 싣고 온 습기 %
    public readonly byte[] ShadowPct;      // 비그늘로 줄어든 %
    public readonly int[] ShadowFrom;      // 비를 막은 산 칸 (없으면 −1)
    public readonly byte[] OroPct;         // 바람 받는 산비탈로 늘어난 %
    public readonly short[] WindSeaSteps;  // 바람을 거슬러 바다까지 칸 수 (못 찾으면 −1)
    public readonly Wind[] Wind;

    // 통계
    public int LandCount { get; internal set; }        // 마른 땅(바다·호수 아님)
    public int LakeCount { get; internal set; }
    public int ContinentCells { get; internal set; }
    public int IslandCount { get; internal set; }
    public int PentagonsOnLand { get; internal set; }  // 땅 또는 호수에 걸린 오각형 수
    public int RiverCells { get; internal set; }
    public int RemovedIslets { get; internal set; }    // 너무 작아 바다로 돌린 땅 조각 수
    public int[] IslandSizes { get; internal set; } = Array.Empty<int>();
    public int OrientationTries { get; internal set; }
    public double GenerationSeconds { get; internal set; }

    internal Planet(GeoGrid grid, ulong seed, PlanetSettings settings)
    {
        Grid = grid; Seed = seed; Settings = settings;
        int n = grid.CellCount, t = grid.TriangleCount;
        X = new double[n]; Y = new double[n]; Z = new double[n];
        TX = new double[t]; TY = new double[t]; TZ = new double[t];
        Elevation = new int[n]; Terrain = new Terrain[n]; Temp10 = new short[n]; PrecipMm = new short[n];
        Biome = new Biome[n]; Flags = new CellFlags[n]; Downstream = new int[n]; RiverSize = new byte[n];
        SeaDistance = new short[n]; Resources = new byte[n * ResourceKinds.Count];
        TempLatitude10 = new short[n]; TempAltitude10 = new short[n]; TempInland10 = new short[n];
        PrecipBelt = new short[n]; MoisturePct = new byte[n]; ShadowPct = new byte[n]; ShadowFrom = new int[n];
        OroPct = new byte[n]; WindSeaSteps = new short[n]; Wind = new Wind[n];
    }

    public bool IsWater(int c) => PlanetNames.IsWater(Terrain[c]);
    public bool IsOcean(int c) => PlanetNames.IsOcean(Terrain[c]);
    public bool IsLand(int c) => !PlanetNames.IsWater(Terrain[c]);
    public int Resource(int c, int r) => Resources[c * ResourceKinds.Count + r];
    public double TempC(int c) => Temp10[c] / 10.0;

    /// <summary>E2 에서 지방 지도를 만들 때 쓸 시드. (세계 시드, "province", 칸 번호) 스트림의 첫 값.</summary>
    public ulong ProvinceSeed(int cell)
    {
        var rng = RngStreams.Create(Seed, "province", cell);
        return ((ulong)rng.NextUInt() << 32) | rng.NextUInt();
    }

    /// <summary>위도·경도(도). 표시용 — 시뮬은 쓰지 않는다.</summary>
    public (double Lat, double Lon) LatLon(int c)
    {
        double lat = Math.Asin(Math.Clamp(Z[c], -1, 1)) * 180 / Math.PI;
        double lon = Math.Atan2(Y[c], X[c]) * 180 / Math.PI;
        return (lat, lon);
    }

    private ulong? _fingerprint;

    /// <summary>칸별 정수 값 전부의 안정 해시. 같은 시드 + 설정 = 같은 지문.</summary>
    public ulong Fingerprint => _fingerprint ??= ComputeFingerprint();

    private ulong ComputeFingerprint()
    {
        ulong h = StableHash.Combine(Seed, (ulong)Settings.LandCells, (ulong)(long)Settings.Climate);
        for (int c = 0; c < CellCount; c++)
        {
            ulong a = (ulong)(uint)Elevation[c] | ((ulong)(byte)Terrain[c] << 32) | ((ulong)(byte)Biome[c] << 40) | ((ulong)Flags[c] << 48) | ((ulong)RiverSize[c] << 56);
            ulong b = (ulong)(ushort)Temp10[c] | ((ulong)(ushort)PrecipMm[c] << 16) | ((ulong)(uint)Downstream[c] << 32);
            h = StableHash.Combine(h, a, b);
            ulong r = 0;
            for (int k = 0; k < ResourceKinds.Count; k++) r = r * 3 + Resources[c * ResourceKinds.Count + k];
            h = StableHash.Combine(h, r);
        }
        return h;
    }
}
