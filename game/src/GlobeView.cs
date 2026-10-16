using Godot;
using Wtw.Sim.Planet;

namespace Wtw.Game;

/// <summary>
/// 3D 지구본. 행성 전체를 메시 하나로 그린다(칸마다 노드를 만들지 않는다).
/// 칸 색은 작은 텍스처(칸 하나 = 화소 하나)에 담고, 지도 모드를 바꾸면 그 텍스처만 다시 칠한다.
/// 조작: 왼쪽 끌기 = 돌리기(관성), 휠 = 확대/축소(부드럽게), 누르기 = 칸 고르기. 화살표 키로도 돌린다.
/// 좌표: 행성(x, y, z; +z = 북극) → Godot(x, z, −y).
/// </summary>
public partial class GlobeView : Node3D
{
    public const float MinDistance = 1.22f, MaxDistance = 4.6f;
    private const int TexWidth = 256;
    private const float HeightScale = 0.028f; // 높이 6,500m = 반지름의 2.8%

    public event Action<int>? CellSelected; // −1 = 선택 해제

    private Planet? _planet;
    private MeshInstance3D _globe = null!, _rivers = null!, _halo = null!;
    private ShaderMaterial _mat = null!;
    private Image _img = null!;
    private ImageTexture _tex = null!;
    private byte[] _pixels = Array.Empty<byte>();
    private int _texHeight;
    private Camera3D _cam = null!;

    private MapMode _mode = MapMode.Terrain;
    private int _resource = ResourceKinds.Copper;

    // 카메라 (행성 기준 경도·위도 방향에서 바라봄)
    private float _yaw, _pitch = 0.35f, _dist = 3.7f, _targetDist = 3.7f;
    private float _yawVel, _pitchVel;
    private bool _dragging;
    private Vector2 _pressPos;
    private float _dragMoved;
    private int _hover = -1, _selected = -1, _walkStart;
    private Vector2 _lastMouse;
    private bool _mouseMoved;

    public MapMode Mode => _mode;
    public int Resource => _resource;
    public int Selected => _selected;
    public float Distance => _dist;
    public Planet? Planet => _planet;
    public bool AutoSpin { get; set; }

