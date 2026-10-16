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

    public static TileReport Describe(Planet.Planet p, ProvinceMap m, int x, int y, ObjectCatalog? catalog = null, RockCatalog? rocks = null)
    {
        catalog ??= ObjectCatalog.Default;
        rocks ??= RockCatalog.Default;
        int i = ProvinceMap.Index(x, y);
        var g = m.Ground[i];
        if (m.IsHidden(i))
        {
            bool cave = (m.Flags[i] & TileFlags.Roof) != 0;
            // 숨은 곳: 아는 것은 "바위 속" 이라는 것뿐 (동굴 · 광맥은 사람이 들어가 봐야 안다 — E3)
            var hf = new List<(string, string)> { ("보이는 것", "아직 아무도 들어가 보지 않은 바위 속"), ("지방", ProvinceName(p, m.Cell)), ("타일", $"({x}, {y})") };
            return new TileReport(cave ? "보이지 않는 곳" : "바위 속", "아직 아무도 들어가 보지 않았습니다", "", hf);
        }
        string rockName = m.Rock[i] < rocks.Rocks.Count ? rocks.Rocks[m.Rock[i]].Name : "";
        int o = m.ObjectAt(i);
        var def = o >= 0 ? catalog.Objects[o] : null;
        int ore = m.VisibleOreAt(i);
        var oreDef = ore >= 0 ? rocks.Ores[ore] : null;
        string ground = GroundInfo.Name(g);
        if (g == Ground.Stone) ground = $"바위 ({rockName})";
        else if (g is Ground.Rock or Ground.Gravel && rockName.Length > 0) ground += $" ({rockName})";
        if ((m.Flags[i] & TileFlags.River) != 0) ground += $" ({PlanetNames.RiverName(Math.Max(1, (int)p.RiverSize[m.Cell]))})";
        else if ((m.Flags[i] & TileFlags.Sea) != 0) ground += " (바다)";
        else if (GroundInfo.IsWater(g)) ground += p.IsLand(m.Cell) && m.Biome[i] == Biome.Wetland ? " (웅덩이)" : " (호수)";
        string biome = m.Biome[i] == Biome.None ? "" : PlanetNames.Of(m.Biome[i]);

        // 다루는 법
        string uses = "";
        var facts = new List<(string, string)>();
        if (oreDef is not null)
        {
            var ys = oreDef.Yields.Select(it => it.Name).Concat(rocks.Rocks[m.Rock[i]].Yields.Select(it => it.Name));
            uses = "캐기 → " + string.Join(", ", ys);
            facts.Add(("광맥", $"{oreDef.Name} ({rockName} 속)"));
            facts.Add(("  캐기", "→ " + string.Join(", ", ys)));
        }
        else if (g == Ground.Stone)
        {
            uses = "캐기 → " + string.Join(", ", rocks.Rocks[m.Rock[i]].Yields.Select(it => it.Name));
            facts.Add(("  캐기", "→ " + string.Join(", ", rocks.Rocks[m.Rock[i]].Yields.Select(it => it.Name))));
        }
        else if (def is not null)
        {
            uses = def.UsesText;
            facts.Add(("오브젝트", def.Name));
            foreach (var u in def.Uses) facts.Add(($"  {u.Action.Name}", "→ " + string.Join(", ", u.Yields.Select(y => y.Name))));
        }
        else if (g == Ground.Gravel) uses = "줍기 → 돌 (자갈 바닥)";
        else if (g == Ground.Cliff) uses = "넘을 수 없는 절벽 (오각형 지방의 막힌 변)";
        facts.Add(("땅 종류", ground));
        if (biome.Length > 0) facts.Add(("생물군계", biome));
        if (g != Ground.Stone && rockName.Length > 0 && !GroundInfo.IsWater(g)) facts.Add(("땅 밑 바위", rockName));
        facts.Add(GroundInfo.IsWater(g) ? ("물 깊이", m.WaterDepth[i] <= 2 ? "얕음" : m.WaterDepth[i] <= 8 ? "보통" : "깊음") : ("높이", $"{m.Height[i].ToString("N0", Inv)}m"));
        facts.Add(("걸어갈 수", GroundInfo.IsWalkable(g) ? "있음" : "없음"));
        facts.Add(("지방", ProvinceName(p, m.Cell)));
        facts.Add(("타일", $"({x}, {y})"));

        string title = oreDef?.Name ?? (g == Ground.Stone ? rockName : def?.Name ?? ground);
        string sub = oreDef is not null || g == Ground.Stone ? "바위 · 지나갈 수 없음" : def is null ? biome : (biome.Length > 0 ? $"{ground} · {biome}" : ground);
        return new TileReport(title, sub, uses, facts);
    }
}
