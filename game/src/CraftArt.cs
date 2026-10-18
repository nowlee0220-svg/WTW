using Godot;
using Wtw.Sim.Data;

namespace Wtw.Game;

/// <summary>
/// (E4) 재료와 발견의 그림 — 모닥불 · 돌 화덕 · 꺼진 불 자리 · 불빛 · 만든 물건 아이콘. 모두 코드로 그린다(나중에 진짜 그림이 생기면 이 파일만 바꾼다).
/// 서 있는 그림은 아래 가운데가 땅에 닿는 자리(PersonArt 와 같다).
/// </summary>
public static partial class Art
{
    private static ImageTexture? _campfire, _campfire2, _hearth, _hearth2, _ashes, _hearthCold, _glow;
    private static readonly Dictionary<int, ImageTexture> ItemIcons = new();

    /// <summary>모닥불(타는) — 불꽃 두 장을 번갈아 그려 일렁이게.</summary>
    public static Texture2D Campfire(int frame) => frame == 0 ? _campfire ??= MakeFire(false, 0) : _campfire2 ??= MakeFire(false, 1);
    public static Texture2D Hearth(int frame) => frame == 0 ? _hearth ??= MakeFire(true, 0) : _hearth2 ??= MakeFire(true, 1);
    /// <summary>꺼진 모닥불 자리(재) · 꺼진 화덕.</summary>
    public static Texture2D Ashes => _ashes ??= MakeFire(false, -1);
    public static Texture2D HearthCold => _hearthCold ??= MakeFire(true, -1);

    /// <summary>불빛(더하는 빛): 가운데가 밝은 둥근 빛, 가장자리는 0.</summary>
    public static Texture2D FireGlow => _glow ??= MakeGlow(128);

