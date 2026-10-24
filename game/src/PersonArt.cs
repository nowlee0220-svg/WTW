using Godot;
using Wtw.Sim.People;

namespace Wtw.Game;

/// <summary>
/// 사람 그림(기획서 7-5 · 17절): 몸 · 머리 · 얼굴 · 머리카락 · 가죽 두름을 부품별로 코드가 그려 겹친다. 정면 · 옆 · 뒤 세 방향.
/// 생김새(피부 · 머리색 · 머리 모양 · 체형 · 키 · 얼굴 · 눈 · 수염)와 나이(아기 · 아이 · 어른 · 늙은이)마다 한 장씩 만들어 기억한다.
/// 그림은 모두 여기서 받는다 — 나중에 진짜 그림이 생기면 이 파일만 바꾼다.
/// </summary>
public static partial class Art
{
    public enum Facing { Front = 0, Side = 1, Back = 2 }

    public const int PersonW = 96, PersonH = 128;
    /// <summary>그림 한 장의 세로가 땅 위에서 차지하는 타일 수(어른).</summary>
    public const float PersonTiles = 1.75f;

    private static readonly Dictionary<(Appearance, bool, LifeStage, Facing, byte), ImageTexture> PersonCache = new();

    /// <summary>(E5b) 입은 것의 그림 번호: 0 기본(허리 두름) · 1 가죽을 걸침(날가죽 · 마른 가죽 · 무두질한 가죽) · 2 털가죽 옷. + 4 = 털가죽 신. + 8 = 다침(감은 끈). (E6b) + 16 × 야윔 단계(0~3).</summary>
    public static byte ClothOf(int worn, int feet, bool injured, int thin = 0)
    {
        var cat = Wtw.Sim.Data.CraftData.Default;
        int body = worn < 0 ? 0 : cat.Prop(worn, cat.P_covers) >= 5 ? 2 : 1;
        return (byte)(body | (feet >= 0 ? 4 : 0) | (injured ? 8 : 0) | (Math.Clamp(thin, 0, 3) << 4));
    }
    private static ImageTexture? _shadow, _zzz, _talk, _cloud, _bones;

    public static Texture2D Person(Appearance look, bool female, LifeStage stage, Facing facing, byte cloth = 0)
    {
        var key = (look, female, stage, facing, cloth);
        if (PersonCache.TryGetValue(key, out var tex)) return tex;
        var sk = new Sketch(PersonW, PersonH);
        PaintPerson(sk, look, female, stage, facing, cloth);
        var data = new byte[PersonW * PersonH * 4];
        sk.CopyTo(data, PersonW, 0, 0);
        var img = Image.CreateFromData(PersonW, PersonH, false, Image.Format.Rgba8, data);
        img.GenerateMipmaps();
        tex = ImageTexture.CreateFromImage(img);
        if (PersonCache.Count > 4000) PersonCache.Clear();
        PersonCache[key] = tex;
        return tex;
    }

    public static Color SkinColor(byte s)
    {
        var light = new Color(0.98f, 0.84f, 0.72f);
        var mid = new Color(0.80f, 0.60f, 0.42f);
        var dark = new Color(0.36f, 0.23f, 0.15f);
        float t = s / 255f;
        return t < 0.5f ? light.Lerp(mid, t * 2) : mid.Lerp(dark, (t - 0.5f) * 2);
    }

    public static Color HairColor(byte h, LifeStage stage)
    {
        if (stage == LifeStage.Elder) return new Color(0.78f, 0.77f, 0.74f).Lerp(new Color(0.55f, 0.54f, 0.52f), h / 255f);
        (float T, Color C)[] stops =
        {
            (0f, new Color(0.08f, 0.07f, 0.07f)), (0.35f, new Color(0.25f, 0.16f, 0.10f)), (0.6f, new Color(0.45f, 0.27f, 0.14f)),
            (0.78f, new Color(0.62f, 0.30f, 0.14f)), (0.9f, new Color(0.80f, 0.62f, 0.34f)), (1f, new Color(0.93f, 0.82f, 0.55f)),
        };
        float t = h / 255f;
        for (int i = 1; i < stops.Length; i++)
            if (t <= stops[i].T) return stops[i - 1].C.Lerp(stops[i].C, (t - stops[i - 1].T) / (stops[i].T - stops[i - 1].T));
        return stops[^1].C;
    }

