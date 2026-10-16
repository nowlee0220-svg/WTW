using Godot;
using Wtw.Sim.Runtime;

namespace Wtw.Game;

/// <summary>
/// 시험 지도와 점들. 스냅샷의 일정(출발·도착·경로)으로 매 프레임 보간해서 그린다 — 시뮬과 무관하게 부드럽다.
/// </summary>
public partial class MapView : Node2D
{
    private const float DotPixels = 9f; // 1080 기준 점 지름
    private MultiMeshInstance2D _mmi = null!;
    private MultiMesh _mm = null!;
    private Rect2 _mapRect;
    private int _mapUnits = 150_000;

    public Rect2 MapRect => _mapRect;
    public int MapUnits => _mapUnits;

    public override void _Ready()
    {
        _mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,
            UseColors = true,
            Mesh = new QuadMesh { Size = new Vector2(DotPixels, DotPixels) },
        };
        _mmi = new MultiMeshInstance2D { Multimesh = _mm, Texture = Art.Dot };
        AddChild(_mmi);
        GetViewport().SizeChanged += Layout;
        Layout();
    }

    private void Layout()
    {
        var vp = GetViewportRect().Size;
        float top = UiTheme.TopBarHeight + 12, bottom = UiTheme.BottomBarHeight + 12;
        float avail = Mathf.Min(vp.X - 24, vp.Y - top - bottom);
        avail = Mathf.Max(avail, 50);
        _mapRect = new Rect2((vp.X - avail) / 2f, top + (vp.Y - top - bottom - avail) / 2f, avail, avail);
        QueueRedraw();
    }

    public Vector2 ToScreen(double x, double y) =>
        _mapRect.Position + new Vector2((float)(x / _mapUnits), (float)(y / _mapUnits)) * _mapRect.Size;

    public bool TryToMap(Vector2 screen, out long x, out long y)
    {
        var p = (screen - _mapRect.Position) / _mapRect.Size;
        x = (long)(p.X * _mapUnits);
        y = (long)(p.Y * _mapUnits);
        return p.X >= 0 && p.Y >= 0 && p.X <= 1 && p.Y <= 1;
    }

    public override void _Draw()
    {
        var vp = GetViewportRect();
        DrawRect(vp, Art.Outside);
        DrawRect(_mapRect, Art.Ground);
        int tiles = _mapUnits / 1000;
        for (int i = 10; i < tiles; i += 10)
        {
            float f = i / (float)tiles;
            DrawLine(new Vector2(_mapRect.Position.X + f * _mapRect.Size.X, _mapRect.Position.Y), new Vector2(_mapRect.Position.X + f * _mapRect.Size.X, _mapRect.End.Y), Art.GroundGrid, 1);
            DrawLine(new Vector2(_mapRect.Position.X, _mapRect.Position.Y + f * _mapRect.Size.Y), new Vector2(_mapRect.End.X, _mapRect.Position.Y + f * _mapRect.Size.Y), Art.GroundGrid, 1);
        }
        DrawRect(_mapRect, Art.GroundEdge, false, 2);
    }

    public void Render(WorldSnapshot snap, double tick)
    {
        if (snap.MapUnits != _mapUnits && snap.MapUnits > 0) { _mapUnits = snap.MapUnits; QueueRedraw(); }
        int n = snap.DotCount;
        if (_mm.InstanceCount != n) _mm.InstanceCount = n;
        float scale = Mathf.Clamp(_mapRect.Size.Y / 950f, 0.6f, 2.5f);
        for (int i = 0; i < n; i++)
        {
            snap.DotPosition(i, tick, out double x, out double y);
            var p = ToScreen(x, y);
            _mm.SetInstanceTransform2D(i, new Transform2D(0, new Vector2(scale, scale), 0, p));
            _mm.SetInstanceColor(i, Art.DotColors[snap.ColorIndex[i] % Art.DotColors.Length]);
        }
    }
}
