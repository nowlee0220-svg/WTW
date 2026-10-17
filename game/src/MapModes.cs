using Godot;
using Wtw.Sim.Planet;

namespace Wtw.Game;

public enum MapMode { Natural, Elevation, Biome, Temperature, Rain, Resource, Language }

/// <summary>
/// 지도 모드마다 칸 색과 범례. 화면 쪽 그림 공급(Art)과 같은 역할 — 색은 여기 한 곳에서만 정한다.
/// </summary>
public static class MapModes
{
    public static readonly MapMode[] All = { MapMode.Natural, MapMode.Elevation, MapMode.Biome, MapMode.Temperature, MapMode.Rain, MapMode.Resource, MapMode.Language };

    public static string Name(MapMode m) => m switch
    {
        MapMode.Natural => "자연",
        MapMode.Elevation => "높낮이",
        MapMode.Biome => "생물군계",
        MapMode.Temperature => "기온",
        MapMode.Rain => "강수",
        MapMode.Language => "언어",
        _ => "자원",
    };

    public static Key Hotkey(MapMode m) => m switch
    {
        MapMode.Natural => Key.Q,
        MapMode.Elevation => Key.W,
        MapMode.Biome => Key.E,
        MapMode.Temperature => Key.R,
        MapMode.Rain => Key.T,
        MapMode.Language => Key.U,
        _ => Key.Y,
    };

    /// <summary>아직 없는 지도 모드(단추 자리만).</summary>
    public static readonly (string Name, string Why)[] Placeholders =
    {
        ("세력", "세력 지도는 아직 없습니다 — 사람들이 무리·나라를 이루면(E7~) 채워집니다."),
        ("인구", "인구 지도는 아직 없습니다 — 사람이 사는 단계(E3~)부터 채워집니다."),
        ("교역", "교역 지도는 아직 없습니다 — 교역이 생기면(E7~) 채워집니다."),
        ("기술", "기술 지도는 아직 없습니다 — 발견이 생기면(E4~) 채워집니다."),
    };

    // 물
    private static readonly Color DeepSea = C(18, 42, 82), ShallowSea = C(40, 88, 142), Lake = C(58, 112, 178);
    // 높낮이 지도(높이 단계만): 낮은 초록 → 노랑·황토 → 갈색 → 높은 산은 보랏빛 회색
    private static readonly Color Plain = C(96, 150, 80), Hill = C(170, 170, 92), Plateau = C(196, 160, 104),
        Mountain = C(140, 96, 70), HighMountain = C(176, 160, 196);
    // 자연 지도: 생물군계 색 + 산 그늘, 산은 바위색, 높은 산 꼭대기는 눈, 얼음 땅은 푸른 기 도는 얼음
    private static readonly Color Rock = C(128, 112, 96), Snow = C(250, 250, 252), IceSheet = C(196, 226, 246);

    // 생물군계
    private static readonly Color[] BiomeColors =
    {
        C(40, 88, 142),   // 물
        C(196, 226, 246), // 얼음 (푸른 기 — 눈 덮인 높은 산과 구분)
        C(160, 166, 142), // 툰드라
        C(50, 96, 72),    // 침엽수림
        C(70, 136, 60),   // 온대림
        C(176, 190, 100), // 초원
        C(230, 206, 138), // 사막
        C(196, 172, 84),  // 사바나
        C(26, 108, 44),   // 열대우림
        C(86, 122, 104),  // 습지
        C(146, 136, 126), // 고산
    };

    private static readonly (double V, Color C)[] TempStops =
    {
        (-30, C(70, 40, 150)), (-15, C(50, 100, 215)), (0, C(185, 222, 245)), (10, C(240, 228, 140)), (20, C(240, 150, 60)), (30, C(200, 40, 40)),
    };

    private static readonly (double V, Color C)[] RainStops =
    {
        (0, C(150, 105, 60)), (250, C(214, 186, 120)), (500, C(196, 204, 110)), (1000, C(92, 170, 88)), (2000, C(40, 132, 160)), (3500, C(40, 66, 170)),
    };

    private static Color C(int r, int g, int b) => new(r / 255f, g / 255f, b / 255f);

    private static Color Water(Planet p, int c) => p.Terrain[c] switch
    {
        Terrain.Lake => Lake,
        Terrain.ShallowSea => ShallowSea,
        _ => DeepSea.Darkened(Mathf.Clamp(-p.Elevation[c] / 9000f, 0, 0.35f)),
    };