    private static readonly Color[] EyeColors = { new(0.18f, 0.12f, 0.08f), new(0.30f, 0.20f, 0.10f), new(0.25f, 0.40f, 0.55f), new(0.30f, 0.42f, 0.25f) };
    private static readonly Color Ink = new(0.12f, 0.09f, 0.07f, 1f);
    private static readonly Color Leather = new(0.52f, 0.36f, 0.22f);

    /// <summary>테두리(진한 선) + 채움 타원.</summary>
    private static void OEllipse(Sketch s, float cx, float cy, float rx, float ry, Color c, float rot = 0, float w = 2.4f)
    {
        s.Ellipse(cx, cy, rx + w, ry + w, Ink, 0, rot);
        s.Ellipse(cx, cy, rx, ry, c, 1, rot);
    }

    private static void OLine(Sketch s, float x0, float y0, float x1, float y1, float w, Color c)
    {
        s.Line(x0, y0, x1, y1, w + 4.4f, Ink);
        s.Line(x0, y0, x1, y1, w, c);
    }

    private static void OPoly(Sketch s, Vector2[] pts, Color c)
    {
        // 테두리: 무게중심에서 조금 키운 다각형
        var cen = Vector2.Zero; foreach (var p in pts) cen += p; cen /= pts.Length;
        var big = pts.Select(p => p + (p - cen).Normalized() * 2.4f).ToArray();
        s.Polygon(big, Ink);
        s.Polygon(pts, c, 1);
    }

    private static readonly Color Fur = new(0.78f, 0.70f, 0.58f), FurDark = new(0.50f, 0.40f, 0.30f), Hide = new(0.60f, 0.44f, 0.30f);

