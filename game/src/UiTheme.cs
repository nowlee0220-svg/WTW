using Godot;

namespace Wtw.Game;

/// <summary>UI 테마 한 곳: 글꼴 · 글자 크기 · 색 · 창 모양.</summary>
public static class UiTheme
{
    public const int FontSize = 20;
    public const int FontSizeBig = 26;
    public const int FontSizeSmall = 16;
    public const int TopBarHeight = 56;
    public const int BottomBarHeight = 64;

    public static readonly Color Text = new(0.92f, 0.90f, 0.84f);
    public static readonly Color TextDim = new(0.66f, 0.64f, 0.58f);
    public static readonly Color Accent = new(0.95f, 0.78f, 0.38f);
    public static readonly Color Bar = new(0.11f, 0.11f, 0.12f, 0.96f);
    public static readonly Color Button = new(0.20f, 0.20f, 0.22f);
    public static readonly Color ButtonHover = new(0.28f, 0.28f, 0.30f);
    public static readonly Color ButtonPressed = new(0.42f, 0.34f, 0.18f);

    public static FontFile Regular { get; private set; } = null!;
    public static FontFile Bold { get; private set; } = null!;

    public static Theme Build()
    {
        Regular = GD.Load<FontFile>("res://fonts/NanumGothic-Regular.ttf");
        Bold = GD.Load<FontFile>("res://fonts/NanumGothic-Bold.ttf");
        ThemeDB.FallbackFont = Regular;
        ThemeDB.FallbackFontSize = FontSize;

        var t = new Theme { DefaultFont = Regular, DefaultFontSize = FontSize };
        t.SetColor("font_color", "Label", Text);
        t.SetColor("font_color", "Button", Text);
        t.SetColor("font_hover_color", "Button", Colors.White);
        t.SetColor("font_pressed_color", "Button", Colors.White);
        t.SetColor("font_hover_pressed_color", "Button", Colors.White);
        t.SetStylebox("panel", "PanelContainer", Box(Bar, 0, 10, 6));
        t.SetStylebox("normal", "Button", Box(Button, 6, 16, 6));
        t.SetStylebox("hover", "Button", Box(ButtonHover, 6, 16, 6));
        t.SetStylebox("pressed", "Button", Box(ButtonPressed, 6, 16, 6));
        t.SetStylebox("hover_pressed", "Button", Box(ButtonPressed.Lightened(0.1f), 6, 16, 6));
        t.SetStylebox("focus", "Button", new StyleBoxEmpty());
        t.SetConstant("separation", "HBoxContainer", 10);
        return t;
    }

    public static StyleBoxFlat Box(Color bg, int radius, int padX, int padY)
    {
        var s = new StyleBoxFlat { BgColor = bg };
        s.SetCornerRadiusAll(radius);
        s.ContentMarginLeft = s.ContentMarginRight = padX;
        s.ContentMarginTop = s.ContentMarginBottom = padY;
        return s;
    }
}