    /// <summary>
    /// 산 그늘(0.65~1.3): 북서쪽에서 해가 비춘다고 보고, 북서쪽을 향한 비탈은 밝게, 남동쪽 비탈은 어둡게.
    /// 이웃 칸과의 높이 차를 방향별로 더해 비탈이 어느 쪽을 보는지 어림한다. 행성마다 한 번 계산해 둔다.
    /// </summary>
    public static float[] HillShade(Planet p)
    {
        var shade = new float[p.CellCount];
        for (int c = 0; c < p.CellCount; c++)
        {
            shade[c] = 1f;
            if (!p.IsLand(c) || p.Terrain[c] == Terrain.Lake) continue;
            double x = p.X[c], y = p.Y[c], z = p.Z[c];
            // 북쪽 = 북극 방향을 땅에 눕힌 것, 동쪽 = 북쪽 × 위
            double nx = -z * x, ny = -z * y, nz = 1 - z * z;
            double nl = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (nl < 1e-6) continue;
            nx /= nl; ny /= nl; nz /= nl;
            double ex = ny * z - nz * y, ey = nz * x - nx * z, ez = nx * y - ny * x;
            double lx = nx - ex, ly = ny - ey, lz = nz - ez; // 북서
            double ll = Math.Sqrt(lx * lx + ly * ly + lz * lz);
            lx /= ll; ly /= ll; lz /= ll;
            double s = 0;
            int n = 0;
            foreach (int nb in p.Grid.Neighbors(c))
            {
                double dx = p.X[nb] - x, dy = p.Y[nb] - y, dz = p.Z[nb] - z;
                double dl = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                int ne = p.IsOcean(nb) ? 0 : p.Elevation[nb];
                s += (ne - p.Elevation[c]) * (dx * lx + dy * ly + dz * lz) / dl;
                n++;
            }
            // 해 쪽으로 오르막(s > 0)이면 해를 등진 비탈 → 어둡게
            shade[c] = (float)Math.Clamp(1 - s / n / 900.0, 0.65, 1.3);
        }
        return shade;
    }

    /// <summary>칸 색 (알파 = 거칠기: 물은 매끈하게 반짝). shade = HillShade 결과(자연 지도만 씀).</summary>
    public static Color CellColor(Planet p, int c, MapMode mode, int resource, float[]? shade = null)
    {
        bool ocean = p.IsOcean(c), lake = p.Terrain[c] == Terrain.Lake;
        Color col;
        switch (mode)
        {
            case MapMode.Natural:
                if (ocean || lake) { col = Water(p, c); break; }
                col = p.Biome[c] == Biome.Ice ? IceSheet : BiomeColors[(int)p.Biome[c]];
                if (p.Terrain[c] == Terrain.Mountain) col = col.Lerp(Rock, 0.45f);
                if (p.Terrain[c] == Terrain.HighMountain) col = p.Elevation[c] >= 3600 || p.Temp10[c] < 0 ? Snow : col.Lerp(Rock, 0.6f).Lerp(Snow, 0.35f);
                if (shade is not null)
                {
                    float k = shade[c];
                    col = k >= 1 ? col.Lerp(Colors.White, (k - 1) * 0.5f) : col.Darkened(1 - k);
                }
                break;
            case MapMode.Elevation:
                col = p.Terrain[c] switch
                {
                    Terrain.DeepSea or Terrain.ShallowSea or Terrain.Lake => Water(p, c),
                    Terrain.Plain => Plain,
                    Terrain.Hill => Hill,
                    Terrain.Plateau => Plateau,
                    Terrain.Mountain => Mountain,
                    _ => HighMountain,
                };
                // 같은 단계 안에서도 높을수록 조금 어둡게
                if (!ocean && !lake) col = col.Darkened(Mathf.Clamp(p.Elevation[c] / 12000f, 0, 0.3f));
                break;
            case MapMode.Biome:
                col = ocean || lake ? Water(p, c) : BiomeColors[(int)p.Biome[c]];
                break;
            case MapMode.Temperature:
                col = Ramp(TempStops, p.Temp10[c] / 10.0);
                if (ocean || lake) col = col.Darkened(0.45f);
                break;
            case MapMode.Rain:
                col = Ramp(RainStops, p.PrecipMm[c]);
                if (ocean || lake) col = col.Darkened(0.55f);
                break;
            case MapMode.Language:
                // 사람이 사는 칸은 GlobeView 가 말의 색으로 덮는다. 나머지 땅은 흐린 회색.
                col = ocean || lake ? Water(p, c).Darkened(0.35f) : C(70, 72, 68);
                break;
            default:
                int lv = p.Resource(c, resource);
                col = ocean || lake ? C(22, 32, 48) : C(62, 64, 60);
                if (lv == 1) col = C(200, 160, 70);
                else if (lv == 2) col = C(255, 220, 95);
                break;
        }
        // 화산: 자연 · 높낮이 · 생물군계 지도에서 검붉게(분화구)
        if (mode is MapMode.Natural or MapMode.Elevation or MapMode.Biome && Volcanoes.IsVolcano(p, c)) col = Volcano;
        col.A = ocean || lake ? 0.45f : 0.92f;
        return col;
    }

