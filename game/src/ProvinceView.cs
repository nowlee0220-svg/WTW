using Godot;
using Wtw.Sim.Planet;
using Wtw.Sim.Provinces;

namespace Wtw.Game;

/// <summary>
/// 2D 지방 화면(기획서 5-3 · 5-4). 지구본에서 땅 칸으로 들어오면 그 지방의 150×150 지도를 그린다.
///   - 한 지방 = 땅 메시 하나(타일마다 노드를 만들지 않는다, 셰이더가 타일 자료 텍스처에서 색을 섞는다) + 오브젝트 MultiMesh 하나.
///   - [이 지방만] / [이웃까지]: 이웃까지면 육각형 지방들을 빈틈없이 이어 붙여(ProvinceLayout) 바다가 아닌 이웃까지 밝게.
///   - 밝은 범위 밖은 완전히 검게, 가장자리에 이웃 방향 · 이름만 흐리게.
/// 조작: 끌기 = 이동(놓기 직전에 움직이고 있었을 때만 관성), 휠 = 확대/축소(마우스 자리를 중심으로 부드럽게), 누르기 = 타일 정보.
/// 지도는 시뮬(ProvinceCache)이 만들고 화면은 읽기만 한다. 지도 만들기는 다른 스레드에서.
/// </summary>
public partial class ProvinceView : Node2D
{
    public const float TilePx = 32f;
    public const float MaxZoom = 2.3f; // 가장 가까이: 타일 하나가 약 74px (손가락만 하게)

    public event Action<int, int, int>? TileClicked;   // 칸, x, y
    public event Action<int, int, int>? HoverChanged;  // 칸, x, y (칸 −1 = 없음)
    public event Action<string>? Notice;

    private sealed class Shown
    {
        public required ProvinceLayout.Placed Place;
        public required ProvinceMap Map;
        public required Node2D Node;
    }

    private Planet? _planet;
    private int _center = -1;
    private bool _neighbors;
    private readonly List<Shown> _shown = new();
    private Task<List<(ProvinceLayout.Placed Place, ProvinceMap Map)>>? _loading;
    private bool _loadingKeepCamera;

    private Camera2D _cam = null!;
    private Node2D _world = null!;
    private CanvasLayer _back = null!, _labelsLayer = null!;
    private Control _labels = null!;
    private Line2D _hoverBox = null!, _centerOutline = null!;
    private Shader _groundShader = null!, _objectShader = null!;
    private ArrayMesh _hexMesh = null!, _spriteMesh = null!;

    // 카메라
    private float _zoom = 0.25f, _targetZoom = 0.25f, _minZoom = 0.1f;
    private Vector2 _zoomAnchorScreen;
    private bool _dragging;
    private Vector2 _vel;
    private readonly Queue<(double T, Vector2 D)> _recent = new();
    private const double InertiaWindow = 0.08;
    private float _dragMoved;
    private Rect2 _bounds;
    private (int Cell, int X, int Y) _hover = (-1, 0, 0);
    private bool _mouseMoved;

    public bool IsOpen => _center >= 0;
    public int Center => _center;
    public bool Neighbors => _neighbors;
    public bool Loading => _loading is not null;
    public float Zoom => _zoom;
    public int ShownCount => _shown.Count;

    public override void _Ready()
    {
        _back = new CanvasLayer { Layer = -1, Visible = false };
        AddChild(_back);
        var black = new ColorRect { Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Ignore };
        black.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _back.AddChild(black);

        _world = new Node2D();
        AddChild(_world);
        _cam = new Camera2D { Enabled = false };
        AddChild(_cam);

        _centerOutline = new Line2D { Width = 6, DefaultColor = new Color(1, 0.85f, 0.4f, 0.35f), Closed = true, Visible = false, ZIndex = 5 };
        AddChild(_centerOutline);
        _hoverBox = new Line2D { Width = 2.5f, DefaultColor = new Color(1, 1, 1, 0.7f), Closed = true, Visible = false, ZIndex = 6 };
        AddChild(_hoverBox);

        _labelsLayer = new CanvasLayer { Layer = 1, Visible = false };
        AddChild(_labelsLayer);
        _labels = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _labels.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _labelsLayer.AddChild(_labels);

        _groundShader = new Shader { Code = GroundShaderCode };
        _objectShader = new Shader { Code = ObjectShaderCode };
        _hexMesh = MakeHexMesh();
        _spriteMesh = MakeSpriteMesh();
        Visible = false;
    }

    // ───────── 열기 · 닫기 ─────────

