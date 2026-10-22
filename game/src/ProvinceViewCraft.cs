using Godot;
using Wtw.Sim.Provinces;
using Wtw.Sim.Runtime;

namespace Wtw.Game;

/// <summary>
/// (E4) 지방 화면의 불 · 불빛 · 땅에 놓인 물건 · 캔 바위. 시뮬 스냅샷을 읽기만 한다.
///   - 모닥불 · 돌 화덕: 사람과 같은 앞뒤 정렬 안에(불꽃 두 장을 번갈아 그려 일렁임), 꺼진 불은 재.
///   - 불빛: 더하는 빛 한 장(밤일수록 밝게, 조금씩 흔들림) — 땅 셰이더는 건드리지 않는다.
///   - 땅의 물건: 작은 아이콘(죽은 사람이 남긴 것 · 내려놓은 것).
///   - 캐낸 바위: 땅 자료의 그 타일만 자갈로 바꿔 다시 올린다.
/// </summary>
public partial class ProvinceView
{
    private Node2D _itemRoot = null!, _glowRoot = null!;
    private readonly List<Sprite2D> _fireSprites = new(), _glowSprites = new(), _itemSprites = new(), _paintSprites = new();
    private CanvasItemMaterial _addMat = null!;
    private readonly Dictionary<int, int> _dugSeen = new();

    private void ReadyCraft()
    {
        _addMat = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        ReadyFarm(); // (E5c) 밭은 땅의 물건보다 먼저(같은 층에서 밑에) 그린다
        _itemRoot = new Node2D { ZIndex = 2 };
        AddChild(_itemRoot);
        _glowRoot = new Node2D { ZIndex = 2 };
        AddChild(_glowRoot);
    }

    /// <summary>불 · 불빛 · 물건 · 캔 자리를 맞춘다(매 프레임).</summary>
    private void UpdateCraft(WorldSnapshot snap)
    {
        float t = (float)(Time.GetTicksMsec() / 1000.0);
        int nf = 0, ng = 0, ni = 0, np = 0;
        foreach (var sh in _shown)
        {
            int cell = sh.Map.Cell;
            if (snap.Fires.TryGetValue(cell, out var fires))
                foreach (var f in fires)
                {
                    var pos = TileWorld(sh, f.Tile);
                    if (nf == _fireSprites.Count) { var sp = new Sprite2D { Material = _premul, Centered = true }; _peopleRoot.AddChild(sp); _fireSprites.Add(sp); }
                    var s = _fireSprites[nf++];
                    int frame = ((int)(t * 6 + f.Tile)) & 1;
                    s.Texture = f.Lit ? (f.Kind == 2 ? Art.Hearth(frame) : Art.Campfire(frame)) : f.Kind == 2 ? Art.HearthCold : Art.Ashes;
                    float scale = (f.Kind == 2 ? 1.8f : 1.5f) * TilePx / 96f;
                    s.Scale = new Vector2(scale, scale);
                    s.Offset = new Vector2(0, -32);
                    s.Position = pos + new Vector2(0, 0.3f * TilePx);
                    s.Visible = true;
                    if (!f.Lit) continue;
                    // 불빛: 밤일수록 · 땔감이 많을수록 크고 밝게
                    if (ng == _glowSprites.Count) { var g = new Sprite2D { Texture = Art.FireGlow, Material = _addMat }; _glowRoot.AddChild(g); _glowSprites.Add(g); }
                    var gl = _glowSprites[ng++];
                    float flick = 0.92f + 0.08f * Mathf.Sin(t * 7.3f + f.Tile) * Mathf.Sin(t * 3.1f + f.Tile * 0.7f);
                    float radius = (f.Kind == 2 ? 9f : 7.5f) * Math.Clamp(0.6f + f.HoursLeft / 8f, 0.6f, 1.2f);
                    gl.Scale = Vector2.One * radius * 2 * TilePx / 128f * flick;
                    gl.Position = pos;
                    gl.Modulate = new Color(1, 1, 1, (0.12f + 0.62f * sh.Night) * flick);
                    gl.Visible = true;
                }
            if (snap.GroundItems.TryGetValue(cell, out var items))
            {
                int lastTile = -1, k = 0;
                foreach (var (tile, item, _) in items)
                {
                    k = tile == lastTile ? k + 1 : 0; lastTile = tile;
                    if (k >= 3) continue;
                    if (ni == _itemSprites.Count) { var sp = new Sprite2D { Material = _premul }; _itemRoot.AddChild(sp); _itemSprites.Add(sp); }
                    var s = _itemSprites[ni++];
                    s.Texture = Art.ItemIcon(item);
                    s.Scale = Vector2.One * 0.62f * TilePx / 48f;
                    s.Position = TileWorld(sh, tile) + new Vector2((k - 1) * 0.28f * TilePx, 0.12f * TilePx);
                    s.Visible = true;
                }
            }
            ApplyDug(snap, sh);
            // (E5) 벽 그림: 벽 타일에서 서서 보는 쪽으로 조금 나와(바위 면에 붙은 것처럼)
            if (snap.Paintings.TryGetValue(cell, out var pics))
                foreach (var g in pics)
                {
                    if (np == _paintSprites.Count) { var sp = new Sprite2D { Material = _premul }; _markerRoot.AddChild(sp); _paintSprites.Add(sp); } // 나무에 가리지 않게 표시 층에
                    var s = _paintSprites[np++];
                    s.Texture = Art.Painting(g.Medium, g.Rule < 0 ? 0 : Art.PaintingGlyph(g.Rule));
                    s.Scale = Vector2.One * 1.3f * TilePx / 64f;
                    var wall = TileWorld(sh, g.Tile); var stand = TileWorld(sh, g.Stand);
                    s.Position = g.Tile == g.Stand ? wall - new Vector2(0, 0.35f * TilePx) : wall.Lerp(stand, 0.35f);
                    s.Modulate = new Color(1, 1, 1, g.YearsLeft <= 1 ? 0.55f : 1f); // 바래 가는 그림은 흐리게
                    s.Visible = true;
                }
        }
        UpdateFarm(snap);
        for (int k = np; k < _paintSprites.Count; k++) _paintSprites[k].Visible = false;
        for (int k = nf; k < _fireSprites.Count; k++) _fireSprites[k].Visible = false;
        for (int k = ng; k < _glowSprites.Count; k++) _glowSprites[k].Visible = false;
        for (int k = ni; k < _itemSprites.Count; k++) _itemSprites[k].Visible = false;
    }

