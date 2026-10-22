using Godot;
using Wtw.Sim.People;
using Wtw.Sim.Runtime;

namespace Wtw.Game;

/// <summary>
/// (E5c) 지방 화면의 밭 · 심은 자리 · 저절로 난 싹 · 모아 둔 자리. 시뮬 스냅샷을 읽기만 한다.
///   - 일군 밭은 고랑이 난 짙은 흙, 그냥 묻은 자리는 작은 흙 무더기, 저절로 난 싹은 흙 없이 풀만.
///   - 단계: 묻음(흙만) → 싹(작은 초록) → 자람(키 큰 초록) → 익음(낟알은 누렇게, 콩은 꼬투리, 덩이뿌리는 넓은 잎). 짐승이 먹은 칸은 듬성듬성, 잡풀이 많으면 풀 포기.
///   - 모아 둔 자리: 돌로 두른 깔개(그 위에 놓인 것은 땅의 물건 아이콘으로).
/// </summary>
public partial class ProvinceView
{
    private Node2D _farmRoot = null!;
    private readonly List<Sprite2D> _plotSprites = new(), _stashSprites = new();

    private void ReadyFarm()
    {
        _farmRoot = new Node2D { ZIndex = 2 }; // 땅(구운 땅 포함)보다 위, 사람 · 불보다 아래
        AddChild(_farmRoot);
    }

    private void UpdateFarm(WorldSnapshot snap)
    {
        int np = 0, ns = 0;
        foreach (var sh in _shown)
        {
            int cell = sh.Map.Cell;
            if (snap.Plots.TryGetValue(cell, out var plots))
                foreach (var q in plots)
                {
                    if (np == _plotSprites.Count) { var sp = new Sprite2D { Material = _premul }; _farmRoot.AddChild(sp); _plotSprites.Add(sp); }
                    var s = _plotSprites[np++];
                    s.Texture = Art.Plot(q.Stage, q.Tilled, q.Wild, q.Crop, q.Damage >= 50, q.Weeds >= 60, q.Tile);
                    s.Scale = Vector2.One * 1.1f * TilePx / 64f;
                    s.Position = TileWorld(sh, q.Tile);
                    var p0 = TileWorld(sh, q.Tile); var p1 = TileWorld(sh, q.Tile + 1);
                    s.Rotation = (p1 - p0).Angle(); // 타일 결을 따라(지방마다 놓인 방향이 다르다)
                    s.Visible = true;
                    if (q.Wild && q.Stage >= PlotStage.Sprout) FadeAround(s.Position, 0.6f); // 저절로 난 싹은 작아서 — 나무에 가리지 않게
                }
            if (snap.Stashes.TryGetValue(cell, out int st))
            {
                if (ns == _stashSprites.Count) { var sp = new Sprite2D { Material = _premul, Texture = Art.Stash }; _farmRoot.AddChild(sp); _stashSprites.Add(sp); }
                var s = _stashSprites[ns++];
                s.Scale = Vector2.One * 1.4f * TilePx / 64f;
                s.Position = TileWorld(sh, st);
                s.Visible = true;
            }
        }
        for (int k = np; k < _plotSprites.Count; k++) _plotSprites[k].Visible = false;
        for (int k = ns; k < _stashSprites.Count; k++) _stashSprites[k].Visible = false;
    }

    private void HideFarm()
    {
        foreach (var s in _plotSprites) s.Visible = false;
        foreach (var s in _stashSprites) s.Visible = false;
    }
}

public static partial class Art
{
    private static readonly Dictionary<int, ImageTexture> PlotTex = new();
    private static ImageTexture? _stash;

    /// <summary>모아 둔 자리: 둥근 깔개를 작은 돌로 두름.</summary>
    public static Texture2D Stash => _stash ??= Make(64, 64, s =>
    {
        s.Ellipse(32, 36, 26, 15, C(0, 0, 0, 60));
        s.Ellipse(32, 34, 24, 13, C(150, 120, 74), 1.2f);
        for (int k = 0; k < 6; k++) s.Line(12 + k * 8, 26, 12 + k * 8, 42, 1.3f, C(120, 92, 52));
        var r = new Rng(11);
        for (int k = 0; k < 10; k++)
        {
            float a = k * Mathf.Tau / 10;
            s.Ellipse(32 + Mathf.Cos(a) * 26, 34 + Mathf.Sin(a) * 15, 4, 3, Jit(C(130, 126, 118), r, 0.05f), 1);
        }
    });