    /// <summary>지방으로 들어간다. 지도 만들기는 다른 스레드에서(끝나면 그린다).</summary>
    public void Open(Planet p, int cell, bool neighbors)
    {
        Art.EnsureProvinceArt();
        _planet = p;
        _center = cell;
        _neighbors = neighbors;
        Visible = true;
        _back.Visible = true;
        _labelsLayer.Visible = true;
        _cam.Enabled = true;
        _cam.MakeCurrent();
        ClearShown();
        Load(keepCamera: false);
    }

    public void SetNeighbors(bool on)
    {
        if (_planet is null || _center < 0 || on == _neighbors) return;
        _neighbors = on;
        Load(keepCamera: true);
    }

    public void Close()
    {
        ClearShown();
        _center = -1;
        _planet = null;
        _loading = null;
        Visible = false;
        _back.Visible = false;
        _labelsLayer.Visible = false;
        _cam.Enabled = false;
        _hoverBox.Visible = false;
        _centerOutline.Visible = false;
        SetHover(-1, 0, 0);
    }

    private void Load(bool keepCamera)
    {
        var p = _planet!;
        int c = _center;
        bool nb = _neighbors;
        _loadingKeepCamera = keepCamera;
        _loading = Task.Run(() =>
        {
            var placed = ProvinceLayout.Around(p, c, n => nb && !p.IsOcean(n));
            var maps = new ProvinceMap[placed.Count];
            Parallel.For(0, placed.Count, k => maps[k] = ProvinceCache.Get(p, placed[k].Cell));
            return placed.Select((pl, k) => (pl, maps[k])).ToList();
        });
    }

    private void ClearShown()
    {
        foreach (var s in _shown) { _world.RemoveChild(s.Node); s.Node.QueueFree(); }
        _shown.Clear();
        foreach (var n in _labels.GetChildren()) { _labels.RemoveChild(n); n.QueueFree(); }
    }

    private void FinishLoading()
    {
        var task = _loading!;
        _loading = null;
        if (task.IsFaulted) { Notice?.Invoke("지방 지도를 만들지 못했습니다: " + task.Exception?.GetBaseException().Message); GD.PrintErr(task.Exception); return; }
        if (_center < 0) return;
        ClearShown();
        foreach (var (pl, map) in task.Result) _shown.Add(Build(pl, map));
        BuildLabels();
        // 밝은 범위
        float half = (float)(_neighbors && _shown.Count > 1 ? HexFrame.A * 3 : HexFrame.R) * TilePx;
        _bounds = new Rect2(-half, -half, half * 2, half * 2);
        var vp = GetViewportRect().Size;
        float usableH = vp.Y - UiTheme.TopBarHeight - UiTheme.BottomBarHeight - 60; // 위 · 아래 막대와 가장자리 이름 자리
        float fitH = usableH / (float)((_neighbors && _shown.Count > 1 ? HexFrame.A * 6 : HexFrame.A * 2) * TilePx * 1.08f);
        float fitW = vp.X / (float)((_neighbors && _shown.Count > 1 ? HexFrame.A * 6 : HexFrame.R * 2) * TilePx * 1.25f);
        _minZoom = Math.Min(fitH, fitW);
        if (!_loadingKeepCamera)
        {
            _zoom = _targetZoom = _minZoom * (_neighbors ? 3 : 1);
            _cam.Position = Vector2.Zero;
        }
        _targetZoom = Math.Max(_targetZoom, _minZoom);
        _zoom = Math.Max(_zoom, _minZoom);
        _centerOutline.Points = HexOutline(0);
        _centerOutline.Visible = _neighbors && _shown.Count > 1;
        ApplyCamera();
    }

    // ───────── 그리기 준비 ─────────

