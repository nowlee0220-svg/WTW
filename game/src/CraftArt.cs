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
            // (E5b) 짐승에게서 나온 것 · 가죽 일 · 사냥 도구
            case "raw_meat": case "cooked_meat": case "dried_meat": case "smoked_meat": case "raw_fish": case "cooked_fish":
                if (id is "raw_fish" or "cooked_fish")
                {
                    var fc = id == "raw_fish" ? C(150, 170, 186) : C(190, 130, 70);
                    s.Ellipse(22, 26, 14, 7, ink); s.Ellipse(22, 26, 12.5f, 5.5f, fc, 1.2f);
                    s.Polygon(new[] { new Vector2(34, 26), new Vector2(42, 19), new Vector2(42, 33) }, fc.Darkened(0.15f));
                    s.Circle(14, 25, 1.4f, ink);
                }
                else
                {
                    var mc = id switch { "raw_meat" => C(200, 70, 70), "cooked_meat" => C(150, 80, 44), "dried_meat" => C(110, 50, 40), _ => C(90, 52, 36) };
                    s.Ellipse(24, 28, 14, 10, ink); s.Ellipse(24, 28, 12.5f, 8.5f, mc, 1.2f);
                    s.Ellipse(20, 26, 4, 2.5f, C(240, 224, 210, 160));
                    s.Line(34, 32, 42, 38, 4, C(232, 224, 206));
                }
                break;
            case "raw_hide": case "scraped_hide": case "dried_hide": case "tanned_hide":
            {
                var hc = id switch { "raw_hide" => C(150, 96, 70), "scraped_hide" => C(196, 150, 112), "dried_hide" => C(176, 138, 96), _ => C(150, 108, 70) };
                var pts = new[] { new Vector2(10, 16), new Vector2(18, 10), new Vector2(30, 12), new Vector2(39, 9), new Vector2(40, 22), new Vector2(36, 36), new Vector2(26, 40), new Vector2(14, 38), new Vector2(8, 28) };
                s.Polygon(pts.Select(q => q + (q - new Vector2(24, 24)).Normalized() * 2f).ToArray(), ink);
                s.Polygon(pts, hc, 1);
                if (id == "raw_hide") s.Ellipse(24, 26, 7, 5, C(170, 60, 60, 120));
                break;
            }
            case "bone":
                s.Line(12, 34, 36, 14, 6, ink); s.Line(12, 34, 36, 14, 4, C(236, 228, 210));
                s.Circle(11, 35, 4, C(236, 228, 210)); s.Circle(37, 13, 4, C(236, 228, 210));
                break;
            case "sinew":
                for (int k = 0; k < 3; k++) s.Line(10 + k * 3, 36 - k, 38 - k * 2, 12 + k * 2, 1.6f, C(220, 200, 170));
                break;
            case "fat":
                s.Ellipse(24, 28, 12, 9, ink); s.Ellipse(24, 28, 10.5f, 7.5f, C(244, 236, 200), 1.2f);
                break;
            case "bone_needle":
                s.Line(10, 38, 38, 10, 3.6f, ink); s.Line(10, 38, 38, 10, 2.2f, C(236, 228, 210));
                s.Circle(13, 35, 2, ink);
                break;
            case "stone_spear":
                s.Line(8, 42, 34, 14, 6, ink); s.Line(8, 42, 34, 14, 4.2f, wood);
                Shard(s, 37, 11, 6, 4, C(70, 76, 90), -0.8f);
                s.Line(29, 18, 33, 16, 3, C(190, 160, 96));
                break;
            case "fur_coat": case "fur_boots":
            {
                var fc = C(200, 180, 150);
                if (id == "fur_coat")
                {
                    var pts = new[] { new Vector2(16, 8), new Vector2(32, 8), new Vector2(42, 18), new Vector2(36, 22), new Vector2(34, 42), new Vector2(14, 42), new Vector2(12, 22), new Vector2(6, 18) };
                    s.Polygon(pts.Select(q => q + (q - new Vector2(24, 24)).Normalized() * 2f).ToArray(), ink);
                    s.Polygon(pts, fc, 1);
                    s.Ellipse(24, 9, 8, 3, C(236, 226, 206));
                }
                else
                {
                    s.Ellipse(18, 30, 7, 12, ink); s.Ellipse(18, 30, 5.5f, 10.5f, fc, 1);
                    s.Ellipse(31, 32, 7, 12, ink); s.Ellipse(31, 32, 5.5f, 10.5f, fc.Darkened(0.1f), 1);
                }
                break;
            }
            // (E5c) 씨 · 낟알로 만든 것 · 괭이
            case "grain": case "roasted_grain": case "flour":
            {
                var c = id switch { "grain" => C(214, 180, 96), "roasted_grain" => C(164, 112, 52), _ => C(238, 230, 210) };
                if (id == "flour") { s.Ellipse(24, 32, 15, 8, ink); s.Ellipse(24, 31, 13.5f, 6.5f, c, 1); break; }
                for (int k = 0; k < 7; k++) { float x = 14 + (k % 4) * 6 + (k / 4) * 3, y = 24 + (k / 4) * 7; s.Ellipse(x, y, 4, 2.6f, ink, 0, 0.5f); s.Ellipse(x, y, 3, 1.8f, c, 0.6f, 0.5f); }
                break;
            }
            case "beans": case "cooked_beans":
                for (int k = 0; k < 5; k++) { float x = 14 + k * 5, y = 28 + (k % 2) * 4; s.Ellipse(x, y, 4.4f, 3.2f, ink); s.Ellipse(x, y, 3.4f, 2.4f, id == "beans" ? C(150, 110, 70) : C(120, 80, 50), 0.8f); }
                break;
            case "tuber": case "roasted_tuber":
                s.Ellipse(24, 28, 14, 9, ink, 0, 0.3f); s.Ellipse(24, 28, 12.5f, 7.5f, id == "tuber" ? C(160, 110, 80) : C(120, 72, 40), 1.2f, 0.3f);
                s.Circle(18, 26, 1.5f, C(90, 60, 40)); s.Circle(28, 30, 1.5f, C(90, 60, 40));
                break;
            case "flatbread":
                s.Ellipse(24, 28, 16, 9, ink); s.Ellipse(24, 28, 14.5f, 7.5f, C(214, 170, 110), 1.2f);
                for (int k = 0; k < 4; k++) s.Circle(16 + k * 5, 27 + (k % 2) * 2, 1.6f, C(150, 100, 50));
                break;
            case "stone_hoe":
                s.Line(12, 42, 30, 10, 5, ink); s.Line(12, 42, 30, 10, 3.4f, wood);
                s.Polygon(new[] { new Vector2(26, 8), new Vector2(40, 12), new Vector2(38, 20), new Vector2(28, 16) }, C(120, 116, 110), 1.2f);
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

