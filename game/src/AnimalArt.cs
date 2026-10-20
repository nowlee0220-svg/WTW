using Godot;

namespace Wtw.Game;

/// <summary>
/// (E5b) 짐승 그림 — 모두 코드로 그린다(옆모습, 오른쪽을 본다). 걷기 두 장 · 엎드림 한 장. 나중에 진짜 그림이 생기면 이 파일만 바꾼다.
/// 서 있는 그림은 아래 가운데가 땅에 닿는 자리(PersonArt 와 같다).
/// </summary>
public static partial class Art
{
    public const int AnimalW = 112, AnimalH = 88;
    private static readonly Dictionary<(string, int, bool), ImageTexture> AnimalCache = new();

    /// <summary>짐승 그림: art = animals.json 의 art, frame 0 · 1 = 걷기, 2 = 엎드려 쉼. tame = 사람을 따르는 짐승(목에 끈).</summary>
    public static Texture2D Animal(string art, int frame, bool tame = false)
    {
        var key = (art, frame, tame);
        if (AnimalCache.TryGetValue(key, out var t)) return t;
        t = Make(AnimalW, AnimalH, s => PaintAnimal(s, art, frame, tame));
        AnimalCache[key] = t;
        return t;
    }

    /// <summary>짐승 그림 한 장의 가로가 땅 위에서 차지하는 타일 수(크기마다).</summary>
    public static float AnimalTiles(string art) => art switch
    {
        "rabbit" => 0.55f, "bird" => 0.5f, "deer" or "reindeer" or "gazelle" or "ibex" => 1.25f, "boar" or "wolf" => 1.1f, "bison" => 1.7f, "bear" => 1.6f, "cat" => 1.45f, _ => 1f,
    };

    private sealed record Shape(Color Body, Color Belly, float Len, float H, float Leg, float LegW, float Neck, float Head, string Ears, string Horns, string Tail, bool Hump, bool Mane);

    private static Shape ShapeOf(string art) => art switch
    {
        "deer" => new(C(168, 116, 70), C(222, 196, 160), 34, 15, 26, 4.2f, 16, 8, "pointed", "antlers", "short", false, false),
        "reindeer" => new(C(124, 104, 86), C(226, 220, 206), 34, 16, 24, 4.6f, 14, 8.5f, "pointed", "bigantlers", "short", false, true),
        "gazelle" => new(C(206, 156, 96), C(244, 236, 220), 30, 13, 26, 3.4f, 15, 7, "pointed", "straight", "short", false, false),
        "ibex" => new(C(136, 126, 110), C(196, 188, 172), 30, 15, 20, 4.4f, 10, 8, "pointed", "curved", "short", false, false),
        "boar" => new(C(84, 70, 60), C(110, 96, 84), 34, 19, 13, 5.4f, 3, 10, "small", "tusks", "thin", true, true),
        "bison" => new(C(86, 60, 42), C(110, 80, 56), 44, 24, 18, 6.8f, 2, 12, "small", "short", "thin", true, true),
        "wolf" => new(C(130, 130, 128), C(206, 204, 198), 32, 14, 18, 4.2f, 9, 8.5f, "pointed", "", "bushy", false, false),
        "bear" => new(C(90, 62, 40), C(110, 78, 52), 42, 26, 15, 8f, 4, 12, "round", "", "none", true, false),
        "cat" => new(C(208, 160, 82), C(238, 214, 168), 38, 16, 18, 5f, 8, 10, "round", "", "long", false, true),
        _ => new(C(150, 130, 110), C(200, 190, 170), 30, 14, 18, 4, 10, 8, "pointed", "", "short", false, false),
    };

