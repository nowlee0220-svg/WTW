using System.Globalization;
using Wtw.Sim.Data;
using Wtw.Sim.Planet;
using Wtw.Sim.Time;

namespace Wtw.Sim.Provinces;

/// <summary>지방 정보 칸의 한 줄: 이름 · 값 · "자세히 보기" 를 누르면 나오는 이유.</summary>
public sealed record ProvinceFact(string Label, string Value, string Why);

/// <summary>
/// 지방 정보 칸(2D 지방 화면): 그 지방에서만 달라지는 것들 — 그 지방의 시각(경도), 지금 기온(연평균 + 낮밤), 해의 높이(위도),
/// 바람, 강수, 높이, 생물군계, 바위, 물(민물 · 짠물), 땅의 비옥함, 나는 자원, 그 밖에 낮밤 길이 · 서리 · 공기의 습한 정도 · 바다까지 · 땅의 험함 · 화산.
/// 화면용 설명이라 시뮬의 상태를 바꾸지 않는다(삼각함수를 써도 결정론과 상관없다).
/// 행성은 기울어지지 않아(계절 없음, 기획서 5-2) 어디서나 해는 그 지방 시각 6시에 뜨고 18시에 진다.
/// </summary>
public static class ProvinceReport
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>그 지방의 시각(0~24): 세계 시간(화면 맨 위 시계 = 경도 0°의 시각) + 경도 ÷ 15°.</summary>
    public static double LocalHour(Planet.Planet p, int cell, double tick)
    {
        double world = (tick % GameTime.TicksPerDay) / GameTime.TicksPerHour;
        var (_, lon) = p.LatLon(cell);
        double h = (world + lon / 15.0) % 24;
        return h < 0 ? h + 24 : h;
    }

    /// <summary>해의 높이(도): 적위 0(계절 없음) — sin(높이) = cos(위도) · cos(시간각).</summary>
    public static double SunAltitude(double latDeg, double localHour)
    {
        double lat = latDeg * Math.PI / 180, ha = (localHour - 12) * 15 * Math.PI / 180;
        return Math.Asin(Math.Clamp(Math.Cos(lat) * Math.Cos(ha), -1, 1)) * 180 / Math.PI;
    }

    /// <summary>낮밤 기온 곡선(−1 = 해 뜰 무렵 가장 차갑고, +1 = 15시 가장 따뜻함).</summary>
    public static double DailyCurve(double h)
    {
        if (h >= 6 && h <= 15) return -Math.Cos((h - 6) / 9 * Math.PI);
        double t = h > 15 ? h - 15 : h + 9; // 15시 → 다음 날 6시 (15시간)
        return Math.Cos(t / 15 * Math.PI);
    }

    /// <summary>하루 기온 차이(가장 따뜻할 때 − 가장 차가울 때, °C): 마를수록 · 내륙일수록 · 높을수록 크고, 바닷가는 작다.</summary>
    public static double DailyRange(Planet.Planet p, int c, out List<string> why)
    {
        why = new List<string>();
        double pr = p.PrecipMm[c];
        double dry = 1 - Math.Clamp(pr / 1000.0, 0, 1);
        double range = 6 + 14 * dry;
        why.Add(dry > 0.6 ? $"공기가 말라(강수 {pr:N0}mm) 낮엔 햇볕이 그대로 데우고 밤엔 열이 빨리 빠져나간다 (+{14 * dry:0.0}°C)" : $"구름 · 습기가 열을 붙잡는다(강수 {pr:N0}mm, +{14 * dry:0.0}°C)");
        if ((p.Flags[c] & CellFlags.Coast) != 0) { range -= 3; why.Add("바닷가: 바다가 낮밤 기온을 고르게 한다 (−3°C)"); }
        else if (p.SeaDistance[c] > 6) { range += 3; why.Add($"내륙(바다까지 {p.SeaDistance[c]}칸): 바다의 누그러뜨림이 없다 (+3°C)"); }
        if (p.Elevation[c] > 2000) { range += 3; why.Add($"높은 곳({p.Elevation[c]:N0}m): 공기가 얇아 밤에 빨리 식는다 (+3°C)"); }
        return Math.Max(range, 3);
    }

    public static double TemperatureNow(Planet.Planet p, int c, double localHour) => p.TempC(c) + DailyRange(p, c, out _) / 2 * DailyCurve(localHour);

    private static string Clock(double h)
    {
        int m = (int)Math.Floor(h * 60) % (24 * 60);
        return $"{m / 60:00}:{m % 60:00}";
    }

    private static string Deg(double v) => v.ToString("0.0", Inv);

    public static IReadOnlyList<ProvinceFact> Describe(Planet.Planet p, ProvinceMap m, double tick)
    {
        int c = m.Cell;
        var list = new List<ProvinceFact>();
        var (lat, lon) = p.LatLon(c);
        double h = LocalHour(p, c, tick);
        double worldH = (tick % GameTime.TicksPerDay) / GameTime.TicksPerHour;
        double offset = lon / 15.0;
        string phase = h < 5 || h >= 19.5 ? "밤" : h < 6.5 ? "새벽" : h < 17 ? "낮" : "저녁";
        list.Add(new("이 지방의 시각", $"{Clock(h)} ({phase}) · 해 뜸 06:00 · 해 짐 18:00",
            $"세계 시간(맨 위 시계 = 경도 0°)은 {Clock(worldH)}. 이곳은 경도 {(lon >= 0 ? "동" : "서")} {Deg(Math.Abs(lon))}° 라 해가 {Math.Abs(offset):0.0}시간 {(offset >= 0 ? "먼저" : "늦게")} 지나간다(15° = 1시간). " +
            "행성이 기울어지지 않아 계절이 없고, 어디서나 낮과 밤이 12시간씩이다 — 그래서 해는 그 지방 시각으로 늘 6시에 뜨고 18시에 진다."));

        double mean = p.TempC(c);
        double range = DailyRange(p, c, out var rangeWhy);
        double now = mean + range / 2 * DailyCurve(h);
        list.Add(new("지금 기온", $"{Deg(now)}°C (밤 최저 {Deg(mean - range / 2)} · 낮 최고 {Deg(mean + range / 2)})",
            $"연평균 {Deg(mean)}°C 에 낮밤 차이를 더했다. 해 뜰 무렵(6시)이 가장 차갑고 15시가 가장 따뜻하다. 하루 기온 차이 {Deg(range)}°C ← " + string.Join(" · ", rangeWhy)));
        list.Add(Water(p, m));
        list.Add(Resources(p, m));
        list.Add(new("연평균 기온", $"{Deg(mean)}°C", CellDescriber.TempWhy(p, c, lat)));

        double noon = 90 - Math.Abs(lat), alt = SunAltitude(lat, h);
        list.Add(new("해의 높이", alt > 0 ? $"지금 {alt:0}° · 한낮 {noon:0}°" : $"지금 지평선 아래 · 한낮 {noon:0}°",
            $"위도 {(lat >= 0 ? "북" : "남")}위 {Deg(Math.Abs(lat))}°: 한낮의 해는 90° − 위도 = {noon:0}° 높이까지 오른다. 해가 낮게 뜰수록 햇볕이 비스듬히 비쳐 땅이 덜 데워진다(위도가 높을수록 춥다)."));

        var (wdir, wspd, wwhy) = Wind(p, c, lat, h);
        list.Add(new("바람", $"{wdir} · {wspd:0} m/s ({WindWord(wspd)})", wwhy));

        int pr = p.PrecipMm[c];
        list.Add(new("강수", $"{pr:N0}mm / 년 ({RainWord(pr)})", CellDescriber.RainWhy(p, c)));
        list.Add(new("땅의 물기", MoistureWord(pr, mean), $"강수 {pr:N0}mm 와 기온 {Deg(mean)}°C 로 어림: 비가 많을수록 젖고, 따뜻할수록 같은 비도 빨리 마른다(증발). 추운 곳은 비가 적어도 잘 마르지 않는다."));

        int minH = int.MaxValue, maxH = int.MinValue, walk = 0, inside = 0;
        for (int i = 0; i < HexFrame.TileCount; i++)
        {
            if (!HexFrame.Inside[i]) continue;
            inside++;
            if (GroundInfo.IsWalkable(m.Ground[i])) walk++;
            if (GroundInfo.IsWater(m.Ground[i])) continue;
            minH = Math.Min(minH, m.Height[i]); maxH = Math.Max(maxH, m.Height[i]);
        }
        list.Add(new("높이", $"{p.Elevation[c]:N0}m ({PlanetNames.Of(p.Terrain[c])}, 지방 안 {minH:N0}~{maxH:N0}m)",
            $"행성 칸의 높이 {p.Elevation[c]:N0}m 에 둘레 칸과 섞은 높이 · 거칠기에 맞는 높낮이(평야는 수 m, 산은 수백 m)를 더했다. 1,000m 오를 때마다 약 6.5°C 서늘하다."));
        list.Add(new("생물군계", PlanetNames.Of(p.Biome[c]), CellDescriber.BiomeWhy(p, c)));

        var rocks = RockCatalog.Default;
        var (ra, rb) = Geology.RocksOf(p, c);
        list.Add(new("바위", Geology.Describe(p, c),
            $"지형({PlanetNames.Of(p.Terrain[c])}) · 생물군계({PlanetNames.Of(p.Biome[c])})에 맞는 바위 + 몇 칸 크기의 넓은 지질 지역으로 정했다. {rocks.Rocks[ra].Name}: 캐면 {string.Join(", ", rocks.Rocks[ra].Yields.Select(y => y.Name))}." +
            (Volcanoes.Distance(p, c) is >= 0 and <= 2 ? " 화산 가까이라 굳은 용암(현무암)이 섞인다." : "")));

        list.Add(Fertility(p, m));

        // 그 밖에 지역마다 달라지는 것
        double tmin = mean - range / 2;
        list.Add(new("서리 · 눈", tmin < -5 ? "늘 얼어 있음" : tmin < 0 ? "밤마다 서리" : tmin < 4 ? "가끔 서리" : "없음",
            $"밤 최저 {Deg(tmin)}°C. 0°C 아래로 내려가면 이슬이 얼어 서리가 되고, 늘 영하면 땅 · 물이 언다(작물 · 사람에게 힘들다)."));
        double rough = 1 - walk / (double)Math.Max(1, inside);
        list.Add(new("걷기 어려움", $"{rough * 100:0}% 가 못 가는 곳 ({RoughWord(rough)})",
            "지방 안에서 바위 · 깊은 물 · 절벽 · 분화구는 지나갈 수 없다. 산일수록 바위가 많아 길이 좁다."));
        list.Add(new("바다까지", p.SeaDistance[c] <= 0 ? "바닷가" : $"{p.SeaDistance[c]}칸",
            "바다가 가까우면 소금 · 물고기를 얻기 쉽고 낮밤 기온이 고르다. 멀면 비가 적어진다(바람이 오는 길에 습기를 잃음)."));
        int vd = Volcanoes.Distance(p, c);
        list.Add(new("화산", vd == 0 ? "이 지방이 화산 (잠듦)" : vd > 0 && vd < 30 ? $"가장 가까운 화산 {vd}칸" : "멀리 없음",
            vd == 0 ? "가운데에 분화구가 있는 현무암 산. 둘레에 흑요석(날이 아주 잘 서는 검은 유리 돌)이 있다. 분화는 아직 없다(재난 단계에서)." : "화산은 산맥 둘레에 드물게 있다. 화산 둘레에서만 흑요석이 난다."));
        list.Add(new("위치", $"{(lat >= 0 ? "북" : "남")}위 {Deg(Math.Abs(lat))}° · {(lon >= 0 ? "동" : "서")}경 {Deg(Math.Abs(lon))}°",
            $"행성 칸 {c}{(p.Grid.IsPentagon(c) ? " (오각형 — 한 변이 막힘)" : "")}. 위도는 해의 높이 · 기온을, 경도는 그 지방의 시각을 정한다."));
        return list;
    }

    private static (string Dir, double Speed, string Why) Wind(Planet.Planet p, int c, double lat, double h)
    {
        bool north = lat >= 0;
        var w = p.Wind[c];
        string dir = w switch
        {
            Planet.Wind.Trade => north ? "북동풍" : "남동풍",
            Planet.Wind.Westerly => north ? "남서풍" : "북서풍",
            _ => north ? "북동풍" : "남동풍",
        };
        double spd = w switch { Planet.Wind.Trade => 5, Planet.Wind.Westerly => 7, _ => 6 };
        var why = new List<string> { $"{PlanetNames.Of(w)} 띠(위도 {Deg(Math.Abs(lat))}°): 기본 {spd:0} m/s" };
        if ((p.Flags[c] & CellFlags.Coast) != 0) { spd += 2; why.Add("바닷가: 막는 것이 없어 +2"); }
        double up = Math.Max(0, p.Elevation[c]) / 1000.0 * 1.5;
        if (up >= 0.5) { spd += up; why.Add($"높은 곳 +{up:0.0}"); }
        int wood = p.Resource(c, ResourceKinds.Wood);
        if (wood == 2) { spd -= 2; why.Add("숲이 바람을 막아 −2"); }
        else if (p.Biome[c] is Biome.Desert or Biome.Grassland or Biome.Tundra or Biome.Savanna) { spd += 1; why.Add("탁 트인 땅 +1"); }
        double day = Math.Max(0, DailyCurve(h)) * 1.5;
        spd += day;
        if (day > 0.3) why.Add($"한낮에 데워진 땅이 공기를 뒤섞어 +{day:0.0}");
        return (dir, Math.Max(0.5, spd), $"바람은 위도대마다 부는 방향이 정해져 있다(지구의 무역풍 · 편서풍처럼): {dir} — " + string.Join(" · ", why));
    }

    private static ProvinceFact Water(Planet.Planet p, ProvinceMap m)
    {
        int fresh = 0, salt = 0, river = 0, pond = 0;
        for (int i = 0; i < HexFrame.TileCount; i++)
        {
            if (!HexFrame.Inside[i] || !GroundInfo.IsWater(m.Ground[i])) continue;
            if ((m.Flags[i] & TileFlags.Sea) != 0) salt++;
            else { fresh++; if ((m.Flags[i] & TileFlags.River) != 0) river++; else pond++; }
        }
        var parts = new List<string>();
        int c = m.Cell;
        if (p.RiverSize[c] > 0) parts.Add($"{PlanetNames.RiverName(p.RiverSize[c])}(민물)");
        if ((p.Flags[c] & CellFlags.LakeShore) != 0) parts.Add("호수(민물)");
        if (pond > 0 && (p.Flags[c] & CellFlags.LakeShore) == 0) parts.Add("웅덩이(민물)");
        if (salt > 0) parts.Add("바다(짠물)");
        string v = parts.Count == 0 ? $"없음 — 바다까지 {p.SeaDistance[c]}칸" : string.Join(" · ", parts);
        string why = $"민물 {fresh:N0} 타일 · 짠물 {salt:N0} 타일. 강 · 호수 · 웅덩이의 물은 빗물이 모인 민물이라 마실 수 있고, 바다는 소금이 녹은 짠물이라 마실 수 없다(대신 소금 · 물고기)." +
                     (parts.Count == 0 ? " 이 지방에는 물이 없어 사람이 살려면 물가까지 가야 한다." : "");
        return new("물", v, why);
    }

    /// <summary>땅의 비옥함(0~100): 생물군계의 흙 + 강가 범람원 + 진흙 − 너무 마르거나 너무 춥거나 비가 흙을 씻어 냄.</summary>
    public static int FertilityScore(Planet.Planet p, int c, out List<string> why)
    {
        why = new List<string>();
        int b = p.Biome[c] switch
        {
            Biome.Temperate => 70, Biome.Grassland => 75, Biome.Rainforest => 55, Biome.Savanna => 50, Biome.Boreal => 40,
            Biome.Wetland => 60, Biome.Tundra => 15, Biome.Desert => 8, Biome.Alpine => 15, _ => 0,
        };
        why.Add($"{PlanetNames.Of(p.Biome[c])}의 흙 {b}");
        int s = b;
        if (p.RiverSize[c] > 0) { s += 15; why.Add("강이 넘쳐 고운 흙을 쌓는다 +15"); }
        if (p.Resource(c, ResourceKinds.Clay) > 0) { s += 5; why.Add("진흙이 물을 머금는다 +5"); }
        if (p.PrecipMm[c] < 300) { s -= 20; why.Add($"비가 너무 적다({p.PrecipMm[c]}mm) −20"); }
        if (p.PrecipMm[c] > 2500) { s -= 10; why.Add("비가 너무 많아 양분이 씻겨 내려간다 −10"); }
        if (p.TempC(c) < 3) { s -= 15; why.Add("추워서 자라는 철이 짧다 −15"); }
        if (p.Terrain[c] >= Terrain.Mountain) { s -= 15; why.Add("산: 흙이 얇고 바위가 많다 −15"); }
        return Math.Clamp(s, 0, 100);
    }

    private static ProvinceFact Fertility(Planet.Planet p, ProvinceMap m)
    {
        int s = FertilityScore(p, m.Cell, out var why);
        int soil = 0, inside = 0;
        for (int i = 0; i < HexFrame.TileCount; i++)
        {
            if (!HexFrame.Inside[i]) continue;
            inside++;
            if (m.Ground[i] is Ground.Grass or Ground.Soil or Ground.Mud or Ground.Marsh) soil++;
        }
        string word = s >= 75 ? "아주 기름짐" : s >= 55 ? "기름짐" : s >= 35 ? "보통" : s >= 15 ? "척박" : "아주 척박";
        return new("땅의 비옥함", $"{word} ({s}/100 · 흙 · 풀밭 {soil * 100 / Math.Max(1, inside)}%)", "작물이 잘 자랄 땅인가: " + string.Join(" · ", why) + ". (농사는 뒤 단계에서 — 지금은 어림값)");
    }

    private static ProvinceFact Resources(Planet.Planet p, ProvinceMap m)
    {
        var cat = ObjectCatalog.Default;
        var rocks = RockCatalog.Default;
        var oc = m.ObjectCounts(cat.Objects.Count);
        int trees = cat.Objects.Where(o => o.IsTree).Sum(o => oc[o.Index]);
        var parts = new List<string>();
        if (trees > 0) parts.Add($"나무 {trees:N0}그루");
        var rk = new int[rocks.Rocks.Count];
        var ores = new int[rocks.Ores.Count];
        for (int i = 0; i < HexFrame.TileCount; i++)
        {
            if (m.Ground[i] == Ground.Stone && m.Rock[i] < rk.Length) rk[m.Rock[i]]++;
            int o = m.VisibleOreAt(i);
            if (o >= 0) ores[o]++;
        }
        for (int r = 0; r < rk.Length; r++) if (rk[r] > 0) parts.Add($"{rocks.Rocks[r].Name} {rk[r]:N0}칸");
        for (int o = 0; o < ores.Length; o++) if (ores[o] > 0) parts.Add($"{rocks.Ores[o].Name} {ores[o]}칸(드러남)");
        foreach (var def in cat.Objects.Where(o => o.Kind is "rock" or "ground" or "plant" or "bush").OrderByDescending(o => oc[o.Index]))
            if (oc[def.Index] > 0 && def.Kind is "rock" or "ground") parts.Add($"{def.Name} {oc[def.Index]}");
        int game = p.Resource(m.Cell, ResourceKinds.Game), fish = p.Resource(m.Cell, ResourceKinds.Fish);
        if (game > 0) parts.Add($"사냥감({ResourceKinds.LevelName(game)})");
        if (fish > 0) parts.Add($"물고기({ResourceKinds.LevelName(fish)})");
        return new("나는 자원", parts.Count == 0 ? "거의 없음" : string.Join(" · ", parts),
            "지방 지도에서 센 것(드러난 광맥만 — 바위 속은 아직 모름) + 행성 칸의 사냥감 · 물고기 단계. 위쪽 [자원 보기](Y)로 어디 있는지 볼 수 있다.");
    }

    private static string WindWord(double v) => v < 3 ? "약함" : v < 6 ? "보통" : v < 10 ? "강함" : "매우 강함";
    private static string RainWord(int pr) => pr < 250 ? "아주 건조" : pr < 500 ? "건조" : pr < 1200 ? "보통" : pr < 2200 ? "습함" : "아주 습함";
    private static string RoughWord(double r) => r < 0.05 ? "탁 트임" : r < 0.2 ? "조금 막힘" : r < 0.45 ? "험함" : "아주 험함";

    /// <summary>
    /// 땅의 물기(비 ÷ 증발 어림). E2 에서는 "공기: 축축함"이 "강수: 아주 건조"와 같이 나와 어긋나 보였다(추운 곳은 비가 적어도 덜 말라서) →
    /// 이름을 "땅의 물기"로 바꾸고, 비가 적은 곳은 "축축함" 대신 "비는 적지만 추워서 덜 마름"으로 까닭을 함께 쓴다.
    /// </summary>
    public static string MoistureWord(int pr, double t)
    {
        double idx = pr / Math.Max(150.0, 30 * (t + 12));
        string w = idx < 0.4 ? "메마름" : idx < 0.8 ? "조금 마름" : idx < 1.6 ? "알맞음" : "젖음";
        if (pr < 500 && idx >= 0.8) w = "비는 적지만 추워서 덜 마름";
        return w;
    }
}