    private Shown Build(ProvinceLayout.Placed pl, ProvinceMap m)
    {
        var node = new Node2D
        {
            Position = new Vector2((float)pl.OffsetX, (float)pl.OffsetY) * TilePx,
            Rotation = pl.Steps * Mathf.Pi / 3,
        };
        _world.AddChild(node);

        // 타일 자료: R 땅 종류 · G 생물군계 · B 물 깊이 · A 육각형 안. 높이는 반정밀 실수(지방 평균을 뺀 값).
        int n = HexFrame.TileCount;
        var data = new byte[n * 4];
        double mean = 0; int cnt = 0;
        for (int i = 0; i < n; i++) if (HexFrame.Inside[i]) { mean += m.Height[i]; cnt++; }
        mean /= Math.Max(1, cnt);
        var hbytes = new byte[n * 2];
        for (int i = 0; i < n; i++)
        {
            data[i * 4] = (byte)m.Ground[i];
            data[i * 4 + 1] = (byte)m.Biome[i];
            data[i * 4 + 2] = m.WaterDepth[i];
            data[i * 4 + 3] = (byte)(HexFrame.Inside[i] ? 255 : 0);
            // 물은 물가 높이로 평평하게(그늘이 생기지 않게)
            double h = HexFrame.Inside[i] ? m.Height[i] - mean : 0;
            ushort bits = BitConverter.HalfToUInt16Bits((Half)(float)h);
            hbytes[i * 2] = (byte)bits; hbytes[i * 2 + 1] = (byte)(bits >> 8);
        }
        // 육각형 밖 타일의 높이는 가까운 안쪽 높이로 채운다(가장자리에 그늘 줄이 생기지 않게)
        FillOutsideHeights(m, mean, hbytes);
        var dataTex = ImageTexture.CreateFromImage(Image.CreateFromData(HexFrame.Size, HexFrame.Size, false, Image.Format.Rgba8, data));
        var heightTex = ImageTexture.CreateFromImage(Image.CreateFromData(HexFrame.Size, HexFrame.Size, false, Image.Format.Rh, hbytes));
        var mat = new ShaderMaterial { Shader = _groundShader };
        mat.SetShaderParameter("data_tex", dataTex);
        mat.SetShaderParameter("height_tex", heightTex);
        mat.SetShaderParameter("palette_tex", Art.GroundPalette);
        mat.SetShaderParameter("noise_tex", Art.GroundNoise);
        // 해는 화면의 왼쪽 위 → 지방을 돌린 만큼 거꾸로 돌려 지도 좌표로
        var light = new Vector2(-0.6f, -0.8f).Rotated(-node.Rotation);
        mat.SetShaderParameter("light_dir", light);
        mat.SetShaderParameter("tile_px", TilePx);
        node.AddChild(new MeshInstance2D { Mesh = _hexMesh, Material = mat, TextureFilter = TextureFilterEnum.Linear });

        node.AddChild(BuildObjects(pl, m, node.Rotation));
        return new Shown { Place = pl, Map = m, Node = node };
    }