    public override void _Ready()
    {
        _cam = new Camera3D { Fov = 38, Near = 0.01f, Far = 50 };
        AddChild(_cam);
        var sun = new DirectionalLight3D { LightEnergy = 1.15f, LightColor = new Color(1, 0.97f, 0.92f) };
        _cam.AddChild(sun);
        sun.RotationDegrees = new Vector3(-28, -42, 0); // 왼쪽 위에서 비추는 해 (카메라와 함께 돈다)

        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = Art.Space,
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.55f, 0.6f, 0.7f),
            AmbientLightEnergy = 0.55f,
        };
        AddChild(new WorldEnvironment { Environment = env });

        _mat = new ShaderMaterial { Shader = new Shader { Code = GlobeShader } };
        _globe = new MeshInstance3D { MaterialOverride = _mat };
        AddChild(_globe);
        _rivers = new MeshInstance3D
        {
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = Art.River,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            },
        };
        AddChild(_rivers);
        _halo = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 1.045f, Height = 2.09f, RadialSegments = 96, Rings = 48 },
            MaterialOverride = new ShaderMaterial { Shader = new Shader { Code = HaloShader } },
        };
        AddChild(_halo);
        UpdateCamera();
    }

    // ───────── 행성 → 메시 ─────────

    public void SetPlanet(Planet p)
    {
        if (ReferenceEquals(p, _planet)) return;
        _planet = p;
        _hover = _selected = -1;
        _walkStart = 0;
        BuildMesh(p);
        BuildRivers(p);
        Recolor();
        _mat.SetShaderParameter("selected", -1f);
        _mat.SetShaderParameter("hovered", -1f);
        CellSelected?.Invoke(-1);
    }

    private static Vector3 ToGodot(double x, double y, double z) => new((float)x, (float)z, (float)-y);

    private static float CellRadius(Planet p, int c)
    {
        if (p.IsOcean(c)) return 1f;
        return 1f + HeightScale * Mathf.Clamp(p.Elevation[c], 0, 6500) / 6500f;
    }

    private void BuildMesh(Planet p)
    {
        var g = p.Grid;
        int n = p.CellCount;
        var cellR = new float[n];
        for (int c = 0; c < n; c++) cellR[c] = CellRadius(p, c);
        // 꼭짓점 높이 = 만나는 세 칸의 평균 → 이음매 없이 이어진다
        var cornerPos = new Vector3[g.TriangleCount];
        for (int t = 0; t < g.TriangleCount; t++)
        {
            float r = (cellR[g.TriA[t]] + cellR[g.TriB[t]] + cellR[g.TriC[t]]) / 3f;
            cornerPos[t] = ToGodot(p.TX[t], p.TY[t], p.TZ[t]) * r;
        }
        var centerPos = new Vector3[n];
        for (int c = 0; c < n; c++) centerPos[c] = ToGodot(p.X[c], p.Y[c], p.Z[c]) * cellR[c];

        // 법선: 점마다 둘레 삼각형 법선의 합
        var cornerN = new Vector3[g.TriangleCount];
        var centerN = new Vector3[n];
        for (int c = 0; c < n; c++)
        {
            var corners = g.Corners(c);
            int k = corners.Length;
            for (int i = 0; i < k; i++)
            {
                int a = corners[i], b = corners[(i + 1) % k];
                var fn = (cornerPos[a] - centerPos[c]).Cross(cornerPos[b] - centerPos[c]);
                if (fn.Dot(centerPos[c]) < 0) fn = -fn;
                centerN[c] += fn; cornerN[a] += fn; cornerN[b] += fn;
            }
        }

        int vcount = n + g.Nbr.Length;
        var verts = new Vector3[vcount];
        var normals = new Vector3[vcount];
        var uv = new Vector2[vcount];
        var uv2 = new Vector2[vcount];
        var idx = new int[g.Nbr.Length * 3];
        _texHeight = (n + TexWidth - 1) / TexWidth;
        int vi = 0, ii = 0;
        for (int c = 0; c < n; c++)
        {
            var cellUv = new Vector2((c % TexWidth + 0.5f) / TexWidth, (c / TexWidth + 0.5f) / _texHeight);
            int center = vi;
            verts[vi] = centerPos[c]; normals[vi] = centerN[c].Normalized(); uv[vi] = cellUv; uv2[vi] = new Vector2(c, 0); vi++;
            var corners = g.Corners(c);
            int k = corners.Length;
            for (int i = 0; i < k; i++)
            {
                int t = corners[i];
                verts[vi] = cornerPos[t]; normals[vi] = cornerN[t].Normalized(); uv[vi] = cellUv; uv2[vi] = new Vector2(c, 1); vi++;
            }
            for (int i = 0; i < k; i++)
            {
                // 꼭짓점은 바깥에서 볼 때 시계 방향 = Godot 의 앞면
                idx[ii++] = center; idx[ii++] = center + 1 + i; idx[ii++] = center + 1 + (i + 1) % k;
            }
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uv;
        arrays[(int)Mesh.ArrayType.TexUV2] = uv2;
        arrays[(int)Mesh.ArrayType.Index] = idx;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        _globe.Mesh = mesh;

        _pixels = new byte[TexWidth * _texHeight * 4];
        _img = Image.CreateFromData(TexWidth, _texHeight, false, Image.Format.Rgba8, _pixels);
        _tex = ImageTexture.CreateFromImage(_img);
        _mat.SetShaderParameter("cell_tex", _tex);
    }

    private void BuildRivers(Planet p)
    {
        var verts = new List<Vector3>();
        var idx = new List<int>();
        for (int c = 0; c < p.CellCount; c++)
        {
            int size = p.RiverSize[c];
            if (size == 0) continue;
            int d = p.Downstream[c];
            if (d < 0) continue;
            var a = ToGodot(p.X[c], p.Y[c], p.Z[c]);
            var b = ToGodot(p.X[d], p.Y[d], p.Z[d]);
            float ra = CellRadius(p, c), rb = p.IsLand(d) ? CellRadius(p, d) : 1f;
            if (!p.IsLand(d)) { b = (a + b).Normalized(); rb = (ra + 1f) / 2f; } // 바다·호수에서는 가장자리까지만
            float w = size switch { 1 => 0.0011f, 2 => 0.0019f, _ => 0.0030f };
            var pa = a * (ra + 0.0022f);
            var pb = b * (rb + 0.0022f);
            var side = (pb - pa).Cross(a).Normalized() * w;
            var ext = (pb - pa).Normalized() * w * 0.8f; // 이음매가 벌어지지 않게 조금 늘인다
            int o = verts.Count;
            verts.Add(pa - side - ext); verts.Add(pa + side - ext); verts.Add(pb + side + ext); verts.Add(pb - side + ext);
            idx.Add(o); idx.Add(o + 1); idx.Add(o + 2); idx.Add(o); idx.Add(o + 2); idx.Add(o + 3);
        }
        if (verts.Count == 0) { _rivers.Mesh = null; return; }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = idx.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        _rivers.Mesh = mesh;
    }

    // ───────── 지도 모드 ─────────

    public void SetMode(MapMode mode, int? resource = null)
    {
        _mode = mode;
        if (resource is int r) _resource = r;
        Recolor();
    }

    private void Recolor()
    {
        var p = _planet;
        if (p is null) return;
        for (int c = 0; c < p.CellCount; c++)
        {
            var col = MapModes.CellColor(p, c, _mode, _resource);
            int o = c * 4;
            _pixels[o] = (byte)(col.R * 255 + 0.5f);
            _pixels[o + 1] = (byte)(col.G * 255 + 0.5f);
            _pixels[o + 2] = (byte)(col.B * 255 + 0.5f);
            _pixels[o + 3] = (byte)(col.A * 255 + 0.5f);
        }
        _img.SetData(TexWidth, _texHeight, false, Image.Format.Rgba8, _pixels);
        _tex.Update(_img);
        _rivers.Visible = _mode is MapMode.Terrain or MapMode.Biome or MapMode.Rain;
    }

    // ───────── 카메라 · 입력 ─────────

    /// <summary>칸 c 가 화면 가운데 오도록 돌리고 dist 까지 다가간다(자동 확인 · 나중의 "그곳으로 가기").</summary>
    public void LookAtCell(int c, float dist)
    {
        if (_planet is null) return;
        var v = ToGodot(_planet.X[c], _planet.Y[c], _planet.Z[c]);
        _pitch = Mathf.Asin(Mathf.Clamp(v.Y, -1, 1));
        _yaw = Mathf.Atan2(v.X, v.Z);
        _yawVel = _pitchVel = 0;
        _dist = _targetDist = Mathf.Clamp(dist, MinDistance, MaxDistance);
        UpdateCamera();
    }

    public void Select(int c)
    {
        _selected = c;
        _mat.SetShaderParameter("selected", (float)c);
        CellSelected?.Invoke(c);
    }

    private void UpdateCamera()
    {
        var dir = new Vector3(Mathf.Cos(_pitch) * Mathf.Sin(_yaw), Mathf.Sin(_pitch), Mathf.Cos(_pitch) * Mathf.Cos(_yaw));
        _cam.Position = dir * _dist;
        _cam.LookAt(Vector3.Zero, Vector3.Up);
        // 가까이 갈수록 칸 경계선이 보인다
        _mat.SetShaderParameter("border", Mathf.Clamp((2.25f - _dist) / 0.8f, 0f, 1f));
    }

    public override void _Process(double delta)
    {
        float dt = (float)Math.Min(delta, 0.1);
        float zoomSpeed = (_dist - 1f);
        if (!_dragging)
        {
            if (AutoSpin) _yawVel = 0.25f;
            _yaw += _yawVel * dt;
            _pitch += _pitchVel * dt;
            float decay = Mathf.Exp(-3.5f * dt);
            if (!AutoSpin) _yawVel *= decay;
            _pitchVel *= decay;
        }
        // 화살표 키
        float key = 1.2f * zoomSpeed * dt;
        if (Input.IsKeyPressed(Key.Left)) _yaw -= key;
        if (Input.IsKeyPressed(Key.Right)) _yaw += key;
        if (Input.IsKeyPressed(Key.Up)) _pitch += key;
        if (Input.IsKeyPressed(Key.Down)) _pitch -= key;
        _pitch = Mathf.Clamp(_pitch, -1.45f, 1.45f);
        _dist = Mathf.Lerp(_dist, _targetDist, 1f - Mathf.Exp(-9f * dt));
        UpdateCamera();

        if (_mouseMoved && !_dragging && _planet is not null)
        {
            _mouseMoved = false;
            int h = Pick(_lastMouse);
            if (h != _hover) { _hover = h; _mat.SetShaderParameter("hovered", (float)h); }
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (_planet is null) return;
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } mb:
                if (mb.Pressed) { _dragging = true; _pressPos = mb.Position; _dragMoved = 0; _yawVel = _pitchVel = 0; }
                else if (_dragging)
                {
                    _dragging = false;
                    if (_dragMoved < 6)
                    {
                        int c = Pick(mb.Position);
                        Select(c == _selected ? -1 : c);
                        if (c < 0) Select(-1);
                    }
                }
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                _targetDist = Mathf.Clamp(1f + (_targetDist - 1f) * 0.86f, MinDistance, MaxDistance);
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                _targetDist = Mathf.Clamp(1f + (_targetDist - 1f) / 0.86f, MinDistance, MaxDistance);
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseMotion mm:
                _lastMouse = mm.Position;
                _mouseMoved = true;
                if (_dragging)
                {
                    _dragMoved += mm.Relative.Length();
                    float h = Mathf.Max(GetViewport().GetVisibleRect().Size.Y, 1);
                    float k = 2.0f * (_dist - 1f) / h; // 가까이 갈수록 천천히
                    float dyaw = -mm.Relative.X * k, dpitch = mm.Relative.Y * k;
                    _yaw += dyaw; _pitch += dpitch;
                    // 관성: 최근 움직임의 빠르기를 기억(초당)
                    float dt = Mathf.Max((float)GetProcessDeltaTime(), 1f / 240f);
                    _yawVel = Mathf.Lerp(_yawVel, dyaw / dt, 0.5f);
                    _pitchVel = Mathf.Lerp(_pitchVel, dpitch / dt, 0.5f);
                }
                break;
        }
    }

    /// <summary>화면 위치의 칸 (지구본 밖이면 −1). 구와 광선의 교점에서 가장 가까운 칸까지 이웃을 따라 걷는다.</summary>
    public int Pick(Vector2 screen)
    {
        var p = _planet;
        if (p is null) return -1;
        var o = _cam.ProjectRayOrigin(screen);
        var d = _cam.ProjectRayNormal(screen);
        float b = o.Dot(d), cc = o.Dot(o) - 1.0f;
        float disc = b * b - cc;
        if (disc < 0) return -1;
        var hit = o + d * (-b - Mathf.Sqrt(disc));
        // Godot → 행성 좌표
        double x = hit.X, y = -hit.Z, z = hit.Y;
        int cur = _walkStart;
        while (true)
        {
            double best = x * p.X[cur] + y * p.Y[cur] + z * p.Z[cur];
            int next = -1;
            foreach (int nb in p.Grid.Neighbors(cur))
            {
                double v = x * p.X[nb] + y * p.Y[nb] + z * p.Z[nb];
                if (v > best) { best = v; next = nb; }
            }
            if (next < 0) break;
            cur = next;
        }
        _walkStart = cur;
        return cur;
    }

    private const string GlobeShader = @"
