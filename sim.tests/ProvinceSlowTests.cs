using Wtw.Sim.Data;
using Wtw.Sim.Planet;
using Wtw.Sim.Provinces;
using Xunit;
using Xunit.Abstractions;

namespace Wtw.Sim.Tests;

/// <summary>
/// 느린 테스트: 시드 20개 × 땅 칸 여러 종류(평야 · 숲 · 산 · 해안 · 강 · 사막 · 툰드라 · 오각형).
/// 종류마다 오브젝트 · 땅 종류 분포를 보고하고, 맞댄 변(이웃 전부)이 이어지는지 · 강이 맞는 변으로 드나드는지 본다.
/// </summary>
[Trait("Category", "Slow")]
public class ProvinceSlowTests
{
    private readonly ITestOutputHelper _out;
    public ProvinceSlowTests(ITestOutputHelper output) { _out = output; }

    [Fact]
    public void TwentySeedsByKind()
    {
        var cat = ObjectCatalog.Default;
        var kinds = ProvinceSamples.Kinds;
        var rows = new List<(ulong Seed, string Kind, int[] Grounds, int[] Objects, double Seconds)>();
        var edges = new List<ProvinceChecks.EdgeResult>();
        int riverEdges = 0, riverEdgeErrors = 0;
        var gate = new object();
        double innerStone = 0; int innerStoneN = 0;
        Parallel.For(1, 21, s =>
        {
            ulong seed = (ulong)s;
            var p = PlanetGenerator.Generate(seed, new PlanetSettings(24_000, ClimateBias.Normal));
            foreach (var kind in kinds)
            {
                int c = ProvinceSamples.Find(p, kind);
                if (c < 0) continue;
                var m = ProvinceGenerator.Generate(p, c);
                var localEdges = new List<ProvinceChecks.EdgeResult>();
                int re = 0, rerr = 0;
                foreach (int nb in p.Grid.Neighbors(c))
                {
                    if (!p.IsLand(nb)) continue;
                    localEdges.Add(ProvinceChecks.CompareEdge(m, ProvinceGenerator.Generate(p, nb)));
                    lock (gate) { innerStone += ProvinceChecks.InteriorStoneMismatch; innerStoneN++; }
                }
                for (int slot = 0; slot < 6; slot++)
                {
                    bool expected = ProvinceChecks.RiverExpected(p, c, m.SlotCell[slot]);
                    if (expected) re++;
                    if (expected != ProvinceChecks.RiverAtEdge(m, slot)) rerr++;
                }
                lock (gate)
                {
                    rows.Add((seed, kind, m.GroundCounts(), m.ObjectCounts(cat.Objects.Count), m.GenerationSeconds));
                    edges.AddRange(localEdges);
                    riverEdges += re; riverEdgeErrors += rerr;
                }
            }
        });

        foreach (var kind in kinds)
        {
            var rs = rows.Where(r => r.Kind == kind).ToList();
            if (rs.Count == 0) { _out.WriteLine($"{ProvinceSamples.Name(kind)}: 없음"); continue; }
            double inside = HexFrame.InsideCount;
            var g = Enumerable.Range(0, GroundInfo.Count).Select(k => Median(rs.Select(r => r.Grounds[k] * 100.0 / inside))).ToArray();
            var o = Enumerable.Range(0, cat.Objects.Count).Select(k => Median(rs.Select(r => (double)r.Objects[k]))).ToArray();
            _out.WriteLine($"{ProvinceSamples.Name(kind)} ({rs.Count}개 · 만드는 시간 중앙값 {Median(rs.Select(r => r.Seconds)):0.000}초 · 오브젝트 중앙값 {Median(rs.Select(r => (double)r.Objects.Sum()))})");
            _out.WriteLine("    땅 종류 %: " + string.Join(" · ", Enumerable.Range(1, GroundInfo.Count - 1).Where(k => g[k] >= 0.5).Select(k => $"{GroundInfo.Name((Ground)k)} {g[k]:0.#}")));
            _out.WriteLine("    오브젝트: " + string.Join(" · ", Enumerable.Range(0, o.Length).Where(k => o[k] > 0).OrderByDescending(k => o[k]).Take(12).Select(k => $"{cat.Objects[k].Name} {o[k]:0}")));
        }
        int samples = edges.Sum(e => e.Samples), water = edges.Sum(e => e.WaterMismatch), river = edges.Sum(e => e.RiverMismatch), ground = edges.Sum(e => e.GroundMismatch);
        double dh = Median(edges.Select(e => e.MeanHeightDiff)), inner = Median(edges.Select(e => e.InteriorHeightDiff));
        _out.WriteLine($"맞댄 변 {edges.Count}개: 표본 {samples} · 물 다름 {water * 100.0 / samples:0.00}% · 강 다름 {river * 100.0 / samples:0.00}% · 땅 종류 다름 {ground * 100.0 / samples:0.0}% · 높이 차 중앙값 {dh:0.0}m (지도 안 이웃 타일 {inner:0.0}m)");
        _out.WriteLine($"강이 드나드는 변 {riverEdges}개 · 틀린 변 {riverEdgeErrors}개");
        int st = edges.Sum(e => e.Stones), sm = edges.Sum(e => e.StoneMismatch);
        // 경계 양쪽 3 타일 띠의 바위 비율이 비슷한가 (바위 덩어리가 경계에서 끊기지 않는다) — 같은 지방의 서로 다른 변끼리와 비교
        double stripDiff = edges.Average(e => Math.Abs(e.StripStoneA - e.StripStoneB)), stripMean = edges.Average(e => (e.StripStoneA + e.StripStoneB) / 2);
        var rnd = new Random(1);
        double randomDiff = Enumerable.Range(0, 2000).Average(_ => Math.Abs(edges[rnd.Next(edges.Count)].StripStoneA - edges[rnd.Next(edges.Count)].StripStoneB));
        _out.WriteLine($"바위 띠(경계 양쪽 3타일): 바위 비율 평균 {stripMean * 100:0.0}% · 양쪽 차이 평균 {stripDiff * 100:0.0}%p (아무 변끼리 짝지으면 {randomDiff * 100:0.0}%p)");
        Assert.True(stripDiff < randomDiff * 0.5, "바위 덩어리가 경계에서 끊김");
        double inRate = innerStone / Math.Max(1, innerStoneN);
        _out.WriteLine($"바위: 변 근처 바위 표본 {st} · 한쪽만 바위 {sm * 100.0 / Math.Max(1, st):0.0}% (같은 지도 안에서 같은 거리 {inRate * 100:0.0}%)");
        Assert.True(sm / (double)Math.Max(1, st) <= inRate * 1.4 + 0.05, "바위가 경계에서 끊김");

        Assert.True(rows.Count >= 20 * 7, $"지방 {rows.Count}개");
        Assert.True(water <= samples * 0.01, "물이 경계에서 어긋남");
        Assert.True(river <= samples * 0.005, "강이 경계에서 어긋남");
        Assert.True(dh <= inner * 2.5 + 4, "높이가 경계에서 어긋남");
        Assert.Equal(0, riverEdgeErrors);
    }

    private static double Median(IEnumerable<double> v)
    {
        var s = v.OrderBy(x => x).ToArray();
        return s.Length == 0 ? 0 : s.Length % 2 == 1 ? s[s.Length / 2] : (s[s.Length / 2 - 1] + s[s.Length / 2]) / 2;
    }
}
