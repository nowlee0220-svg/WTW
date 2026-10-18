using Godot;
using Wtw.Sim.Provinces;

namespace Wtw.Game;

/// <summary>
/// (E4) 땅을 한 번만 그려 두기(그래픽 카드 사용 줄이기). 땅 셰이더는 화면 점 하나마다 그림을 60번쯤 읽는다(둘레 9타일 섞기 · 무늬 · 높이 16곳) —
/// 거의 바뀌지 않는 땅을 매 화면 처음부터 그릴 까닭이 없다.
///   - 굽기: 지방마다 그 땅 셰이더로 화면 밖 그림판(SubViewport)에 한 번 그려 텍스처로 남긴다. 멀리 볼 때는 한 타일 8화소(지방 전체 한 장),
///     가까이 볼 때(타일이 화면에서 12화소보다 크면) 보이는 곳만 25×25 타일 조각을 한 타일 32화소로 따로 굽는다(최근 ChunkCache 조각만 기억).
///   - 덧칠: 구운 그림에 물결 · 분화구 빛(움직이는 것) · 낮밤 밝기만 가볍게(그림 읽기 2~4번).
///   - 땅이 바뀌면(캔 바위 · 걷힌 동굴 · 자원 보기) 그 지방만 다시 굽는다.
///   - 굽기는 한 프레임에 하나씩(그림판을 그리고 다음 프레임에 읽음). 다 구워지기 전에는 옛 방식(셰이더가 바로 그림)으로 보인다.
/// 무늬 좌표는 화면 세상 좌표라서, 같은 자리 · 같은 돌림으로 구우면 이어 붙인 지방끼리 무늬가 그대로 이어진다.
/// </summary>
public partial class ProvinceView
{
    /// <summary>땅을 구워 두나(끄면 옛 방식 — 측정 비교용, 명령줄 --livebake=0).</summary>
    public static bool BakeGround = true;
    public const int BakeK = 8, ChunkTiles = 25, ChunkK = 32, ChunkCache = 36;
    public const float ChunkScreenPx = 12f;
    public static long BakesDone;

    private sealed class Chunk
    {
        public required int Cx, Cy;
        public required MeshInstance2D Node;
        public required ShaderMaterial Mat;
        public long LastUsed;
    }

    private sealed class BakeJob
    {
        public required Shown Sh;
        public int Cx = -1, Cy = -1; // −1 = 지방 전체
        public SubViewport? Vp;
        public int Frames;
    }

    private readonly List<BakeJob> _bakeQueue = new();
    private BakeJob? _baking;
    private Shader _lightShader = null!;
    private ArrayMesh _quadMesh = null!;
    private long _frameNo;

    /// <summary>굽는 중이거나 굽기가 남았나(덜 그리기를 잠깐 미룬다).</summary>
    public bool Baking => _baking is not null || _bakeQueue.Count > 0;

    private void ReadyBake()
    {
        _lightShader = new Shader { Code = LightShaderCode };
        _quadMesh = MakeQuadMesh();
    }

    /// <summary>지방 하나의 구운 땅 노드를 만들고 굽기를 맡긴다(Build 에서).</summary>
    private void SetupBake(Shown sh)
    {
        var mat = new ShaderMaterial { Shader = _lightShader };
        mat.SetShaderParameter("data_tex", sh.Ground.GetShaderParameter("data_tex"));
        mat.SetShaderParameter("noise_tex", Art.GroundNoise);
        mat.SetShaderParameter("uv_offset", new Vector2(0, 0));
        mat.SetShaderParameter("uv_scale", new Vector2(1, 1));
        mat.SetShaderParameter("bake_full", 1f);
        mat.SetShaderParameter("tile_px", TilePx);
        sh.BakedMat = mat;
        sh.BakedNode = new MeshInstance2D { Mesh = _hexMesh, Material = mat, TextureFilter = TextureFilterEnum.LinearWithMipmaps, Visible = false };
        sh.Node.AddChild(sh.BakedNode);
        sh.ChunkRoot = new Node2D();
        sh.Node.AddChild(sh.ChunkRoot);
        if (BakeGround) Queue(sh, -1, -1);
    }