    private void HideCraft()
    {
        foreach (var s in _fireSprites) s.Visible = false;
        foreach (var s in _glowSprites) s.Visible = false;
        foreach (var s in _itemSprites) s.Visible = false;
        foreach (var s in _paintSprites) s.Visible = false;
        HideFarm();
    }

    private static Vector2 TileWorld(Shown sh, int tile)
    {
        var (lx, ly) = sh.Place.ToLayout(tile % HexFrame.Size + 0.5, tile / HexFrame.Size + 0.5);
        return new Vector2((float)lx, (float)ly) * TilePx;
    }

    /// <summary>캐낸 바위: 그 타일만 자갈 바닥으로 바꿔 땅 자료를 다시 올린다(바뀐 지방만).</summary>
    private void ApplyDug(WorldSnapshot snap, Shown sh)
    {
        if (!snap.Dug.TryGetValue(sh.Map.Cell, out var dug)) return;
        if (_dugSeen.TryGetValue(sh.Map.Cell, out int seen) && seen == dug.Length) return;
        _dugSeen[sh.Map.Cell] = dug.Length;
        foreach (int t in dug) if ((uint)t < HexFrame.TileCount) sh.GroundBytes[t * 4] = (byte)Ground.Gravel;
        sh.Ground.SetShaderParameter("data_tex", ImageTexture.CreateFromImage(Image.CreateFromData(HexFrame.Size, HexFrame.Size, false, Image.Format.Rgba8, sh.GroundBytes)));
        sh.Dirty = true;
        Rebake(sh);
    }

    /// <summary>사람이 든 도구 · 그릇 · 불씨(오른손 자리에 작은 아이콘).</summary>
    private void DrawHeld(PersonNode node, PersonView v, bool lying)
    {
        var tool = node.Tool;
        int item = v.Tool >= 0 ? v.Tool : v.Vessel;
        if (v.Ember) item = Wtw.Sim.Data.CraftData.Default.ItemIndex("ember");
        bool show = item >= 0 && !v.Dead && !lying && v.Carrier < 0 && v.Stage >= Wtw.Sim.People.LifeStage.Child;
        tool.Visible = show;
        if (!show) return;
        tool.Texture = Art.ItemIcon(item);
        float sz = (v.Stage == Wtw.Sim.People.LifeStage.Child ? 0.4f : 0.5f) * TilePx / 48f;
        bool left = node.Facing == Art.Facing.Side && node.FlipLeft;
        float bob = v.Working ? Mathf.Sin((float)Time.GetTicksMsec() / 120f + v.Id) * 0.06f * TilePx : 0;
        tool.Scale = new Vector2(left ? -sz : sz, sz);
        tool.Position = new Vector2((left ? -0.3f : 0.3f) * TilePx, -0.62f * TilePx * (v.Stage == Wtw.Sim.People.LifeStage.Child ? 0.7f : 1f) + bob);
        tool.ZIndex = node.Facing == Art.Facing.Back ? -1 : 1;
    }
}
