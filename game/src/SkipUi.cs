using Godot;
using Wtw.Sim.Data;
using Wtw.Sim.People;
using Wtw.Sim.Runtime;
using Wtw.Sim.Time;

namespace Wtw.Game;

/// <summary>
/// (E4) [넘기기] 창: 무엇까지 넘길지 고른다 — 시간(10 · 50년) · 다음 발견(아무거나 / 고른 물건) · 고른 물건이 n명 · n무리에게 퍼짐 ·
/// 새 땅으로 옮김 · 땅 이름 · 말 갈라짐 · 인구 n명. 아래에 "큰 일로 멈추기" 종류별 켜고 끄기.
/// 목표 목록은 SkipGoals(시뮬)에 있어 나중 단계가 쉽게 더한다.
/// </summary>
public partial class SkipPanel : PanelContainer
{
    public const int Width = 460;
    public event Action<SkipGoal>? StartRequested;
    public event Action? CloseRequested;
    private OptionButton _item = null!;
    private SpinBox _people = null!, _groups = null!, _pop = null!;
    private readonly List<(WorldEventKind Kind, CheckBox Box)> _toggles = new();
    public PopupGate? Gate;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;
        AnchorLeft = 0.5f; AnchorRight = 0.5f; AnchorTop = 0; AnchorBottom = 1;
        OffsetLeft = -Width / 2f; OffsetRight = Width / 2f;
        OffsetTop = UiTheme.TopBarHeight + 40; OffsetBottom = -UiTheme.BottomBarHeight - 40;
        AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Panel, 8, 16, 14));
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        AddChild(box);
        var head = new HBoxContainer();
        box.AddChild(head);
        var title = PeopleUi.L("넘기기", UiTheme.FontSizeBig, UiTheme.Accent, bold: true);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(title);
        var close = new Button { Text = "닫기", FocusMode = FocusModeEnum.None };
        close.Pressed += () => CloseRequested?.Invoke();
        head.AddChild(close);
        box.AddChild(PeopleUi.L("화면을 그리지 않고 가장 빠르게 시간을 보냅니다. 위급한 일(사람이 거의 다 죽음)이 생기면 멈추고, 끝나면 그동안 일어난 일을 보여 줍니다.", UiTheme.FontSizeSmall, UiTheme.TextDim, wrap: true));

        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill };
        box.AddChild(scroll);
        var v = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        v.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(v);

        Row(v, "시간", B("10년", () => SkipGoals.Years(10)), B("50년", () => SkipGoals.Years(50)));
        Row(v, "다음 발견", B("아무거나", () => SkipGoals.Discovery(null)));
        _item = new OptionButton { FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var c = CraftData.Default;
        var made = c.Rules.Select(r => r.Result).Distinct().OrderBy(i => c.ItemName(i), StringComparer.Ordinal).ToList();
        foreach (int it in made) { _item.AddItem(c.ItemName(it)); _item.SetItemMetadata(_item.ItemCount - 1, c.Catalog.Items[it].Id); }
        Row(v, "고른 물건", _item, B("알아낼 때까지", () => SkipGoals.Discovery(ItemId())));
        _people = Spin(10, 1, 500); _groups = Spin(2, 1, 20);
        Row(v, "퍼지기", PeopleUi.L("고른 물건을", UiTheme.FontSizeSmall + 1, UiTheme.Text), _people, PeopleUi.L("명 ·", UiTheme.FontSizeSmall + 1, UiTheme.Text), _groups, PeopleUi.L("무리", UiTheme.FontSizeSmall + 1, UiTheme.Text),
            B("이 알 때까지", () => SkipGoals.Spread(ItemId(), (int)_people.Value, (int)_groups.Value)));
        Row(v, "땅과 말", B("새 땅으로 옮김", SkipGoals.NewLand), B("땅 이름", SkipGoals.LandNamed), B("말 갈라짐", SkipGoals.LanguageSplit));
        _pop = Spin(100, 5, 3000);
        Row(v, "인구", _pop, PeopleUi.L("명", UiTheme.FontSizeSmall + 1, UiTheme.Text), B("이 될 때까지", () => SkipGoals.Population((int)_pop.Value)));

        v.AddChild(new HSeparator());
        v.AddChild(PeopleUi.L("큰 일로 멈추기(★★★ 세계 수준 — 팝업)", UiTheme.FontSizeSmall + 1, UiTheme.Accent, bold: true));
        v.AddChild(PeopleUi.L("★★ 무리 수준(무리에서 처음 · 새 땅 · 땅 이름)은 알림만, ★ 한 사람의 일(태어남 · 죽음 · 무너짐 …)은 그 지방을 볼 때만 알립니다. 같은 종류는 게임 1년(4배속 2년 · 5배속 5년) · 실제 30초에 한 번만 멈춥니다.", UiTheme.FontSizeSmall, UiTheme.TextDim, wrap: true));
        foreach (var k in EventLevels.PopupKinds)
        {
            var cb = new CheckBox { Text = EventLevels.KindName(k), ButtonPressed = true, FocusMode = FocusModeEnum.None };
            var kind = k;
            cb.Toggled += on => { if (Gate is not null) Gate.Muted[(int)kind] = !on; };
            _toggles.Add((k, cb));
            v.AddChild(cb);
        }
    }

    /// <summary>창을 열 때 켜고 끄기를 지금 상태로.</summary>
    public void Sync() { if (Gate is null) return; foreach (var (k, cb) in _toggles) cb.SetPressedNoSignal(!Gate.Muted[(int)k]); }

    private string ItemId() => _item.ItemCount == 0 ? "campfire" : (string)_item.GetItemMetadata(Math.Max(0, _item.Selected));

    private Button B(string text, Func<SkipGoal> make)
    {
        var b = new Button { Text = text, FocusMode = FocusModeEnum.None };
        b.Pressed += () => StartRequested?.Invoke(make());
        return b;
    }

    private static SpinBox Spin(int value, int min, int max) => new() { MinValue = min, MaxValue = max, Value = value, Step = 1, CustomMinimumSize = new Vector2(80, 0) };

    private static void Row(VBoxContainer v, string label, params Control[] items)
    {
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 6);
        var l = PeopleUi.L(label, UiTheme.FontSizeSmall + 1, UiTheme.TextDim);
        l.CustomMinimumSize = new Vector2(84, 0);
        h.AddChild(l);
        foreach (var c in items) h.AddChild(c);
        v.AddChild(h);
    }
}