    private static ImageTexture MakeGlow(int n)
    {
        var img = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        float c = (n - 1) / 2f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                float a = Mathf.Clamp(1 - d, 0, 1);
                a = a * a * (3 - 2 * a);
                img.SetPixel(x, y, new Color(1f, 0.62f, 0.25f, a));
            }
        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }

    private static ImageTexture MakeFire(bool hearth, int frame) => Make(96, 96, s =>
    {
        const float bx = 48, by = 80;
        s.Ellipse(bx + 3, by + 2, 32, 9, C(0, 0, 0, 70));
        if (hearth)
        {
            // 둘러싼 돌
            var r = new Rng(7);
            for (int k = 0; k < 9; k++)
            {
                float a = Mathf.Pi * (0.05f + k / 8f * 0.9f) + Mathf.Pi;
                float x = bx + Mathf.Cos(a) * 30, y = by - 4 + Mathf.Sin(a) * 12;
                s.Ellipse(x, y + 1, 9.5f, 7.5f, C(30, 28, 26));
                s.Ellipse(x, y, 8, 6, Jit(C(140, 134, 126), r, 0.05f), 1.2f);
            }
        }
        // 장작(엇갈림)
        s.Line(bx - 20, by - 2, bx + 16, by - 12, 7, C(88, 60, 38));
        s.Line(bx + 20, by - 2, bx - 14, by - 13, 7, C(104, 72, 46));
        s.Line(bx - 4, by + 1, bx + 6, by - 16, 6, C(78, 54, 34));
        if (frame < 0)
        {
            // 꺼짐: 재와 숯
            s.Ellipse(bx, by - 6, 16, 6, C(120, 118, 114, 220));
            for (int k = 0; k < 6; k++) s.Circle(bx - 12 + k * 5, by - 6 + (k % 2) * 2, 2.6f, C(30, 28, 28));
            return;
        }
        s.Ellipse(bx, by - 6, 15, 6, C(255, 150, 40, 200));
        float sway = frame == 0 ? -3 : 3;
        // 불꽃: 바깥(붉은 주황) → 안(노랑)
        s.Polygon(new[] { new Vector2(bx - 16, by - 8), new Vector2(bx - 6 + sway, by - 46), new Vector2(bx + 2 + sway * 0.5f, by - 30), new Vector2(bx + 8 - sway, by - 52), new Vector2(bx + 16, by - 8) }, C(232, 96, 30, 235));
        s.Polygon(new[] { new Vector2(bx - 9, by - 8), new Vector2(bx - 2 + sway, by - 34), new Vector2(bx + 4 - sway * 0.5f, by - 38), new Vector2(bx + 10, by - 8) }, C(255, 186, 60, 245));
        s.Polygon(new[] { new Vector2(bx - 4, by - 8), new Vector2(bx + sway * 0.5f, by - 22), new Vector2(bx + 5, by - 8) }, C(255, 245, 190, 250));
        // 불티
        s.Circle(bx + 10 - sway * 2, by - 58, 1.8f, C(255, 200, 90));
        s.Circle(bx - 8 + sway, by - 62, 1.4f, C(255, 170, 70));
    });

    /// <summary>아이템 아이콘(48×48): 만든 물건은 모양으로, 자원은 색 · 크기로.</summary>
    public static Texture2D ItemIcon(int item)
    {
        if (ItemIcons.TryGetValue(item, out var t)) return t;
        var cat = ObjectCatalog.Default;
        string id = (uint)item < (uint)cat.Items.Count ? cat.Items[item].Id : "";
        t = Make(48, 48, s => PaintItem(id, s));
        ItemIcons[item] = t;
        return t;
    }

    private static void Shard(Sketch s, float cx, float cy, float w, float h, Color c, float tilt)
    {
        var pts = new[] { new Vector2(cx - w, cy + h * 0.4f), new Vector2(cx - w * 0.2f, cy - h), new Vector2(cx + w, cy - h * 0.2f), new Vector2(cx + w * 0.3f, cy + h) };
        for (int i = 0; i < pts.Length; i++) pts[i] = (pts[i] - new Vector2(cx, cy)).Rotated(tilt) + new Vector2(cx, cy);
        var big = pts.Select(p => p + (p - new Vector2(cx, cy)).Normalized() * 2f).ToArray();
        s.Polygon(big, C(24, 22, 22, 230));
        s.Polygon(pts, c, 1.3f);
    }

    private static void PaintItem(string id, Sketch s)
    {
        var ink = C(24, 22, 20, 230);
        var wood = C(118, 82, 50);
        switch (id)
        {
            case "flake": case "dull_flake": case "stone_knife": case "scraper": case "broken_stone":
                Shard(s, 24, 26, id == "stone_knife" ? 15 : 11, id == "scraper" ? 9 : 12, id == "broken_stone" ? C(130, 126, 120) : C(70, 76, 90), -0.5f);
                if (id is "flake" or "stone_knife") s.Line(14, 34, 34, 16, 2, C(230, 236, 240));
                break;
            case "chopper": case "ground_axe":
                s.Ellipse(24, 26, 15, 12, ink); s.Ellipse(24, 26, 13, 10, id == "ground_axe" ? C(110, 116, 112) : C(132, 126, 118), 1.3f);
                s.Line(12, 30, 20, 16, 2.4f, C(220, 224, 226));
                break;
            case "hafted_axe":
                s.Line(12, 42, 32, 10, 5, ink); s.Line(12, 42, 32, 10, 3.4f, wood);
                s.Ellipse(32, 14, 10, 7, ink, 0, 0.6f); s.Ellipse(32, 14, 8.5f, 5.5f, C(110, 116, 112), 1.2f, 0.6f);
                s.Line(27, 18, 33, 12, 2, C(196, 170, 120));
                break;
            case "stick": case "spear": case "split_wood": case "wood":
                s.Line(10, 40, 38, 10, id == "split_wood" || id == "wood" ? 9 : 6, ink); s.Line(10, 40, 38, 10, id == "split_wood" || id == "wood" ? 7 : 4.4f, wood);
                if (id == "spear") s.Polygon(new[] { new Vector2(35, 13), new Vector2(42, 5), new Vector2(38, 16) }, C(52, 36, 24));
                break;
            case "cord":
                for (int k = 0; k < 3; k++) s.Ellipse(24, 24, 15 - k * 4, 10 - k * 3, C(190, 160, 96), 0);
                s.Ellipse(24, 24, 3, 2, C(60, 50, 30));
                break;
            case "basket":
                s.Ellipse(24, 30, 17, 11, ink); s.Ellipse(24, 30, 15, 9.5f, C(184, 150, 88), 1);
                for (int k = 0; k < 5; k++) s.Line(12 + k * 6, 24, 12 + k * 6, 38, 1.4f, C(130, 100, 56));
                s.Ellipse(24, 23, 15, 4, C(120, 92, 52));
                break;
            case "leaf_cup":
                s.Ellipse(24, 28, 15, 9, ink); s.Ellipse(24, 28, 13, 7.5f, C(80, 150, 70), 1);
                break;
            case "wet_pot": case "dry_pot": case "pot": case "cracked_pot":
            {
                var c = id switch { "wet_pot" => C(122, 88, 66), "dry_pot" => C(176, 146, 112), "cracked_pot" => C(170, 104, 72), _ => C(192, 104, 64) };
                s.Ellipse(24, 30, 14, 13, ink); s.Ellipse(24, 30, 12.5f, 11.5f, c, 1.2f);
                s.Ellipse(24, 16, 9, 4, ink); s.Ellipse(24, 16, 7.5f, 3, c.Darkened(0.35f));
                if (id == "pot") s.Line(13, 30, 35, 30, 1.6f, C(110, 52, 30));
                if (id == "cracked_pot") s.Line(20, 20, 26, 38, 1.6f, ink);
                break;
            }
            case "copper_lump": case "molten_copper": case "copper_blade":
                if (id == "copper_blade") Shard(s, 24, 24, 15, 7, C(200, 112, 60), -0.6f);
                else { s.Ellipse(24, 28, 13, 9, ink); s.Ellipse(24, 28, 11.5f, 7.5f, C(196, 108, 56), 1.4f); }
                s.Circle(20, 24, 2, C(255, 210, 160));
                break;
            case "charcoal":
                for (int k = 0; k < 3; k++) { s.Ellipse(16 + k * 8, 28 - k * 2, 7, 5, C(20, 20, 22)); s.Line(13 + k * 8, 27 - k * 2, 19 + k * 8, 26 - k * 2, 1, C(80, 80, 86)); }
                break;
            case "ash":
                s.Ellipse(24, 30, 14, 6, C(150, 148, 144));
                break;
            case "dry_grass": case "grass": case "reed":
                for (int k = 0; k < 9; k++) s.Line(18 + k * 1.5f, 40, 10 + k * 3.5f, 10, 1.8f, id == "dry_grass" ? C(214, 190, 110) : C(120, 160, 80));
                s.Line(14, 30, 32, 30, 2.4f, C(140, 110, 60));
                break;
            case "ember":
                s.Circle(24, 28, 9, C(255, 120, 30, 200)); s.Circle(24, 28, 5, C(255, 220, 120));
                break;
            case "cooked_fruit": case "roasted_mushroom": case "roasted_acorn": case "cracked_coconut": case "dried_fruit": case "stew":
                s.Ellipse(24, 30, 13, 10, ink);
                s.Ellipse(24, 30, 11.5f, 8.5f, id switch { "dried_fruit" => C(140, 70, 50), "stew" => C(150, 110, 70), "cracked_coconut" => C(240, 236, 220), _ => C(178, 92, 46) }, 1.2f);
                break;
            default:
                // 자원: 둥근 돌 · 열매
                s.Ellipse(24, 28, 11, 9, ink);
                s.Ellipse(24, 28, 9.5f, 7.5f, id switch
                {
                    "flint" => C(70, 76, 90), "obsidian" => C(28, 26, 34), "quartz" => C(232, 232, 236), "clay" => C(140, 96, 72), "copper_ore" => C(80, 160, 120),
                    "tin_ore" => C(200, 204, 214), "iron_ore" => C(160, 72, 50), "salt" => C(246, 246, 244), "birch_bark" => C(236, 232, 220), "big_leaf" => C(80, 150, 70),
                    "twigs" => C(130, 96, 60), "resin" => C(220, 150, 50), "apple" => C(200, 50, 40), "berries" => C(140, 40, 90), "banana" => C(230, 200, 70),
                    "mushroom" => C(190, 60, 40), "acorn" => C(140, 100, 60), "coconut" => C(110, 80, 50), _ => C(146, 140, 132),
                }, 1.3f);
                break;
        }
    }
}