    private static void PaintPerson(Sketch s, Appearance look, bool female, LifeStage stage, Facing f, byte cloth = 0)
    {
        int body = cloth & 3; bool boots = (cloth & 4) != 0, hurt = (cloth & 8) != 0;
        int thin = (cloth >> 4) & 3; // (E6b) 야윔: 몸 · 팔다리가 가늘어지고 낯빛이 흐려진다
        var skin = SkinColor(look.Skin);
        if (thin >= 2) skin = skin.Lerp(new Color(0.62f, 0.60f, 0.55f), thin == 3 ? 0.30f : 0.15f);
        var hair = HairColor(look.Hair, stage);
        float build = (0.85f + 0.3f * look.Build / 255f) * (thin switch { 1 => 0.92f, 2 => 0.78f, 3 => 0.64f, _ => 1f }); // 체형(마름 ~ 통통)
        float height = 0.92f + 0.16f * look.Height / 255f;         // 키
        float scale = stage switch { LifeStage.Baby => 0.42f, LifeStage.Child => 0.66f, _ => 1f } * height;
        float headR = stage switch { LifeStage.Baby => 30f, LifeStage.Child => 24f, _ => 21f } * (stage == LifeStage.Baby ? 1f : height);
        const float cx = PersonW / 2f;
        float feet = PersonH - 4;
        float bodyH = 52 * scale, bodyW = (female ? 15f : 17f) * build * (stage == LifeStage.Child ? 0.8f : 1f);
        float hip = feet - 22 * scale;               // 다리 위
        float shoulder = hip - bodyH * 0.72f;
        float hunch = stage == LifeStage.Elder ? 3 : 0;
        float headY = shoulder - headR * 0.82f + hunch;
        bool side = f == Facing.Side, back = f == Facing.Back;

        if (stage == LifeStage.Baby)
        {
            // 포대기에 싸인 아기: 둥근 몸 + 큰 머리
            OEllipse(s, cx, feet - 22, 22, 20, Leather.Lightened(0.15f));
            OEllipse(s, cx, feet - 46, headR * 0.9f, headR * 0.85f, skin);
            if (!back) { s.Circle(cx - 7 + (side ? 6 : 0), feet - 47, 2.2f, Ink); if (!side) s.Circle(cx + 7, feet - 47, 2.2f, Ink); }
            OEllipse(s, cx + (side ? -5 : 0), feet - 60, headR * 0.7f, headR * 0.35f, hair);
            return;
        }

        // 다리
        float legGap = side ? 3 : bodyW * 0.45f;
        OLine(s, cx - legGap, hip, cx - legGap - (side ? 3 : 1), feet - 3, 7 * build, skin.Darkened(0.05f));
        OLine(s, cx + legGap, hip, cx + legGap + (side ? 3 : 1), feet - 3, 7 * build, skin.Darkened(0.05f));
        if (boots || body == 2)
        {
            // 털가죽 신 · 바지(털옷이면 다리도 감싼다)
            var bc = body == 2 ? Fur.Darkened(0.12f) : FurDark;
            float legTop = body == 2 ? hip + 2 : feet - 16;
            OLine(s, cx - legGap, legTop, cx - legGap - (side ? 3 : 1), feet - 3, 9.5f * build, bc);
            OLine(s, cx + legGap, legTop, cx + legGap + (side ? 3 : 1), feet - 3, 9.5f * build, bc);
            if (boots) { s.Ellipse(cx - legGap - (side ? 1 : 1), feet - 4, 6.5f, 3.5f, FurDark.Darkened(0.2f)); s.Ellipse(cx + legGap + (side ? 3 : 1), feet - 4, 6.5f, 3.5f, FurDark.Darkened(0.2f)); }
        }
        // 몸통
        if (side) OEllipse(s, cx, (hip + shoulder) / 2, bodyW * 0.62f, bodyH * 0.5f, skin);
        else OEllipse(s, cx, (hip + shoulder) / 2, bodyW, bodyH * 0.5f, skin);
        // 가죽 두름: 어깨 한쪽에서 무릎까지
        float wrapTop = shoulder + 4, wrapBot = hip + 12 * scale;
        var wrapCol = Leather.Lerp(new Color(0.42f, 0.30f, 0.20f), look.Face / 4f);
        if (side)
            OPoly(s, new[] { new Vector2(cx - bodyW * 0.62f, wrapTop + 6), new Vector2(cx + bodyW * 0.55f, wrapTop), new Vector2(cx + bodyW * 0.75f, wrapBot), new Vector2(cx - bodyW * 0.75f, wrapBot) }, wrapCol);
        else
            OPoly(s, new[] { new Vector2(cx - bodyW * 0.95f, wrapTop + (female ? 2 : 10)), new Vector2(cx + bodyW * 0.9f, wrapTop), new Vector2(cx + bodyW * 1.1f, wrapBot), new Vector2(cx - bodyW * 1.1f, wrapBot) }, wrapCol);
        s.Line(cx - bodyW * 0.9f, hip - 2, cx + bodyW * 0.9f, hip - 4, 3, wrapCol.Darkened(0.3f)); // 허리끈
        if (body == 1)
        {
            // (E5b) 가죽을 어깨에 걸침: 어깨부터 무릎까지 덮는 가죽 망토
            var hc = Hide.Lerp(FurDark, look.Face / 6f);
            if (side) OPoly(s, new[] { new Vector2(cx - bodyW * 0.8f, shoulder - 2), new Vector2(cx + bodyW * 0.5f, shoulder - 3), new Vector2(cx + bodyW * 0.8f, hip + 14 * scale), new Vector2(cx - bodyW * 0.95f, hip + 16 * scale) }, hc);
            else OPoly(s, new[] { new Vector2(cx - bodyW * 1.15f, shoulder), new Vector2(cx + bodyW * 1.15f, shoulder), new Vector2(cx + bodyW * 1.25f, hip + 14 * scale), new Vector2(cx - bodyW * 1.25f, hip + 14 * scale) }, hc);
            s.Line(cx - bodyW * 1.0f, shoulder + 1, cx + bodyW * 1.0f, shoulder + 1, 4, Fur.Darkened(0.05f)); // 털 쪽 가장자리
        }
        else if (body == 2)
        {
            // (E5b) 털가죽 옷: 몸 전체 · 소매 · 목둘레 털
            if (side) OEllipse(s, cx, (hip + shoulder) / 2 + 2, bodyW * 0.85f, bodyH * 0.58f, Fur);
            else OEllipse(s, cx, (hip + shoulder) / 2 + 2, bodyW * 1.18f, bodyH * 0.58f, Fur);
            s.Line(cx - bodyW * 1.0f, hip + 6, cx + bodyW * 1.0f, hip + 4, 3, FurDark); // 허리 끈
            for (int k = -2; k <= 2; k++) s.Line(cx + k * bodyW * 0.4f, hip + 8, cx + k * bodyW * 0.4f + 1, hip + 13, 2, FurDark.Lightened(0.2f)); // 털 술
        }
        // 팔
        var arm = body == 2 ? Fur.Darkened(0.06f) : skin;
        float aw = body == 2 ? 9f : 6.5f;
        if (side) OLine(s, cx + 1, shoulder + 3, cx + 5, hip + 2, aw * build, arm);
        else
        {
            OLine(s, cx - bodyW - 1, shoulder + 4, cx - bodyW - 4, hip + 3, aw * build, arm);
            OLine(s, cx + bodyW + 1, shoulder + 4, cx + bodyW + 4, hip + 3, aw * build, arm);
        }
        if (body == 2) { s.Circle(side ? cx + 5 : cx - bodyW - 4, hip + 4, 3.4f, skin); if (!side) s.Circle(cx + bodyW + 4, hip + 4, 3.4f, skin); } // 손
        if (hurt) s.Line(side ? cx + 1 : cx - bodyW - 2, (shoulder + hip) / 2, side ? cx + 6 : cx - bodyW + 3, (shoulder + hip) / 2 + 2, 4, new Color(0.92f, 0.90f, 0.84f)); // 다친 팔에 감은 끈

        // 머리
        float hx = cx + (side ? 2 : 0);
        // 뒷머리(긴 머리는 머리 뒤로)
        int style = look.HairStyle % 8;
        bool longHair = style is 1 or 4 or 6 || (female && style is 0 or 3);
        if (longHair && style != 5) OEllipse(s, hx - (side ? 6 : 0), headY + headR * 0.55f, headR * (side ? 0.8f : 1.05f), headR * 1.0f, hair);
        float jaw = look.Face switch { 0 => 1.0f, 1 => 0.94f, 2 => 1.06f, _ => 0.98f };
        s.Ellipse(hx, headY, headR * jaw * (side ? 0.92f : 1f) + 2.4f, headR + 2.4f, Ink);
        s.Ellipse(hx, headY, headR * jaw * (side ? 0.92f : 1f), headR, skin, 1);
        if (side) OEllipse(s, hx + headR * 0.86f, headY + 3, 3.2f, 3.6f, skin); // 코
        // 얼굴
        var eye = EyeColors[look.Eyes % EyeColors.Length];
        if (!back)
        {
            if (side)
            {
                s.Circle(hx + headR * 0.42f, headY + 1, 2.6f, eye.Darkened(0.4f));
                s.Line(hx + headR * 0.3f, headY - 6, hx + headR * 0.6f, headY - 6.5f, 2, hair.Darkened(0.2f));
                s.Line(hx + headR * 0.45f, headY + 10, hx + headR * 0.7f, headY + 9.5f, 1.6f, Ink);
            }
            else
            {
                float ex = headR * 0.38f;
                s.Circle(hx - ex, headY + 1, 2.7f, eye.Darkened(0.4f));
                s.Circle(hx + ex, headY + 1, 2.7f, eye.Darkened(0.4f));
                s.Circle(hx - ex + 0.8f, headY, 0.9f, Colors.White);
                s.Circle(hx + ex + 0.8f, headY, 0.9f, Colors.White);
                s.Line(hx - ex - 3, headY - 6, hx - ex + 3, headY - 6.5f, 2, hair.Darkened(0.2f));
                s.Line(hx + ex - 3, headY - 6.5f, hx + ex + 3, headY - 6, 2, hair.Darkened(0.2f));
                s.Line(hx - 3.5f, headY + 10, hx + 3.5f, headY + 10, 1.6f, Ink);
                s.Ellipse(hx - ex - 3, headY + 6, 3, 1.6f, new Color(0.95f, 0.45f, 0.4f, 0.25f));
                s.Ellipse(hx + ex + 3, headY + 6, 3, 1.6f, new Color(0.95f, 0.45f, 0.4f, 0.25f));
            }
            // 수염
            if (!female && stage >= LifeStage.Adult && look.Beard > 0)
            {
                var bc = hair.Darkened(0.05f);
                if (side) s.Ellipse(hx + headR * 0.35f, headY + headR * 0.7f, headR * 0.45f, headR * 0.28f * look.Beard, bc);
                else s.Ellipse(hx, headY + headR * 0.72f, headR * 0.6f, headR * 0.18f * (1 + look.Beard * 0.5f), bc);
            }
        }
        // 앞머리 · 머리 모양
        float top = headY - headR * 0.55f;
        switch (style)
        {
            case 2: // 짧게 깎음
                s.Ellipse(hx, top + 2, headR * 0.95f, headR * 0.42f, hair, 1);
                break;
            case 3: // 뾰족
                for (int k = -2; k <= 2; k++) s.Polygon(new[] { new Vector2(hx + k * 7 - 6, top + 6), new Vector2(hx + k * 7 + (side ? 6 : 0), top - 14), new Vector2(hx + k * 7 + 6, top + 6) }, hair);
                s.Ellipse(hx, top + 4, headR * 0.98f, headR * 0.42f, hair, 1);
                break;
            case 5: // 묶음(뒤로 꽁지)
                s.Ellipse(hx, top + 3, headR, headR * 0.5f, hair, 1);
                OEllipse(s, hx - (side ? headR * 0.9f : 0), top - (side ? -4 : 8), 7, 7, hair);
                break;
            case 7: // 곱슬
                for (int k = 0; k < 7; k++) { float a = Mathf.Pi * (0.1f + 0.8f * k / 6f); s.Circle(hx - Mathf.Cos(a) * headR * 0.85f, top + 6 - Mathf.Sin(a) * headR * 0.45f, 7, hair, 1); }
                break;
            default:
                s.Ellipse(hx - (side ? 3 : 0), top + 3, headR * 1.02f, headR * 0.52f, hair, 1);
                if (!side && !back) s.Ellipse(hx + headR * 0.35f, top + 10, headR * 0.45f, headR * 0.22f, hair, 1, -0.3f);
                break;
        }
        if (back) s.Ellipse(hx, headY + 2, headR * 0.96f, headR * 0.92f, hair, 1); // 뒤에서는 머리카락
        if (body == 2)
        {
            // 털 두건 가장자리(얼굴 둘레)
            for (int k = 0; k < 11; k++)
            {
                float a = Mathf.Pi * (0.95f + 1.1f * k / 10f);
                s.Circle(hx + Mathf.Cos(a) * headR * 1.05f, headY + Mathf.Sin(a) * headR * 1.05f + 2, 4.6f, Fur.Lightened(0.15f), 1);
            }
            s.Ellipse(cx, shoulder + 1, bodyW * 0.9f, 5, Fur.Lightened(0.18f), 1); // 목둘레 털
        }
    }

