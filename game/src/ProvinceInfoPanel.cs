using Godot;
using Wtw.Sim.Provinces;

namespace Wtw.Game;

/// <summary>
/// 지방 정보 칸(2D 지방 화면의 왼쪽): 그 지방의 시각 · 지금 기온 · 해 · 바람 · 강수 · 높이 · 생물군계 · 바위 · 물 · 비옥함 · 자원 …
/// 줄을 누르면 그 아래에 이유가 펼쳐진다(E3: 따로 있던 "자세히 보기" 줄을 없애 길이를 반으로, 자주 보는 시각 · 기온 · 물 · 자원을 위로). 맨 위 제목을 누르면 칸 전체를 접는다.
/// 오른쪽 칸 정보 창 · 위아래 막대 · 왼쪽 아래 범례와 겹치지 않는 자리(Main 이 아래 끝을 맞춘다).
/// </summary>
public partial class ProvinceInfoPanel : PanelContainer
{
    public const int Width = 340;

    private Button _head = null!;
    private ScrollContainer _scroll = null!;
    private VBoxContainer _rows = null!;
    private readonly List<(Label Label, Label Value, Label More, Label Why)> _lines = new();
    private readonly HashSet<string> _open = new();
    private bool _collapsed = true; // 처음엔 접어 둔다(제목만) — 지도를 넓게

    public bool Collapsed => _collapsed;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        AnchorLeft = 0; AnchorRight = 0; AnchorTop = 0; AnchorBottom = 1;
        OffsetLeft = 12; OffsetRight = 12 + Width;
        OffsetTop = UiTheme.TopBarHeight + 12; OffsetBottom = -UiTheme.BottomBarHeight - 12;
        AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Panel, 8, 14, 10));
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        AddChild(box);
        _head = new Button { Flat = true, FocusMode = FocusModeEnum.None, Alignment = HorizontalAlignment.Left, TooltipText = "눌러서 접기 · 펼치기" };
        _head.AddThemeFontOverride("font", UiTheme.Bold);
        _head.AddThemeColorOverride("font_color", UiTheme.Accent);
        _head.AddThemeColorOverride("font_hover_color", UiTheme.Accent.Lightened(0.3f));
        _head.Pressed += () => { _collapsed = !_collapsed; Relayout(); };
        box.AddChild(_head);
        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill };
        box.AddChild(_scroll);
        _rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _rows.AddThemeConstantOverride("separation", 4);
        _scroll.AddChild(_rows);
        Relayout();
    }

    private string _title = "";

    /// <summary>
    /// 내용을 채운다. 줄 수 · 이름이 같으면 글자만 바꿔 펼친 상태를 지킨다.
    /// 줄 하나 = 누를 수 있는 단추(이름 · 값 · ▸). 누르면 그 아래에 이유가 펼쳐진다(따로 있던 "자세히 보기" 줄은 없앰 → 길이가 반).
    /// </summary>
    public void Show(string title, IReadOnlyList<ProvinceFact> facts)
    {
        _title = title;
        bool same = _lines.Count == facts.Count && _lines.Select(l => l.Label.Text).SequenceEqual(facts.Select(f => f.Label));
        if (!same)
        {
            foreach (var n in _rows.GetChildren()) { _rows.RemoveChild(n); n.QueueFree(); }
            _lines.Clear();
            foreach (var f in facts)
            {
                var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Stop, TooltipText = "눌러서 이유 보기 · 접기", MouseDefaultCursorShape = CursorShape.PointingHand };
                row.AddThemeConstantOverride("separation", 8);
                var l = new Label { Text = f.Label, CustomMinimumSize = new Vector2(92, 0), VerticalAlignment = VerticalAlignment.Top, MouseFilter = MouseFilterEnum.Ignore };
                l.AddThemeColorOverride("font_color", UiTheme.TextDim);
                l.AddThemeFontSizeOverride("font_size", UiTheme.FontSizeSmall + 1);
                var v = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(Width - 92 - 64, 0), MouseFilter = MouseFilterEnum.Ignore };
                v.AddThemeFontSizeOverride("font_size", UiTheme.FontSizeSmall + 1);
                var more = new Label { MouseFilter = MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Top };
                more.AddThemeColorOverride("font_color", UiTheme.Accent.Darkened(0.1f));
                more.AddThemeFontSizeOverride("font_size", UiTheme.FontSizeSmall);
                row.AddChild(l); row.AddChild(v); row.AddChild(more);
                string key = f.Label;
                var why = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(Width - 40, 0) };
                why.AddThemeFontSizeOverride("font_size", UiTheme.FontSizeSmall);
                why.AddThemeColorOverride("font_color", UiTheme.Text.Darkened(0.12f));
                row.GuiInput += e =>
                {
                    if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) { if (!_open.Add(key)) _open.Remove(key); UpdateOpen(); }
                };
                row.MouseEntered += () => v.AddThemeColorOverride("font_color", UiTheme.Accent.Lightened(0.35f));
                row.MouseExited += () => v.RemoveThemeColorOverride("font_color");
                _rows.AddChild(row);
                _rows.AddChild(why);
                _rows.AddChild(new HSeparator());
                _lines.Add((l, v, more, why));
            }
        }
        for (int k = 0; k < facts.Count; k++)
        {
            _lines[k].Value.Text = facts[k].Value;
            _lines[k].Why.Text = facts[k].Why;
        }
        UpdateOpen();
        Relayout();
    }

    private void UpdateOpen()
    {
        foreach (var (l, _, more, why) in _lines)
        {
            bool open = _open.Contains(l.Text);
            why.Visible = open;
            more.Text = open ? "▾" : "▸";
        }
    }

    private void Relayout()
    {
        _head.Text = (_collapsed ? "▶ " : "▼ ") + (_title.Length > 0 ? _title : "지방 정보");
        _scroll.Visible = !_collapsed;
    }

    /// <summary>아래 끝(화면 좌표 y). 접혀 있으면 제목 높이만.</summary>
    public void SetBottom(float screenY, float viewportH)
    {
        if (_collapsed) { AnchorBottom = 0; OffsetBottom = OffsetTop + 48; }
        else { AnchorBottom = 1; OffsetBottom = Math.Min(-UiTheme.BottomBarHeight - 12, screenY - viewportH); }
    }
}