    private static void FillOutsideHeights(ProvinceMap m, double mean, byte[] hbytes)
    {
        int S = HexFrame.Size;
        var h = new float[HexFrame.TileCount];
        var known = new bool[HexFrame.TileCount];
        for (int i = 0; i < h.Length; i++) if (HexFrame.Inside[i]) { h[i] = (float)(m.Height[i] - mean); known[i] = true; }
        for (int pass = 0; pass < 3; pass++)
        {
            var add = new List<(int, float)>();
            for (int i = 0; i < h.Length; i++)
            {
                if (known[i]) continue;
                int x = i % S, y = i / S; float sum = 0; int cnt = 0;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= S || ny >= S) continue;
                        int j = ny * S + nx;
                        if (known[j]) { sum += h[j]; cnt++; }
                    }
                if (cnt > 0) add.Add((i, sum / cnt));
            }
            foreach (var (i, v) in add) { h[i] = v; known[i] = true; }
        }
        for (int i = 0; i < h.Length; i++)
        {
            if (HexFrame.Inside[i] || !known[i]) continue;
            ushort bits = BitConverter.HalfToUInt16Bits((Half)h[i]);
            hbytes[i * 2] = (byte)bits; hbytes[i * 2 + 1] = (byte)(bits >> 8);
        }
    }

    private MultiMeshInstance2D BuildObjects(ProvinceLayout.Placed pl, ProvinceMap m, float rotation)
    {
        var sprites = Art.ObjectSprites;
        var list = new List<(float SortY, int Tile)>();
        for (int i = 0; i < HexFrame.TileCount; i++)
        {
            if (m.Object[i] == 0) continue;
            var (_, ly) = pl.ToLayout(i % HexFrame.Size + 0.5, i / HexFrame.Size + 0.5);
            list.Add(((float)ly, i));
        }
        list.Sort((a, b) => a.SortY != b.SortY ? a.SortY.CompareTo(b.SortY) : a.Tile.CompareTo(b.Tile)); // 아래쪽이 나중(앞)에
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,
            UseColors = true,
            UseCustomData = true,
            Mesh = _spriteMesh,
            InstanceCount = list.Count,
        };
        var unrotate = Transform2D.Identity.Rotated(-rotation);
        int atlasRows = Art.ObjectAtlas.GetHeight() / Art.AtlasCell;
        for (int k = 0; k < list.Count; k++)
        {
            int i = list[k].Tile;
            int o = m.Object[i] - 1;
            var sp = sprites[o];
            int v = m.Variant[i];
            int cell = sp.Cell + v % Art.Variants;
            float scale = sp.SizeTiles * TilePx * (0.86f + 0.28f * ((v >> 2) & 15) / 15f);
            bool flip = ((v >> 6) & 1) == 1;
            float jx = (((v * 37) & 15) / 15f - 0.5f) * 0.3f, jy = (((v * 91) & 15) / 15f - 0.5f) * 0.3f;
            var local = new Vector2((float)(i % HexFrame.Size + 0.5 + jx - HexFrame.Center), (float)(i / HexFrame.Size + 0.5 + jy - HexFrame.Center)) * TilePx;
            // 서 있는 그림: 발이 타일 가운데보다 조금 아래. 납작한 그림: 타일 가운데에 펼침(메시는 아래 가운데가 원점이라 반만큼 내림).
            var feet = sp.Upright ? new Vector2(0, 0.3f * TilePx) : new Vector2(0, 0.5f * scale);
            var basis = unrotate * Transform2D.Identity.Scaled(new Vector2(flip ? -scale : scale, scale));
            var t = new Transform2D(basis.X, basis.Y, local + unrotate.BasisXform(feet));
            mm.SetInstanceTransform2D(k, t);
            float tint = 0.9f + 0.2f * ((v >> 3) & 7) / 7f;
            mm.SetInstanceColor(k, new Color(tint, tint, tint * 0.97f, 1));
            mm.SetInstanceCustomData(k, new Color(cell % Art.AtlasCols, cell / Art.AtlasCols, sp.Sway, (i * 0.618f) % 6.283f));
        }
        var mat = new ShaderMaterial { Shader = _objectShader };
        mat.SetShaderParameter("atlas", Art.ObjectAtlas);
        mat.SetShaderParameter("grid", new Vector2(Art.AtlasCols, atlasRows));
        return new MultiMeshInstance2D { Multimesh = mm, Material = mat, ZIndex = 1 };
    }

    private static ArrayMesh MakeHexMesh()
    {
        // 육각형 = 가운데 + 꼭짓점 6개, UV 는 150×150 사각형 기준(0~1)
        var verts = new Vector2[7];
        var uvs = new Vector2[7];
        verts[0] = Vector2.Zero; uvs[0] = new Vector2(0.5f, 0.5f);
        for (int s = 0; s < 6; s++)
        {
            var hx = (float)HexFrame.HX[s]; var hy = (float)HexFrame.HY[s];
            verts[s + 1] = new Vector2(hx - (float)HexFrame.Center, hy - (float)HexFrame.Center) * TilePx;
            uvs[s + 1] = new Vector2(hx / HexFrame.Size, hy / HexFrame.Size);
        }
        var idx = new int[18];
        for (int s = 0; s < 6; s++) { idx[s * 3] = 0; idx[s * 3 + 1] = 1 + s; idx[s * 3 + 2] = 1 + (s + 1) % 6; }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Index] = idx;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    private static ArrayMesh MakeSpriteMesh()
    {
        // 너비 1 · 높이 1, 아래 가운데가 원점
        var verts = new[] { new Vector2(-0.5f, -1), new Vector2(0.5f, -1), new Vector2(0.5f, 0), new Vector2(-0.5f, 0) };
        var uvs = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Index] = new[] { 0, 1, 2, 0, 2, 3 };
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    private static Vector2[] HexOutline(int steps)
    {
        var pts = new Vector2[6];
        for (int s = 0; s < 6; s++) pts[s] = new Vector2((float)(HexFrame.HX[s] - HexFrame.Center), (float)(HexFrame.HY[s] - HexFrame.Center)) * TilePx;
        return pts;
    }

    // ───────── 가장자리 이름 ─────────

    private void BuildLabels()
    {
        var p = _planet!;
        var (_, _, slots, _) = ProvinceGenerator.Slots(p, _center);
        bool ring = _neighbors && _shown.Count > 1;
        for (int s = 0; s < 6; s++)
        {
            int nb = slots[s];
            string text = nb < 0 ? "막힌 쪽 — 절벽" : $"{TileDescriber.Direction(s)}: {TileDescriber.ProvinceName(p, nb)}";
            bool shownNb = ring && _shown.Any(x => x.Map.Cell == nb);
            // 이 지방만: 변 바로 바깥. 이웃까지: 보이는 이웃은 그 지방 바깥쪽 끝, 안 보이는 쪽(바다)은 변 바로 바깥
            double d = shownNb ? HexFrame.A * 3 + 6 : HexFrame.A + 8;
            if (ring && !shownNb) d = HexFrame.A * 1.55;
            var pos = new Vector2((float)(HexFrame.NX[s] * d), (float)(HexFrame.NY[s] * d)) * TilePx;
            var l = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore, HorizontalAlignment = HorizontalAlignment.Center };
            l.AddThemeColorOverride("font_color", new Color(0.8f, 0.78f, 0.7f, 0.55f));
            l.AddThemeFontSizeOverride("font_size", UiTheme.FontSizeSmall + 2);
            l.SetMeta("world", pos);
            _labels.AddChild(l);
        }
    }

    private void UpdateLabels()
    {
        var xf = GetViewport().GetCanvasTransform();
        foreach (var n in _labels.GetChildren())
        {
            if (n is not Label l) continue;
            var w = (Vector2)l.GetMeta("world");
            l.ResetSize();
            l.Position = xf * w - l.Size / 2;
        }
    }

    // ───────── 카메라 · 입력 ─────────

    private void ApplyCamera()
    {
        _cam.Zoom = new Vector2(_zoom, _zoom);
        var vp = GetViewportRect().Size / _zoom / 2;
        // 밝은 범위 밖으로 너무 나가지 않게(가운데가 범위 안)
        var pos = _cam.Position;
        pos.X = Math.Clamp(pos.X, _bounds.Position.X, _bounds.End.X);
        pos.Y = Math.Clamp(pos.Y, _bounds.Position.Y, _bounds.End.Y);
        _ = vp;
        _cam.Position = pos;
    }

    public override void _Process(double delta)
    {
        if (_loading is not null && _loading.IsCompleted) FinishLoading();
        if (!IsOpen) return;
        float dt = (float)Math.Min(delta, 0.1);
        if (!_dragging && _vel.LengthSquared() > 0.01f)
        {
            _cam.Position += _vel * dt;
            _vel *= Mathf.Exp(-4f * dt);
        }
        // 확대/축소: 마우스 자리의 땅이 제자리에 있도록
        if (Math.Abs(_zoom - _targetZoom) > 1e-5f)
        {
            var before = ScreenToWorld(_zoomAnchorScreen);
            _zoom = Mathf.Lerp(_zoom, _targetZoom, 1f - Mathf.Exp(-12f * dt));
            if (Math.Abs(_zoom - _targetZoom) < 1e-4f) _zoom = _targetZoom;
            _cam.Zoom = new Vector2(_zoom, _zoom);
            var after = ScreenToWorld(_zoomAnchorScreen);
            _cam.Position += before - after;
        }
        // 화살표 키
        var key = Vector2.Zero;
        if (Input.IsKeyPressed(Key.Left)) key.X -= 1;
        if (Input.IsKeyPressed(Key.Right)) key.X += 1;
        if (Input.IsKeyPressed(Key.Up)) key.Y -= 1;
        if (Input.IsKeyPressed(Key.Down)) key.Y += 1;
        if (key != Vector2.Zero) _cam.Position += key * 900 * dt / _zoom;
        ApplyCamera();
        _centerOutline.Width = 3.5f / _zoom;
        _hoverBox.Width = Math.Max(2f, 2.5f / _zoom);
        if (_mouseMoved && !_dragging) { _mouseMoved = false; UpdateHover(GetViewport().GetMousePosition()); }
        UpdateLabels();
    }

    private Vector2 ScreenToWorld(Vector2 screen) => GetViewport().GetCanvasTransform().AffineInverse() * screen;

    public override void _UnhandledInput(InputEvent e)
    {
        if (!IsOpen) return;
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } mb:
                if (mb.Pressed) { _dragging = true; _dragMoved = 0; _vel = Vector2.Zero; _recent.Clear(); }
                else if (_dragging)
                {
                    _dragging = false;
                    ReleaseInertia();
                    if (_dragMoved < 6)
                    {
                        var hit = Pick(mb.Position);
                        if (hit.Cell >= 0) TileClicked?.Invoke(hit.Cell, hit.X, hit.Y);
                    }
                }
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true } w:
                ZoomBy(1.18f, w.Position);
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true } w:
                ZoomBy(1 / 1.18f, w.Position);
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseMotion mm:
                _mouseMoved = true;
                if (_dragging)
                {
                    _dragMoved += mm.Relative.Length();
                    var d = -mm.Relative / _zoom;
                    _cam.Position += d;
                    double now = Time.GetTicksUsec() / 1e6;
                    _recent.Enqueue((now, d));
                    while (_recent.Count > 0 && now - _recent.Peek().T > InertiaWindow) _recent.Dequeue();
                    if (_hoverBox.Visible) SetHover(-1, 0, 0);
                }
                break;
        }
    }

    public void ZoomBy(float factor, Vector2 screenAnchor)
    {
        _zoomAnchorScreen = screenAnchor;
        _targetZoom = Math.Clamp(_targetZoom * factor, _minZoom, MaxZoom);
    }

    /// <summary>자동 확인용: 배치 좌표(타일)의 점을 화면 가운데에 두고 바로 그 확대로.</summary>
    public void LookAt(double layoutX, double layoutY, float zoom)
    {
        _cam.Position = new Vector2((float)layoutX, (float)layoutY) * TilePx;
        _zoom = _targetZoom = Math.Clamp(zoom, _minZoom, MaxZoom);
        _vel = Vector2.Zero;
        ApplyCamera();
    }

    private void ReleaseInertia()
    {
        double now = Time.GetTicksUsec() / 1e6;
        var sum = Vector2.Zero;
        foreach (var (t, d) in _recent) if (now - t <= InertiaWindow) sum += d;
        _recent.Clear();
        _vel = sum / (float)InertiaWindow;
    }

    /// <summary>화면 위치의 타일(칸 −1 = 없음).</summary>
    public (int Cell, int X, int Y) Pick(Vector2 screen)
    {
        var w = ScreenToWorld(screen) / TilePx;
        foreach (var s in _shown)
        {
            var (tx, ty) = s.Place.ToLocal(w.X, w.Y);
            int ix = (int)Math.Floor(tx), iy = (int)Math.Floor(ty);
            if (ix < 0 || iy < 0 || ix >= HexFrame.Size || iy >= HexFrame.Size) continue;
            if (!HexFrame.Inside[iy * HexFrame.Size + ix]) continue;
            return (s.Map.Cell, ix, iy);
        }
        return (-1, 0, 0);
    }

    public ProvinceMap? MapOf(int cell) => _shown.FirstOrDefault(s => s.Map.Cell == cell)?.Map;

    /// <summary>자동 확인용: 그 지방 타일 (x, y) 의 화면 위치.</summary>
    public Vector2? ScreenOf(int cell, int x, int y)
    {
        var s = _shown.FirstOrDefault(q => q.Map.Cell == cell);
        if (s is null) return null;
        var (lx, ly) = s.Place.ToLayout(x + 0.5, y + 0.5);
        return GetViewport().GetCanvasTransform() * (new Vector2((float)lx, (float)ly) * TilePx);
    }

    public (double X, double Y)? LayoutOf(int cell, int x, int y)
    {
        var s = _shown.FirstOrDefault(q => q.Map.Cell == cell);
        if (s is null) return null;
        return s.Place.ToLayout(x + 0.5, y + 0.5);
    }

    private void UpdateHover(Vector2 screen)
    {
        var h = Pick(screen);
        SetHover(h.Cell, h.X, h.Y);
    }

    public void SetHover(int cell, int x, int y)
    {
        if (cell == _hover.Cell && x == _hover.X && y == _hover.Y) return;
        _hover = (cell, x, y);
        var s = _shown.FirstOrDefault(q => q.Map.Cell == cell);
        if (s is null) _hoverBox.Visible = false;
        else
        {
            var pts = new Vector2[4];
            (double, double)[] corners = { (x, y), (x + 1, y), (x + 1, y + 1), (x, y + 1) };
            for (int k = 0; k < 4; k++)
            {
                var (lx, ly) = s.Place.ToLayout(corners[k].Item1, corners[k].Item2);
                pts[k] = new Vector2((float)lx, (float)ly) * TilePx;
            }
            _hoverBox.Points = pts;
            _hoverBox.Width = Math.Max(2f, 2.5f / _zoom);
            _hoverBox.Visible = true;
        }
        HoverChanged?.Invoke(cell, x, y);
    }

    // ───────── 셰이더 ─────────

    private const string GroundShaderCode = @"
