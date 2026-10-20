using Godot;
using Wtw.Sim.People;
using Wtw.Sim.Runtime;

namespace Wtw.Game;

/// <summary>
/// (E5b) 지방 화면의 짐승: 무리 하나는 시뮬에서 한 점(일정 · 경로)이고, 화면은 그 둘레에 마릿수만큼(여덟까지) 그린다 — 사람처럼 매 프레임 보간.
/// 걸을 때는 다리를 번갈아, 잘 때는 엎드려. 사람과 같은 앞뒤 정렬 안에 그린다. 마우스를 올리면 AnimalHover(칸, 무리).
/// </summary>
public partial class ProvinceView
{
    public event Action<int, int>? AnimalHover; // 칸, 무리 번호(−1 = 없음)

    private sealed class HerdDraw { public float LastDir = 1; public Vector2 Center; public float Radius; public int Cell; }
    private readonly List<Sprite2D> _animalSprites = new();
    private readonly Dictionary<(int Cell, int Id), HerdDraw> _herdDraw = new();
    private (int Cell, int Id) _hoverAnimal = (-1, -1);
    public const int MaxDrawnPerHerd = 8;

    public (int Cell, int Id) HoverAnimal => _hoverAnimal;

    private void UpdateAnimals(WorldSnapshot snap, double tick)
    {
        int n = 0;
        float t = (float)(Time.GetTicksMsec() / 1000.0);
        var fauna = Wtw.Sim.Data.AnimalData.Default;
        foreach (var sh in _shown)
        {
            int cell = sh.Map.Cell;
            if (!snap.Animals.TryGetValue(cell, out var herds)) continue;
            foreach (var h in herds)
            {
                if ((uint)h.Species >= (uint)fauna.Animals.Count) continue;
                var def = fauna.Animals[h.Species];
                h.PositionAt(tick, out double mx, out double my, out double dx, out double dy);
                var (lx, ly) = sh.Place.ToLayout(mx / 1000.0, my / 1000.0);
                var center = new Vector2((float)lx, (float)ly) * TilePx;
                var key = (cell, h.Id);
                if (!_herdDraw.TryGetValue(key, out var hd)) _herdDraw[key] = hd = new HerdDraw();
                bool moving = dx != 0 || dy != 0;
                if (moving)
                {
                    var dir = new Vector2((float)dx, (float)dy).Rotated(sh.Node.Rotation - _cam.Rotation);
                    if (Math.Abs(dir.X) > 0.05f) hd.LastDir = Math.Sign(dir.X);
                }
                bool lie = !moving && h.State is HerdState.Sleep;
                int count = Math.Min(h.Count, MaxDrawnPerHerd);
                float spread = (def.Size == 1 ? 0.45f : 0.75f) * TilePx;
                float tiles = Art.AnimalTiles(def.Art);
                float scale = tiles * TilePx / Art.AnimalW;
                hd.Center = center; hd.Cell = cell; hd.Radius = spread * MathF.Sqrt(count) + 0.4f * TilePx;
                if (def.Size == 1 || (cell, h.Id) == _hoverAnimal) FadeAround(center, 0.6f); // 작은 짐승 · 고른 무리는 나무에 가리지 않게
                for (int k = 0; k < count; k++)
                {
                    // 무리 안의 자리: 해바라기 씨 모양으로 퍼지게(무리마다 다르게 돌림)
                    float ang = k * 2.39996f + h.Id * 0.7f;
                    float rad = k == 0 ? 0 : spread * MathF.Sqrt(k);
                    var off = new Vector2(MathF.Cos(ang) * rad, MathF.Sin(ang) * rad * 0.6f);
                    int frame = lie ? 2 : moving ? ((int)(t * (h.State == HerdState.Flee ? 9 : 5) + k * 0.5f + h.Id) & 1) : (((int)(t * 0.6f + k + h.Id)) % 7 == 0 ? 1 : 0);
                    if (n == _animalSprites.Count) { var sp = new Sprite2D { Material = _premul, Centered = false }; _peopleRoot.AddChild(sp); _animalSprites.Add(sp); }
                    var s = _animalSprites[n++];
                    s.Texture = Art.Animal(def.Art, frame, h.Owner >= 0);
                    float flip = k % 5 == 3 && !moving ? -hd.LastDir : hd.LastDir;
                    s.FlipH = flip < 0;
                    s.Scale = new Vector2(scale, scale);
                    s.Position = center + off + new Vector2(-Art.AnimalW * scale / 2, -Art.AnimalH * scale);
                    var mod = (cell, h.Id) == _hoverAnimal ? new Color(1.18f, 1.15f, 1.05f) : Colors.White;
                    if (h.State == HerdState.Stalk) mod = mod.Darkened(0.05f);
                    s.Modulate = mod;
                    s.Visible = true;
                }
            }
        }
        for (int k = n; k < _animalSprites.Count; k++) _animalSprites[k].Visible = false;
        if (_herdDraw.Count > 4000) _herdDraw.Clear();
    }

    private void HideAnimals() { foreach (var s in _animalSprites) s.Visible = false; }

    /// <summary>화면 위치의 짐승 무리(없으면 (−1, −1)): 무리 둘레 안에서 가운데가 가장 가까운 것.</summary>
    public (int Cell, int Id) PickAnimal(Vector2 screen)
    {
        if (_snap is null) return (-1, -1);
        var w = ScreenToWorld(screen);
        (int, int) best = (-1, -1); float bd = float.MaxValue;
        foreach (var sh in _shown)
        {
            if (!_snap.Animals.TryGetValue(sh.Map.Cell, out var herds)) continue;
            foreach (var h in herds)
            {
                if (!_herdDraw.TryGetValue((sh.Map.Cell, h.Id), out var hd)) continue;
                var d = w - (hd.Center - new Vector2(0, 0.35f * TilePx));
                float dd = d.LengthSquared();
                if (dd < hd.Radius * hd.Radius && dd < bd) { bd = dd; best = (sh.Map.Cell, h.Id); }
            }
        }
        return best;
    }

    private void SetHoverAnimal((int Cell, int Id) a)
    {
        if (a == _hoverAnimal) return;
        _hoverAnimal = a;
        AnimalHover?.Invoke(a.Cell, a.Id);
    }

    /// <summary>자동 확인용: 그 무리의 화면 위치.</summary>
    public Vector2? AnimalScreen(int cell, int id) => _herdDraw.TryGetValue((cell, id), out var hd) ? GetViewport().GetCanvasTransform() * (hd.Center - new Vector2(0, 0.35f * TilePx)) : null;
}
