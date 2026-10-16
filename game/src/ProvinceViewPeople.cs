using Godot;
using Wtw.Sim.People;
using Wtw.Sim.Provinces;
using Wtw.Sim.Runtime;

namespace Wtw.Game;

/// <summary>
/// 지방 화면의 사람들(기획서 6-1 · 17절): 시뮬이 정한 일정(구간)을 화면 시계로 매 프레임 보간해 그린다 → 뚝뚝 끊기지 않는다.
/// 걷기 흔들림 · 방향(정면 · 옆 · 뒤) · 앉기 · 눕기(잘 때) · 말풍선 · 먹구름(정신 붕괴)은 위치 · 기울기 · 크기만 바꿔서.
/// 마우스를 올리면 PersonHover, 누르면 PersonClicked. [따라가기]면 카메라가 그 사람을 따라간다. 화면은 스냅샷을 읽기만 한다.
/// </summary>
public partial class ProvinceView
{
    public event Action<int>? PersonHover;     // 사람 번호(−1 = 없음)
    public event Action<int>? PersonClicked;
    public event Action<int, int, int, bool>? SpawnClicked; // 칸, x, y, 가족인가

    private sealed class PersonNode
    {
        public required Node2D Root;
        public required Sprite2D Shadow, Body, Icon;
        public int Id = -1;
        public Art.Facing Facing = Art.Facing.Front;
        public bool FlipLeft;
        public Vector2 Feet;          // 발 위치(세상 좌표)
        public bool Visible;
    }

    private Node2D _peopleRoot = null!;
    private readonly List<PersonNode> _pool = new();
    private readonly Dictionary<int, PersonNode> _byId = new();
    private readonly Dictionary<int, int> _indexOfId = new();
    private Line2D _selRing = null!;
    private int _selected = -1, _hoverPerson = -1, _follow = -1;
    private CanvasItemMaterial _premul = null!;
    private WorldSnapshot? _snap;
    private double _peopleTick;
    private int _spawnTool; // 0 없음 · 1 한 명 · 2 가족
    private readonly Dictionary<int, int> _revealedSeen = new();

    public int SelectedPerson => _selected;
    public int FollowPerson => _follow;
    public int SpawnTool => _spawnTool;

