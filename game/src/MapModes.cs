using Godot;
using Wtw.Sim.Planet;

namespace Wtw.Game;

public enum MapMode { Terrain, Biome, Temperature, Rain, Resource }

/// <summary>
/// 지도 모드마다 칸 색과 범례. 화면 쪽 그림 공급(Art)과 같은 역할 — 색은 여기 한 곳에서만 정한다.
/// </summary>
public static class MapModes
{
    public static readonly MapMode[] All = { MapMode.Terrain, MapMode.Biome, MapMode.Temperature, MapMode.Rain, MapMode.Resource };

    public static string Name(MapMode m) => m switch
    {
        MapMode.Terrain => "지형",
        MapMode.Biome => "생물군계",
        MapMode.Temperature => "기온",
        MapMode.Rain => "강수",
        _ => "자원",
    };

    public static Key Hotkey(MapMode m) => m switch
    {
        MapMode.Terrain => Key.Q,
        MapMode.Biome => Key.W,
        MapMode.Temperature => Key.E,
        MapMode.Rain => Key.R,
        _ => Key.T,
    };

    /// <summary>아직 없는 지도 모드(단추 자리만).</summary>
    public static readonly (string Name, string Why)[] Placeholders =
    {
        ("세력", "세력 지도는 아직 없습니다 — 사람들이 무리·나라를 이루면(E7~) 채워집니다."),
        ("인구", "인구 지도는 아직 없습니다 — 사람이 사는 단계(E3~)부터 채워집니다."),
        ("교역", "교역 지도는 아직 없습니다 — 교역이 생기면(E7~) 채워집니다."),
        ("기술", "기술 지도는 아직 없습니다 — 발견이 생기면(E4~) 채워집니다."),
    };

    // 지형
    private static readonly Color DeepSea = C(18, 42, 82), ShallowSea = C(40, 88, 142), Lake = C(58, 112, 178);
    private static readonly Color Plain = C(108, 150, 78), Hill = C(148, 148, 90), Plateau = C(176, 158, 110),
        Mountain = C(124, 102, 84), HighMountain = C(236, 236, 240), IceLand = C(222, 230, 238);

    // 생물군계
    private static readonly Color[] BiomeColors =
    {
        C(40, 88, 142),   // 물
        C(236, 242, 250), // 얼음
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

    /// <summary>칸 색 (알파 = 거칠기: 물은 매끈하게 반짝).</summary>
    public static Color CellColor(Planet p, int c, MapMode mode, int resource)
    {
        bool ocean = p.IsOcean(c), lake = p.Terrain[c] == Terrain.Lake;
        Color col;
        switch (mode)
        {
            case MapMode.Terrain:
                col = p.Terrain[c] switch
                {
                    Terrain.DeepSea => DeepSea,
                    Terrain.ShallowSea => ShallowSea,
                    Terrain.Lake => Lake,
                    Terrain.Plain => Plain,
                    Terrain.Hill => Hill,
                    Terrain.Plateau => Plateau,
                    Terrain.Mountain => Mountain,
                    _ => HighMountain,
                };
                if (p.Biome[c] == Biome.Ice) col = IceLand;
                else if (p.Terrain[c] == Terrain.DeepSea) col = DeepSea.Darkened(Mathf.Clamp(-p.Elevation[c] / 9000f, 0, 0.35f));
                break;
            case MapMode.Biome:
                col = ocean ? ShallowSea.Darkened(p.Terrain[c] == Terrain.DeepSea ? 0.25f : 0) : lake ? Lake : BiomeColors[(int)p.Biome[c]];
                break;
            case MapMode.Temperature:
                col = Ramp(TempStops, p.Temp10[c] / 10.0);
                if (ocean || lake) col = col.Darkened(0.45f);
                break;
            case MapMode.Rain:
                col = Ramp(RainStops, p.PrecipMm[c]);
                if (ocean || lake) col = col.Darkened(0.55f);
                break;
            default:
                int lv = p.Resource(c, resource);
                col = ocean || lake ? C(22, 32, 48) : C(62, 64, 60);
                if (lv == 1) col = C(200, 160, 70);
                else if (lv == 2) col = C(255, 220, 95);
                break;
        }
        col.A = ocean || lake ? 0.45f : 0.92f;
        return col;
    }

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
            case MapMode.Terrain:
                l.Add((Plain, "평야")); l.Add((Hill, "언덕")); l.Add((Plateau, "고원")); l.Add((Mountain, "산"));
                l.Add((HighMountain, "높은 산 (3,000m~)")); l.Add((IceLand, "얼음 땅")); l.Add((Lake, "호수"));
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
            default:
                l.Add((C(255, 220, 95), "많음")); l.Add((C(200, 160, 70), "있음")); l.Add((C(62, 64, 60), "없음"));
                break;
        }
        return l;
    }
}