/// <summary>(E5) 벽 그림 — 바위 면 · 동굴 벽은 잿빛 돌판, 나무껍질은 갈색 띠 위에 숯으로 그린 무늬. 그린 것(불 · 그릇 · 날 · 끈 · 그 밖)에 따라 모양이 다르다.</summary>
public static partial class Art
{
    private static readonly Dictionary<(byte, int), ImageTexture> PaintingTex = new();

    /// <summary>glyph: 0 손자국(알아볼 수 없는 무늬) · 1 불 · 2 그릇 · 3 날 · 4 끈 · 5 먹을 것 · 6 그 밖.</summary>
    public static Texture2D Painting(byte medium, int glyph)
    {
        if (PaintingTex.TryGetValue((medium, glyph), out var t)) return t;
        t = Make(64, 64, s =>
        {
            var ink = C(28, 22, 20, 235);
            var ochre = C(150, 64, 40, 220);
            if (medium == 0)
            {
                s.Polygon(new[] { new Vector2(14, 4), new Vector2(50, 6), new Vector2(52, 60), new Vector2(12, 58) }, C(112, 82, 58), 0.15f);
                for (int k = 0; k < 6; k++) s.Line(16 + k * 6, 8, 15 + k * 6, 56, 1.2f, C(80, 58, 40, 160));
            }
            else
            {
                s.Polygon(new[] { new Vector2(6, 10), new Vector2(56, 4), new Vector2(60, 54), new Vector2(4, 60) }, medium == 2 ? C(96, 92, 88) : C(132, 126, 118), 0.25f);
                s.Line(10, 20, 22, 26, 1, C(70, 66, 62, 120)); s.Line(40, 50, 54, 46, 1, C(70, 66, 62, 120));
            }
            switch (glyph)
            {
                case 1: // 불: 장작 위의 불꽃
                    s.Line(20, 50, 44, 42, 3, ink); s.Line(44, 50, 20, 42, 3, ink);
                    s.Polygon(new[] { new Vector2(32, 14), new Vector2(42, 36), new Vector2(32, 44), new Vector2(22, 36) }, ochre);
                    s.Polygon(new[] { new Vector2(32, 24), new Vector2(37, 37), new Vector2(32, 42), new Vector2(27, 37) }, ink);
                    break;
                case 2: // 그릇
                    s.Ellipse(32, 38, 14, 12, ink); s.Ellipse(32, 36, 11, 9, ochre); s.Line(18, 26, 46, 26, 3, ink);
                    break;
                case 3: // 날 · 도끼
                    s.Line(22, 52, 38, 18, 3, ink);
                    s.Polygon(new[] { new Vector2(34, 14), new Vector2(48, 20), new Vector2(42, 32), new Vector2(32, 24) }, ochre);
                    break;
                case 4: // 끈 · 엮음
                    for (int k = 0; k < 4; k++) s.Line(16 + k * 8, 20 + (k % 2) * 20, 24 + k * 8, 40 - (k % 2) * 20, 2.5f, ink);
                    break;
                case 5: // 먹을 것(열매)
                    s.Circle(26, 34, 7, ochre); s.Circle(38, 30, 7, ochre); s.Circle(34, 42, 7, ink); s.Line(32, 22, 34, 14, 2, ink);
                    break;
                case 6: // 그 밖: 사람이 무언가를 하는 모습(막대 사람)
                    s.Circle(26, 20, 4, ink); s.Line(26, 24, 26, 40, 2.5f, ink); s.Line(26, 40, 20, 52, 2.5f, ink); s.Line(26, 40, 32, 52, 2.5f, ink);
                    s.Line(26, 30, 40, 28, 2.5f, ink); s.Circle(44, 30, 5, ochre);
                    break;
                default: // 손자국
                    s.Ellipse(30, 36, 8, 10, ochre);
                    for (int k = 0; k < 4; k++) s.Line(24 + k * 4, 28, 22 + k * 5, 16, 3, ochre);
                    s.Line(38, 36, 46, 30, 3, ochre);
                    break;
            }
        });
        PaintingTex[(medium, glyph)] = t;
        return t;
    }

