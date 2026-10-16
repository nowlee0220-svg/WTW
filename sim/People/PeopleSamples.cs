using Wtw.Sim.Planet;
using Wtw.Sim.Provinces;

namespace Wtw.Sim.People;

/// <summary>측정 · 테스트 · 자동 확인용: 사람을 뿌리기 좋은 칸과 자리(결정론 — 칸 번호 순으로 가장 좋은 것).</summary>
public static class PeopleSamples
{
    /// <summary>"따뜻하고 열매가 많은 땅(숲 · 초원, 강가)". rank = 0 이면 가장 좋은 칸, 1 이면 그다음 …</summary>
    public static int Home(Planet.Planet p, int rank = 0) => Ranked(p, HomeScore).Skip(rank).FirstOrDefault(-1);

    /// <summary>추운 땅(툰드라).</summary>
    public static int Cold(Planet.Planet p) => Ranked(p, (pl, c) => pl.Biome[c] == Biome.Tundra ? 100 - Math.Abs(pl.Temp10[c] + 80) / 10 + (pl.RiverSize[c] > 0 ? 10 : 0) : int.MinValue).FirstOrDefault(-1);

    public static IEnumerable<int> Ranked(Planet.Planet p, Func<Planet.Planet, int, int> score)
    {
        var list = new List<(int Score, int Cell)>();
        for (int c = 0; c < p.CellCount; c++)
        {
            if (!p.IsLand(c) || p.Terrain[c] >= Terrain.Mountain) continue;
            int s = score(p, c);
            if (s > int.MinValue / 2) list.Add((s, c));
        }
        return list.OrderByDescending(x => x.Score).ThenBy(x => x.Cell).Select(x => x.Cell);
    }

    private static int HomeScore(Planet.Planet p, int c)
    {
        var b = p.Biome[c];
        if (b is not (Biome.Temperate or Biome.Rainforest or Biome.Grassland or Biome.Savanna)) return int.MinValue;
        int t = p.Temp10[c];
        int s = 100 - Math.Abs(t - 190) / 4;
        if (p.RiverSize[c] > 0) s += 30 + p.RiverSize[c] * 5;
        if (b is Biome.Temperate or Biome.Rainforest) s += 25;
        s += p.Resource(c, ResourceKinds.Wood) * 10;
        if (p.Terrain[c] == Terrain.Plain) s += 5;
        if ((p.Flags[c] & CellFlags.Coast) != 0) s -= 10;
        return s;
    }

    /// <summary>그 지방 가운데에서 가까운, 물 마실 곳 가까이의 걸을 수 있는 타일들(뿌릴 자리).</summary>
    public static List<(int X, int Y)> SpawnSpots(ProvinceLife life, int count, int seed = 0)
    {
        // 가운데에서 가장 가까운 물 마실 곳(없으면 가운데)
        int cx = 75, cy = 75;
        int water = life.NearestDrink(75, 75, 60);
        if (water >= 0) { cx = water % HexFrame.Size; cy = water / HexFrame.Size; }
        var spots = new List<(int, int)>();
        // 가장 넓은 이어진 땅(강 가운데 작은 섬 같은 곳에 뿌리지 않게)
        var size = new Dictionary<int, int>();
        foreach (int r0 in life.Region) if (r0 >= 0) size[r0] = size.GetValueOrDefault(r0) + 1;
        int startReg = size.Count == 0 ? -1 : size.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).First().Key;
        if (water >= 0 && life.Region[water] != startReg)
        {
            int w2 = -1; long bd = long.MaxValue;
            for (int t = 0; t < HexFrame.TileCount; t++)
                if (life.Drink[t] && life.Region[t] == startReg) { long d = ProvinceLife.D2(t, 75, 75); if (d < bd) { bd = d; w2 = t; } }
            if (w2 >= 0) { cx = w2 % HexFrame.Size; cy = w2 / HexFrame.Size; }
        }
        for (int r = 2; r < 70 && spots.Count < count; r++)
            for (int k = 0; k < 8 * r && spots.Count < count; k++)
            {
                double a = (k + seed * 0.37) / (8.0 * r) * Math.PI * 2;
                int x = cx + (int)Math.Round(Math.Cos(a) * r), y = cy + (int)Math.Round(Math.Sin(a) * r);
                if ((uint)x >= HexFrame.Size || (uint)y >= HexFrame.Size) continue;
                int t = y * HexFrame.Size + x;
                if (!life.Walk[t] || HexFrame.BorderDistance(t) < 6) continue;
                if (life.Region[t] != startReg || spots.Contains((x, y))) continue;
                if (k % 3 != 0) continue; // 조금 흩어지게
                spots.Add((x, y));
            }
        return spots;
    }
}
