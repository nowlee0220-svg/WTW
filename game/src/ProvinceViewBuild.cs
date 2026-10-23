using Godot;
using Wtw.Sim.Provinces;
using Wtw.Sim.Runtime;

namespace Wtw.Game;

/// <summary>
/// (E6) 지방 화면의 건물 · 무덤 · 걷은 덤불. 시뮬 스냅샷을 읽기만 한다.
///   - 세워진 것(바람막이 · 움집 · 시렁 · 가죽 틀 · 가마 · 돌무더기)은 사람과 같은 앞뒤 정렬 안에, 바닥에 붙은 것(구덩이 · 받침 돌 · 흙무덤)은 밭과 같은 층에.
///   - 짓는 중: 날라 온 재료 무더기 + 반쯤 비치는 뼈대(지은 만큼 짙게). 낡음: 거무스름하게, 군데군데 빈 곳.
///   - 걷은 덤불 · 풀: 그 타일의 오브젝트를 빼고 다시 놓는다.
/// </summary>
public partial class ProvinceView
{
    private readonly List<Sprite2D> _buildUp = new(), _buildFlat = new(), _buildPile = new();
    private readonly Dictionary<int, int> _clearedSeen = new();

    private void UpdateBuild(WorldSnapshot snap)
    {
        int nu = 0, nf = 0, np = 0;
        var defs = Wtw.Sim.Data.BuildingData.Default.Buildings;
        foreach (var sh in _shown)
        {
            int cell = sh.Map.Cell;
            ApplyCleared(snap, sh);
            if (!snap.Buildings.TryGetValue(cell, out var list)) continue;
            foreach (var b in list)
            {
                var d = defs[b.Kind];
                if (d.Id == "fire_pit") continue; // 불자리는 불(돌 화덕)로 그린다
                // 차지한 타일들의 가운데 · 발 자리(그 아래 끝)
                Vector2 c = Vector2.Zero;
                for (int y = 0; y < b.H; y++) for (int x = 0; x < b.W; x++) c += TileWorld(sh, b.Tile + y * HexFrame.Size + x);
                c /= b.W * b.H;
                bool flat = Art.BuildingFlat(d.Id);
                var tex = Art.Building(d.Id, b.Done, b.Condition < Wtw.Sim.People.PeopleSim.WornCond, b.Quality, b.Id);
                Sprite2D s;
                if (flat)
                {
                    if (nf == _buildFlat.Count) { var sp = new Sprite2D { Material = _premul }; _farmRoot.AddChild(sp); _buildFlat.Add(sp); }
                    s = _buildFlat[nf++];
                    s.Offset = Vector2.Zero;
                    s.Position = c;
                }
                else
                {
                    if (nu == _buildUp.Count) { var sp = new Sprite2D { Material = _premul, Centered = true }; _peopleRoot.AddChild(sp); _buildUp.Add(sp); }
                    s = _buildUp[nu++];
                    s.Offset = new Vector2(0, -tex.GetHeight() / 2f + 6);
                    s.Position = c + new Vector2(0, (b.H * 0.5f) * TilePx);
                }
                s.Texture = tex;
                float sc = Math.Max(b.W, 1) * (flat ? 1.2f : 1.25f) * TilePx / tex.GetWidth();
                s.Scale = new Vector2(sc, sc);
                // 짓는 중: 비치는 뼈대(지은 만큼 짙게)
                float a = b.Done ? 1f : 0.25f + 0.55f * b.Progress; s.Modulate = new Color(a, a, a, a); // 미리 곱한 알파
                s.Visible = true;
                if (!b.Done)
                {
                    // 날라 온 재료 무더기
                    if (np == _buildPile.Count) { var sp = new Sprite2D { Material = _premul, Texture = Art.BuildPile }; _farmRoot.AddChild(sp); _buildPile.Add(sp); }
                    var pile = _buildPile[np++];
                    pile.Scale = Vector2.One * 0.8f * TilePx / 64f;
                    pile.Position = c + new Vector2(0.35f * TilePx * b.W, 0.25f * TilePx);
                    pile.Visible = b.Progress > 0.02f;
                }
            }
        }
        for (int k = nu; k < _buildUp.Count; k++) _buildUp[k].Visible = false;
        for (int k = nf; k < _buildFlat.Count; k++) _buildFlat[k].Visible = false;
        for (int k = np; k < _buildPile.Count; k++) _buildPile[k].Visible = false;
    }

    private void HideBuild()
    {
        foreach (var s in _buildUp) s.Visible = false;
        foreach (var s in _buildFlat) s.Visible = false;
        foreach (var s in _buildPile) s.Visible = false;
    }

