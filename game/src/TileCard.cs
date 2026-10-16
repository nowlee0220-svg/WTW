using Godot;
using Wtw.Sim.Provinces;

namespace Wtw.Game;

/// <summary>지방 화면에서 마우스를 올린 타일 · 오브젝트의 작은 카드(이름 · 땅 종류 · 다루는 법 → 자원). 마우스 옆에 따라다닌다.</summary>
public partial class TileCard : PanelContainer
{
    private Label _title = null!, _sub = null!, _uses = null!;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.08f, 0.08f, 0.09f, 0.92f), 6, 12, 8));
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 2);
        AddChild(box);
        _title = new Label { MouseFilter = MouseFilterEnum.Ignore };
        _title.AddThemeFontOverride("font", UiTheme.Bold);
        _title.AddThemeColorOverride("font_color", UiTheme.Accent);
        box.AddChild(_title);
        _sub = new Label { MouseFilter = MouseFilterEnum.Ignore };
        _sub.AddThemeColorOverride("font_color", UiTheme.TextDim);
        _sub.AddThemeFontSizeOverride("font_size", UiTheme.FontSizeSmall + 1);
        box.AddChild(_sub);
        _uses = new Label { MouseFilter = MouseFilterEnum.Ignore };
        _uses.AddThemeFontSizeOverride("font_size", UiTheme.FontSizeSmall + 1);
        box.AddChild(_uses);
    }

    public void ShowAt(TileReport r, Vector2 mouse)
    {
        _title.Text = r.Title;
        _sub.Text = r.Subtitle;
        _sub.Visible = r.Subtitle.Length > 0;
        _uses.Text = r.Uses;
        _uses.Visible = r.Uses.Length > 0;
        Visible = true;
        ResetSize();
        Follow(mouse);
    }

    public void Follow(Vector2 mouse)
    {
        if (!Visible) return;
        var vp = GetViewportRect().Size;
        var pos = mouse + new Vector2(20, 22);
        if (pos.X + Size.X > vp.X - 8) pos.X = mouse.X - Size.X - 12;
        if (pos.Y + Size.Y > vp.Y - UiTheme.BottomBarHeight - 8) pos.Y = mouse.Y - Size.Y - 12;
        Position = pos;
    }
}