/// <summary>(E4) 넘기는 동안 화면 전체를 덮는 막(진행 막대 · 흐른 해 · [그만]). 그동안 지도는 그리지 않는다.</summary>
public partial class SkipOverlay : ColorRect
{
    public event Action? StopRequested;
    private Label _goal = null!, _years = null!;
    private ProgressBar _bar = null!;

    public override void _Ready()
    {
        Color = new Color(0.04f, 0.05f, 0.07f, 1f);
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(center);
        var v = new VBoxContainer { CustomMinimumSize = new Vector2(520, 0) };
        v.AddThemeConstantOverride("separation", 12);
        center.AddChild(v);
        v.AddChild(PeopleUi.L("넘기는 중", UiTheme.FontSizeBig, UiTheme.Accent, bold: true));
        _goal = PeopleUi.L("", UiTheme.FontSizeSmall + 2, UiTheme.Text, wrap: true);
        v.AddChild(_goal);
        _bar = new ProgressBar { MinValue = 0, MaxValue = 1, Step = 0.001, CustomMinimumSize = new Vector2(0, 22), ShowPercentage = false };
        v.AddChild(_bar);
        _years = PeopleUi.L("", UiTheme.FontSizeSmall + 1, UiTheme.TextDim);
        v.AddChild(_years);
        var stop = new Button { Text = "그만", FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(120, 44), SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        stop.Pressed += () => StopRequested?.Invoke();
        v.AddChild(stop);
    }

    public void Show(SimHost.SkipInfo info, long now)
    {
        Visible = true;
        _goal.Text = $"목표: {info.Goal}";
        // 목표가 시간이 아니면 몇 년이 걸릴지 모른다 — 막대는 흐른 해(최대 1000년)를 따라 천천히
        _bar.Value = info.Progress >= 0 ? info.Progress : Math.Min(1, info.Years / SkipRun.MaxYears);
        _years.Text = $"{info.Years:0.0}년 흘렀습니다 · 지금 {GameTime.Format(now)}";
    }
}

/// <summary>(E4) 큰 일(★★★) 팝업 · 넘기기가 끝난 뒤의 "그동안 일어난 일". 여러 일은 한 팝업에 묶는다.</summary>
public partial class EventPopup : PanelContainer
{
    public event Action<int, int>? GoRequested;     // 사람, 칸
    public event Action? ContinueRequested;
    public event Action<WorldEventKind[]>? MuteRequested;
    private Label _title = null!;
    private VBoxContainer _rows = null!;
    private Button _go = null!, _mute = null!;
    private readonly List<WorldEvent> _events = new();
    public bool Open => Visible;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;
        AnchorLeft = 0.5f; AnchorRight = 0.5f; AnchorTop = 0.5f; AnchorBottom = 0.5f;
        OffsetLeft = -300; OffsetRight = 300; OffsetTop = -200; OffsetBottom = 200;
        GrowVertical = GrowDirection.Both;
        AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.12f, 0.11f, 0.09f, 0.98f), 10, 18, 16));
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 10);
        AddChild(v);
        _title = PeopleUi.L("", UiTheme.FontSizeBig, UiTheme.Accent, bold: true, wrap: true);
        v.AddChild(_title);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 220) };
        v.AddChild(scroll);
        _rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _rows.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(_rows);
        var h = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        h.AddThemeConstantOverride("separation", 8);
        v.AddChild(h);
        _go = new Button { Text = "그곳으로 가기", FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 40) };
        _go.Pressed += () => { if (_events.Count > 0) { var e = _events[0]; Close(); GoRequested?.Invoke(e.Person, e.Cell); } };
        var cont = new Button { Text = "계속", FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 40) };
        cont.Pressed += () => { Close(); ContinueRequested?.Invoke(); };
        _mute = new Button { Text = "이런 일로는 멈추지 않기", FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 40) };
        _mute.Pressed += () => { var ks = _events.Select(e => e.Kind).Distinct().ToArray(); Close(); MuteRequested?.Invoke(ks); ContinueRequested?.Invoke(); };
        h.AddChild(_go); h.AddChild(cont); h.AddChild(_mute);
    }

    /// <summary>큰 일을 더한다(이미 열려 있으면 같은 팝업에 묶는다).</summary>
    public void AddEvents(IEnumerable<WorldEvent> events)
    {
        foreach (var e in events)
        {
            _events.Add(e);
            var l = PeopleUi.L($"{EventLevels.Stars(EventLevels.Of(e.Kind))} {e.Text}  · {GameTime.Format(e.Tick)}", UiTheme.FontSizeSmall + 2, UiTheme.Text, wrap: true);
            _rows.AddChild(l);
        }
        _title.Text = _events.Count == 1 ? $"큰 일 — {EventLevels.KindName(_events[0].Kind)}" : $"큰 일 {_events.Count}가지";
        _go.Visible = _mute.Visible = true;
        Visible = true;
    }

    /// <summary>넘기기가 끝난 뒤: 그동안 일어난 일.</summary>
    public void ShowSummary(List<string> lines)
    {
        Close();
        _title.Text = "그동안 일어난 일";
        foreach (var s in lines) _rows.AddChild(PeopleUi.L(s, UiTheme.FontSizeSmall + 1, s.StartsWith("   ") ? UiTheme.TextDim : UiTheme.Text, wrap: true));
        _go.Visible = _mute.Visible = false;
        Visible = true;
    }

    public void Close()
    {
        _events.Clear();
        foreach (var n in _rows.GetChildren()) { _rows.RemoveChild(n); n.QueueFree(); }
        Visible = false;
    }
}