    public static readonly Color Volcano = new(0.62f, 0.12f, 0.06f);

    private static Color Ramp((double V, Color C)[] stops, double v)
    {
        if (v <= stops[0].V) return stops[0].C;
        for (int i = 1; i < stops.Length; i++)
            if (v <= stops[i].V) return stops[i - 1].C.Lerp(stops[i].C, (float)((v - stops[i - 1].V) / (stops[i].V - stops[i - 1].V)));
        return stops[^1].C;
    }

    /// <summary>범례 줄들: (색, 글).</summary>
    public static List<(Color Color, string Text)> Legend(MapMode mode)
    {
        var l = new List<(Color, string)>();
        switch (mode)
        {
            case MapMode.Natural:
                l.Add((BiomeColors[(int)Biome.Rainforest], "숲 (열대·온대·침엽수)")); l.Add((BiomeColors[(int)Biome.Grassland], "초원 · 사바나"));
                l.Add((BiomeColors[(int)Biome.Desert], "사막")); l.Add((BiomeColors[(int)Biome.Tundra], "툰드라"));
                l.Add((IceSheet, "얼음 땅")); l.Add((Rock, "산 (바위)")); l.Add((Snow, "높은 산의 눈")); l.Add((Volcano, "화산"));
                l.Add((ShallowSea, "얕은 바다")); l.Add((DeepSea, "깊은 바다")); l.Add((Art.River, "강"));
                l.Add((Colors.Transparent, "(북서쪽 해가 만드는 산 그늘)"));
                break;
            case MapMode.Elevation:
                l.Add((HighMountain, "높은 산 (3,000m~)")); l.Add((Mountain, "산")); l.Add((Plateau, "고원"));
                l.Add((Hill, "언덕")); l.Add((Plain, "평야")); l.Add((Lake, "호수")); l.Add((Volcano, "화산"));
                l.Add((ShallowSea, "얕은 바다")); l.Add((DeepSea, "깊은 바다")); l.Add((Art.River, "강"));
                break;
            case MapMode.Biome:
                for (int b = 1; b < BiomeColors.Length; b++) l.Add((BiomeColors[b], PlanetNames.Of((Biome)b)));
                l.Add((ShallowSea, "바다 · 호수"));
                break;
            case MapMode.Temperature:
                for (int i = TempStops.Length - 1; i >= 0; i--) l.Add((TempStops[i].C, $"{TempStops[i].V:0}°C"));
                l.Add((Colors.Transparent, "(연평균 · 바다는 어둡게)"));
                break;
            case MapMode.Rain:
                for (int i = RainStops.Length - 1; i >= 0; i--) l.Add((RainStops[i].C, $"{RainStops[i].V:N0}mm"));
                l.Add((Colors.Transparent, "(한 해 강수량)"));
                break;
            case MapMode.Language:
                l.Add((Colors.Transparent, "사람이 사는 칸을 그 칸에서 가장 많이 쓰는 말의 색으로"));
                l.Add((Colors.Transparent, "비슷한 말은 비슷한 색(사투리 = 같은 색의 옅고 짙음)"));
                l.Add((Colors.Transparent, "사람 사는 칸을 누르면: 그 말이 어디까지 통하나(밝을수록 잘 통함)"));
                l.Add((C(70, 72, 68), "아무도 살지 않는 땅"));
                break;
            default:
                l.Add((C(255, 220, 95), "많음")); l.Add((C(200, 160, 70), "있음")); l.Add((C(62, 64, 60), "없음"));
                break;
        }
        return l;
    }

    /// <summary>
    /// 말의 색: 뿌리(가장 먼 조상 말)마다 다른 빛깔(황금각으로 흩음), 갈라져 나온 말은 같은 빛깔에서 밝기 · 짙기만 조금씩 다르게.
    /// </summary>
    public static Color LanguageColor(int root, int id, int depth)
    {
        float hue = (root * 0.618034f) % 1f;
        if (depth == 0) return Color.FromHsv(hue, 0.62f, 0.86f);
        uint h = (uint)(id * 2654435761u);
        float v = 0.62f + 0.32f * ((h >> 8) % 7) / 6f;
        float sat = 0.42f + 0.4f * ((h >> 16) % 5) / 4f;
        return Color.FromHsv((hue + ((h >> 4) % 5 - 2) * 0.012f + 1f) % 1f, sat, v);
    }
}