    /// <summary>발밑 그림자.</summary>
    public static Texture2D PersonShadow => _shadow ??= Make(64, 24, s => s.Ellipse(32, 12, 28, 9, new Color(0, 0, 0, 0.35f)));
    /// <summary>잠 표시(Z).</summary>
    public static Texture2D Zzz => _zzz ??= Make(48, 48, s =>
    {
        void Z(float x, float y, float k) { s.Line(x, y, x + 10 * k, y, 3 * k, Colors.White); s.Line(x + 10 * k, y, x, y + 10 * k, 3 * k, Colors.White); s.Line(x, y + 10 * k, x + 10 * k, y + 10 * k, 3 * k, Colors.White); }
        Z(6, 26, 1.0f); Z(24, 8, 1.4f);
    });
    /// <summary>이야기 표시(말풍선).</summary>
    public static Texture2D TalkBubble => _talk ??= Make(48, 48, s =>
    {
        s.Ellipse(24, 20, 20, 15, Ink); s.Ellipse(24, 20, 18, 13, new Color(1, 1, 0.95f));
        s.Polygon(new[] { new Vector2(14, 30), new Vector2(10, 44), new Vector2(24, 32) }, new Color(1, 1, 0.95f));
        for (int k = -1; k <= 1; k++) s.Circle(24 + k * 9, 20, 2.6f, new Color(0.3f, 0.3f, 0.3f));
    });
    /// <summary>정신 붕괴 표시(먹구름).</summary>
    public static Texture2D BreakCloud => _cloud ??= Make(56, 48, s =>
    {
        var c = new Color(0.35f, 0.35f, 0.42f);
        s.Circle(18, 22, 12, c); s.Circle(32, 16, 14, c); s.Circle(42, 24, 10, c); s.Ellipse(28, 28, 22, 8, c);
        s.Line(24, 34, 20, 44, 2.5f, new Color(0.5f, 0.7f, 1f)); s.Line(34, 34, 30, 44, 2.5f, new Color(0.5f, 0.7f, 1f));
    });
    /// <summary>오래된 시신(뼈).</summary>
    public static Texture2D Bones => _bones ??= Make(64, 32, s =>
    {
        var b = new Color(0.9f, 0.88f, 0.8f);
        s.Circle(14, 16, 8, b); s.Line(22, 16, 54, 16, 4, b); s.Line(30, 8, 30, 24, 3, b); s.Line(40, 9, 40, 23, 3, b);
    });