    private void ReadyPeople()
    {
        _premul = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.PremultAlpha };
        _peopleRoot = new Node2D { ZIndex = 3, YSortEnabled = true };
        AddChild(_peopleRoot);
        _selRing = new Line2D { Width = 2.5f, DefaultColor = new Color(1, 0.85f, 0.3f, 0.95f), Closed = true, Visible = false, ZIndex = 2 };
        var pts = new Vector2[24];
        for (int k = 0; k < 24; k++) pts[k] = new Vector2(Mathf.Cos(k * Mathf.Tau / 24) * 0.55f, Mathf.Sin(k * Mathf.Tau / 24) * 0.22f) * TilePx;
        _selRing.Points = pts;
        AddChild(_selRing);
    }

    public void SetSpawnTool(int tool) { _spawnTool = tool; if (tool != 0) SetHoverPerson(-1); }

    public void Select(int id) { _selected = id; }
    public void Follow(int id) { _follow = id; }

    private PersonNode NewNode()
    {
        var root = new Node2D();
        var shadow = new Sprite2D { Texture = Art.PersonShadow, Material = _premul };
        var body = new Sprite2D { Material = _premul, Centered = false };
        var icon = new Sprite2D { Material = _premul, Visible = false };
        root.AddChild(shadow); root.AddChild(body); root.AddChild(icon);
        _peopleRoot.AddChild(root);
        var n = new PersonNode { Root = root, Shadow = shadow, Body = body, Icon = icon };
        _pool.Add(n);
        return n;
    }

    /// <summary>매 프레임: 스냅샷의 사람들을 화면 시계(tick)에서 그린다.</summary>
    /// <summary>측정: 사람 그리기 준비에 쓴 시간(밀리초, 합 · 프레임 수).</summary>
    public static double PeopleMsTotal; public static long PeopleFrames;

    public void SetPeople(WorldSnapshot snap, double tick)
    {
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        try { SetPeopleInner(snap, tick); }
        finally { PeopleMsTotal += (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency; PeopleFrames++; }
    }

    private void SetPeopleInner(WorldSnapshot snap, double tick)
    {
        _snap = snap; _peopleTick = tick;
        if (!IsOpen || _shown.Count == 0) { foreach (var n in _pool) n.Root.Visible = false; _selRing.Visible = false; return; }
        ApplyRevealed(snap);
        _indexOfId.Clear();
        for (int i = 0; i < snap.PeopleCount; i++) _indexOfId[snap.People[i].Id] = i;
        foreach (var n in _pool) n.Visible = false;
        int used = 0;
        // 먼저 혼자 다니는 사람, 그다음 안긴 아기(안은 사람의 자리가 필요)
        for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < snap.PeopleCount; i++)
            {
                var v = snap.People[i];
                bool carried = v.Carrier >= 0 && !v.Dead;
                if ((pass == 0) == carried) continue;
                Shown? sh = null;
                foreach (var s0 in _shown) if (s0.Map.Cell == v.Cell) { sh = s0; break; }
                if (sh is null) continue;
                if (!_byId.TryGetValue(v.Id, out var node))
                {
                    node = null;
                    foreach (var q in _pool) if (q.Id < 0 || !_indexOfId.ContainsKey(q.Id)) { node = q; break; }
                    node ??= NewNode();
                    if (node.Id >= 0) _byId.Remove(node.Id);
                    node.Id = v.Id; _byId[v.Id] = node;
                }
                used++;
                DrawPerson(node, v, sh, tick);
            }
        foreach (var n in _pool) n.Root.Visible = n.Visible;
        // 고른 사람 고리 · 따라가기
        if (_selected >= 0 && _byId.TryGetValue(_selected, out var sel) && sel.Visible)
        {
            _selRing.Visible = true;
            _selRing.Position = sel.Feet;
            _selRing.Width = Math.Max(2f, 2.5f / _zoom);
        }
        else _selRing.Visible = false;
        if (_follow >= 0 && _byId.TryGetValue(_follow, out var fo) && fo.Visible && !_dragging) _cam.Position = _cam.Position.Lerp(fo.Feet - new Vector2(0, 0.8f * TilePx), 0.25f);
        _ = used;
    }

    private void DrawPerson(PersonNode node, PersonView v, Shown sh, double tick)
    {
        double mx, my, dx, dy;
        float extraX = 0, extraY = 0;
        if (v.Carrier >= 0 && !v.Dead && _indexOfId.TryGetValue(v.Carrier, out int ci) && _byId.TryGetValue(v.Carrier, out var carrierNode) && carrierNode.Visible)
        {
            snapPos(_snap!.People[ci], tick, out mx, out my, out dx, out dy);
            extraX = 0.28f * TilePx; extraY = -0.05f * TilePx;
        }
        else snapPos(v, tick, out mx, out my, out dx, out dy);
        var (lx, ly) = sh.Place.ToLayout(mx / 1000.0, my / 1000.0);
        var feet = new Vector2((float)lx, (float)ly) * TilePx + new Vector2(extraX, extraY);
        node.Feet = feet;
        node.Visible = true;
        node.Root.Position = feet;

        // 방향: 지방을 돌린 만큼 걷는 방향도 돌려 화면 방향으로
        if (dx != 0 || dy != 0)
        {
            var dir = new Vector2((float)dx, (float)dy).Rotated(sh.Node.Rotation);
            if (Math.Abs(dir.X) > Math.Abs(dir.Y) * 0.8f) { node.Facing = Art.Facing.Side; node.FlipLeft = dir.X < 0; }
            else node.Facing = dir.Y > 0 ? Art.Facing.Front : Art.Facing.Back;
        }
        bool moving = v.Walking && (dx != 0 || dy != 0) && !v.Dead;
        bool lying = v.Dead || (v.Asleep && v.Carrier < 0);
        bool sitting = !moving && !lying && v.Act is Act.Rest or Act.Warm or Act.Follow;
        var tex = Art.Person(v.Look, v.Female, v.Stage, lying ? Art.Facing.Front : node.Facing);
        float scale = Art.PersonTiles * TilePx / Art.PersonH;
        var body = node.Body;
        body.Texture = tex;
        body.FlipH = node.Facing == Art.Facing.Side && node.FlipLeft && !lying;
        // 발이 원점에 오게: 그림의 아래 가운데
        float t = (float)(Time.GetTicksMsec() / 1000.0);
        float bob = 0, tilt = 0;
        if (moving)
        {
            float phase = (float)((mx + my) / 1000.0 * Math.PI * 1.6);
            bob = Math.Abs(Mathf.Sin(phase)) * 0.06f * TilePx;
            tilt = Mathf.Sin(phase) * 0.06f;
        }
        else if (!lying) bob = Mathf.Sin(t * 1.7f + v.Id) * 0.012f * TilePx; // 숨쉬기
        float sy = sitting ? 0.86f : 1f;
        body.Scale = new Vector2(scale, scale * sy);
        if (lying)
        {
            body.Rotation = -Mathf.Pi / 2;
            body.Position = new Vector2(-Art.PersonH * scale * 0.55f, Art.PersonW * scale * 0.5f - 0.05f * TilePx);
        }
        else
        {
            body.Rotation = tilt;
            body.Position = new Vector2(-Art.PersonW * scale / 2, -Art.PersonH * scale * sy - bob);
        }
        bool bones = v.Dead && v.Eaten;
        var mod = v.Dead ? new Color(0.62f, 0.6f, 0.58f, 0.95f) : Colors.White;
        if (v.Id == _hoverPerson && !v.Dead) mod = new Color(1.15f, 1.12f, 1.05f);
        body.Modulate = mod;
        if (bones) { body.Texture = Art.Bones; body.Rotation = 0; body.Scale = Vector2.One * (0.9f * TilePx / 64f); body.Position = new Vector2(-0.45f * TilePx, -0.25f * TilePx); }
        node.Shadow.Scale = Vector2.One * (TilePx / 64f) * (v.Stage switch { LifeStage.Baby => 0.4f, LifeStage.Child => 0.65f, _ => 0.9f }) * (lying ? 1.4f : 1f);
        node.Shadow.Position = new Vector2(lying ? -0.1f * TilePx : 0, 0);
        node.Shadow.Visible = v.Carrier < 0 || v.Dead;
        // 머리 위 표시
        var icon = node.Icon;
        Texture2D? it = null;
        if (!v.Dead)
        {
            if (v.Breaking) it = Art.BreakCloud;
            else if (lying) it = Art.Zzz;
            else if (v.Talking && ((int)(t * 1.2f + v.Id) % 3) != 0) it = Art.TalkBubble;
        }
        icon.Visible = it is not null && v.Carrier < 0;
        if (icon.Visible)
        {
            icon.Texture = it;
            float s2 = 0.55f * TilePx / 48f;
            icon.Scale = Vector2.One * s2;
            icon.Position = lying ? new Vector2(0.3f * TilePx, -0.55f * TilePx - Mathf.Sin(t * 1.5f + v.Id) * 3) : new Vector2(0.38f * TilePx, -Art.PersonTiles * TilePx * (v.Stage == LifeStage.Child ? 0.7f : 1f) - 0.1f * TilePx);
            icon.Modulate = new Color(1, 1, 1, lying ? 0.55f + 0.35f * Mathf.Sin(t * 2f + v.Id) : 1f);
        }
    }

    private static void snapPos(PersonView v, double tick, out double x, out double y, out double dx, out double dy) => v.PositionAt(tick, out x, out y, out dx, out dy);

    /// <summary>화면 위치의 사람(−1 = 없음): 몸 한가운데에서 가까운 사람.</summary>
    public int PickPerson(Vector2 screen)
    {
        if (_snap is null) return -1;
        var w = ScreenToWorld(screen);
        int best = -1; float bd = float.MaxValue;
        foreach (var n in _pool)
        {
            if (!n.Visible || n.Id < 0) continue;
            var c = n.Feet - new Vector2(0, 0.7f * TilePx);
            var d = w - c;
            float dd = (d.X * d.X) / (0.5f * 0.5f) + (d.Y * d.Y) / (0.95f * 0.95f);
            if (dd < TilePx * TilePx && dd < bd) { bd = dd; best = n.Id; }
        }
        return best;
    }

    public void SetHoverPerson(int id)
    {
        if (id == _hoverPerson) return;
        _hoverPerson = id;
        PersonHover?.Invoke(id);
    }

    /// <summary>자동 확인용: 그 사람의 화면 위치(발).</summary>
    public Vector2? PersonScreen(int id) => _byId.TryGetValue(id, out var n) && n.Visible ? GetViewport().GetCanvasTransform() * (n.Feet - new Vector2(0, 0.7f * TilePx)) : null;
    public Vector2? PersonWorld(int id) => _byId.TryGetValue(id, out var n) && n.Visible ? n.Feet : null;

    /// <summary>사람이 동굴에 들어가 걷힌 곳을 지도에 반영한다(숨김이 걷히면 검은 동굴이 어두운 바닥으로).</summary>
    private void ApplyRevealed(WorldSnapshot snap)
    {
        foreach (var sh in _shown)
        {
            if (!snap.Revealed.TryGetValue(sh.Map.Cell, out var tiles)) continue;
            if (_revealedSeen.TryGetValue(sh.Map.Cell, out int seen) && seen == tiles.Length) continue;
            _revealedSeen[sh.Map.Cell] = tiles.Length;
            sh.RevealedTiles = tiles;
            sh.Ground.SetShaderParameter("data2_tex", MakeData2(sh.Map, tiles));
        }
    }
}
