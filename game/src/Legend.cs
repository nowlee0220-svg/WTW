using Godot;
using Wtw.Sim.Planet;

namespace Wtw.Game;

/// <summary>지도 모드의 작은 범례(왼쪽 아래). 자원 모드에서는 볼 자원을 고르는 단추도 여기 있다.</summary>
public partial class Legend : PanelContainer
{
    public event Action<int>? ResourcePicked;

    private VBoxContainer _box = null!;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        AnchorLeft = 0; AnchorRight = 0; AnchorTop = 1; AnchorBottom = 1;
        OffsetLeft = 12; OffsetBottom = -UiTheme.BottomBarHeight - 12;
        GrowVertical = GrowDirection.Begin;
        AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Panel, 8, 14, 10));
        _box = new VBoxContainer();
        _box.AddThemeConstantOverride("separation", 4);
        AddChild(_box);
    }

    public void Show(MapMode mode, int resource)
    {
        foreach (var n in _box.GetChildren()) { _box.RemoveChild(n); n.QueueFree(); }
        var title = new Label { Text = mode == MapMode.Resource ? $"지도: 자원 — {ResourceKinds.Names[resource]}" : $"지도: {MapModes.Name(mode)}" };
        title.AddThemeFontOverride("font", UiTheme.Bold);
        title.AddThemeColorOverride("font_color", UiTheme.Accent);
        _box.AddChild(title);

        if (mode == MapMode.Resource)
        {
            var grid = new GridContainer { Columns = 2 };
            grid.AddThemeConstantOverride("h_separation", 6);
            grid.AddThemeConstantOverride("v_separation", 4);
            for (int r = 0; r < ResourceKinds.Count; r++)
            {
                int id = r;
                var b = new Button { Text = ResourceKinds.Names[r], ToggleMode = true, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(120, 34) };
                b.AddThemeFontSizeOverride("font_size", UiTheme.FontSizeSmall);
                b.SetPressedNoSignal(r == resource);
                b.Pressed += () => ResourcePicked?.Invoke(id);
                grid.AddChild(b);
            }
            _box.AddChild(grid);
        }

        foreach (var (color, text) in MapModes.Legend(mode))
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            if (color.A > 0) row.AddChild(new ColorRect { Color = color, CustomMinimumSize = new Vector2(20, 20) });
            var l = new Label { Text = text };
            l.AddThemeFontSizeOverride("font_size", UiTheme.FontSizeSmall);
            if (color.A == 0) l.AddThemeColorOverride("font_color", UiTheme.TextDim);
            row.AddChild(l);
            _box.AddChild(row);
        }
        // 아래 모서리에 붙이고 높이 0 으로 두면, 내용의 최소 크기만큼 위로 자란다(GrowVertical = Begin)
        OffsetTop = OffsetBottom;
        OffsetRight = OffsetLeft;
    }
}
