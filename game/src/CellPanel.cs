using Godot;
using Wtw.Sim.Planet;
using Wtw.Sim.Provinces;

namespace Wtw.Game;

/// <summary>
/// 칸 정보 창(오른쪽 한 칸). 범례(왼쪽 아래)·막대와 겹치지 않는 자리에 둔다(기획서 7-6 "창끼리 가리지 않게").
/// 내용은 시뮬의 CellDescriber 가 만든 말을 그대로 보여 준다.
/// </summary>
public partial class CellPanel : PanelContainer
{
    public const int Width = 470;
    public event Action? CloseRequested;
    public event Action? EnterRequested;

    private Label _title = null!, _subtitle = null!;
    private GridContainer _facts = null!;
    private VBoxContainer _why = null!;
    private Button _whyToggle = null!, _enter = null!;
    private HSeparator _whySep = null!;
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
        _enter = new Button { Text = "들어가기", FocusMode = FocusModeEnum.None, TooltipText = "이 지방의 2D 지도로 (칸을 두 번 눌러도 됩니다)" };
        _enter.AddThemeColorOverride("font_color", UiTheme.Accent);
        _enter.Pressed += () => EnterRequested?.Invoke();
        head.AddChild(_enter);
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

        _whySep = new HSeparator();
        box.AddChild(_whySep);
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

    private bool _whyAvailable = true;

    private void UpdateWhy()
    {
        _whyToggle.Visible = _whySep.Visible = _whyAvailable;
        _why.Visible = _whyOpen && _whyAvailable;
        _whyToggle.Text = _whyOpen ? "왜? ▲ 접기" : "왜? ▼ 펼치기";
    }

    public void ShowCell(Planet p, int c)
    {
        var r = CellDescriber.Describe(p, c);
        _enter.Visible = p.IsLand(c);
        _whyAvailable = true;
        Fill(r.Title, r.Subtitle, r.Facts, r.Why);
    }

    /// <summary>지방 화면의 타일 · 오브젝트 자세히(같은 자리).</summary>
    public void ShowTile(TileReport r)
    {
        _enter.Visible = false;
        _whyAvailable = false;
        Fill(r.Title, r.Subtitle, r.Facts, Array.Empty<string>());
    }

    private void Fill(string title, string subtitle, IReadOnlyList<(string Label, string Value)> facts, IReadOnlyList<string> why)
    {
        _title.Text = title;
        _subtitle.Text = subtitle;
        _subtitle.Visible = subtitle.Length > 0;
        foreach (var n in _facts.GetChildren()) n.QueueFree();
        foreach (var n in _why.GetChildren()) n.QueueFree();
        UpdateWhy();
        foreach (var (label, value) in facts)
        {
            var l = new Label { Text = label, VerticalAlignment = VerticalAlignment.Top };
            l.AddThemeColorOverride("font_color", UiTheme.TextDim);
            _facts.AddChild(l);
            _facts.AddChild(new Label { Text = value, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(Width - 150, 0) });
        }
        foreach (var w in why)
        {
            var l = new Label { Text = "· " + w, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(Width - 50, 0) };
            l.AddThemeFontSizeOverride("font_size", UiTheme.FontSizeSmall + 1);
            _why.AddChild(l);
        }
        Visible = true;
    }
}
