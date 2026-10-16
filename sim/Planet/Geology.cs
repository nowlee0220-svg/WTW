using System.Runtime.CompilerServices;
using Wtw.Sim.Core;
using Wtw.Sim.Data;

namespace Wtw.Sim.Planet;

/// <summary>
/// 행성 칸마다 나는 바위 종류(주된 바위 · 섞인 바위). 지형 · 생물군계에 맞는 정도(data/rocks.json) × 넓은 지질 잡음
/// (몇 칸 크기의 지역 — 이웃 칸은 대개 같은 바위). 행성 + 시드로 정해지므로 저장하지 않는다(행성 지문과는 따로).
/// 결정론: 세계 시드의 "planet.geology" 스트림 잡음 · 사칙연산만.
/// </summary>
public static class Geology
{
    private sealed class Table { public byte[] Primary = Array.Empty<byte>(), Secondary = Array.Empty<byte>(); }
    private static readonly ConditionalWeakTable<Planet, Table> Tables = new();

    public static (int Primary, int Secondary) RocksOf(Planet p, int cell)
    {
        var t = Tables.GetValue(p, Build);
        return (t.Primary[cell], t.Secondary[cell]);
    }

    private static Table Build(Planet p)
    {
        var cat = RockCatalog.Default;
        int k = cat.Rocks.Count;
        var noise = new Noise3[k];
        for (int r = 0; r < k; r++) noise[r] = new Noise3(RngStreams.Create(p.Seed, "planet.geology", r));
        var t = new Table { Primary = new byte[p.CellCount], Secondary = new byte[p.CellCount] };
        const double F = 9.0; // 지역 크기: 칸 몇 개
        for (int c = 0; c < p.CellCount; c++)
        {
            var ter = p.IsWater(c) ? Terrain.Plain : p.Terrain[c];
            int bi = (int)p.Biome[c];
            int best = 0, second = 0;
            double bs = double.NegativeInfinity, ss = double.NegativeInfinity;
            for (int r = 0; r < k; r++)
            {
                var def = cat.Rocks[r];
                double n = noise[r].Fbm(p.X[c] * F, p.Y[c] * F, p.Z[c] * F, 2);
                double sc = def.TerrainWeight[(int)ter] * def.BiomeMult[bi] * (1 + 1.6 * n);
                if (sc > bs) { ss = bs; second = best; bs = sc; best = r; }
                else if (sc > ss) { ss = sc; second = r; }
            }
            t.Primary[c] = (byte)best; t.Secondary[c] = (byte)(k > 1 ? second : best);
        }
        return t;
    }

    public static string Describe(Planet p, int c)
    {
        var cat = RockCatalog.Default;
        var (a, b) = RocksOf(p, c);
        return a == b ? cat.Rocks[a].Name : $"{cat.Rocks[a].Name} (조금: {cat.Rocks[b].Name})";
    }
}
