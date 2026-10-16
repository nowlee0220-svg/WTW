using Godot;
using Wtw.Sim.Data;
using Wtw.Sim.Planet;
using Wtw.Sim.Provinces;
using GroundKind = Wtw.Sim.Provinces.Ground;

namespace Wtw.Game;

/// <summary>
/// 지방 지도의 그림(기획서 17절: 림월드풍 단순 일러스트, 코드로 그림). 나중에 진짜 그림이 생기면 이 파일만 바꾼다.
///   - 땅 색표(땅 종류 × 생물군계) · 무늬용 잡음 텍스처 → ProvinceView 의 땅 셰이더가 쓴다.
///   - 오브젝트 그림 모음(atlas): 그림 이름(objects.json 의 art)마다 4가지 변형, 칸 하나 128×128.
/// </summary>
public static partial class Art
{
    public const int AtlasCell = 128, AtlasCols = 16, Variants = 4;

    /// <summary>오브젝트 그림: atlas 의 첫 칸 번호 · 변형 수 · 세상에서의 크기(타일) · 서 있는 그림인가(아니면 바닥에 납작) · 바람에 흔들림.</summary>
    public sealed record Sprite(int Cell, float SizeTiles, bool Upright, float Sway);

    private static ImageTexture? _atlas, _palette, _noise;
    private static Sprite[] _sprites = Array.Empty<Sprite>();

    public static Texture2D ObjectAtlas { get { EnsureProvinceArt(); return _atlas!; } }
    public static Texture2D GroundPalette { get { EnsureProvinceArt(); return _palette!; } }
    public static Texture2D GroundNoise { get { EnsureProvinceArt(); return _noise!; } }
    /// <summary>[카탈로그 번호] → 그림.</summary>
    public static Sprite[] ObjectSprites { get { EnsureProvinceArt(); return _sprites; } }

    public static void EnsureProvinceArt()
    {
        if (_atlas is not null) return;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _palette = MakePalette();
        _noise = MakeNoise(256);
        MakeAtlas(ObjectCatalog.Default);
        GD.Print($"[art] 지방 그림 준비 {sw.Elapsed.TotalSeconds:0.00}초");
    }

    // ───────── 땅 색표 ─────────

    private static Color C(int r, int g, int b, int a = 255) => new(r / 255f, g / 255f, b / 255f, a / 255f);

    /// <summary>땅 종류 × 생물군계의 바탕색. 풀밭은 생물군계에 따라(사바나 누런 풀, 툰드라 칙칙한 풀, 열대 짙은 풀).</summary>
    public static Color GroundColor(GroundKind g, Biome b) => g switch
    {
        GroundKind.Grass => b switch
        {
            Biome.Savanna => C(168, 158, 82),
            Biome.Tundra => C(128, 136, 98),
            Biome.Rainforest => C(64, 122, 52),
            Biome.Boreal => C(84, 116, 66),
            Biome.Desert => C(160, 150, 96),
            Biome.Alpine => C(116, 130, 92),
            Biome.Wetland => C(88, 122, 70),
            Biome.Grassland => C(120, 150, 70),
            _ => C(98, 138, 64),
        },
        GroundKind.Soil => b switch
        {
            Biome.Desert or Biome.Savanna => C(150, 112, 74),
            Biome.Tundra or Biome.Alpine => C(112, 98, 80),
            Biome.Rainforest => C(104, 74, 50),
            _ => C(118, 92, 64),
        },
        GroundKind.Sand => b == Biome.Desert ? C(222, 196, 140) : C(214, 198, 152),
        GroundKind.Mud => C(96, 80, 60),
        GroundKind.Rock => b is Biome.Desert or Biome.Savanna ? C(150, 128, 110) : C(132, 130, 126),
        GroundKind.Snow => C(236, 240, 246),
        GroundKind.Ice => C(190, 218, 236),
        GroundKind.Marsh => C(82, 100, 72),
        GroundKind.ShallowWater => C(64, 128, 160),
        GroundKind.DeepWater => C(30, 72, 120),
        GroundKind.Cliff => C(78, 68, 62),
        _ => C(0, 0, 0),
    };

