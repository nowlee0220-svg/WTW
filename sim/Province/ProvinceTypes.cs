using Wtw.Sim.Planet;

namespace Wtw.Sim.Provinces;

/// <summary>
/// 지방 지도 타일의 땅 종류. 값은 지문·화면 색표에 들어가므로 바꾸지 말고 뒤에만 더할 것.
/// None = 육각형 바깥(쓰지 않는 타일).
/// </summary>
public enum Ground : byte
{
    None = 0, Soil, Grass, Sand, Mud, Rock, Snow, Ice, Marsh, ShallowWater, DeepWater, Cliff,
    /// <summary>바위(림월드의 바위 벽): 타일 하나를 차지하는 단단한 바위, 지나갈 수 없다. 모이면 바위 덩어리 · 산.</summary>
    Stone,
    /// <summary>자갈 바닥(강가 · 물가).</summary>
    Gravel,
    /// <summary>화산의 분화구: 지나갈 수 없다(분화는 재난 단계에서).</summary>
    Crater,
}

public static class GroundInfo
{
    public const int Count = 15;

    /// <summary>데이터(objects.json) 에서 쓰는 아이디. 순서 = Ground 값.</summary>
    public static readonly string[] Ids = { "none", "soil", "grass", "sand", "mud", "rock", "snow", "ice", "marsh", "shallow_water", "deep_water", "cliff", "stone", "gravel", "crater" };

    public static string Name(Ground g) => g switch
    {
        Ground.Soil => "흙",
        Ground.Grass => "풀밭",
        Ground.Sand => "모래",
        Ground.Mud => "진흙 땅",
        Ground.Rock => "바위 바닥",
        Ground.Snow => "눈",
        Ground.Ice => "얼음",
        Ground.Marsh => "습지",
        Ground.ShallowWater => "얕은 물",
        Ground.DeepWater => "깊은 물",
        Ground.Cliff => "절벽(넘을 수 없음)",
        Ground.Stone => "바위",
        Ground.Gravel => "자갈",
        Ground.Crater => "분화구",
        _ => "쓰지 않는 땅",
    };

    public static bool IsWater(Ground g) => g is Ground.ShallowWater or Ground.DeepWater;
    /// <summary>사람이 걸어 다닐 수 있나(E3 에서 씀). 깊은 물 · 절벽 · 바위 · 바깥은 못 간다.</summary>
    public static bool IsWalkable(Ground g) => g is not (Ground.None or Ground.DeepWater or Ground.Cliff or Ground.Stone or Ground.Crater);
}

/// <summary>데이터(objects.json)에서 쓰는 생물군계 아이디. 순서 = Planet.Biome 값.</summary>
public static class BiomeIds
{
    public static readonly string[] Ids = { "none", "ice", "tundra", "boreal", "temperate", "grassland", "desert", "savanna", "rainforest", "wetland", "alpine" };
    public static int Count => Ids.Length;
}

/// <summary>타일 표시(비트).</summary>
[Flags]
public enum TileFlags : byte
{
    None = 0, River = 1, Sea = 2,
    /// <summary>위가 바위로 덮인 곳(동굴 바닥).</summary>
    Roof = 4,
    /// <summary>아직 아무도 보지 못한 곳: 바위 덩어리 안쪽(숨은 광맥) · 동굴. E3 부터 사람이 들어가면 걷힌다(ProvinceChanges).</summary>
    Hidden = 8,
    /// <summary>화산(산 · 분화구 둘레).</summary>
    Volcano = 16,
    /// <summary>(E3b) 개울: 지방 안의 작은 물줄기(이웃 지방과 경계에서 이어진다).</summary>
    Stream = 32,
    /// <summary>(E3b) 샘 · 웅덩이 · 고인 물(지방 안쪽, 경계에 닿지 않는다). 샘이면 Stream 도 함께(샘에서 개울이 흘러나간다).</summary>
    Pool = 64,
    /// <summary>(E3b) 샘이 솟는 자리.</summary>
    Spring = 128,
}
