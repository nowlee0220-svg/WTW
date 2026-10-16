using Godot;

namespace Wtw.Game;

/// <summary>
/// 코드로 그림을 그리는 작은 붓(기획서 17절 — 그림 재료가 없어서 그림도 코드로).
/// 화소마다 도형까지의 거리로 가장자리를 부드럽게(안티에일리어싱) 칠한다. 색은 미리 곱한 알파로 쌓는다.
/// 좌표는 화소 단위, y 는 아래쪽.
/// </summary>
public sealed class Sketch
{
    public readonly int W, H;
    private readonly float[] _r, _g, _b, _a;

    public Sketch(int w, int h)
    {
        W = w; H = h;
        _r = new float[w * h]; _g = new float[w * h]; _b = new float[w * h]; _a = new float[w * h];
    }

    public void Clear()
    {
        Array.Clear(_r); Array.Clear(_g); Array.Clear(_b); Array.Clear(_a);
    }

    private void Blend(int x, int y, Color c, float cov)
    {
        if ((uint)x >= (uint)W || (uint)y >= (uint)H || cov <= 0) return;
        float a = Math.Clamp(cov, 0, 1) * c.A;
        int i = y * W + x;
        float k = 1 - a;
        _r[i] = c.R * a + _r[i] * k;
        _g[i] = c.G * a + _g[i] * k;
        _b[i] = c.B * a + _b[i] * k;
        _a[i] = a + _a[i] * k;
    }

    /// <summary>타원(기울기 rot 라디안). shade &gt; 0 이면 왼쪽 위가 밝고 오른쪽 아래가 어둡게.</summary>
    public void Ellipse(float cx, float cy, float rx, float ry, Color c, float shade = 0, float rot = 0)
    {
        float cs = Mathf.Cos(rot), sn = Mathf.Sin(rot);
        float ext = Math.Max(rx, ry) + 2;
        int x0 = (int)(cx - ext), x1 = (int)(cx + ext) + 1, y0 = (int)(cy - ext), y1 = (int)(cy + ext) + 1;
        float m = Math.Min(rx, ry);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                float u = (dx * cs + dy * sn) / rx, v = (-dx * sn + dy * cs) / ry;
                float d = Mathf.Sqrt(u * u + v * v);
                float cov = (1 - d) * m + 0.5f;
                if (cov <= 0) continue;
                var col = c;
                if (shade > 0)
                {
                    float l = Math.Clamp(-(u + v) * 0.5f, -1, 1); // 왼쪽 위 = +1
                    col = l > 0 ? c.Lerp(Colors.White, l * shade * 0.35f) : c.Lerp(Colors.Black, -l * shade * 0.45f);
                    col.A = c.A;
                }
                Blend(x, y, col, cov);
            }
    }

    public void Circle(float cx, float cy, float r, Color c, float shade = 0) => Ellipse(cx, cy, r, r, c, shade);

    /// <summary>두께 있는 선(양 끝 둥글게). 굵기는 w0 → w1 로 바뀐다.</summary>
    public void Line(float x0, float y0, float x1, float y1, float w0, float w1, Color c)
    {
        float ext = Math.Max(w0, w1) + 2;
        int bx0 = (int)(Math.Min(x0, x1) - ext), bx1 = (int)(Math.Max(x0, x1) + ext) + 1;
        int by0 = (int)(Math.Min(y0, y1) - ext), by1 = (int)(Math.Max(y0, y1) + ext) + 1;
        float dx = x1 - x0, dy = y1 - y0, l2 = dx * dx + dy * dy;
        for (int y = by0; y <= by1; y++)
            for (int x = bx0; x <= bx1; x++)
            {
                float px = x + 0.5f - x0, py = y + 0.5f - y0;
                float t = l2 > 0 ? Math.Clamp((px * dx + py * dy) / l2, 0, 1) : 0;
                float ex = px - dx * t, ey = py - dy * t;
                float d = Mathf.Sqrt(ex * ex + ey * ey);
                float hw = (w0 + (w1 - w0) * t) * 0.5f;
                Blend(x, y, c, hw - d + 0.5f);
            }
    }

    public void Line(float x0, float y0, float x1, float y1, float w, Color c) => Line(x0, y0, x1, y1, w, w, c);

    /// <summary>볼록하지 않아도 되는 다각형(짝홀 규칙). 가장자리는 변까지 거리로 부드럽게.</summary>
    public void Polygon(Vector2[] pts, Color c, float shade = 0)
    {
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue, cx = 0, cy = 0;
        foreach (var p in pts) { minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X); minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y); cx += p.X; cy += p.Y; }
        cx /= pts.Length; cy /= pts.Length;
        float span = Math.Max(maxX - minX, maxY - minY) * 0.5f + 1;
        for (int y = (int)minY - 1; y <= (int)maxY + 1; y++)
            for (int x = (int)minX - 1; x <= (int)maxX + 1; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                bool inside = false;
                float best = float.MaxValue;
                for (int i = 0, j = pts.Length - 1; i < pts.Length; j = i++)
                {
                    var a = pts[i]; var b = pts[j];
                    if ((a.Y > py) != (b.Y > py) && px < (b.X - a.X) * (py - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
                    float ex = b.X - a.X, ey = b.Y - a.Y, l2 = ex * ex + ey * ey;
                    float t = l2 > 0 ? Math.Clamp(((px - a.X) * ex + (py - a.Y) * ey) / l2, 0, 1) : 0;
                    float qx = a.X + ex * t - px, qy = a.Y + ey * t - py;
                    best = Math.Min(best, qx * qx + qy * qy);
                }
                float d = Mathf.Sqrt(best);
                float cov = inside ? d + 0.5f : 0.5f - d;
                if (cov <= 0) continue;
                var col = c;
                if (shade > 0)
                {
                    float l = Math.Clamp(-((px - cx) + (py - cy)) / (span * 1.4f), -1, 1);
                    col = l > 0 ? c.Lerp(Colors.White, l * shade * 0.35f) : c.Lerp(Colors.Black, -l * shade * 0.45f);
                    col.A = c.A;
                }
                Blend(x, y, col, cov);
            }
    }

    /// <summary>atlas 의 (ox, oy) 자리에 이 그림을 옮긴다. 미리 곱한 알파 그대로(셰이더는 blend_premul_alpha) — 축소해도 가장자리가 검게 번지지 않는다.</summary>
    public void CopyTo(byte[] atlas, int atlasW, int ox, int oy)
    {
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int i = y * W + x;
                float a = _a[i];
                int o = ((oy + y) * atlasW + ox + x) * 4;
                atlas[o] = (byte)Math.Clamp(_r[i] * 255 + 0.5f, 0, 255);
                atlas[o + 1] = (byte)Math.Clamp(_g[i] * 255 + 0.5f, 0, 255);
                atlas[o + 2] = (byte)Math.Clamp(_b[i] * 255 + 0.5f, 0, 255);
                atlas[o + 3] = (byte)Math.Clamp(a * 255 + 0.5f, 0, 255);
            }
    }
}