    private static ImageTexture MakePalette()
    {
        var img = Image.CreateEmpty(16, 16, false, Image.Format.Rgba8);
        for (int g = 0; g < 16; g++)
            for (int b = 0; b < 16; b++)
                img.SetPixel(g, b, g < GroundInfo.Count && b < BiomeIds.Count ? GroundColor((GroundKind)g, (Biome)b) : Colors.Black);
        return ImageTexture.CreateFromImage(img);
    }

    /// <summary>
    /// 이어 붙여도 이음매가 없는 경사(펄린) 잡음 네 겹(RGBA 채널마다 다른 크기, 채널마다 두 옥타브).
    /// 값 잡음은 격자 방향의 네모 얼룩이 보여서 경사 잡음을 쓴다. 땅 무늬 · 경계 흔들기 · 물결에 쓴다.
    /// </summary>
    private static ImageTexture MakeNoise(int n)
    {
        var data = new byte[n * n * 4];
        var rnd = new Rng(12345);
        int[] cellsPerSide = { 8, 16, 32, 64 };
        var sum = new float[n * n];
        for (int ch = 0; ch < 4; ch++)
        {
            Array.Clear(sum);
            float amp = 1, norm = 0;
            for (int oct = 0; oct < 2; oct++)
            {
                int k = cellsPerSide[ch] << oct;
                var gx = new float[k * k];
                var gy = new float[k * k];
                for (int i = 0; i < gx.Length; i++) { float a = rnd.F() * Mathf.Tau; gx[i] = Mathf.Cos(a); gy[i] = Mathf.Sin(a); }
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float fx = x * (float)k / n, fy = y * (float)k / n;
                        int ix = (int)fx, iy = (int)fy;
                        float tx = fx - ix, ty = fy - iy;
                        float Dot(int cx, int cy, float dx, float dy) { int g = (cy % k) * k + cx % k; return gx[g] * dx + gy[g] * dy; }
                        float v00 = Dot(ix, iy, tx, ty), v10 = Dot(ix + 1, iy, tx - 1, ty);
                        float v01 = Dot(ix, iy + 1, tx, ty - 1), v11 = Dot(ix + 1, iy + 1, tx - 1, ty - 1);
                        float ux = tx * tx * tx * (tx * (tx * 6 - 15) + 10), uy = ty * ty * ty * (ty * (ty * 6 - 15) + 10);
                        sum[y * n + x] += amp * Mathf.Lerp(Mathf.Lerp(v00, v10, ux), Mathf.Lerp(v01, v11, ux), uy);
                    }
                norm += amp;
                amp *= 0.5f;
            }
            for (int i = 0; i < sum.Length; i++)
                data[i * 4 + ch] = (byte)Math.Clamp((sum[i] / norm * 1.6f + 0.5f) * 255, 0, 255);
        }
        var img = Image.CreateFromData(n, n, false, Image.Format.Rgba8, data);
        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }

    // ───────── 오브젝트 그림 ─────────

    private sealed class Rng
    {
        private ulong _s;
        public Rng(ulong seed) { _s = seed * 0x9E3779B97F4A7C15UL + 1; }
        public float F()
        {
            _s ^= _s << 13; _s ^= _s >> 7; _s ^= _s << 17;
            return (_s >> 40) / (float)(1 << 24);
        }
        public float R(float a, float b) => a + (b - a) * F();
    }

    private static void MakeAtlas(ObjectCatalog cat)
    {
        var arts = cat.Objects.Select(o => o.Art).Distinct().ToList();
        int cells = arts.Count * Variants;
        int rows = (cells + AtlasCols - 1) / AtlasCols;
        int w = AtlasCols * AtlasCell, h = Math.Max(1, rows) * AtlasCell;
        var data = new byte[w * h * 4];
        var sk = new Sketch(AtlasCell, AtlasCell);
        var cellOf = new Dictionary<string, int>();
        for (int a = 0; a < arts.Count; a++)
        {
            cellOf[arts[a]] = a * Variants;
            for (int v = 0; v < Variants; v++)
            {
                sk.Clear();
                Paint(arts[a], sk, new Rng((ulong)(a * 31 + v * 7 + 3)));
                int cell = a * Variants + v;
                sk.CopyTo(data, w, (cell % AtlasCols) * AtlasCell, (cell / AtlasCols) * AtlasCell);
            }
        }
        var img = Image.CreateFromData(w, h, false, Image.Format.Rgba8, data);
        img.GenerateMipmaps();
        _atlas = ImageTexture.CreateFromImage(img);
        _sprites = cat.Objects.Select(o => new Sprite(cellOf[o.Art], SizeOf(o), o.Kind is "tree" or "bush" or "plant" or "rock", o.Kind switch { "tree" => 1f, "plant" => 1.6f, "bush" => 0.5f, _ => 0f })).ToArray();
    }

    private static float SizeOf(ObjectDef o) => o.Art switch
    {
        "tree_kapok" or "tree_baobab" => 3.2f,
        "tree_spruce" or "tree_pine" or "tree_palm" => 2.7f,
        _ when o.Kind == "tree" => 2.5f,
        "cactus" => 1.7f,
        _ when o.Kind == "bush" => 1.35f,
        "reeds" or "grass_tall" => 1.2f,
        _ when o.Kind == "plant" => 1.0f,
        "boulder" => 1.35f,
        _ => 1.15f,
    };

    // 서 있는 그림은 아래 가운데(64, 118)가 땅에 닿는 자리.
    private const float Bx = 64, By = 118;

    private static Color Jit(Color c, Rng r, float amount = 0.08f) =>
        new(Math.Clamp(c.R + r.R(-amount, amount), 0, 1), Math.Clamp(c.G + r.R(-amount, amount), 0, 1), Math.Clamp(c.B + r.R(-amount, amount) * 0.6f, 0, 1), c.A);

    private static void Shadow(Sketch s, float rx, float ry = 0) => s.Ellipse(Bx + 4, By - 1, rx, ry > 0 ? ry : rx * 0.32f, C(0, 0, 0, 70));

    private static void Trunk(Sketch s, float top, float w, Color c, float lean = 0)
    {
        s.Line(Bx, By, Bx + lean, top, w, w * 0.6f, c);
        s.Line(Bx - w * 0.2f, By, Bx + lean - w * 0.15f, top, w * 0.3f, w * 0.2f, c.Lightened(0.15f));
    }

    /// <summary>둥근 잎뭉치: 원 여러 개를 겹쳐 그늘 → 밝은 순서로.</summary>
    private static void Crown(Sketch s, Rng r, float cx, float cy, float rad, Color leaf, int blobs = 7)
    {
        s.Circle(cx + rad * 0.12f, cy + rad * 0.15f, rad * 1.02f, leaf.Darkened(0.35f));
        for (int i = 0; i < blobs; i++)
        {
            float a = i * Mathf.Tau / blobs + r.R(-0.3f, 0.3f);
            float d = rad * r.R(0.35f, 0.62f);
            s.Circle(cx + Mathf.Cos(a) * d, cy + Mathf.Sin(a) * d * 0.85f, rad * r.R(0.42f, 0.56f), Jit(leaf, r, 0.04f), 1);
        }
        s.Circle(cx - rad * 0.12f, cy - rad * 0.12f, rad * 0.5f, Jit(leaf.Lightened(0.08f), r, 0.03f), 1);
    }

    private static void Paint(string art, Sketch s, Rng r)
    {
        float lean = r.R(-5, 5);
        switch (art)
        {
            case "tree_oak":
            case "tree_apple":
            {
                float top = r.R(58, 70), rad = r.R(30, 38);
                Shadow(s, rad * 0.9f);
                Trunk(s, top, r.R(9, 12), C(96, 70, 46), lean);
                Crown(s, r, Bx + lean, top - rad * 0.55f, rad, art == "tree_apple" ? C(86, 140, 60) : C(70, 122, 48), 8);
                if (art == "tree_apple")
                    for (int i = 0; i < 9; i++)
                        s.Circle(Bx + lean + r.R(-rad, rad) * 0.75f, top - rad * 0.55f + r.R(-rad, rad) * 0.65f, r.R(3.2f, 4.2f), C(200, 40, 34), 1);
                break;
            }
            case "tree_birch":
            {
                float top = r.R(44, 56), rad = r.R(22, 28);
                Shadow(s, rad * 0.8f);
                s.Line(Bx, By, Bx + lean, top - 10, 8, 5, C(232, 230, 220));
                for (int i = 0; i < 5; i++) { float y = r.R(top, By - 6); s.Line(Bx - 3, y, Bx + 2, y + 1, 2, C(40, 40, 40)); }
                Crown(s, r, Bx + lean, top - rad * 0.5f, rad, C(124, 170, 72), 7);
                break;
            }
            case "tree_pine":
            case "tree_spruce":
            {
                bool spruce = art == "tree_spruce";
                float height = r.R(86, 104), width = spruce ? r.R(24, 28) : r.R(30, 36);
                Shadow(s, width * 0.8f);
                s.Line(Bx, By, Bx, By - 30, 8, 6, C(92, 64, 44));
                var leaf = spruce ? C(44, 92, 70) : C(52, 102, 58);
                int tiers = spruce ? 5 : 4;
                for (int k = 0; k < tiers; k++)
                {
                    float f = k / (float)tiers;
                    float baseY = By - 18 - f * (height - 24), topY = baseY - height / tiers * 1.5f;
                    float hw = width * (1 - f * 0.75f);
                    s.Polygon(new[] { new Vector2(Bx - hw + lean * f, baseY), new Vector2(Bx + hw + lean * f, baseY + 2), new Vector2(Bx + lean * (f + 0.2f), topY) }, Jit(leaf, r, 0.03f).Darkened(0.05f * (tiers - k)), 1);
                }
                break;
            }
            case "tree_willow":
            {
                float top = r.R(56, 64), rad = r.R(30, 34);
                Shadow(s, rad);
                Trunk(s, top, 10, C(90, 72, 52), lean);
                var leaf = C(118, 158, 78);
                Crown(s, r, Bx + lean, top - rad * 0.4f, rad, leaf, 6);
                for (int i = 0; i < 16; i++)
                {
                    float x = Bx + lean + r.R(-rad, rad) * 0.95f, y0 = top - rad * 0.3f + r.R(-8, 8);
                    s.Line(x, y0, x + r.R(-3, 3), y0 + r.R(26, 40), 3, 1.5f, Jit(leaf.Lightened(0.1f), r, 0.04f));
                }
                break;
            }
            case "tree_kapok":
            {
                float top = r.R(50, 58), rad = r.R(42, 48);
                Shadow(s, rad);
                s.Polygon(new[] { new Vector2(Bx - 16, By), new Vector2(Bx + 16, By), new Vector2(Bx + 6, top), new Vector2(Bx - 6, top) }, C(118, 100, 80), 1);
                Crown(s, r, Bx + lean, top - rad * 0.35f, rad, C(46, 112, 46), 10);
                break;
            }
            case "tree_palm":
            {
                float h = r.R(78, 92), bend = r.R(10, 20) * (r.F() < 0.5f ? -1 : 1);
                Shadow(s, 26);
                float px = Bx, py = By;
                for (int i = 1; i <= 8; i++)
                {
                    float t = i / 8f;
                    float x = Bx + bend * t * t, y = By - h * t;
                    s.Line(px, py, x, y, 9 - t * 3, C(140, 112, 76));
                    s.Line(px - 3, py, x - 3, y, 2, C(112, 88, 60));
                    px = x; py = y;
                }
                var leaf = C(62, 132, 56);
                for (int k = 0; k < 7; k++)
                {
                    float a = -Mathf.Pi / 2 + (k - 3) * 0.48f + r.R(-0.1f, 0.1f);
                    float ex = px + Mathf.Cos(a) * 40, ey = py + Mathf.Sin(a) * 26 + 14;
                    s.Line(px, py, (px + ex) / 2, (py + ey) / 2 - 8, 9, 7, Jit(leaf, r, 0.04f));
                    s.Line((px + ex) / 2, (py + ey) / 2 - 8, ex, ey, 7, 2, Jit(leaf.Darkened(0.1f), r, 0.04f));
                }
                for (int i = 0; i < 3; i++) s.Circle(px + r.R(-6, 6), py + r.R(2, 8), 5, C(120, 86, 48), 1);
                break;
            }
            case "tree_banana":
            {
                Shadow(s, 26);
                s.Line(Bx, By, Bx + lean * 0.5f, By - 46, 12, 9, C(120, 140, 70));
                var leaf = C(82, 160, 64);
                for (int k = 0; k < 6; k++)
                {
                    float a = -Mathf.Pi / 2 + (k - 2.5f) * 0.6f;
                    s.Ellipse(Bx + lean * 0.5f + Mathf.Cos(a) * 24, By - 52 + Mathf.Sin(a) * 18, 26, 9, Jit(leaf, r, 0.05f), 1, a);
                }
                s.Ellipse(Bx + 8, By - 40, 6, 10, C(220, 200, 70), 1);
                break;
            }
            case "tree_acacia":
            {
                float top = r.R(56, 64);
                Shadow(s, 38, 9);
                s.Line(Bx, By, Bx + lean, top + 10, 7, 5, C(96, 78, 58));
                s.Line(Bx + lean, top + 10, Bx + lean - 20, top, 4, 3, C(96, 78, 58));
                s.Line(Bx + lean, top + 10, Bx + lean + 22, top + 2, 4, 3, C(96, 78, 58));
                var leaf = C(112, 140, 62);
                s.Ellipse(Bx + lean + 2, top - 4, 50, 15, leaf.Darkened(0.3f));
                for (int i = 0; i < 7; i++) s.Ellipse(Bx + lean + r.R(-36, 36), top - 8 + r.R(-4, 4), r.R(16, 22), r.R(8, 11), Jit(leaf, r, 0.04f), 1);
                break;
            }
            case "tree_baobab":
            {
                float top = r.R(44, 52);
                Shadow(s, 30);
                s.Polygon(new[] { new Vector2(Bx - 24, By), new Vector2(Bx + 24, By), new Vector2(Bx + 18, top + 10), new Vector2(Bx + 8, top), new Vector2(Bx - 8, top), new Vector2(Bx - 18, top + 10) }, C(150, 124, 100), 1);
                for (int k = 0; k < 5; k++)
                {
                    float x = Bx - 16 + k * 8;
                    s.Line(x * 0.6f + Bx * 0.4f, top + 2, x + r.R(-10, 10) * 1.5f, top - r.R(14, 22), 4, 2, C(130, 104, 82));
                }
                Crown(s, r, Bx, top - 22, 22, C(98, 132, 62), 6);
                break;
            }
            case "bush_dwarf":
            {
                Shadow(s, 24);
                for (int i = 0; i < 10; i++) s.Line(Bx + r.R(-6, 6), By, Bx + r.R(-24, 24), By - r.R(14, 30), 2.5f, 1.2f, C(110, 84, 64));
                for (int i = 0; i < 14; i++) s.Circle(Bx + r.R(-24, 24), By - r.R(10, 30), r.R(5, 8), Jit(C(110, 128, 74), r, 0.05f), 1);
                break;
            }
            case "bush_berry":
            {
                Shadow(s, 30);
                Crown(s, r, Bx, By - 26, r.R(26, 30), C(66, 116, 54), 8);
                var berry = r.F() < 0.5f ? C(170, 30, 60) : C(80, 40, 120);
                for (int i = 0; i < 12; i++) s.Circle(Bx + r.R(-22, 22), By - 26 + r.R(-18, 16), r.R(3, 4), berry, 1);
                break;
            }
            case "bush_thorn":
            {
                Shadow(s, 28);
                for (int i = 0; i < 18; i++)
                {
                    float x = Bx + r.R(-26, 26), y = By - r.R(8, 36);
                    s.Line(Bx + r.R(-8, 8), By - 2, x, y, 2.4f, 1, C(104, 86, 62));
                    s.Circle(x, y, r.R(4, 6), Jit(C(108, 120, 64), r, 0.05f), 1);
                }
                break;
            }
            case "bush_dry":
            {
                Shadow(s, 24);
                for (int i = 0; i < 22; i++)
                {
                    float a = r.R(-Mathf.Pi, 0), len = r.R(14, 28);
                    s.Line(Bx, By - 4, Bx + Mathf.Cos(a) * len, By - 4 + Mathf.Sin(a) * len * 0.9f, 2.2f, 1, Jit(C(150, 124, 84), r, 0.06f));
                }
                break;
            }
            case "cactus":
            {
                Shadow(s, 18);
                var g = C(84, 140, 82);
                float h = r.R(70, 86);
                s.Line(Bx, By, Bx, By - h, 18, 16, g);
                s.Line(Bx - 3, By, Bx - 3, By - h + 4, 4, g.Lightened(0.2f));
                float ay = By - h * r.R(0.4f, 0.6f);
                s.Line(Bx, ay, Bx - 18, ay, 10, g); s.Line(Bx - 18, ay, Bx - 18, ay - 22, 10, g);
                if (r.F() < 0.7f) { float by = ay + r.R(-12, 8); s.Line(Bx, by, Bx + 17, by, 10, g); s.Line(Bx + 17, by, Bx + 17, by - 18, 10, g); }
                if (r.F() < 0.6f) s.Circle(Bx, By - h - 4, 4, C(230, 90, 120), 1);
                break;
            }
            case "grass_tall":
            {
                var g = r.F() < 0.5f ? C(132, 168, 76) : C(170, 170, 90);
                for (int i = 0; i < 26; i++)
                {
                    float x = Bx + r.R(-26, 26);
                    s.Line(x, By, x + r.R(-10, 10), By - r.R(22, 48), 3, 1, Jit(g, r, 0.07f));
                }
                break;
            }
            case "reeds":
            {
                for (int i = 0; i < 14; i++)
                {
                    float x = Bx + r.R(-24, 24), top = By - r.R(56, 80), dx = r.R(-6, 6);
                    s.Line(x, By, x + dx, top, 3, 2, Jit(C(110, 140, 70), r, 0.05f));
                    if (r.F() < 0.6f) s.Line(x + dx * 0.9f, top + 4, x + dx * 0.8f, top + 18, 6, 6, C(110, 72, 44));
                }
                break;
            }
            case "mushroom":
            {
                var cap = r.F() < 0.5f ? C(184, 52, 40) : C(150, 110, 70);
                for (int k = 0; k < 3; k++)
                {
                    float x = Bx + (k - 1) * 22 + r.R(-4, 4), sc = r.R(0.7f, 1.1f);
                    s.Ellipse(x + 3, By - 1, 12 * sc, 4 * sc, C(0, 0, 0, 60));
                    s.Line(x, By - 2, x, By - 20 * sc, 7 * sc, C(232, 224, 204));
                    s.Ellipse(x, By - 22 * sc, 14 * sc, 9 * sc, cap, 1);
                    if (cap.R > 0.6f) for (int d = 0; d < 3; d++) s.Circle(x + r.R(-8, 8) * sc, By - 24 * sc + r.R(-3, 3), 2, C(250, 245, 235));
                }
                break;
            }
            case "lichen":
            {
                s.Ellipse(Bx, By - 12, 30, 14, C(126, 124, 118), 1);
                for (int i = 0; i < 10; i++) s.Circle(Bx + r.R(-24, 24), By - 14 + r.R(-8, 6), r.R(4, 8), Jit(C(150, 160, 100), r, 0.06f), 1);
                break;
            }
            case "boulder":
            {
                Shadow(s, 36);
                var rock = Jit(C(140, 138, 132), r, 0.05f);
                var pts = new Vector2[9];
                for (int k = 0; k < pts.Length; k++)
                {
                    float a = k * Mathf.Tau / pts.Length, rr = r.R(0.8f, 1.05f);
                    pts[k] = new Vector2(Bx + Mathf.Cos(a) * 36 * rr, By - 28 + Mathf.Sin(a) * 28 * rr);
                }
                s.Polygon(pts, rock, 1.2f);
                s.Line(Bx - 12, By - 34, Bx + 6, By - 24, 2, C(90, 88, 84));
                break;
            }
            case "stones":
            {
                for (int i = 0; i < 6; i++)
                {
                    float x = Bx + r.R(-30, 30), y = By - r.R(4, 30), rr = r.R(6, 11);
                    s.Ellipse(x + 2, y + rr * 0.6f, rr, rr * 0.4f, C(0, 0, 0, 60));
                    s.Ellipse(x, y, rr, rr * 0.75f, Jit(C(146, 142, 134), r, 0.06f), 1.2f);
                }
                break;
            }
            case "flint":
            {
                for (int i = 0; i < 6; i++)
                {
                    float x = Bx + r.R(-26, 26), y = 64 + r.R(-22, 22), rr = r.R(7, 12);
                    s.Polygon(new[] { new Vector2(x - rr, y + rr * 0.4f), new Vector2(x - rr * 0.2f, y - rr * 0.7f), new Vector2(x + rr, y - rr * 0.2f), new Vector2(x + rr * 0.3f, y + rr * 0.6f) }, Jit(C(58, 58, 66), r, 0.04f), 1.4f);
                    s.Line(x - rr * 0.2f, y - rr * 0.6f, x + rr * 0.8f, y - rr * 0.2f, 1.5f, C(220, 220, 225));
                }
                break;
            }
            case "clay":
            {
                for (int i = 0; i < 5; i++) s.Ellipse(64 + r.R(-16, 16), 64 + r.R(-12, 12), r.R(22, 34), r.R(16, 24), C(138, 92, 70, 120), 0.3f, r.R(0, 3));
                for (int i = 0; i < 6; i++) { float x = 64 + r.R(-30, 30), y = 64 + r.R(-20, 20); s.Line(x, y, x + r.R(-12, 12), y + r.R(-8, 8), 1.4f, C(96, 62, 46, 160)); }
                break;
            }
            case "ore_copper":
            case "ore_tin":
            case "ore_iron":
            {
                var rock = Jit(C(118, 114, 108), r, 0.04f);
                var pts = new Vector2[8];
                for (int k = 0; k < pts.Length; k++)
                {
                    float a = k * Mathf.Tau / pts.Length, rr = r.R(0.8f, 1.05f);
                    pts[k] = new Vector2(64 + Mathf.Cos(a) * 46 * rr, 66 + Mathf.Sin(a) * 34 * rr);
                }
                s.Ellipse(68, 74, 46, 30, C(0, 0, 0, 60));
                s.Polygon(pts, rock, 1);
                var vein = art == "ore_copper" ? C(64, 170, 130) : art == "ore_tin" ? C(214, 218, 226) : C(170, 70, 40);
                var vein2 = art == "ore_copper" ? C(206, 120, 60) : art == "ore_tin" ? C(170, 178, 190) : C(120, 50, 32);
                for (int i = 0; i < 9; i++) s.Circle(64 + r.R(-30, 30), 66 + r.R(-20, 20), r.R(3, 6), r.F() < 0.6f ? vein : vein2, 1);
                break;
            }
            case "salt":
            {
                s.Ellipse(64, 64, 50, 36, C(236, 236, 232, 220), 0.4f);
                for (int i = 0; i < 9; i++)
                {
                    float x = 64 + r.R(-34, 34), y = 64 + r.R(-22, 22), rr = r.R(4, 8);
                    s.Polygon(new[] { new Vector2(x, y - rr), new Vector2(x + rr, y), new Vector2(x, y + rr), new Vector2(x - rr, y) }, C(250, 250, 252), 1);
                }
                break;
            }
            default:
                s.Circle(64, 64, 30, C(255, 0, 255));
                break;
        }
    }
}
