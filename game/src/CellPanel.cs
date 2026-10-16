using Godot;
using Wtw.Sim.Planet;

namespace Wtw.Game;

/// <summary>
/// 칸 정보 창(오른쪽 한 칸). 범례(왼쪽 아래)·막대와 겹치지 않는 자리에 둔다(기획서 7-6 "창끼리 가리지 않게").
/// 내용은 시뮬의 CellDescriber 가 만든 말을 그대로 보여 준다.
/// </summary>
public partial class CellPanel : PanelContainer
{
    public const int Width = 470;
    public event Action? CloseRequested;

    private Label _title = null!, _subtitle = null!;
    private GridContainer _facts = null!;
    private VBoxContainer _why = null!;
    private Button _whyToggle = null!;
    private bool _whyOpen; // 처음엔 접어 둔다. 한 번 펼치면 다른 칸을 눌러도 펼친 채로(이번 실행 동안)

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        AnchorLeft = 1; AnchorRight = 1; AnchorTop = 0; AnchorBottom = 1;
        OffsetLeft = -Width - 12; OffsetRight = -12;
        OffsetTop = UiTheme.TopBarHeight + 12; OffsetBottom = -UiTheme.BottomBarHeight - 12;
        AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Panel, 8, 18, 14));

        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        AddChild(scroll);
        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(box);

        var head = new HBoxContainer();
        box.AddChild(head);
        _title = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _title.AddThemeFontOverride("font", UiTheme.Bold);
        _title.AddThemeFontSizeOverride("font_size", UiTheme.FontSizeBig + 4);
        _title.AddThemeColorOverride("font_color", UiTheme.Accent);
        head.AddChild(_title);
        var close = new Button { Text = "닫기", FocusMode = FocusModeEnum.None, TooltipText = "Esc" };
        close.Pressed += () => CloseRequested?.Invoke();
        head.AddChild(close);

        _subtitle = new Label();
        _subtitle.AddThemeColorOverride("font_color", UiTheme.TextDim);
        box.AddChild(_subtitle);
        box.AddChild(new HSeparator());

        _facts = new GridContainer { Columns = 2 };
        _facts.AddThemeConstantOverride("h_separation", 14);
        _facts.AddThemeConstantOverride("v_separation", 6);
        box.AddChild(_facts);

        box.AddChild(new HSeparator());
        // "왜?" — 누르면 같은 창 안에서 펼치고 접는다
        _whyToggle = new Button { FocusMode = FocusModeEnum.None, Alignment = HorizontalAlignment.Left, Flat = true };
        _whyToggle.AddThemeFontOverride("font", UiTheme.Bold);
        _whyToggle.AddThemeColorOverride("font_color", UiTheme.Accent);
        _whyToggle.AddThemeColorOverride("font_hover_color", UiTheme.Accent.Lightened(0.3f));
        _whyToggle.Pressed += () => { _whyOpen = !_whyOpen; UpdateWhy(); };
        box.AddChild(_whyToggle);
        _why = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _why.AddThemeConstantOverride("separation", 6);
        box.AddChild(_why);
        UpdateWhy();
    }

    public void SetWhyOpen(bool open) { _whyOpen = open; UpdateWhy(); }

    private void UpdateWhy()
    {
        _why.Visible = _whyOpen;
        _whyToggle.Text = _whyOpen ? "왜? ▲ 접기" : "왜? ▼ 펼치기";
    }

    public void ShowCell(Planet p, int c)
    {
        var r = CellDescriber.Describe(p, c);
        _title.Text = r.Title;
        _subtitle.Text = r.Subtitle;
        foreach (var n in _facts.GetChildren()) n.QueueFree();
        foreach (var n in _why.GetChildren()) n.QueueFree();
        foreach (var (label, value) in r.Facts)
        {
            var l = new Label { Text = label, VerticalAlignment = VerticalAlignment.Top };
            l.AddThemeColorOverride("font_color", UiTheme.TextDim);
            _facts.AddChild(l);
            _facts.AddChild(new Label { Text = value, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(Width - 150, 0) });
        }
        foreach (var w in r.Why)
        {
            var l = new Label { Text = "· " + w, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(Width - 50, 0) };
            l.AddThemeFontSizeOverride("font_size", UiTheme.FontSizeSmall + 1);
            _why.AddChild(l);
        }
        Visible = true;
    }
}