    private static ImageTexture? _marker, _foot;
    /// <summary>발밑 표시(숲에서도 사람을 찾을 수 있게): 밝은 타원 고리.</summary>
    public static Texture2D FootMarker => _foot ??= Make(64, 28, s =>
    {
        void Ring(float rx, float ry, float w, Color c)
        {
            const int n = 40;
            for (int k = 0; k < n; k++)
            {
                float a0 = k * Mathf.Tau / n, a1 = (k + 1) * Mathf.Tau / n;
                s.Line(32 + Mathf.Cos(a0) * rx, 14 + Mathf.Sin(a0) * ry, 32 + Mathf.Cos(a1) * rx, 14 + Mathf.Sin(a1) * ry, w, c);
            }
        }
        Ring(23, 9, 5.5f, new Color(0, 0, 0, 0.5f));
        Ring(23, 9, 2.8f, new Color(1, 1, 1, 1));
    });
    /// <summary>지구본의 "사람이 사는 칸" 표시.</summary>
    public static Texture2D PeopleMarker => _marker ??= Make(44, 52, s =>
    {
        s.Circle(22, 14, 11, Ink); s.Circle(22, 14, 8.5f, new Color(1f, 0.86f, 0.5f));
        s.Ellipse(22, 40, 17, 14, Ink); s.Ellipse(22, 40, 14.5f, 11.5f, new Color(1f, 0.86f, 0.5f));
    });

    private static ImageTexture Make(int w, int h, Action<Sketch> paint)
    {
        var sk = new Sketch(w, h);
        paint(sk);
        var data = new byte[w * h * 4];
        sk.CopyTo(data, w, 0, 0);
        var img = Image.CreateFromData(w, h, false, Image.Format.Rgba8, data);
        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }
}