    /// <summary>그린 법의 결과 아이템 → 그림 모양.</summary>
    public static int PaintingGlyph(int rule)
    {
        var c = CraftData.Default;
        if ((uint)rule >= (uint)c.Rules.Count) return 0;
        string id = c.Catalog.Items[c.Rules[rule].Result].Id;
        return id switch
        {
            "campfire" or "ember" or "hearth" or "charcoal" => 1,
            "wet_pot" or "dry_pot" or "pot" or "leaf_cup" or "stew" => 2,
            "flake" or "scraper" or "stone_knife" or "chopper" or "ground_axe" or "hafted_axe" or "spear" or "copper_blade" => 3,
            "cord" or "basket" or "dry_grass" => 4,
            "cooked_fruit" or "roasted_mushroom" or "roasted_acorn" or "dried_fruit" or "cracked_coconut" => 5,
            _ => 6,
        };
    }
}

/// <summary>(E5) 머리 위 표시: 가르침(손짓과 빛) · 배움(물음표) · 그림 보기(눈) · 그리기(숯 막대).</summary>
public static partial class Art
{
    private static ImageTexture? _teach, _learn, _look, _draw;
    public static Texture2D TeachIcon => _teach ??= Make(48, 48, s =>
    {
        s.Circle(24, 22, 17, C(250, 236, 190, 235), 0.2f);
        s.Line(16, 30, 24, 14, 3.5f, C(120, 84, 40)); s.Line(24, 14, 32, 30, 3.5f, C(120, 84, 40)); s.Line(19, 24, 29, 24, 3, C(120, 84, 40));
        s.Circle(36, 10, 3, C(255, 210, 90));
    });
    public static Texture2D LearnIcon => _learn ??= Make(48, 48, s =>
    {
        s.Circle(24, 22, 17, C(220, 236, 250, 235), 0.2f);
        s.Line(18, 16, 24, 11, 3.5f, C(50, 80, 140)); s.Line(24, 11, 30, 16, 3.5f, C(50, 80, 140)); s.Line(30, 16, 24, 24, 3.5f, C(50, 80, 140)); s.Line(24, 24, 24, 28, 3.5f, C(50, 80, 140));
        s.Circle(24, 33, 2.4f, C(50, 80, 140));
    });
    public static Texture2D LookIcon => _look ??= Make(48, 48, s =>
    {
        s.Ellipse(24, 22, 18, 11, C(245, 240, 230, 235), 0.2f);
        s.Circle(24, 22, 7, C(70, 50, 30)); s.Circle(24, 22, 3, C(10, 10, 10)); s.Circle(22, 20, 1.5f, C(255, 255, 255));
    });
    public static Texture2D DrawIcon => _draw ??= Make(48, 48, s =>
    {
        s.Circle(24, 22, 17, C(236, 226, 214, 235), 0.2f);
        s.Line(14, 32, 32, 12, 5, C(30, 26, 24)); s.Line(12, 34, 20, 28, 2, C(150, 64, 40));
    });
}