    private void Queue(Shown sh, int cx, int cy)
    {
        foreach (var j in _bakeQueue) if (j.Sh == sh && j.Cx == cx && j.Cy == cy) return;
        if (_baking is { } b && b.Sh == sh && b.Cx == cx && b.Cy == cy) return;
        var job = new BakeJob { Sh = sh, Cx = cx, Cy = cy };
        if (cx < 0) _bakeQueue.Insert(0, job); else _bakeQueue.Add(job);
    }

    /// <summary>땅이 바뀌었다(캔 바위 · 걷힌 동굴 · 자원 보기): 지방 전체를 다시 굽고 조각은 버린다.</summary>
    private void Rebake(Shown sh)
    {
        sh.BakedMat?.SetShaderParameter("data_tex", sh.Ground.GetShaderParameter("data_tex"));
        foreach (var c in sh.Chunks.Values) { c.Node.QueueFree(); }
        sh.Chunks.Clear();
        if (BakeGround) Queue(sh, -1, -1);
    }

    /// <summary>매 프레임: 굽기 하나를 진행하고, 가까이 볼 때 필요한 조각을 맡기고 보인다.</summary>
    private void ProcessBake()
    {
        _frameNo++;
        foreach (var sh in _shown)
        {
            bool baked = BakeGround && sh.BakedNode is { } bn && sh.BakedTex is not null;
            if (sh.BakedNode is not null) sh.BakedNode.Visible = baked;
            sh.LiveNode!.Visible = !baked;
            if (sh.ChunkRoot is not null) sh.ChunkRoot.Visible = baked;
        }
        if (_baking is { } job)
        {
            if (++job.Frames >= 2) FinishJob(job);
        }
        else if (_bakeQueue.Count > 0)
        {
            var j = _bakeQueue[0];
            _bakeQueue.RemoveAt(0);
            if (_shown.Contains(j.Sh)) StartJob(j);
        }
        if (BakeGround && _zoom * TilePx > ChunkScreenPx) UpdateChunks();
        else foreach (var sh in _shown) if (sh.ChunkRoot is not null) foreach (var c in sh.Chunks.Values) c.Node.Visible = false;
    }

    private void StartJob(BakeJob j)
    {
        var sh = j.Sh;
        bool full = j.Cx < 0;
        int k = full ? BakeK : ChunkK;
        int tiles = full ? HexFrame.Size + 1 : ChunkTiles;
        var vp = new SubViewport { Size = new Vector2I(tiles * k, tiles * k), TransparentBg = true, Disable3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Once, CanvasItemDefaultTextureFilter = Viewport.DefaultCanvasItemTextureFilter.Linear };
        AddChild(vp);
        // 그림판 안에 땅 메시를 화면과 같은 자리 · 같은 돌림으로 두고, 카메라를 그 지방의 돌림에 맞춰 돌려 지방 지도 좌표 그대로 찍는다
        var holder = new Node2D { Position = sh.Node.Position, Rotation = sh.Node.Rotation };
        vp.AddChild(holder);
        var bm = (ShaderMaterial)sh.Ground.Duplicate();
        bm.SetShaderParameter("daylight", Vector3.One); // 낮밤 밝기는 덧칠에서
        holder.AddChild(new MeshInstance2D { Mesh = _hexMesh, Material = bm, TextureFilter = TextureFilterEnum.Linear });
        double cx = full ? HexFrame.Center : j.Cx * ChunkTiles + ChunkTiles / 2.0, cy = full ? HexFrame.Center : j.Cy * ChunkTiles + ChunkTiles / 2.0;
        var local = new Vector2((float)(cx - HexFrame.Center), (float)(cy - HexFrame.Center)) * TilePx;
        var cam = new Camera2D { Enabled = true, IgnoreRotation = false, Zoom = new Vector2(k / TilePx, k / TilePx), Position = sh.Node.Transform * local, Rotation = sh.Node.Rotation };
        vp.AddChild(cam);
        j.Vp = vp;
        _baking = j;
    }