    private static void PaintAnimal(Sketch s, string art, int frame, bool tame)
    {
        const float cx = AnimalW / 2f, ground = AnimalH - 6;
        s.Ellipse(cx, ground, 30, 5, new Color(0, 0, 0, 0.28f));
        if (art == "rabbit") { PaintRabbit(s, cx, ground, frame); return; }
        if (art == "bird") { PaintBird(s, cx, ground, frame); return; }
        var sh = ShapeOf(art);
        bool lie = frame == 2;
        float leg = lie ? 3 : sh.Leg;
        float by = ground - leg - sh.H * 0.55f; // 몸 가운데
        float bx = cx - 4;
        float swing = frame == 0 ? 5 : frame == 1 ? -5 : 0;
        // 다리(먼 쪽은 어둡게)
        if (!lie)
        {
            var far = sh.Body.Darkened(0.28f);
            OLine(s, bx - sh.Len * 0.55f, by + sh.H * 0.3f, bx - sh.Len * 0.55f - swing * 0.6f, ground - 1, sh.LegW, far);
            OLine(s, bx + sh.Len * 0.5f, by + sh.H * 0.3f, bx + sh.Len * 0.5f + swing * 0.6f, ground - 1, sh.LegW, far);
        }
        // 꼬리
        float tx = bx - sh.Len - 2, ty = by - sh.H * 0.3f;
        switch (sh.Tail)
        {
            case "bushy": OEllipse(s, tx - 6, ty + 8, 9, 5, sh.Body.Darkened(0.05f), 0.6f); break;
            case "long": OLine(s, tx + 4, ty, tx - 12, ty + 14, 2.6f, sh.Body); s.Circle(tx - 13, ty + 15, 3.4f, sh.Body.Darkened(0.4f)); break;
            case "thin": OLine(s, tx + 4, ty, tx - 4, ty + 9, 1.6f, sh.Body.Darkened(0.2f)); break;
            case "short": OEllipse(s, tx + 3, ty, 4, 3, sh.Belly); break;
        }
        // 몸통 · 혹
        OEllipse(s, bx, by, sh.Len, sh.H, sh.Body);
        s.Ellipse(bx + 2, by + sh.H * 0.5f, sh.Len * 0.75f, sh.H * 0.32f, sh.Belly);
        if (sh.Hump) OEllipse(s, bx + sh.Len * 0.45f, by - sh.H * 0.45f, sh.Len * 0.45f, sh.H * 0.7f, sh.Mane ? sh.Body.Darkened(0.12f) : sh.Body);
        // 가까운 다리
        if (!lie)
        {
            OLine(s, bx - sh.Len * 0.45f, by + sh.H * 0.4f, bx - sh.Len * 0.45f + swing, ground, sh.LegW, sh.Body.Darkened(0.08f));
            OLine(s, bx + sh.Len * 0.6f, by + sh.H * 0.4f, bx + sh.Len * 0.6f - swing, ground, sh.LegW, sh.Body.Darkened(0.08f));
        }
        else
        {
            s.Ellipse(bx - sh.Len * 0.4f, ground - 3, 7, 3, sh.Body.Darkened(0.2f));
            s.Ellipse(bx + sh.Len * 0.6f, ground - 3, 7, 3, sh.Body.Darkened(0.2f));
        }
        // 목 · 머리
        float nx = bx + sh.Len * 0.85f, ny = by - sh.H * 0.2f;
        float hx = nx + sh.Neck * 0.55f, hy = ny - sh.Neck - (lie ? -4 : 0);
        if (sh.Neck > 4) OLine(s, nx - 2, ny + 2, hx, hy + 2, sh.H * 0.7f, sh.Body);
        if (sh.Mane && art is "cat") OEllipse(s, hx - 2, hy + 2, sh.Head * 1.3f, sh.Head * 1.35f, C(150, 96, 40));
        if (sh.Mane && art is "reindeer") s.Ellipse(nx + 2, ny + 4, sh.H * 0.6f, sh.H * 0.55f, sh.Belly);
        // 귀
        switch (sh.Ears)
        {
            case "pointed": OPoly(s, new[] { new Vector2(hx - 3, hy - sh.Head * 0.6f), new Vector2(hx - 6, hy - sh.Head * 1.6f), new Vector2(hx + 1, hy - sh.Head * 0.8f) }, sh.Body.Darkened(0.1f)); break;
            case "round": OEllipse(s, hx - 3, hy - sh.Head * 0.85f, 3.4f, 3.4f, sh.Body.Darkened(0.1f)); break;
            case "small": OEllipse(s, hx - 4, hy - sh.Head * 0.7f, 2.6f, 2.2f, sh.Body.Darkened(0.1f)); break;
        }
        // 뿔
        var horn = C(220, 206, 176);
        switch (sh.Horns)
        {
            case "antlers":
                OLine(s, hx - 1, hy - sh.Head * 0.7f, hx - 4, hy - sh.Head * 2.4f, 1.6f, horn);
                OLine(s, hx - 3, hy - sh.Head * 1.6f, hx + 3, hy - sh.Head * 2.2f, 1.4f, horn);
                break;
            case "bigantlers":
                OLine(s, hx - 1, hy - sh.Head * 0.7f, hx - 8, hy - sh.Head * 2.7f, 1.8f, horn);
                OLine(s, hx - 5, hy - sh.Head * 1.8f, hx + 4, hy - sh.Head * 2.6f, 1.5f, horn);
                OLine(s, hx - 7, hy - sh.Head * 2.4f, hx - 13, hy - sh.Head * 2.9f, 1.4f, horn);
                OLine(s, hx + 1, hy - sh.Head * 0.5f, hx + 6, hy + 2, 1.4f, horn);
                break;
            case "straight": OLine(s, hx - 1, hy - sh.Head * 0.6f, hx - 4, hy - sh.Head * 2.4f, 1.6f, C(60, 50, 40)); break;
            case "curved":
                OLine(s, hx - 1, hy - sh.Head * 0.7f, hx - 8, hy - sh.Head * 1.9f, 2.2f, C(120, 104, 80));
                OLine(s, hx - 8, hy - sh.Head * 1.9f, hx - 13, hy - sh.Head * 0.8f, 2f, C(120, 104, 80));
                break;
            case "short": OLine(s, hx - 3, hy - sh.Head * 0.6f, hx - 8, hy - sh.Head * 1.3f, 2.4f, C(60, 52, 44)); break;
        }
        // 머리 · 주둥이 · 눈
        OEllipse(s, hx, hy, sh.Head, sh.Head * 0.8f, sh.Body);
        OEllipse(s, hx + sh.Head * 0.85f, hy + sh.Head * 0.25f, sh.Head * 0.6f, sh.Head * 0.45f, art is "boar" or "bear" ? sh.Body.Darkened(0.1f) : sh.Belly);
        if (sh.Horns == "tusks") s.Line(hx + sh.Head * 0.9f, hy + sh.Head * 0.5f, hx + sh.Head * 1.2f, hy - sh.Head * 0.1f, 1.8f, horn);
        s.Circle(hx + sh.Head * 1.35f, hy + sh.Head * 0.2f, 1.6f, Ink);
        s.Circle(hx + sh.Head * 0.25f, hy - sh.Head * 0.15f, 1.7f, Ink);
        if (tame) s.Line(nx + 1, ny - 2, nx + 6, ny + 6, 2.6f, C(200, 60, 50)); // 목끈
    }

