namespace Wtw.Sim.Planet;

/// <summary>기후 경향(새 세계 설정). 값은 저장 파일에 숫자로 남으므로 바꾸지 말 것.</summary>
public enum ClimateBias { Cold = -1, Normal = 0, Warm = 1 }

/// <summary>행성 설정: 땅 칸 수(16,000 / 20,000 / 24,000) · 기후 경향. 같은 시드면 땅 비율만 바뀐 같은 행성.</summary>
public readonly record struct PlanetSettings(int LandCells, ClimateBias Climate)
{
    public static readonly int[] LandChoices = { 16_000, 20_000, 24_000 };
    public static PlanetSettings Default => new(16_000, ClimateBias.Normal);
}

/// <summary>지형(높이 단계). 값은 지문에 들어가므로 바꾸지 말 것.</summary>
public enum Terrain : byte { DeepSea = 0, ShallowSea = 1, Lake = 2, Plain = 3, Hill = 4, Plateau = 5, Mountain = 6, HighMountain = 7 }

/// <summary>생물군계. None = 물(바다·호수).</summary>
public enum Biome : byte { None = 0, Ice, Tundra, Boreal, Temperate, Grassland, Desert, Savanna, Rainforest, Wetland, Alpine }

/// <summary>탁월풍(위도대별).</summary>
public enum Wind : byte { Trade = 0, Westerly = 1, Polar = 2 }

/// <summary>칸 표시(비트).</summary>
[Flags]
public enum CellFlags : byte { None = 0, Coast = 1, LakeShore = 2, River = 4 }

/// <summary>
/// 칸의 자원. 아이디는 data/resources.json 과 같고, E4 재료 데이터가 이 아이디를 그대로 쓴다.
/// 순서는 Planet.Resources 배열 순서 — 바꾸지 말고 뒤에만 더할 것.
/// </summary>
public static class ResourceKinds
{
    public static readonly string[] Ids = { "stone", "flint", "clay", "copper", "tin", "iron", "salt", "wood", "game", "fish" };
    public static readonly string[] Names = { "돌", "부싯돌", "진흙", "구리", "주석", "철", "소금", "숲(나무)", "사냥감", "물고기" };
    public static int Count => Ids.Length;
    public const int Stone = 0, Flint = 1, Clay = 2, Copper = 3, Tin = 4, Iron = 5, Salt = 6, Wood = 7, Game = 8, Fish = 9;

    public static string LevelName(int level) => level switch { 0 => "없음", 1 => "있음", _ => "많음" };
}

public static class PlanetNames
{
    public static string Of(Terrain t) => t switch
    {
        Terrain.DeepSea => "깊은 바다",
        Terrain.ShallowSea => "얕은 바다",
        Terrain.Lake => "호수",
        Terrain.Plain => "평야",
        Terrain.Hill => "언덕",
        Terrain.Plateau => "고원",
        Terrain.Mountain => "산",
        Terrain.HighMountain => "높은 산",
        _ => "?",
    };

    public static string Of(Biome b) => b switch
    {
        Biome.None => "물",
        Biome.Ice => "얼음",
        Biome.Tundra => "툰드라",
        Biome.Boreal => "침엽수림",
        Biome.Temperate => "온대림",
        Biome.Grassland => "초원",
        Biome.Desert => "사막",
        Biome.Savanna => "사바나",
        Biome.Rainforest => "열대우림",
        Biome.Wetland => "습지",
        Biome.Alpine => "고산",
        _ => "?",
    };

    public static string Of(ClimateBias c) => c switch { ClimateBias.Cold => "추움", ClimateBias.Warm => "따뜻함", _ => "보통" };

    public static string Of(Wind w) => w switch
    {
        Wind.Trade => "무역풍(동쪽에서 부는 바람)",
        Wind.Westerly => "편서풍(서쪽에서 부는 바람)",
        _ => "극동풍(동쪽에서 부는 찬 바람)",
    };

    public static string RiverName(int size) => size switch { 1 => "개울", 2 => "강", 3 => "큰 강", _ => "" };

    public static bool IsWater(Terrain t) => t <= Terrain.Lake;
    public static bool IsOcean(Terrain t) => t <= Terrain.ShallowSea;
}