    private void FinishJob(BakeJob j)
    {
        _baking = null;
        var vp = j.Vp!;
        if (_shown.Contains(j.Sh))
        {
            var img = vp.GetTexture().GetImage();
            img.GenerateMipmaps();
            var tex = ImageTexture.CreateFromImage(img);
            BakesDone++;
            if (j.Cx < 0)
            {
                j.Sh.BakedTex = tex;
                j.Sh.BakedMat!.SetShaderParameter("baked", tex);
            }
            else AddChunk(j.Sh, j.Cx, j.Cy, tex);
        }
        RemoveChild(vp);
        vp.QueueFree();
    }

    private void AddChunk(Shown sh, int cx, int cy, ImageTexture tex)
    {
        var mat = new ShaderMaterial { Shader = _lightShader };
        mat.SetShaderParameter("baked", tex);
        mat.SetShaderParameter("data_tex", sh.Ground.GetShaderParameter("data_tex"));
        mat.SetShaderParameter("noise_tex", Art.GroundNoise);
        mat.SetShaderParameter("uv_offset", new Vector2(cx * ChunkTiles / (float)HexFrame.Size, cy * ChunkTiles / (float)HexFrame.Size));
        mat.SetShaderParameter("uv_scale", new Vector2(ChunkTiles / (float)HexFrame.Size, ChunkTiles / (float)HexFrame.Size));
        mat.SetShaderParameter("bake_full", 0f);
        mat.SetShaderParameter("tile_px", TilePx);
        mat.SetShaderParameter("daylight", sh.Ground.GetShaderParameter("daylight"));
        var node = new MeshInstance2D
        {
            Mesh = _quadMesh, Material = mat, TextureFilter = TextureFilterEnum.LinearWithMipmaps,
            Position = new Vector2((float)(cx * ChunkTiles - HexFrame.Center), (float)(cy * ChunkTiles - HexFrame.Center)) * TilePx,
            Scale = Vector2.One * ChunkTiles * TilePx,
        };
        sh.ChunkRoot!.AddChild(node);
        sh.Chunks[(cx, cy)] = new Chunk { Cx = cx, Cy = cy, Node = node, Mat = mat, LastUsed = _frameNo };
        // 너무 많으면 오래 안 쓴 것부터 버린다
        int total = _shown.Sum(s => s.Chunks.Count);
        while (total > ChunkCache)
        {
            Shown? os = null; (int, int) ok = default; long oldest = long.MaxValue;
            foreach (var s in _shown) foreach (var (key, c) in s.Chunks) if (c.LastUsed < oldest) { oldest = c.LastUsed; os = s; ok = key; }
            if (os is null) break;
            os.Chunks[ok].Node.QueueFree();
            os.Chunks.Remove(ok);
            total--;
        }
    }

