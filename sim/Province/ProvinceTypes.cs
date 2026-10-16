using Wtw.Sim.Planet;

namespace Wtw.Sim.Provinces;

/// <summary>
/// 지방 지도 타일의 땅 종류. 값은 지문·화면 색표에 들어가므로 바꾸지 말고 뒤에만 더할 것.
/// None = 육각형 바깥(쓰지 않는 타일).
/// </summary>
public enum Ground : byte
{
    None = 0, Soil, Grass, Sand, Mud, Rock, Snow, Ice, Marsh, ShallowWater, DeepWater, Cliff,
}

public static class GroundInfo
{
    public const int Count = 12;

    /// <summary>데이터(objects.json) 에서 쓰는 아이디. 순서 = Ground 값.</summary>
    public static readonly string[] Ids = { "none", "soil", "grass", "sand", "mud", "rock", "snow", "ice", "marsh", "shallow_water", "deep_water", "cliff" };

    public static string Name(Ground g) => g switch
    {
        Ground.Soil => "흙",
        Ground.Grass => "풀밭",
        Ground.Sand => "모래",
        Ground.Mud => "진흙 땅",
        Ground.Rock => "바위 땅",
        Ground.Snow => "눈",
        Ground.Ice => "얼음",
        Ground.Marsh => "습지",
        Ground.ShallowWater => "얕은 물",
        Ground.DeepWater => "깊은 물",
        Ground.Cliff => "절벽(넘을 수 없음)",
        _ => "쓰지 않는 땅",
    };

    public static bool IsWater(Ground g) => g is Ground.ShallowWater or Ground.DeepWater;
    /// <summary>사람이 걸어 다닐 수 있나(E3 에서 씀). 깊은 물 · 절벽 · 바깥은 못 간다.</summary>
    public static bool IsWalkable(Ground g) => g is not (Ground.None or Ground.DeepWater or Ground.Cliff);
}

/// <summary>데이터(objects.json)에서 쓰는 생물군계 아이디. 순서 = Planet.Biome 값.</summary>
public static class BiomeIds
{
    public static readonly string[] Ids = { "none", "ice", "tundra", "boreal", "temperate", "grassland", "desert", "savanna", "rainforest", "wetland", "alpine" };
    public static int Count => Ids.Length;
}

/// <summary>타일 표시(비트).</summary>
[Flags]
public enum TileFlags : byte { None = 0, River = 1, Sea = 2 }