    /// <summary>걷은 덤불 · 풀: 수가 바뀌면 그 지방의 오브젝트를 다시 놓는다(걷은 타일은 빼고).</summary>
    private void ApplyCleared(WorldSnapshot snap, Shown sh)
    {
        int n = snap.Cleared.TryGetValue(sh.Map.Cell, out var cl) ? cl.Length : 0;
        int seen = _clearedSeen.TryGetValue(sh.Map.Cell, out int v) ? v : 0;
        if (n == seen) return;
        _clearedSeen[sh.Map.Cell] = n;
        _clearedTiles[sh.Map.Cell] = cl is null ? new HashSet<int>() : new HashSet<int>(cl);
        FreeObjects(sh.Objects);
        _faded.Clear();
        sh.Objects = BuildObjects(sh.Place, sh.Map, sh.Node);
    }

    /// <summary>지방마다 걷은 타일(오브젝트를 놓을 때 뺀다).</summary>
    private readonly Dictionary<int, HashSet<int>> _clearedTiles = new();
    private bool IsCleared(int cell, int tile) => _clearedTiles.TryGetValue(cell, out var set) && set.Contains(tile);
}

public static partial class Art
{
    private static readonly Dictionary<long, ImageTexture> BuildTex = new();
    private static ImageTexture? _buildPile;

    public static bool BuildingFlat(string id) => id is "storage_pit" or "knap_spot" or "grave";

    /// <summary>짓는 자리에 쌓인 재료(가지 · 풀 단 · 돌).</summary>
    public static Texture2D BuildPile => _buildPile ??= Make(64, 64, s =>
    {
        s.Ellipse(32, 44, 22, 8, C(0, 0, 0, 60));
        for (int k = 0; k < 5; k++) s.Line(12 + k * 3, 40 - k, 50 - k * 2, 34 + k, 2.2f, C(120, 86, 52));
        s.Ellipse(24, 36, 9, 6, C(186, 168, 96), 0.8f);
        s.Ellipse(42, 40, 6, 4, C(140, 136, 128), 1);
    });

    /// <summary>건물 그림(코드로). 같은 모습은 한 번만 그린다(변형 셋 — 번호마다 조금 다르게).</summary>
    public static Texture2D Building(string id, bool done, bool worn, int quality, int seed)
    {
        int variant = (int)((uint)seed * 2654435761u >> 30) % 3;
        long key = (long)(Wtw.Sim.Data.BuildingData.Default.Index(id) + 1) << 8 | (done ? 1L : 0) | (worn ? 2L : 0) | (long)variant << 2 | (long)Math.Clamp(quality, 0, 3) << 4;
        if (BuildTex.TryGetValue(key, out var t)) return t;
        var r = new Rng((ulong)key * 31 + 7);
        t = id switch
        {
            "windbreak" => Make(128, 80, s => Windbreak(s, r, worn)),
            "hut" => Make(128, 120, s => Hut(s, r, worn, quality)),
            "tent" => Make(128, 120, s => Tent(s, r, worn)),
            "storage_pit" => Make(64, 64, s => Pit(s, r, worn)),
            "rack" => Make(64, 88, s => Rack(s, r, worn)),
            "knap_spot" => Make(64, 64, s => Knap(s, r)),
            "hide_frame" => Make(64, 88, s => HideFrame(s, r, worn)),
            "kiln" => Make(72, 72, s => Kiln(s, r, worn)),
            "grave" => Make(64, 64, s => Grave(s, r)),
            "cairn" => Make(64, 64, s => Cairn(s, r)),
            _ => Make(64, 64, s => s.Ellipse(32, 40, 20, 12, C(140, 110, 70), 1)),
        };
        BuildTex[key] = t;
        return t;
    }

    private static Color Aged(Color c, bool worn) => worn ? c.Darkened(0.28f).Lerp(C(110, 100, 86), 0.25f) : c;

    private static void Thatch(Sketch s, Rng r, float x0, float x1, float yTop, float yBot, bool worn, int strands)
    {
        var straw = Aged(C(196, 172, 102), worn);
        for (int k = 0; k < strands; k++)
        {
            if (worn && r.F() < 0.18f) continue; // 낡아 군데군데 빠짐
            float x = x0 + (x1 - x0) * k / Math.Max(1, strands - 1) + r.R(-1.5f, 1.5f);
            s.Line(x, yTop + r.R(-2, 2), x + r.R(-3, 3), yBot, 2.4f, Jit(straw, r, 0.06f));
        }
    }