    /// <summary>밭 한 칸(64×64, 가운데가 타일 가운데). 같은 모습은 한 번만 그린다(변형 셋 — 타일마다 조금 다르게).</summary>
    public static Texture2D Plot(PlotStage stage, bool tilled, bool wild, int crop, bool eaten, bool weedy, int tile)
    {
        var cat = Wtw.Sim.Data.ObjectCatalog.Default;
        string id = (uint)crop < (uint)cat.Items.Count ? cat.Items[crop].Id : "";
        int kind = id == "beans" ? 1 : id == "tuber" ? 2 : 0;
        int variant = (int)((uint)tile * 2654435761u >> 30) % 3;
        int key = (int)stage | (tilled ? 8 : 0) | (wild ? 16 : 0) | (kind << 5) | (eaten ? 128 : 0) | (weedy ? 256 : 0) | (variant << 9);
        if (PlotTex.TryGetValue(key, out var t)) return t;
        t = Make(64, 64, s => PaintPlot(s, stage, tilled, wild, kind, eaten, weedy, new Rng((ulong)key * 7 + 1)));
        PlotTex[key] = t;
        return t;
    }

    private static void PaintPlot(Sketch s, PlotStage stage, bool tilled, bool wild, int kind, bool eaten, bool weedy, Rng r)
    {
        var soil = C(104, 74, 48);
        if (tilled)
        {
            s.Polygon(new[] { new Vector2(4, 9), new Vector2(60, 9), new Vector2(60, 57), new Vector2(4, 57) }, C(0, 0, 0, 40));
            s.Polygon(new[] { new Vector2(5, 7), new Vector2(59, 7), new Vector2(59, 53), new Vector2(5, 53) }, soil);
            for (int k = 0; k < 5; k++) s.Line(7, 13 + k * 9, 57, 13 + k * 9, 2.2f, soil.Darkened(0.3f));
        }
        else if (!wild) s.Ellipse(32, 36, 20, 11, soil, 1.5f);
        else { s.Ellipse(32, 35, 21, 13, C(0, 0, 0, 50)); s.Ellipse(32, 34, 19, 11, C(126, 96, 62, 235), 1.2f); } // 흘린 씨가 떨어진 맨땅(머물던 자리)
        if (stage == PlotStage.Empty) { if (weedy) Weeds(s, r, 6); return; }
        if (stage == PlotStage.Sown) { for (int k = 0; k < 6; k++) s.Circle(14 + k * 7, 34 + r.R(-6, 6), 1.6f, C(214, 180, 96)); return; }
        int rows = tilled ? 5 : 3, per = tilled ? 6 : 3;
        for (int row = 0; row < rows; row++)
            for (int k = 0; k < per; k++)
            {
                if (eaten && r.F() < 0.55f) continue;
                float x = (tilled ? 10 + k * 9 : 20 + k * 10) + r.R(-2, 2), y = (tilled ? 15 + row * 9 : 28 + row * 6) + r.R(-1, 1);
                if (stage == PlotStage.Sprout) { s.Line(x, y + 3, x - 2.5f, y - 4, 2.4f, C(150, 210, 80)); s.Line(x, y + 3, x + 3, y - 3, 2.4f, C(150, 210, 80)); continue; }
                bool ripe = stage == PlotStage.Ripe;
                switch (kind)
                {
                    case 0: // 낟알: 줄기 + 이삭
                        s.Line(x, y + 3, x + r.R(-1, 1), y - 11, 1.6f, ripe ? C(196, 170, 90) : C(120, 160, 70));
                        s.Ellipse(x, y - 12, 1.8f, 4, ripe ? C(226, 188, 96) : C(150, 180, 90), 0.5f);
                        break;
                    case 1: // 콩: 둥근 잎 + 꼬투리
                        s.Circle(x, y - 4, 3.6f, C(80, 136, 62));
                        if (ripe) s.Line(x - 1, y - 2, x + 2, y + 3, 2.2f, C(170, 150, 80));
                        break;
                    default: // 덩이뿌리: 넓은 잎
                        s.Ellipse(x - 2, y - 5, 3.6f, 2, ripe ? C(110, 140, 60) : C(70, 140, 60), 0.5f, -0.6f);
                        s.Ellipse(x + 2, y - 5, 3.6f, 2, ripe ? C(110, 140, 60) : C(70, 140, 60), 0.5f, 0.6f);
                        break;
                }
            }
        if (weedy) Weeds(s, r, 4);
    }

    private static void Weeds(Sketch s, Rng r, int n)
    {
        for (int i = 0; i < n; i++)
        {
            float x = r.R(8, 56), y = r.R(14, 52);
            for (int k = 0; k < 4; k++) s.Line(x, y, x + r.R(-5, 5), y - r.R(5, 10), 1.4f, C(96, 140, 60));
        }
    }
}