    private static void PaintRabbit(Sketch s, float cx, float ground, int frame)
    {
        var fur = C(150, 128, 104);
        bool lie = frame == 2;
        float hop = frame == 1 ? 4 : 0;
        float by = ground - 12 - hop + (lie ? 3 : 0);
        OEllipse(s, cx - 4, by, 14, 10, fur);
        OEllipse(s, cx - 16, by - 2, 4, 4, C(240, 236, 228)); // 꼬리
        OEllipse(s, cx + 10, by - 8, 7, 6, fur);
        OLine(s, cx + 8, by - 12, cx + 4, by - 28, 2.6f, fur);
        OLine(s, cx + 11, by - 12, cx + 10, by - 27, 2.6f, fur.Darkened(0.08f));
        if (!lie) OLine(s, cx - 8, by + 6, cx - 2 - hop * 2, ground - 1, 2.6f, fur.Darkened(0.1f));
        s.Circle(cx + 13, by - 9, 1.4f, Ink);
    }

    private static void PaintBird(Sketch s, float cx, float ground, int frame)
    {
        var body = C(126, 98, 70);
        float lift = frame == 1 ? 3 : 0;
        float by = ground - 14 - lift;
        OPoly(s, new[] { new Vector2(cx - 20, by - 4), new Vector2(cx - 8, by - 2), new Vector2(cx - 10, by + 4) }, body.Darkened(0.2f)); // 꼬리
        OEllipse(s, cx - 2, by, 12, 8, body);
        s.Ellipse(cx - 2, by + 3, 9, 4, C(196, 172, 140));
        OEllipse(s, cx - 3, by - 1, 8, 4.5f, body.Darkened(frame == 1 ? 0.25f : 0.12f), frame == 1 ? -0.5f : 0.15f); // 날개
        OEllipse(s, cx + 9, by - 7, 5, 4.5f, body);
        s.Polygon(new[] { new Vector2(cx + 13, by - 8), new Vector2(cx + 18, by - 6.5f), new Vector2(cx + 13, by - 5.5f) }, C(200, 160, 60));
        s.Circle(cx + 10, by - 8, 1.2f, Ink);
        if (frame != 2) { s.Line(cx - 3, by + 7, cx - 4, ground - 1, 1.4f, C(170, 120, 60)); s.Line(cx + 1, by + 7, cx + 2, ground - 1, 1.4f, C(170, 120, 60)); }
    }
}
