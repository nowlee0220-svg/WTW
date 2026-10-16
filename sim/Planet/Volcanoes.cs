using System.Runtime.CompilerServices;
using Wtw.Sim.Core;

namespace Wtw.Sim.Planet;

/// <summary>
/// 화산 칸: 산맥 둘레(산 · 높은 산, 또는 산과 맞닿은 언덕 · 고원)에 드물게 몇 개(땅 5,000칸에 하나꼴, 2~6개).
/// 서로 멀리(약 8칸 이상) 떨어지게 고른다. 분화는 아직 하지 않는다(재난 단계에서).
/// 행성 + 시드로 정해지므로 저장하지 않는다(E1 행성 지문과는 따로 — 결정론은 같음).
/// 난수는 세계 시드의 "planet.volcano" 스트림에서만.
/// </summary>
public static class Volcanoes
{
    private sealed class Table { public bool[] Is = Array.Empty<bool>(); public int[] List = Array.Empty<int>(); public short[] Distance = Array.Empty<short>(); }
    private static readonly ConditionalWeakTable<Planet, Table> Tables = new();

    public static bool IsVolcano(Planet p, int cell) => Tables.GetValue(p, Build).Is[cell];
    public static IReadOnlyList<int> All(Planet p) => Tables.GetValue(p, Build).List;
    /// <summary>가장 가까운 화산까지 칸 수(이웃을 따라, 없으면 −1, 30칸 넘으면 30).</summary>
    public static int Distance(Planet p, int cell) => Tables.GetValue(p, Build).Distance[cell];

    private static bool IsMountain(Terrain t) => t is Terrain.Mountain or Terrain.HighMountain;

    private static Table Build(Planet p)
    {
        var g = p.Grid;
        var cand = new List<int>();
        for (int c = 0; c < p.CellCount; c++)
        {
            if (!p.IsLand(c) || g.IsPentagon(c)) continue;
            int mtn = 0, land = 0;
            foreach (int nb in g.Neighbors(c)) { if (IsMountain(p.Terrain[nb])) mtn++; if (p.IsLand(nb)) land++; }
            if (land < g.NeighborCount(c)) continue; // 둘레가 모두 땅(지도 가운데에 산을 세울 자리)
            if (IsMountain(p.Terrain[c]) || (p.Terrain[c] is Terrain.Hill or Terrain.Plateau && mtn >= 2)) cand.Add(c);
        }
        int want = Math.Clamp((int)Math.Round(p.LandCount / 5000.0), 2, 6);
        var rng = RngStreams.Create(p.Seed, "planet.volcano", RngStreams.GlobalProvince);
        // 후보를 정해진 순서로 섞는다(Fisher-Yates)
        for (int i = cand.Count - 1; i > 0; i--) { int j = rng.NextInt(i + 1); (cand[i], cand[j]) = (cand[j], cand[i]); }
        var chosen = new List<int>();
        const double MinDot = 0.9895; // 약 8.3°(이웃 칸 약 0.88° → 9칸 남짓) 이상 떨어지게
        foreach (int c in cand)
        {
            if (chosen.Count >= want) break;
            bool far = true;
            foreach (int o in chosen)
                if (p.X[c] * p.X[o] + p.Y[c] * p.Y[o] + p.Z[c] * p.Z[o] > MinDot) { far = false; break; }
            if (far) chosen.Add(c);
        }
        chosen.Sort();
        var t = new Table { Is = new bool[p.CellCount], List = chosen.ToArray(), Distance = new short[p.CellCount] };
        Array.Fill(t.Distance, (short)-1);
        var queue = new Queue<int>();
        foreach (int c in chosen) { t.Is[c] = true; t.Distance[c] = 0; queue.Enqueue(c); }
        while (queue.Count > 0)
        {
            int c = queue.Dequeue();
            if (t.Distance[c] >= 30) continue;
            foreach (int nb in g.Neighbors(c))
                if (t.Distance[nb] < 0) { t.Distance[nb] = (short)(t.Distance[c] + 1); queue.Enqueue(nb); }
        }
        return t;
    }
}