shader_type canvas_item;
uniform sampler2D data_tex : filter_nearest;
uniform sampler2D height_tex : filter_nearest;
uniform sampler2D palette_tex : filter_nearest;
uniform sampler2D noise_tex : filter_linear_mipmap, repeat_enable;
uniform vec2 light_dir = vec2(-0.6, -0.8);
uniform float tile_px = 32.0;
const float N = 150.0;
varying vec2 world;   // 화면 세상 좌표(타일) — 무늬를 이 좌표로 그려 이어 붙인 지방끼리 무늬가 이어진다
void vertex() { world = (MODEL_MATRIX * vec4(VERTEX, 0.0, 1.0)).xy / tile_px; }

// 타일 하나의 색: 땅 종류 × 생물군계의 바탕색에, 화소마다 한 번만 뽑은 무늬(n)를 종류에 맞게 입힌다.
// n: x 큰 얼룩 · y 중간 얼룩 · z 타일 안 무늬 · w 알갱이,  m: x 풀결 · y 모래 물결 · z 바위 금 · w 습지 물,  wv: 물결
vec3 tile_color(vec4 d, vec4 n, vec4 m, vec2 wv) {
    float g = floor(d.r * 255.0 + 0.5);
    float b = floor(d.g * 255.0 + 0.5);
    vec3 c = texture(palette_tex, vec2((g + 0.5) / 16.0, (b + 0.5) / 16.0)).rgb;
    c *= (0.86 + 0.24 * n.x) * (0.93 + 0.14 * n.y);
    if (g == 2.0) c *= 0.86 + 0.22 * m.x + 0.08 * n.z;                       // 풀밭
    else if (g == 1.0 || g == 4.0) c *= 0.9 + 0.16 * n.w + 0.06 * n.z;     // 흙 · 진흙
    else if (g == 3.0) c *= 0.94 + 0.08 * n.w + 0.06 * m.y;                // 모래
    else if (g == 5.0 || g == 11.0) c *= (0.84 + 0.26 * n.z) * (1.0 - 0.35 * m.z); // 바위 · 절벽
    else if (g == 6.0) c = mix(c, vec3(0.82, 0.88, 0.98), 0.25 * (1.0 - n.y)) * (0.97 + 0.05 * n.z); // 눈
    else if (g == 7.0) c *= 0.95 + 0.1 * n.z;                              // 얼음
    else if (g == 8.0) c = mix(c * (0.9 + 0.2 * n.z), vec3(0.24, 0.36, 0.38), m.w * 0.6); // 습지
    else if (g == 9.0 || g == 10.0) {                                      // 물: 깊이에 따라
        float depth = d.b * 255.0;
        vec3 shallow = vec3(0.30, 0.55, 0.62), deep = vec3(0.09, 0.24, 0.42);
        c = mix(shallow, deep, clamp((depth - 1.0) / 14.0, 0.0, 1.0));
        if (g == 10.0) c = mix(c, deep * 0.85, 0.5);
        c *= 0.98 + 0.12 * (wv.x + wv.y - 1.0);
        c += vec3(0.9, 0.95, 1.0) * smoothstep(0.82, 0.95, wv.x * wv.y * 1.6) * 0.18;
    }
    return c;
}

