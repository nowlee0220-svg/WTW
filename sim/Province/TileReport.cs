using System.Globalization;
using Wtw.Sim.Data;
using Wtw.Sim.Planet;

namespace Wtw.Sim.Provinces;

/// <summary>지방 지도의 타일 하나를 사람이 읽는 말로: 작은 카드(이름 · 땅 · 다루는 법)와 정보 창(자세히).</summary>
public sealed record TileReport(string Title, string Subtitle, string Uses, IReadOnlyList<(string Label, string Value)> Facts);

public static class TileDescriber
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>자리(변) 번호 → 대략의 방향. 자리 0 = 가장 북쪽 이웃.</summary>
    public static string Direction(int slot) => slot switch { 0 => "북쪽", 1 => "북동쪽", 2 => "남동쪽", 3 => "남쪽", 4 => "남서쪽", 5 => "북서쪽", _ => "" };

    /// <summary>지방 이름(아직 이름이 없어서 생물군계 + 지형 + 칸 번호).</summary>
    public static string ProvinceName(Planet.Planet p, int c) =>
        p.IsOcean(c) ? "바다" : p.Terrain[c] == Terrain.Lake ? $"호수 (칸 {c})" : $"{PlanetNames.Of(p.Biome[c])} {PlanetNames.Of(p.Terrain[c])} (칸 {c})";

    public static TileReport Describe(Planet.Planet p, ProvinceMap m, int x, int y, ObjectCatalog? catalog = null)
    {
        catalog ??= ObjectCatalog.Default;
        int i = ProvinceMap.Index(x, y);
        var g = m.Ground[i];
        int o = m.ObjectAt(i);
        var def = o >= 0 ? catalog.Objects[o] : null;
        string ground = GroundInfo.Name(g);
        if ((m.Flags[i] & TileFlags.River) != 0) ground += $" ({PlanetNames.RiverName(Math.Max(1, (int)p.RiverSize[m.Cell]))})";
        else if ((m.Flags[i] & TileFlags.Sea) != 0) ground += " (바다)";
        else if (GroundInfo.IsWater(g)) ground += p.IsLand(m.Cell) && m.Biome[i] == Biome.Wetland ? " (웅덩이)" : " (호수)";
        string biome = m.Biome[i] == Biome.None ? "" : PlanetNames.Of(m.Biome[i]);

        var facts = new List<(string, string)>();
        if (def is not null)
        {
            facts.Add(("오브젝트", def.Name));
            foreach (var u in def.Uses) facts.Add(($"  {u.Action.Name}", "→ " + string.Join(", ", u.Yields.Select(y => y.Name))));
        }
        facts.Add(("땅 종류", ground));
        if (biome.Length > 0) facts.Add(("생물군계", biome));
        facts.Add(GroundInfo.IsWater(g) ? ("물 깊이", m.WaterDepth[i] <= 2 ? "얕음" : m.WaterDepth[i] <= 8 ? "보통" : "깊음") : ("높이", $"{m.Height[i].ToString("N0", Inv)}m"));
        facts.Add(("걸어갈 수", GroundInfo.IsWalkable(g) ? "있음" : "없음"));
        facts.Add(("지방", ProvinceName(p, m.Cell)));
        facts.Add(("타일", $"({x}, {y})"));

        string title = def?.Name ?? ground;
        string sub = def is null ? (biome.Length > 0 ? biome : "") : (biome.Length > 0 ? $"{ground} · {biome}" : ground);
        string uses = def?.UsesText ?? (g == Ground.Cliff ? "넘을 수 없는 절벽 (오각형 지방의 막힌 변)" : "");
        return new TileReport(title, sub, uses, facts);
    }
}
