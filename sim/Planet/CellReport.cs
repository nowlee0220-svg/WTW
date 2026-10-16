using System.Globalization;
using System.Text;

namespace Wtw.Sim.Planet;

/// <summary>칸 정보 창에 보일 내용: 이름표 · 값 목록 · "왜".</summary>
public sealed record CellReport(string Title, string Subtitle, IReadOnlyList<(string Label, string Value)> Facts, IReadOnlyList<string> Why);

/// <summary>
/// 칸 하나를 사람이 읽는 말로 풀어 준다. 행성에 저장된 정수(기온의 위도·높이·내륙 몫, 습기, 비그늘 …)만 읽는다.
/// </summary>
public static class CellDescriber
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static CellReport Describe(Planet p, int c)
    {
        var facts = new List<(string, string)>();
        var why = new List<string>();
        var ter = p.Terrain[c];
        int e = p.Elevation[c];
        double t = p.Temp10[c] / 10.0;
        int pr = p.PrecipMm[c];
        var (lat, lon) = p.LatLon(c);
        string coord = $"{(lat >= 0 ? "북위" : "남위")} {Math.Abs(lat).ToString("0.0", Inv)}° · {(lon >= 0 ? "동경" : "서경")} {Math.Abs(lon).ToString("0.0", Inv)}°";

        if (p.IsOcean(c))
        {
            facts.Add(("지형", $"{PlanetNames.Of(ter)} (깊이 {(-e).ToString("N0", Inv)}m)"));
            facts.Add(("연평균 기온", Temp(t)));
            facts.Add(("자원", Resources(p, c)));
            facts.Add(("좌표", coord));
            facts.Add(("칸 번호", $"{c}{(p.Grid.IsPentagon(c) ? " (오각형)" : "")}"));
            why.Add(ter == Terrain.ShallowSea ? "얕은 바다 ← 땅에서 가까운 대륙붕" : "깊은 바다 ← 땅에서 멀다");
            return new CellReport(PlanetNames.Of(ter), "바다", facts, why);
        }

        string title = ter == Terrain.Lake ? "호수" : PlanetNames.Of(p.Biome[c]);
        string sub = ter == Terrain.Lake ? $"수면 높이 {e.ToString("N0", Inv)}m" : $"{PlanetNames.Of(ter)} · {e.ToString("N0", Inv)}m";
        if (ter != Terrain.Lake) facts.Add(("생물군계", PlanetNames.Of(p.Biome[c])));
        facts.Add(("지형", ter == Terrain.Lake ? "호수" : $"{PlanetNames.Of(ter)} (높이 {e.ToString("N0", Inv)}m)"));
        facts.Add(("연평균 기온", Temp(t)));
        facts.Add(("강수", $"{pr.ToString("N0", Inv)}mm / 년"));
        facts.Add(("물", Water(p, c)));
        facts.Add(("자원", Resources(p, c)));
        facts.Add(("좌표", coord));
        facts.Add(("칸 번호", $"{c}{(p.Grid.IsPentagon(c) ? " (오각형)" : "")} · 지방 시드 {p.ProvinceSeed(c) % 1_000_000_000UL}"));

        why.Add(TempWhy(p, c, lat));
        why.Add(RainWhy(p, c));
        if (ter != Terrain.Lake) why.Add(BiomeWhy(p, c));
        else why.Add("호수 ← 둘레가 막힌 오목한 땅에 물이 고였다");
        why.AddRange(Details(p, c, lat));
        return new CellReport(title, sub, facts, why);
    }

    public static string Temp(double t) => $"{t.ToString("0.0", Inv)}°C";

    private static string Water(Planet p, int c)
    {
        var parts = new List<string>();
        if (p.RiverSize[c] > 0) parts.Add(PlanetNames.RiverName(p.RiverSize[c]));
        if ((p.Flags[c] & CellFlags.Coast) != 0) parts.Add("해안");
        if ((p.Flags[c] & CellFlags.LakeShore) != 0) parts.Add("호숫가");
        if (p.Terrain[c] == Terrain.Lake) parts.Add("호수");
        if (parts.Count == 0) parts.Add($"없음 (바다까지 {p.SeaDistance[c]}칸)");
        return string.Join(" · ", parts);
    }

    public static string Resources(Planet p, int c)
    {
        var parts = new List<string>();
        for (int r = 0; r < ResourceKinds.Count; r++)
        {
            int lv = p.Resource(c, r);
            if (lv > 0) parts.Add(lv == 2 ? $"{ResourceKinds.Names[r]}(많음)" : ResourceKinds.Names[r]);
        }
        return parts.Count == 0 ? "없음" : string.Join(" · ", parts);
    }

    private static string Signed(double v) => (v >= 0 ? "+" : "−") + Math.Abs(v).ToString("0.0", Inv) + "°";

    private static string TempWhy(Planet p, int c, double lat)
    {
        double t = p.Temp10[c] / 10.0, tl = p.TempLatitude10[c] / 10.0, ta = p.TempAltitude10[c] / 10.0, ti = p.TempInland10[c] / 10.0;
        double bias = p.Settings.Climate switch { ClimateBias.Cold => -4, ClimateBias.Warm => 4, _ => 0 };
        string feel = t < -8 ? "아주 춥다" : t < 0 ? "춥다" : t < 8 ? "서늘하다" : t < 18 ? "온화하다" : t < 24 ? "따뜻하다" : "덥다";
        var causes = new List<string>();
        string side = lat >= 0 ? "북쪽" : "남쪽";
        if (t < 18)
        {
            if (tl < 10) causes.Add($"{side} 높은 위도");
            else if (tl < 18) causes.Add("중간 위도");
            if (ta <= -3) causes.Add(ta <= -12 ? "높은 산 위" : "높은 땅");
            if (ti <= -1) causes.Add("바다에서 먼 내륙");
            if (bias < 0) causes.Add("추운 기후 경향");
        }
        else
        {
            if (tl >= 22) causes.Add("적도 가까움");
            if (ti >= 0.5) causes.Add("열대의 내륙");
            if (bias > 0) causes.Add("따뜻한 기후 경향");
            if (ta <= -3) causes.Add("높은 땅이라 조금 덜 덥다");
        }
        if (causes.Count == 0) causes.Add("위도");
        var parts = new List<string> { $"위도로 정해지는 기본 {tl.ToString("0.0", Inv)}°" };
        if (ta != 0) parts.Add($"높이 {Signed(ta)}");
        if (ti != 0) parts.Add($"내륙 {Signed(ti)}");
        if (bias != 0) parts.Add($"기후 경향 {Signed(bias)}");
        return $"{feel} ← {string.Join(" + ", causes)}  ({string.Join(" · ", parts)})";
    }

    private static string RainWhy(Planet p, int c)
    {
        int pr = p.PrecipMm[c];
        string feel = pr < 250 ? "비가 아주 적다" : pr < 500 ? "비가 적다" : pr < 1000 ? "비가 보통이다" : pr < 2000 ? "비가 많다" : "비가 아주 많다";
        var causes = new List<string>();
        int belt = p.PrecipBelt[c];
        bool dry = pr < 800;
        if (dry)
        {
            if (p.ShadowPct[c] >= 15) causes.Add("산맥 뒤 비그늘");
            if (p.WindSeaSteps[c] < 0 || p.WindSeaSteps[c] >= 15) causes.Add("바람이 오는 쪽으로 바다가 멀다");
            if (belt < 500) causes.Add("아열대 건조 띠(바람이 내려앉아 마른다)");
            if (Math.Abs(p.Z[c]) > 0.9) causes.Add("극지방의 차고 마른 공기");
        }
        else
        {
            if (belt >= 1800) causes.Add("적도 비구름 띠");
            else if (belt >= 800) causes.Add("편서풍 비구름 띠");
            if (p.MoisturePct[c] >= 70) causes.Add("바다에서 바로 불어오는 바람");
            if (p.OroPct[c] >= 10) causes.Add("바람을 받는 산비탈");
        }
        if (causes.Count == 0) causes.Add(dry ? "바다의 습기가 오다가 줄어듦" : "바다에서 온 습기");
        return $"{feel} ← {string.Join(" + ", causes)}";
    }

    private static string BiomeWhy(Planet p, int c)
    {
        double t = p.Temp10[c] / 10.0;
        int pr = p.PrecipMm[c];
        var b = p.Biome[c];
        string extra = b switch
        {
            Biome.Alpine => " (높은 산이라 나무가 자라지 못함)",
            Biome.Wetland => " (평평한 낮은 땅에 강·호수 물이 고임)",
            _ => "",
        };
        return $"{PlanetNames.Of(b)} ← 기온 {Temp(t)} · 강수 {pr.ToString("N0", Inv)}mm{extra}";
    }

    private static IEnumerable<string> Details(Planet p, int c, double lat)
    {
        var w = p.Wind[c];
        var sb = new StringBuilder($"바람: {PlanetNames.Of(w)}");
        int steps = p.WindSeaSteps[c];
        if (steps >= 0) sb.Append($" — 바람길로 {steps}칸 거슬러 가면 바다");
        else sb.Append(" — 바람길로 40칸 안에 바다가 없다");
        sb.Append($", 남은 습기 {p.MoisturePct[c]}%");
        yield return sb.ToString();
        if (p.ShadowPct[c] > 0 && p.ShadowFrom[c] >= 0)
            yield return $"비그늘: 바람이 오는 쪽의 {p.Elevation[p.ShadowFrom[c]].ToString("N0", Inv)}m 산이 비구름을 막아 습기 −{p.ShadowPct[c]}%";
        if (p.OroPct[c] > 0)
            yield return $"산비탈: 바람이 산을 타고 오르며 비를 뿌림 +{p.OroPct[c]}%";
    }
}