    private static void Windbreak(Sketch s, Rng r, bool worn)
    {
        s.Ellipse(64, 72, 56, 7, C(0, 0, 0, 60));
        var wood = Aged(C(112, 80, 48), worn);
        // 비스듬히 기댄 가지들 + 그 위에 덮은 풀
        for (int k = 0; k < 9; k++) { float x = 14 + k * 12.5f; s.Line(x, 72, x + 10, 14 + r.R(-3, 3), 3f, Jit(wood, r)); }
        Thatch(s, r, 14, 116, 20, 70, worn, 34);
        s.Line(10, 22, 120, 18, 3f, wood.Darkened(0.2f)); // 가로 묶은 가지
    }

    /// <summary>(E6) 가죽 천막: 뼈 · 막대 기둥을 원뿔로 세우고 가죽을 이어 덮음.</summary>
    private static void Tent(Sketch s, Rng r, bool worn)
    {
        s.Ellipse(64, 108, 54, 9, C(0, 0, 0, 70));
        var hide = Aged(C(150, 112, 74), worn);
        s.Polygon(new[] { new Vector2(64, 18), new Vector2(118, 106), new Vector2(10, 106) }, hide.Darkened(0.12f), 0.8f);
        s.Polygon(new[] { new Vector2(64, 22), new Vector2(110, 102), new Vector2(18, 102) }, hide, 0.6f);
        // 가죽 이음새 · 꿰맨 자리
        for (int k = 0; k < 4; k++) { float x = 30 + k * 18 + r.R(-2, 2); s.Line(64, 26, x, 102, 1.4f, hide.Darkened(0.3f)); }
        if (worn) for (int k = 0; k < 4; k++) s.Ellipse(36 + r.R(0, 56), 60 + r.R(0, 36), 5, 3, C(86, 70, 48, 200));
        // 꼭대기로 삐져나온 기둥(뼈 · 막대)
        var pole = Aged(C(222, 210, 180), worn);
        s.Line(58, 24, 50, 6, 2.6f, pole); s.Line(70, 24, 78, 6, 2.6f, pole); s.Line(64, 22, 64, 4, 2.6f, pole);
        // 문(걷어 올린 가죽)
        s.Polygon(new[] { new Vector2(64, 66), new Vector2(78, 104), new Vector2(50, 104) }, C(40, 30, 22));
    }

    private static void Hut(Sketch s, Rng r, bool worn, int quality)
    {
        s.Ellipse(64, 108, 58, 10, C(0, 0, 0, 70));
        // 둥근 지붕(풀로 덮음) — 품질이 좋을수록 촘촘
        var straw = Aged(C(184, 156, 90), worn);
        s.Ellipse(64, 70, 56, 46, straw.Darkened(0.12f), 1.4f);
        s.Ellipse(64, 66, 50, 40, straw, 1.2f);
        for (int row = 0; row < 7 + quality; row++)
        {
            float y = 34 + row * (64f / (7 + quality));
            float half = 48 * Mathf.Sqrt(Math.Max(0.05f, 1 - Mathf.Pow((y - 66) / 44f, 2)));
            s.Line(64 - half, y, 64 + half, y + 1, 1.6f, straw.Darkened(0.25f));
        }
        if (worn) for (int k = 0; k < 5; k++) s.Ellipse(36 + r.R(0, 56), 40 + r.R(0, 40), 5, 3, C(86, 70, 48, 200));
        // 꼭대기 묶음 · 문
        s.Line(60, 26, 64, 16, 3f, C(120, 90, 56)); s.Line(68, 26, 64, 16, 3f, C(120, 90, 56));
        s.Ellipse(64, 96, 13, 14, C(34, 26, 20), 0.6f);
        s.Polygon(new[] { new Vector2(51, 96), new Vector2(77, 96), new Vector2(77, 110), new Vector2(51, 110) }, C(34, 26, 20));
    }

    private static void Pit(Sketch s, Rng r, bool worn)
    {
        s.Ellipse(32, 34, 26, 16, C(78, 58, 38));
        s.Ellipse(32, 32, 22, 13, Aged(C(176, 156, 92), worn), 1);
        for (int k = 0; k < 10; k++) s.Line(14 + k * 4, 24 + r.R(-2, 2), 16 + k * 4, 42, 1.4f, Aged(C(150, 128, 70), worn));
        for (int k = 0; k < 6; k++) { float a = k * Mathf.Tau / 6; s.Circle(32 + Mathf.Cos(a) * 25, 34 + Mathf.Sin(a) * 15, 3, C(128, 124, 116), 1); }
    }