    /// <summary>가까이 볼 때: 화면에 보이는 조각을 보이고, 없으면 굽기를 맡긴다.</summary>
    private void UpdateChunks()
    {
        var inv = GetViewport().GetCanvasTransform().AffineInverse();
        var vs = GetViewportRect().Size;
        var corners = new[] { inv * Vector2.Zero, inv * new Vector2(vs.X, 0), inv * vs, inv * new Vector2(0, vs.Y) };
        foreach (var sh in _shown)
        {
            if (sh.BakedTex is null) continue;
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            foreach (var c in corners)
            {
                var (tx, ty) = sh.Place.ToLocal(c.X / TilePx, c.Y / TilePx);
                x0 = Math.Min(x0, tx); y0 = Math.Min(y0, ty); x1 = Math.Max(x1, tx); y1 = Math.Max(y1, ty);
            }
            int n = (HexFrame.Size + ChunkTiles - 1) / ChunkTiles;
            int cx0 = Math.Max(0, (int)Math.Floor(x0 / ChunkTiles)), cy0 = Math.Max(0, (int)Math.Floor(y0 / ChunkTiles));
            int cx1 = Math.Min(n - 1, (int)Math.Floor(x1 / ChunkTiles)), cy1 = Math.Min(n - 1, (int)Math.Floor(y1 / ChunkTiles));
            foreach (var c in sh.Chunks.Values) c.Node.Visible = false;
            if (cx0 > cx1 || cy0 > cy1) continue;
            if ((cx1 - cx0 + 1) * (cy1 - cy0 + 1) > 12) continue; // 너무 넓게 보이면 조각 없이
            for (int cy = cy0; cy <= cy1; cy++)
                for (int cx = cx0; cx <= cx1; cx++)
                {
                    if (sh.Chunks.TryGetValue((cx, cy), out var ch)) { ch.Node.Visible = true; ch.LastUsed = _frameNo; }
                    else Queue(sh, cx, cy);
                }
        }
    }

    private void SetBakedDaylight(Shown sh, Vector3 v)
    {
        sh.BakedMat?.SetShaderParameter("daylight", v);
        foreach (var c in sh.Chunks.Values) c.Mat.SetShaderParameter("daylight", v);
    }

    private void FreeBake(Shown sh)
    {
        _bakeQueue.RemoveAll(j => j.Sh == sh);
        sh.Chunks.Clear();
    }

    private static ArrayMesh MakeQuadMesh()
    {
        var verts = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
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

    /// <summary>덧칠 셰이더: 구운 땅 + 물결 · 분화구 빛 · 낮밤 밝기. 그림 읽기는 구운 그림 1 · 땅 종류 1 · (물 · 분화구일 때만) 무늬 2.</summary>
    private const string LightShaderCode = @"
shader_type canvas_item;
uniform sampler2D baked : filter_linear_mipmap;
uniform sampler2D data_tex : filter_nearest;
uniform sampler2D noise_tex : filter_linear_mipmap, repeat_enable;
uniform vec3 daylight = vec3(1.0);
uniform vec2 uv_offset = vec2(0.0);
uniform vec2 uv_scale = vec2(1.0);
uniform float bake_full = 1.0;
uniform float tile_px = 32.0;
const float N = 150.0;
varying vec2 world;
void vertex() { world = (MODEL_MATRIX * vec4(VERTEX, 0.0, 1.0)).xy / tile_px; }
void fragment() {
    vec2 puv = uv_offset + UV * uv_scale;                       // 지방 지도 좌표(0~1)
    vec2 buv = bake_full > 0.5 ? (puv * N + 0.5) / (N + 1.0) : UV; // 지방 전체 그림은 반 타일 더 넓게 구웠다
    vec4 c = texture(baked, buv);
    float g = floor(texture(data_tex, puv).r * 255.0 + 0.5);
    vec3 col = c.rgb;
    vec2 wp = world + vec2(500.0);
    if (g == 9.0 || g == 10.0) {                                 // 물결: 움직이는 것만 덧칠
        float a = texture(noise_tex, wp * 0.09 + vec2(TIME * 0.006, TIME * 0.004)).g;
        float b = texture(noise_tex, wp * 0.21 - vec2(TIME * 0.009, -TIME * 0.005)).b;
        col *= 0.96 + 0.08 * (a + b - 1.0);
        col += vec3(0.9, 0.95, 1.0) * smoothstep(0.82, 0.95, a * b * 1.6) * 0.12;
    } else if (g == 14.0) {                                      // 분화구: 달아오름
        float glow = smoothstep(0.35, 0.75, texture(noise_tex, wp * 0.25 + vec2(TIME * 0.01, 0.0)).g);
        col += vec3(0.5, 0.15, 0.03) * glow * 0.4;
    }
    COLOR = vec4(col * daylight, c.a);
}
";
}
