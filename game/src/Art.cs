using Godot;

namespace Wtw.Game;

/// <summary>
/// 그림 공급처 — 모든 그림은 여기서 받는다(기획서 17절). 지금은 코드로 그린 원,
/// 나중에 진짜 그림이 생기면 이 파일만 바꿔 같은 자리에 갈아 끼운다.
/// </summary>
public static class Art
{
    public static readonly Color[] DotColors =
    {
        new(0.93f, 0.78f, 0.55f), // 모래
        new(0.85f, 0.55f, 0.40f), // 황토
        new(0.62f, 0.78f, 0.52f), // 풀
        new(0.55f, 0.70f, 0.85f), // 하늘
        new(0.86f, 0.86f, 0.80f), // 뼈
        new(0.80f, 0.62f, 0.78f), // 꽃
        new(0.95f, 0.88f, 0.45f), // 볏짚
        new(0.70f, 0.58f, 0.46f), // 가죽
    };

    public static readonly Color Ground = new(0.20f, 0.24f, 0.17f);
    public static readonly Color GroundGrid = new(1f, 1f, 1f, 0.045f);
    public static readonly Color GroundEdge = new(0.45f, 0.42f, 0.30f);
    public static readonly Color Outside = new(0.06f, 0.06f, 0.07f);

    private static Texture2D? _dot;

    /// <summary>점 그림: 가장자리를 부드럽게 한 원 + 아래쪽 그늘 + 얇은 테두리.</summary>
    public static Texture2D Dot => _dot ??= MakeDot(48);

    private static Texture2D MakeDot(int size)
    {
        var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        float c = (size - 1) / 2f, r = size / 2f - 1.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = x - c, dy = y - c;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            float alpha = Mathf.Clamp(r - d + 0.5f, 0f, 1f);
            if (alpha <= 0f) { img.SetPixel(x, y, new Color(0, 0, 0, 0)); continue; }
            float shade = 1.0f - 0.22f * Mathf.Clamp((dy / r + 1f) / 2f, 0f, 1f); // 위가 밝고 아래가 어둡게
            float rim = Mathf.Clamp((d - (r - 3.5f)) / 2f, 0f, 1f);               // 테두리
            float v = Mathf.Lerp(shade, 0.35f, rim);
            img.SetPixel(x, y, new Color(v, v, v, alpha));
        }
        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }
}