    private static void Rack(Sketch s, Rng r, bool worn)
    {
        s.Ellipse(32, 82, 24, 5, C(0, 0, 0, 60));
        var wood = Aged(C(116, 84, 52), worn);
        foreach (float x in new[] { 10f, 54f }) s.Line(x, 82, x, 24, 3.2f, wood);
        s.Line(6, 34, 58, 34, 3f, wood.Darkened(0.15f)); s.Line(6, 46, 58, 46, 3f, wood.Darkened(0.15f));
        for (int k = 0; k < 4; k++) s.Ellipse(16 + k * 10, 30, 5, 4, Jit(C(196, 170, 96), r), 0.8f); // 말리는 다발
        s.Line(8, 24, 56, 24, 1.4f, C(160, 140, 90)); // 묶은 끈
    }

    private static void Knap(Sketch s, Rng r)
    {
        s.Ellipse(32, 40, 22, 9, C(0, 0, 0, 60));
        s.Ellipse(32, 34, 20, 11, C(132, 128, 120), 1.2f);
        s.Ellipse(30, 31, 14, 6, C(156, 152, 144), 0.8f);
        for (int k = 0; k < 9; k++) s.Polygon(new[] { new Vector2(10 + r.R(0, 44), 44 + r.R(-2, 6)), new Vector2(13 + r.R(0, 44), 46), new Vector2(11 + r.R(0, 44), 48) }, C(70, 70, 76)); // 깨진 조각
    }

    private static void HideFrame(Sketch s, Rng r, bool worn)
    {
        s.Ellipse(32, 82, 24, 5, C(0, 0, 0, 60));
        var wood = Aged(C(110, 80, 50), worn);
        s.Line(10, 82, 12, 18, 3f, wood); s.Line(54, 82, 52, 18, 3f, wood);
        s.Line(8, 22, 56, 22, 3f, wood); s.Line(8, 64, 56, 64, 3f, wood);
        s.Polygon(new[] { new Vector2(15, 26), new Vector2(49, 26), new Vector2(47, 60), new Vector2(17, 60) }, Aged(C(176, 138, 98), worn), 0.6f); // 펴서 맨 가죽
        for (int k = 0; k < 4; k++) { s.Line(14, 28 + k * 9, 16, 28 + k * 9, 1.2f, C(70, 52, 36)); s.Line(48, 28 + k * 9, 50, 28 + k * 9, 1.2f, C(70, 52, 36)); }
    }

    private static void Kiln(Sketch s, Rng r, bool worn)
    {
        s.Ellipse(36, 64, 30, 7, C(0, 0, 0, 70));
        var clay = Aged(C(166, 112, 74), worn);
        s.Ellipse(36, 42, 28, 24, clay.Darkened(0.12f), 1.2f);
        s.Ellipse(36, 40, 24, 20, clay, 1);
        for (int k = 0; k < 7; k++) { float a = Mathf.Pi * (0.1f + 0.8f * k / 6); s.Circle(36 + Mathf.Cos(a) * 27, 60 - Mathf.Sin(a) * 4, 4, C(120, 116, 110), 1); }
        s.Ellipse(36, 50, 9, 8, C(40, 22, 14)); // 아궁이
        s.Ellipse(36, 22, 6, 3, C(60, 40, 30)); // 위 구멍
        s.Ellipse(33, 14, 9, 5, C(80, 80, 80, 70)); // 그을음
    }

    private static void Grave(Sketch s, Rng r)
    {
        s.Ellipse(32, 36, 24, 13, C(0, 0, 0, 50));
        s.Ellipse(32, 33, 22, 11, C(112, 84, 56), 1.2f);
        s.Ellipse(30, 30, 16, 6, C(132, 102, 70), 0.6f);
        for (int k = 0; k < 4; k++) s.Circle(18 + k * 9 + r.R(-1, 1), 26 + r.R(-2, 2), 2.2f, C(150, 146, 138), 1);
    }

    private static void Cairn(Sketch s, Rng r)
    {
        s.Ellipse(32, 52, 24, 7, C(0, 0, 0, 60));
        for (int row = 0; row < 4; row++)
            for (int k = 0; k <= 3 - row; k++)
            {
                float x = 32 + (k - (3 - row) / 2f) * 11 + r.R(-1.5f, 1.5f), y = 46 - row * 9;
                s.Ellipse(x, y, 6.5f, 5, Jit(C(140, 136, 128), r, 0.06f), 1.2f);
            }
    }
}
