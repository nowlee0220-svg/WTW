using Wtw.Sim.Core;
using Wtw.Sim.Planet;

namespace Wtw.Sim.Provinces;

/// <summary>
/// 지방 하나의 2D 지도(읽기 전용). 행성 칸 정보 + 지방 시드로 언제나 똑같이 다시 만들 수 있어서 저장하지 않는다
/// (저장 파일에는 나중에 "바뀐 것"만 — ProvinceChanges).
///
/// 변(자리) s = 0..5 와 행성 이웃의 짝: SlotCell[s] = 그 변 너머의 칸(막힌 변은 −1).
/// 오각형 칸은 이웃이 다섯이라 한 변(BlockedSlot)을 절벽으로 막는다.
/// SlotCornerTri[s] = 꼭짓점 H[s](변 s 와 s+1 사이)에 해당하는 행성 꼭짓점(삼각형) 번호 — 이웃 지방과 같은 점.
/// </summary>
public sealed class ProvinceMap
{
    public int Cell { get; }
    public ulong Seed { get; }
    /// <summary>행성 이웃 k 가 자리 (k + Rotation) % 6 에 온다. 가장 북쪽 이웃이 위(자리 0)에 오도록 고른 값.</summary>
    public int Rotation { get; }
    public int BlockedSlot { get; }
    public int[] SlotCell { get; }
    public int[] SlotCornerTri { get; }

    public readonly Ground[] Ground = new Ground[HexFrame.TileCount];
    /// <summary>높이(m). 물은 물밑 높이.</summary>
    public readonly short[] Height = new short[HexFrame.TileCount];
    /// <summary>물 깊이(단계, 0 = 물이 아님). 물가에서 멀수록 깊다.</summary>
    public readonly byte[] WaterDepth = new byte[HexFrame.TileCount];
    public readonly Biome[] Biome = new Biome[HexFrame.TileCount];
    /// <summary>오브젝트: 0 = 없음, 아니면 ObjectCatalog 번호 + 1.</summary>
    public readonly ushort[] Object = new ushort[HexFrame.TileCount];
    /// <summary>같은 종류도 조금씩 다르게(크기 · 기울기 · 색) 그리기 위한 값.</summary>
    public readonly byte[] Variant = new byte[HexFrame.TileCount];
    public readonly TileFlags[] Flags = new TileFlags[HexFrame.TileCount];
    /// <summary>그 자리의 바위 종류(RockCatalog 번호). 바위 · 바위 바닥 · 자갈의 돌이 이것이다. 물 · 바깥은 255.</summary>
    public readonly byte[] Rock = new byte[HexFrame.TileCount];
    /// <summary>바위 타일 안의 광맥: 0 = 없음, 아니면 RockCatalog.Ores 번호 + 1. 숨은(Hidden) 바위 속 광맥은 아직 아무도 모른다.</summary>
    public readonly byte[] Ore = new byte[HexFrame.TileCount];

    public double GenerationSeconds { get; internal set; }

    internal ProvinceMap(int cell, ulong seed, int rotation, int blockedSlot, int[] slotCell, int[] slotCornerTri)
    {
        Cell = cell; Seed = seed; Rotation = rotation; BlockedSlot = blockedSlot; SlotCell = slotCell; SlotCornerTri = slotCornerTri;
    }

    public static int Index(int x, int y) => y * HexFrame.Size + x;
    public bool InBounds(int x, int y) => (uint)x < HexFrame.Size && (uint)y < HexFrame.Size;
    public int ObjectAt(int i) => Object[i] - 1;
    public int OreAt(int i) => Ore[i] - 1;
    public bool IsHidden(int i) => (Flags[i] & TileFlags.Hidden) != 0;
    /// <summary>바깥 면에 드러나 보이는 광맥(숨은 것은 −1).</summary>
    public int VisibleOreAt(int i) => IsHidden(i) ? -1 : Ore[i] - 1;

    /// <summary>행성 칸 nb 가 어느 자리(변)에 있나. 이웃이 아니면 −1.</summary>
    public int SlotOf(int nb) => System.Array.IndexOf(SlotCell, nb);

    /// <summary>오브젝트 종류별 개수([카탈로그 번호]).</summary>
    public int[] ObjectCounts(int kinds)
    {
        var n = new int[kinds];
        foreach (var o in Object) if (o > 0) n[o - 1]++;
        return n;
    }

    public int[] GroundCounts()
    {
        var n = new int[GroundInfo.Count];
        foreach (var g in Ground) n[(int)g]++;
        return n;
    }

    /// <summary>대략의 메모리(바이트): 타일 배열들.</summary>
    public static int ApproxBytes => HexFrame.TileCount * (1 + 2 + 1 + 1 + 2 + 1 + 1 + 1 + 1);

    private ulong? _fingerprint;

    /// <summary>타일별 값 전부의 안정 해시. 같은 행성 + 같은 칸 = 같은 지문.</summary>
    public ulong Fingerprint => _fingerprint ??= ComputeFingerprint();

    private ulong ComputeFingerprint()
    {
        ulong h = StableHash.Combine(Seed, (ulong)Cell, (ulong)(Rotation * 8 + BlockedSlot + 1));
        for (int i = 0; i < HexFrame.TileCount; i++)
        {
            ulong a = (ulong)(byte)Ground[i] | ((ulong)(ushort)Height[i] << 8) | ((ulong)(byte)Biome[i] << 24) | ((ulong)Object[i] << 32) | ((ulong)Variant[i] << 48) | ((ulong)Flags[i] << 56);
            h = StableHash.Combine(h, a, (ulong)WaterDepth[i] | ((ulong)Rock[i] << 8) | ((ulong)Ore[i] << 16));
        }
        return h;
    }
}
