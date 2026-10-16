using Wtw.Sim.Planet;

namespace Wtw.Sim.Provinces;

/// <summary>측정 · 테스트 · 스크린샷용: 종류별로 그럴듯한 땅 칸 하나를 고른다(결정론 — 칸 번호 순으로 첫 번째 가장 좋은 칸).</summary>
public static class ProvinceSamples
{
    public static readonly string[] Kinds = { "plain", "forest", "mountain", "coast", "river", "desert", "tundra", "pentagon", "ore" };

    public static string Name(string kind) => kind switch
    {
        "plain" => "평야", "forest" => "숲", "mountain" => "산", "coast" => "해안", "river" => "강",
        "desert" => "사막", "tundra" => "툰드라", "pentagon" => "오각형", "ore" => "광맥 언덕", "riverbank" => "자갈 강가", "wetland" => "습지", "savanna" => "사바나", "rainforest" => "열대우림",
        _ => kind,
    };

    /// <summary>그 종류의 칸(없으면 −1).</summary>
    public static int Find(Planet.Planet p, string kind)
    {
        int best = -1, bestScore = int.MinValue;
        for (int c = 0; c < p.CellCount; c++)
        {
            if (!p.IsLand(c)) continue;
            int sc = Score(p, c, kind);
            if (sc > bestScore) { bestScore = sc; best = c; }
        }
        return bestScore > int.MinValue / 2 ? best : -1;
    }

    private static int Score(Planet.Planet p, int c, string kind)
    {
        const int No = int.MinValue / 2;
        var t = p.Terrain[c]; var b = p.Biome[c]; var f = p.Flags[c];
        bool coast = (f & CellFlags.Coast) != 0, river = p.RiverSize[c] > 0;
        int wood = p.Resource(c, ResourceKinds.Wood);
        int landNbs = 0;
        foreach (int nb in p.Grid.Neighbors(c)) if (p.IsLand(nb)) landNbs++;
        switch (kind)
        {
            case "plain": return t == Terrain.Plain && !coast && !river && b is Biome.Grassland or Biome.Temperate or Biome.Savanna ? 100 + landNbs * 10 - wood * 20 : No;
            case "forest": return wood == 2 && b is Biome.Temperate or Biome.Boreal && !coast && t <= Terrain.Hill ? 100 + landNbs * 5 + (b == Biome.Temperate ? 20 : 0) - (river ? 10 : 0) : No;
            case "mountain": return t >= Terrain.Mountain && !coast ? 100 + (t == Terrain.HighMountain ? 10 : 0) + landNbs * 5 : No;
            case "coast": return coast && t == Terrain.Plain && p.Temp10[c] > 100 ? 100 + (6 - landNbs) * 8 - Math.Abs(landNbs - 3) * 12 : No;
            case "river": return river && !coast && p.RiverSize[c] >= 2 ? 100 + p.RiverSize[c] * 20 + landNbs * 5 : No;
            case "desert": return b == Biome.Desert ? 100 + landNbs * 5 - (coast ? 30 : 0) : No;
            case "tundra": return b == Biome.Tundra ? 100 + landNbs * 5 - (coast ? 30 : 0) : No;
            case "pentagon": return p.Grid.IsPentagon(c) ? 100 + landNbs * 5 : No;
            case "wetland": return b == Biome.Wetland ? 100 + landNbs * 5 : No;
            case "savanna": return b == Biome.Savanna ? 100 + landNbs * 5 + (river ? 10 : 0) : No;
            case "rainforest": return b == Biome.Rainforest ? 100 + landNbs * 5 : No;
            case "riverbank": // 자갈 강가(습지 · 사막이 아닌 강)
                return river && !coast && b is Biome.Temperate or Biome.Grassland or Biome.Boreal or Biome.Savanna ? 100 + p.RiverSize[c] * 20 + landNbs * 5 : No;
            case "ore": // 구리 · 철이 많은 언덕(광맥이 드러난 바위 덩어리를 보기 좋게)
                return t is Terrain.Hill or Terrain.Plateau && p.Temp10[c] > 50 && p.Resource(c, ResourceKinds.Copper) == 2 ? 100 + p.Resource(c, ResourceKinds.Iron) * 20 + p.Resource(c, ResourceKinds.Tin) * 10 + landNbs * 3 - (coast ? 30 : 0) : No;
            default: return No;
        }
    }
}