/// <summary>(E4) [불러오기] 창: 빠른 저장 + 시험용 장면(zip 에 들어 있는 scenes/*.wtw).</summary>
public partial class LoadPanel : PanelContainer
{
    public event Action<string?>? Picked; // null = 빠른 저장, 아니면 res://scenes/파일
    public event Action? CloseRequested;
    private VBoxContainer _rows = null!;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;
        AnchorLeft = 0.5f; AnchorRight = 0.5f; AnchorTop = 0.5f; AnchorBottom = 0.5f;
        OffsetLeft = -280; OffsetRight = 280; OffsetTop = -220; OffsetBottom = 220;
        GrowVertical = GrowDirection.Both;
        AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Panel, 8, 16, 14));
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 8);
        AddChild(v);
        var head = new HBoxContainer();
        v.AddChild(head);
        var t = PeopleUi.L("불러오기", UiTheme.FontSizeBig, UiTheme.Accent, bold: true);
        t.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(t);
        var close = new Button { Text = "닫기", FocusMode = FocusModeEnum.None };
        close.Pressed += () => CloseRequested?.Invoke();
        head.AddChild(close);
        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 6);
        v.AddChild(_rows);
    }

    /// <summary>scenes/index.txt 의 장면들(파일|제목|설명).</summary>
    public static List<(string File, string Title, string Desc)> Scenes()
    {
        var list = new List<(string, string, string)>();
        if (!Godot.FileAccess.FileExists("res://scenes/index.txt")) return list;
        foreach (var line in Godot.FileAccess.GetFileAsString("res://scenes/index.txt").Split('\n'))
        {
            var p = line.Trim().Split('|');
            if (p.Length >= 3) list.Add((p[0], p[1], p[2]));
        }
        return list;
    }

    public void Open(bool hasQuick)
    {
        foreach (var n in _rows.GetChildren()) { _rows.RemoveChild(n); n.QueueFree(); }
        Add("빠른 저장", hasQuick ? "F5 로 저장한 세계" : "아직 저장한 세계가 없습니다", hasQuick ? null : "-");
        _rows.AddChild(new HSeparator());
        _rows.AddChild(PeopleUi.L("시험용 장면", UiTheme.FontSizeSmall + 1, UiTheme.TextDim));
        foreach (var (file, title, desc) in Scenes()) Add(title, desc, "res://scenes/" + file);
        Visible = true;
    }

    private void Add(string title, string desc, string? path)
    {
        var b = new Button { Text = title, FocusMode = FocusModeEnum.None, Alignment = HorizontalAlignment.Left, Disabled = path == "-", CustomMinimumSize = new Vector2(0, 40) };
        b.Pressed += () => { Visible = false; Picked?.Invoke(path); };
        _rows.AddChild(b);
        _rows.AddChild(PeopleUi.L(desc, UiTheme.FontSizeSmall, UiTheme.TextDim, wrap: true));
    }
}