float hash12(vec2 v) { return fract(sin(dot(v, vec2(12.9898, 78.233))) * 43758.5453); }

void fragment() {
    vec2 p = UV * N;                      // 타일 좌표(연속). 타일 i 의 가운데 = i + 0.5
    vec2 wp = world + vec2(500.0);        // 무늬용(화면 세상 좌표 — 이어 붙인 지방끼리 무늬가 이어진다)
    vec4 n = vec4(texture(noise_tex, wp * 0.012).g, texture(noise_tex, wp * 0.07).b, texture(noise_tex, wp * 0.55).a, texture(noise_tex, wp * 2.3).a);
    float crackN = texture(noise_tex, wp * 0.3).b;
    vec4 m = vec4(texture(noise_tex, vec2(wp.x * 3.1, wp.y * 1.3)).a,
                  texture(noise_tex, vec2(wp.x * 0.4 + wp.y * 0.9, wp.y * 0.25)).r,
                  smoothstep(0.47, 0.5, crackN) * (1.0 - smoothstep(0.5, 0.53, crackN)),
                  smoothstep(0.55, 0.7, texture(noise_tex, wp * 0.2).r));
    vec2 wv = vec2(texture(noise_tex, wp * 0.09 + vec2(TIME * 0.006, TIME * 0.004)).g,
                   texture(noise_tex, wp * 0.21 - vec2(TIME * 0.009, -TIME * 0.005)).b);

    // 둘레 3×3 타일을 매끈한 무게(2차 B-스플라인)로 섞는다. 무게를 타일마다 다른 잡음으로 흔들고 날카롭게 하면
    // 땅 종류의 경계가 격자 · 계단 대신 부드러운 곡선이 된다(빠지는 타일은 무게가 0 이 되는 자리에서 빠져서 끊김이 없다).
    vec2 c0 = floor(p);
    vec2 u = p - c0 - 0.5;                // −0.5 ~ 0.5
    vec3 bx = vec3(0.5 * (0.5 - u.x) * (0.5 - u.x), 0.75 - u.x * u.x, 0.5 * (0.5 + u.x) * (0.5 + u.x));
    vec3 by = vec3(0.5 * (0.5 - u.y) * (0.5 - u.y), 0.75 - u.y * u.y, 0.5 * (0.5 + u.y) * (0.5 + u.y));
    // 1) 타일마다 매끈한 무게 · 색 · 종류
    float tw[9]; float tg[9]; vec3 tc[9];
    float total = 0.0;
    for (int j = 0; j < 3; j++) {
        for (int i = 0; i < 3; i++) {
            int k = j * 3 + i;
            vec2 t = c0 + vec2(float(i - 1), float(j - 1));
            vec4 d = texture(data_tex, (t + 0.5) / N);
            float jit = 0.45 + 1.1 * texture(noise_tex, wp * 0.35 + vec2(hash12(t), hash12(t + 7.31))).r;
            tw[k] = d.a < 0.5 ? 0.0 : bx[i] * by[j] * jit;   // 육각형 밖은 섞지 않는다
            tg[k] = floor(d.r * 255.0 + 0.5);
            tc[k] = tw[k] > 0.0 ? tile_color(d, n, m, wv) : vec3(0.0);
            total += tw[k];
        }
    }
    // 2) 같은 종류끼리 무게를 모아(매끈한 면) 그 면을 가파르게 잘라 경계를 만든다 → 경계가 계단 대신 부드러운 곡선
    vec3 col = vec3(0.0);
    float wsum = 0.0, water = 0.0;
    for (int k = 0; k < 9; k++) {
        if (tw[k] <= 0.0) continue;
        float grp = 0.0;
        for (int q = 0; q < 9; q++) grp += tg[q] == tg[k] ? tw[q] : 0.0;
        float share = grp / max(total, 1e-6);
        float keep = smoothstep(0.30, 0.62, share) + 0.02;
        float w = tw[k] / grp * keep;
        col += w * tc[k];
        water += w * float(tg[k] >= 9.0 && tg[k] <= 10.0);
        wsum += w;
    }
    col /= max(wsum, 1e-6);
    water /= max(wsum, 1e-6);

    // 물가: 물과 땅이 섞이는 곳에 옅은 거품
    float foam = smoothstep(0.15, 0.5, water) * (1.0 - smoothstep(0.5, 0.9, water));
    col = mix(col, vec3(0.86, 0.9, 0.9), foam * 0.35 * (0.6 + 0.4 * texture(noise_tex, wp * 0.5 + TIME * 0.01).r));

    // 언덕 그늘: 높이(m)를 3차 B-스플라인(4×4 타일)으로 매끈하게 이은 면의 기울기 · 해의 방향.
    // (타일마다 각진 면이 보이지 않게) 산에서도 지나치지 않게 tanh 로 눌러 준다.
    vec2 q = p - 0.5;
    vec2 k0 = floor(q);
    vec2 s = q - k0;
    vec4 wx = vec4((1.0 - s.x) * (1.0 - s.x) * (1.0 - s.x), 3.0 * s.x * s.x * s.x - 6.0 * s.x * s.x + 4.0, -3.0 * s.x * s.x * s.x + 3.0 * s.x * s.x + 3.0 * s.x + 1.0, s.x * s.x * s.x) / 6.0;
    vec4 wy = vec4((1.0 - s.y) * (1.0 - s.y) * (1.0 - s.y), 3.0 * s.y * s.y * s.y - 6.0 * s.y * s.y + 4.0, -3.0 * s.y * s.y * s.y + 3.0 * s.y * s.y + 3.0 * s.y + 1.0, s.y * s.y * s.y) / 6.0;
    vec4 dx = vec4(-(1.0 - s.x) * (1.0 - s.x), 3.0 * s.x * s.x - 4.0 * s.x, -3.0 * s.x * s.x + 2.0 * s.x + 1.0, s.x * s.x) / 2.0;
    vec4 dy = vec4(-(1.0 - s.y) * (1.0 - s.y), 3.0 * s.y * s.y - 4.0 * s.y, -3.0 * s.y * s.y + 2.0 * s.y + 1.0, s.y * s.y) / 2.0;
    float hx = 0.0, hy = 0.0;
    for (int j = 0; j < 4; j++) {
        for (int i = 0; i < 4; i++) {
            float h = texture(height_tex, (k0 + vec2(float(i - 1), float(j - 1)) + 0.5) / N).r;
            hx += dx[i] * wy[j] * h;
            hy += wx[i] * dy[j] * h;
        }
    }
    float slope = dot(vec2(hx, hy), -light_dir);
    col *= mix(1.0, 1.0 + 0.38 * tanh(slope * 0.03), 1.0 - water);
    COLOR = vec4(col, 1.0);
}
";

    private const string ObjectShaderCode = @"
shader_type canvas_item;
render_mode blend_premul_alpha;
uniform sampler2D atlas : filter_linear_mipmap;
uniform vec2 grid = vec2(16.0, 8.0);
void vertex() {
    vec2 cell = INSTANCE_CUSTOM.xy;
    float sway = INSTANCE_CUSTOM.z;
    float top = 1.0 - UV.y;
    VERTEX.x += sway * top * top * sin(TIME * 1.3 + INSTANCE_CUSTOM.w) * 0.025; // VERTEX 는 그림 한 장 = 1 인 좌표
    UV = (cell + UV * 0.996 + 0.002) / grid;
}
void fragment() {
    vec4 c = texture(atlas, UV);
    COLOR = vec4(c.rgb * COLOR.rgb, c.a) * COLOR.a;
}
";
}