shader_type spatial;
render_mode blend_mix, cull_back, diffuse_lambert, specular_schlick_ggx;
uniform sampler2D cell_tex : source_color, filter_nearest;
uniform float border = 0.0;
uniform float selected = -1.0;
uniform float hovered = -1.0;
varying float v_cell;
varying float v_edge;
void vertex() { v_cell = UV2.x; v_edge = UV2.y; }
void fragment() {
    vec4 c = texture(cell_tex, UV);
    vec3 col = c.rgb;
    float w = max(fwidth(v_edge), 1e-4);
    float line = smoothstep(1.0 - 1.8 * w, 1.0 - 0.3 * w, v_edge);
    col = mix(col, col * 0.5, line * border);
    if (abs(v_cell - hovered) < 0.5) {
        col = mix(col, vec3(1.0), 0.15 + 0.45 * line);
    }
    if (abs(v_cell - selected) < 0.5) {
        float l2 = smoothstep(1.0 - 4.5 * w, 1.0 - 1.5 * w, v_edge);
        col = mix(col, vec3(1.0, 0.85, 0.3), max(l2, 0.18));
    }
    ALBEDO = col;
    ROUGHNESS = c.a;
    SPECULAR = 0.1 + 0.25 * (1.0 - c.a);
}
";

    private const string HaloShader = @"
shader_type spatial;
render_mode blend_add, unshaded, cull_back, depth_draw_never;
void fragment() {
    float f = 1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0);
    float a = pow(f, 3.0) * 0.55;
    ALBEDO = vec3(0.45, 0.65, 1.0) * a;
}
";
}
