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
    /// <summary>(E3b) 따라가던 사람이 가운데 지방 밖으로 넘어갔다(새 칸).</summary>
    public event Action<int>? FollowCrossed;

    private sealed class PersonNode
    {
        public required Node2D Root;
        public required Sprite2D Shadow, Body, Icon, Tool;
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
        _markerRoot = new Node2D { ZIndex = 4 };
        AddChild(_markerRoot);
        _overlay = new CanvasLayer { Layer = 1, Visible = false };
        AddChild(_overlay);
        _overlayRoot = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _overlayRoot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _overlay.AddChild(_overlayRoot);
    }

    // ── 숲에서도 보이게: 발밑 표시 · 앞을 가리는 나무 반투명 · 가까이 보면 이름표 · 말풍선 ──
    private Node2D _markerRoot = null!;
    private CanvasLayer _overlay = null!;
    private Control _overlayRoot = null!;
    private readonly List<Sprite2D> _markers = new();
    private readonly List<Label> _nameLabels = new();
    private readonly List<Label> _bubbles = new();
    private readonly HashSet<(ObjectSet Set, int Index)> _faded = new();
    private readonly HashSet<(ObjectSet Set, int Index)> _fadeNow = new();
    public const float NameZoom = 0.9f, BubbleZoom = 0.45f;

    private void UpdateVisibilityAids()
    {
        _overlay.Visible = IsOpen;
        var xf = GetViewport().GetCanvasTransform();
        int mk = 0, nl = 0, bb = 0; // (_fadeNow 는 UpdateAnimals 앞에서 비운다 — 짐승도 나무를 흐리게 한다)
        foreach (var n in _pool)
        {
            if (!n.Visible || n.Id < 0 || !_indexOfId.TryGetValue(n.Id, out int vi)) continue;
            var v = _snap!.People[vi];
            bool carried = v.Carrier >= 0 && !v.Dead;
            if (!v.Dead && !carried)
            {
                // 발밑 표시(나무 위에도 보이게 맨 위 층에)
                if (mk == _markers.Count) { var m = new Sprite2D { Texture = Art.FootMarker, Material = _premul }; _markerRoot.AddChild(m); _markers.Add(m); }
                var mark = _markers[mk++];
                mark.Visible = true;
                mark.Position = n.Feet;
                mark.Scale = Vector2.One * (TilePx / 64f) * (v.Stage == Wtw.Sim.People.LifeStage.Child ? 0.75f : 1f);
                mark.Modulate = v.Id == _selected ? new Color(1f, 0.85f, 0.3f, 0.95f) : v.Breaking ? new Color(0.6f, 0.65f, 1f, 0.85f) : new Color(1f, 1f, 1f, 0.7f);
                // 앞을 가리는 키 큰 것들을 반투명하게
                FadeAround(n.Feet, v.Stage == Wtw.Sim.People.LifeStage.Child ? 0.7f : 1f);
            }
            var screen = xf * n.Feet;
            // 이름표(가까이 볼 때)
            if (_zoom >= NameZoom && !carried)
            {
                if (nl == _nameLabels.Count) _nameLabels.Add(NewOverlayLabel(false));
                var l = _nameLabels[nl++];
                l.Text = (v.Dead ? v.Name + " †" : v.Name) + (v.Nickname.Length > 0 ? $" '{v.Nickname}'" : "");
                l.Modulate = new Color(1, 1, 1, v.Dead ? 0.55f : 0.92f);
                l.ResetSize();
                l.Position = screen + new Vector2(-l.Size.X / 2, 6);
                l.Visible = true;
            }
            // 말풍선: 그 말의 낱말(마우스를 올리면 뜻)
            if (_zoom >= BubbleZoom && v.Say.Length > 0)
            {
                if (bb == _bubbles.Count) _bubbles.Add(NewOverlayLabel(true));
                var b = _bubbles[bb++];
                b.Text = v.Say;
                b.TooltipText = $"뜻: {v.SayMeaning}  ({v.Name}의 말)";
                b.ResetSize();
                b.Position = screen + new Vector2(0.15f * TilePx * _zoom, -Art.PersonTiles * TilePx * _zoom - b.Size.Y - 2);
                b.Visible = true;
                n.Icon.Visible = n.Icon.Visible && n.Icon.Texture != Art.TalkBubble;
            }
        }
        for (int k = mk; k < _markers.Count; k++) _markers[k].Visible = false;
        for (int k = nl; k < _nameLabels.Count; k++) _nameLabels[k].Visible = false;
        for (int k = bb; k < _bubbles.Count; k++) _bubbles[k].Visible = false;
        // 반투명 바꾸기: 새로 가리는 것만 흐리게, 더는 안 가리는 것은 되돌린다
        foreach (var f in _faded) if (!_fadeNow.Contains(f)) { var t = f.Set.Tall[f.Index]; t.Mm.SetInstanceColor(t.Index, t.Base); }
        foreach (var f in _fadeNow) if (!_faded.Contains(f)) { var t = f.Set.Tall[f.Index]; var c = t.Base; c.A *= 0.32f; t.Mm.SetInstanceColor(t.Index, c); }
        _faded.Clear();
        foreach (var f in _fadeNow) _faded.Add(f);
    }

    /// <summary>사람(발 = feet) 앞에 서서 몸을 가리는 키 큰 오브젝트를 찾는다.</summary>
    private void FadeAround(Vector2 feet, float size)
    {
        float bodyH = Art.PersonTiles * TilePx * size;
        foreach (var sh in _shown)
        {
            var set = sh.Objects;
            int cx0 = Mathf.FloorToInt((feet.X - 2 * TilePx) / (2 * TilePx)), cx1 = Mathf.FloorToInt((feet.X + 2 * TilePx) / (2 * TilePx));
            int cy0 = Mathf.FloorToInt(feet.Y / (2 * TilePx)), cy1 = Mathf.FloorToInt((feet.Y + 4 * TilePx) / (2 * TilePx));
            for (int cy = cy0; cy <= cy1; cy++)
                for (int cx = cx0; cx <= cx1; cx++)
                {
                    if (!set.Hash.TryGetValue(ObjectSet.Key(cx, cy), out var list)) continue;
                    foreach (int idx in list)
                    {
                        var t = set.Tall[idx];
                        float dy = t.Feet.Y - feet.Y;
                        if (dy <= 0 || dy > t.Height) continue;                 // 사람보다 뒤(위)거나 너무 앞
                        if (Math.Abs(t.Feet.X - feet.X) > t.HalfW + 0.3f * TilePx) continue;
                        if (t.Height - dy < 0.15f * bodyH) continue;            // 그림 꼭대기가 사람 발 근처까지 안 옴
                        _fadeNow.Add((set, idx));
                    }
                }
        }
    }

    // ── (E3b) 경계를 넘는 사람 표시: 밝은 범위 밖으로 나가면 가장자리에 "○○가 국경을 넘어감", 밖에서 들어오면 다른 테두리로 ──
    private long _crossSeenTick = long.MinValue, _crossSeenEpoch = -1;
    private int _crossSeenCount;
    private readonly List<(Label L, Vector2 World, double Born)> _crossLabels = new();

    private void ClearCrossLabels()
    {
        foreach (var (l, _, _) in _crossLabels) l.QueueFree();
        _crossLabels.Clear();
    }

    private void UpdateCrossLabels(WorldSnapshot snap)
    {
        double now = Time.GetTicksMsec() / 1000.0;
        if (snap.Epoch != _crossSeenEpoch) { _crossSeenEpoch = snap.Epoch; _crossSeenTick = snap.RecentCrossings.Length > 0 ? snap.RecentCrossings[^1].Tick : long.MinValue; _crossSeenCount = 0; }
        int sameTick = 0;
        foreach (var cr in snap.RecentCrossings)
        {
            if (cr.Tick < _crossSeenTick) continue;
            if (cr.Tick == _crossSeenTick) { if (++sameTick <= _crossSeenCount) continue; }
            Shown? from = null, to = null;
            foreach (var s0 in _shown) { if (s0.Map.Cell == cr.From) from = s0; if (s0.Map.Cell == cr.To) to = s0; }
            if (from is null == to is null) continue; // 둘 다 보이면(이웃까지) 사람이 그대로 보이고, 둘 다 안 보이면 상관없음
            string name = _indexOfId.TryGetValue(cr.Person, out int vi) ? snap.People[vi].Name : "누군가";
            bool leaving = from is not null;
            var sh = leaving ? from! : to!;
            int tile = leaving ? cr.FromTile : cr.ToTile;
            var (lx, ly) = sh.Place.ToLayout(tile % HexFrame.Size + 0.5, tile / HexFrame.Size + 0.5);
            string where = LandName?.Invoke(leaving ? cr.To : cr.From) is { } ln ? ln.Split('(')[0] : "이웃 땅";
            var l = NewOverlayLabel(true);
            l.MouseFilter = Control.MouseFilterEnum.Ignore;
            l.Text = leaving ? $"{Hangul.I(name)} 국경을 넘어감 → {where}" : $"{Hangul.I(name)} 들어옴 ← {where}";
            if (!leaving) { var box = UiTheme.Box(new Color(0.85f, 0.93f, 1f, 0.95f), 8, 7, 2); box.BorderColor = new Color(0.2f, 0.45f, 0.85f); box.SetBorderWidthAll(2); l.AddThemeStyleboxOverride("normal", box); }
            _crossLabels.Add((l, new Vector2((float)lx, (float)ly) * TilePx, now));
        }
        if (snap.RecentCrossings.Length > 0)
        {
            long last = snap.RecentCrossings[^1].Tick;
            int cnt = 0; foreach (var cr in snap.RecentCrossings) if (cr.Tick == last) cnt++;
            if (last != _crossSeenTick) { _crossSeenTick = last; _crossSeenCount = cnt; } else _crossSeenCount = cnt;
        }
        var xf = GetViewport().GetCanvasTransform();
        for (int i = _crossLabels.Count - 1; i >= 0; i--)
        {
            var (l, wpos, born) = _crossLabels[i];
            double age = now - born;
            if (age > 5 || !IsOpen) { l.QueueFree(); _crossLabels.RemoveAt(i); continue; }
            l.ResetSize();
            l.Position = xf * wpos + new Vector2(-l.Size.X / 2, -l.Size.Y - 18 - (float)age * 6);
            l.Modulate = new Color(1, 1, 1, (float)Math.Min(1, (5 - age) / 1.5));
        }
    }

    private Label NewOverlayLabel(bool bubble)
    {
        var l = new Label { MouseFilter = bubble ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore };
        l.AddThemeFontOverride("font", bubble ? UiTheme.Bold : UiTheme.Regular);
        l.AddThemeFontSizeOverride("font_size", bubble ? UiTheme.FontSizeSmall + 1 : UiTheme.FontSizeSmall - 1);
        if (bubble)
        {
            l.AddThemeColorOverride("font_color", new Color(0.12f, 0.1f, 0.08f));
            var box = UiTheme.Box(new Color(1f, 0.98f, 0.9f, 0.95f), 8, 7, 2);
            box.BorderColor = new Color(0.15f, 0.12f, 0.1f); box.SetBorderWidthAll(1);
            l.AddThemeStyleboxOverride("normal", box);
        }
        else
        {
            l.AddThemeColorOverride("font_color", Colors.White);
            l.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
            l.AddThemeConstantOverride("outline_size", 5);
        }
        _overlayRoot.AddChild(l);
        return l;
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
        var tool = new Sprite2D { Material = _premul, Visible = false };
        root.AddChild(shadow); root.AddChild(body); root.AddChild(tool); root.AddChild(icon);
        _peopleRoot.AddChild(root);
        var n = new PersonNode { Root = root, Shadow = shadow, Body = body, Icon = icon, Tool = tool };
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
        if (!IsOpen || _shown.Count == 0)
        {
            foreach (var n in _pool) n.Root.Visible = false;
            _selRing.Visible = false;
            foreach (var m in _markers) m.Visible = false;
            _overlay.Visible = false;
            HideCraft();
            HideAnimals();
            return;
        }
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
                if (v.Dead && v.Buried) continue; // (E6) 묻힌 사람은 무덤으로 그린다
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
        if (_follow >= 0 && _byId.TryGetValue(_follow, out var fo) && fo.Visible && !_dragging)
        {
            var goal = fo.Feet - new Vector2(0, 0.8f * TilePx);
            _cam.Position = _snapToFollow ? goal : _cam.Position.Lerp(goal, 0.25f);
            _snapToFollow = false;
        }
        // (E3b) 따라가던 사람이 경계를 넘으면 밝은 범위도 그 지방으로
        if (_follow >= 0 && _loading is null && _indexOfId.TryGetValue(_follow, out int fi) && snap.People[fi].Cell != _center && !snap.People[fi].Dead)
            FollowCrossed?.Invoke(snap.People[fi].Cell);
        UpdateCrossLabels(snap);
        UpdateCraft(snap);
        _fadeNow.Clear();
        UpdateAnimals(snap, tick);
        UpdateVisibilityAids();
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
        // (E6) 밭일 · 짓기 중에는 그 칸 아래쪽 끝에 서서 그린다 — 사람 그림이 밭 칸 · 짓는 자리를 가리지 않게(E5c 에서 넘어옴)
        if (!v.Walking && !v.Dead && v.Act is Act.Farm or Act.Build) extraY = 0.42f * TilePx;
        var (lx, ly) = sh.Place.ToLayout(mx / 1000.0, my / 1000.0);
        var feet = new Vector2((float)lx, (float)ly) * TilePx + new Vector2(extraX, extraY);
        node.Feet = feet;
        node.Visible = true;
        node.Root.Position = feet;

        // 방향: 지방을 돌린 만큼 걷는 방향도 돌려 화면 방향으로
        if (dx != 0 || dy != 0)
        {
            var dir = new Vector2((float)dx, (float)dy).Rotated(sh.Node.Rotation - _cam.Rotation);
            if (Math.Abs(dir.X) > Math.Abs(dir.Y) * 0.8f) { node.Facing = Art.Facing.Side; node.FlipLeft = dir.X < 0; }
            else node.Facing = dir.Y > 0 ? Art.Facing.Front : Art.Facing.Back;
        }
        bool moving = v.Walking && (dx != 0 || dy != 0) && !v.Dead;
        bool lying = v.Dead || (v.Asleep && v.Carrier < 0);
        bool sitting = !moving && !lying && v.Act is Act.Rest or Act.Warm or Act.Follow;
        var tex = Art.Person(v.Look, v.Female, v.Stage, lying ? Art.Facing.Front : node.Facing, Art.ClothOf(v.Worn, v.WornFeet, v.Injured && !v.Dead, v.Dead ? 0 : v.Thin));
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
        DrawHeld(node, v, lying);
        // 머리 위 표시
        var icon = node.Icon;
        Texture2D? it = null;
        if (!v.Dead)
        {
            if (v.Breaking) it = Art.BreakCloud;
            else if (lying) it = Art.Zzz;
            else if (v.Talking && ((int)(t * 1.2f + v.Id) % 3) != 0) it = Art.TalkBubble;
            else if (v.Teaching) it = ((int)(t * 1.2f + v.Id) % 3) != 0 ? Art.TeachIcon : null;
            else if (v.Learning) it = Art.LearnIcon;
            else if (v.Studying) it = Art.LookIcon;
            else if (v.Painting) it = Art.DrawIcon;
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
            Rebake(sh);
        }
    }
}
