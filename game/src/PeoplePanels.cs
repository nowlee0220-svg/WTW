using Godot;
using Wtw.Sim.People;
using Wtw.Sim.Runtime;
using Wtw.Sim.Time;

namespace Wtw.Game;

internal static class PeopleUi
{
    public static Label L(string text, int size = UiTheme.FontSizeSmall + 1, Color? color = null, bool bold = false, bool wrap = false)
    {
        var l = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", size);
        if (color is { } c) l.AddThemeColorOverride("font_color", c);
        if (bold) l.AddThemeFontOverride("font", UiTheme.Bold);
        if (wrap) { l.AutowrapMode = TextServer.AutowrapMode.WordSmart; l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; }
        return l;
    }

    public static Color MoodColor(int m) => m >= 55 ? new Color(0.55f, 0.9f, 0.5f) : m >= 40 ? new Color(0.9f, 0.88f, 0.6f) : m >= 25 ? new Color(0.95f, 0.65f, 0.35f) : new Color(1f, 0.4f, 0.35f);

    /// <summary>얼굴 그림(정면 그림의 위쪽).</summary>
    public static TextureRect Face(Appearance look, bool female, LifeStage stage, float px)
    {
        var tex = Art.Person(look, female, stage, Art.Facing.Front);
        float top = stage switch { LifeStage.Baby => 54, LifeStage.Child => 30, _ => 4 };
        var at = new AtlasTexture { Atlas = tex, Region = new Rect2(8, top, 80, 80) };
        return new TextureRect { Texture = at, CustomMinimumSize = new Vector2(px, px), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = Control.MouseFilterEnum.Ignore, Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.PremultAlpha } };
    }

    public static Control Bar(int value, int max, Color c, float w = 150)
    {
        var bar = new ProgressBar { MinValue = 0, MaxValue = max, Value = value, ShowPercentage = false, CustomMinimumSize = new Vector2(w, 14), MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        bar.AddThemeStyleboxOverride("background", UiTheme.Box(new Color(0.2f, 0.2f, 0.22f), 3, 0, 0));
        bar.AddThemeStyleboxOverride("fill", UiTheme.Box(c, 3, 0, 0));
        return bar;
    }
}

/// <summary>사람에 마우스를 올리면 나오는 작은 카드(얼굴 · 이름 · 나이 · 지금 하는 일 · 기분).</summary>
public partial class PersonHoverCard : PanelContainer
{
    private HBoxContainer _row = null!;
    public int PersonId { get; private set; } = -1;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.08f, 0.08f, 0.09f, 0.94f), 6, 10, 8));
        _row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_row);
    }

    public void Show(PersonCard c, Vector2 mouse)
    {
        PersonId = c.Id;
        foreach (var n in _row.GetChildren()) { _row.RemoveChild(n); n.QueueFree(); }
        _row.AddChild(PeopleUi.Face(c.Look, c.Female, c.Stage, 64));
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 1);
        _row.AddChild(box);
        box.AddChild(PeopleUi.L($"{c.Name}  ·  {c.StageName} {c.Age}살", UiTheme.FontSizeSmall + 3, UiTheme.Accent, bold: true));
        box.AddChild(PeopleUi.L(c.Unnamed ? "아직 이름이 없다 — 생김새로 부른다" : $"이름의 뜻: {c.NameMeaning}", UiTheme.FontSizeSmall - 1, UiTheme.TextDim));
        box.AddChild(PeopleUi.L(c.Activity));
        if (c.Alive) box.AddChild(PeopleUi.L($"기분 {c.Mood} ({c.MoodWord})", UiTheme.FontSizeSmall, PeopleUi.MoodColor(c.Mood)));
        box.AddChild(PeopleUi.L("누르면 큰 카드", UiTheme.FontSizeSmall - 1, UiTheme.TextDim));
        Visible = true;
        ResetSize();
        Follow(mouse);
    }

    public void Hide2() { Visible = false; PersonId = -1; }

    public void Follow(Vector2 mouse)
    {
        if (!Visible) return;
        var vp = GetViewportRect().Size;
        var pos = mouse + new Vector2(22, 24);
        if (pos.X + Size.X > vp.X - 8) pos.X = mouse.X - Size.X - 14;
        if (pos.Y + Size.Y > vp.Y - UiTheme.BottomBarHeight - 8) pos.Y = mouse.Y - Size.Y - 14;
        Position = pos;
    }
}

/// <summary>
/// 인물 큰 카드(기획서 7-6): 슬롯 7개 — 개요 · 능력 · 몸 · 마음 · 관계 · 지식 · 이야기. 오른쪽 한 칸(다른 창과 겹치지 않게), 한 번에 하나.
/// [따라가기] → 카메라가 그 사람을 따라간다. 내용은 시뮬이 만든 카드(PersonCard)를 그대로 보여 준다.
/// </summary>
public partial class PersonCardPanel : PanelContainer
{
    public const int Width = 440;
    public event Action? CloseRequested;
    public event Action<int>? FollowRequested;
    public event Action<int>? PersonRequested;

    private TabContainer _tabs = null!;
    private readonly VBoxContainer[] _pages = new VBoxContainer[7];
    private HBoxContainer _head = null!;
    private Button _follow = null!;
    public int PersonId { get; private set; } = -1;
    public static readonly string[] TabNames = { "개요", "능력", "몸", "마음", "관계", "지식", "이야기" };

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;
        AnchorLeft = 1; AnchorRight = 1; AnchorTop = 0; AnchorBottom = 1;
        OffsetLeft = -Width - 12; OffsetRight = -12;
        OffsetTop = UiTheme.TopBarHeight + 12; OffsetBottom = -UiTheme.BottomBarHeight - 12;
        AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Panel, 8, 14, 12));
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        AddChild(box);
        _head = new HBoxContainer();
        box.AddChild(_head);
        var buttons = new HBoxContainer();
        box.AddChild(buttons);
        _follow = new Button { Text = "따라가기", FocusMode = FocusModeEnum.None, TooltipText = "카메라가 이 사람을 따라간다 (끌면 멈춤)", ToggleMode = true };
        _follow.Pressed += () => FollowRequested?.Invoke(_follow.ButtonPressed ? PersonId : -1);
        buttons.AddChild(_follow);
        buttons.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var close = new Button { Text = "닫기", FocusMode = FocusModeEnum.None, TooltipText = "Esc" };
        close.Pressed += () => CloseRequested?.Invoke();
        buttons.AddChild(close);
        _tabs = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        _tabs.AddThemeFontSizeOverride("font_size", UiTheme.FontSizeSmall + 1);
        box.AddChild(_tabs);
        for (int k = 0; k < 7; k++)
        {
            var scroll = new ScrollContainer { Name = TabNames[k], HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            var page = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            page.AddThemeConstantOverride("separation", 6);
            scroll.AddChild(page);
            _tabs.AddChild(scroll);
            _pages[k] = page;
        }
    }

    public void SetFollowing(bool on) => _follow.SetPressedNoSignal(on);
    public void SetTab(int k) => _tabs.CurrentTab = Math.Clamp(k, 0, 6);
    public void Open(int id) { PersonId = id; Visible = true; }
    public void Close() { PersonId = -1; Visible = false; }

    private static void Clear(Container c) { foreach (var n in c.GetChildren()) { c.RemoveChild(n); n.QueueFree(); } }

    public void Fill(PersonCard c)
    {
        if (c.Id != PersonId) return;
        Clear(_head);
        _head.AddChild(PeopleUi.Face(c.Look, c.Female, c.Stage, 88));
        var hb = new VBoxContainer();
        _head.AddChild(hb);
        hb.AddChild(PeopleUi.L(c.Name, UiTheme.FontSizeBig + 2, UiTheme.Accent, bold: true, wrap: true));
        if (c.Nickname.Length > 0) hb.AddChild(PeopleUi.L($"별명 '{c.Nickname}' — 뜻: {c.NicknameMeaning} ({c.NicknameReason})", UiTheme.FontSizeSmall, new Color(1f, 0.85f, 0.45f), wrap: true));
        hb.AddChild(PeopleUi.L(c.Unnamed ? "아직 이름이 없다 — 생김새로 부른다(함께 지내는 사람이 생기면 그 말로 이름을 지어 준다)" : $"이름의 뜻: {c.NameMeaning}", UiTheme.FontSizeSmall - 1, UiTheme.TextDim, wrap: true));
        hb.AddChild(PeopleUi.L($"{c.StageName} · {c.Age}살 · {c.Place}", UiTheme.FontSizeSmall + 1, UiTheme.TextDim));
        hb.AddChild(c.Alive ? PeopleUi.L($"기분 {c.Mood} — {c.MoodWord}", UiTheme.FontSizeSmall + 2, PeopleUi.MoodColor(c.Mood), bold: true) : PeopleUi.L("죽음", UiTheme.FontSizeSmall + 2, new Color(0.7f, 0.7f, 0.7f), bold: true));
        _follow.Disabled = !c.Alive;

        // ① 개요
        var p = _pages[0]; Clear(p);
        p.AddChild(PeopleUi.L("지금 하는 일", UiTheme.FontSizeSmall, UiTheme.TextDim));
        p.AddChild(PeopleUi.L(c.Activity, UiTheme.FontSizeSmall + 4, null, bold: true, wrap: true));
        if (c.Why.Length > 0)
        {
            p.AddChild(PeopleUi.L("왜", UiTheme.FontSizeSmall, UiTheme.TextDim));
            p.AddChild(PeopleUi.L(c.Why, UiTheme.FontSizeSmall + 2, UiTheme.Accent.Lightened(0.2f), wrap: true));
        }
        p.AddChild(new HSeparator());
        if (c.Alive)
        {
            p.AddChild(PeopleUi.L($"기분 {c.Mood} ({c.MoodWord}) — 기분이 {c.BreakThreshold} 아래로 떨어지면 무너질 수 있다", wrap: true));
            p.AddChild(PeopleUi.Bar(c.Mood, 100, PeopleUi.MoodColor(c.Mood), 420));
        }
        p.AddChild(PeopleUi.L($"특성: {string.Join(" · ", c.Traits.Select(t => t.Name))}", wrap: true));
        p.AddChild(PeopleUi.L($"들고 있는 것: {c.Carry}", wrap: true));
        if (c.Speech.Length > 0) p.AddChild(PeopleUi.L($"말: {c.Speech}", UiTheme.FontSizeSmall, UiTheme.TextDim, wrap: true));
        foreach (var r in c.Relations.Where(r => r.Kind is "짝" or "어머니" or "아버지").Take(3)) p.AddChild(PeopleUi.L($"{r.Kind}: {r.Name}{(r.Alive ? "" : " (죽음)")}", wrap: true));

        // ② 능력
        p = _pages[1]; Clear(p);
        p.AddChild(PeopleUi.L("타고난 능력 (1~10)", UiTheme.FontSizeSmall + 1, UiTheme.Accent, bold: true));
        foreach (var (name, v, note) in c.Abilities)
        {
            var row = new HBoxContainer();
            row.AddChild(PeopleUi.L(name, UiTheme.FontSizeSmall + 1, null, bold: true)); row.GetChild<Label>(0).CustomMinimumSize = new Vector2(72, 0);
            row.AddChild(PeopleUi.L($"{v}", UiTheme.FontSizeSmall + 2, UiTheme.Accent, bold: true)); row.GetChild<Label>(1).CustomMinimumSize = new Vector2(28, 0);
            row.AddChild(PeopleUi.Bar(v, 10, new Color(0.55f, 0.7f, 0.95f), 120));
            row.AddChild(PeopleUi.L(note, UiTheme.FontSizeSmall, UiTheme.TextDim, wrap: true));
            p.AddChild(row);
        }
        p.AddChild(new HSeparator());
        p.AddChild(PeopleUi.L("기술 (0~10, 손에 익은 솜씨 — 아는 것과 다르다) · ★ 열정", UiTheme.FontSizeSmall + 1, UiTheme.Accent, bold: true, wrap: true));
        foreach (var (name, lv, prog, pas) in c.Skills)
        {
            var row = new HBoxContainer();
            row.AddChild(PeopleUi.L(name + (pas == 2 ? " ★★" : pas == 1 ? " ★" : ""), UiTheme.FontSizeSmall + 1, pas > 0 ? new Color(1f, 0.6f, 0.3f) : null)); row.GetChild<Label>(0).CustomMinimumSize = new Vector2(120, 0);
            row.AddChild(PeopleUi.L($"{lv}", UiTheme.FontSizeSmall + 2, UiTheme.Accent, bold: true)); row.GetChild<Label>(1).CustomMinimumSize = new Vector2(28, 0);
            row.AddChild(PeopleUi.Bar(lv * 10 + prog / 10, 100, new Color(0.6f, 0.85f, 0.55f), 160));
            p.AddChild(row);
        }
        p.AddChild(PeopleUi.L("쓰이는 기술: 채집 · 제작(돌 · 끈 · 그릇) · 요리 · 실험(불 · 숯 · 구리) · 건설(화덕). 사냥 · 농사 · 가르치기는 다음 단계.", UiTheme.FontSizeSmall, UiTheme.TextDim, wrap: true));
        p.AddChild(new HSeparator());
        p.AddChild(PeopleUi.L("특성 (마우스를 올리면 효과)", UiTheme.FontSizeSmall + 1, UiTheme.Accent, bold: true));
        foreach (var (name, eff) in c.Traits)
        {
            var b = new Button { Text = name, TooltipText = eff, FocusMode = FocusModeEnum.None, Flat = true, Alignment = HorizontalAlignment.Left };
            b.AddThemeFontSizeOverride("font_size", UiTheme.FontSizeSmall + 2);
            p.AddChild(b);
            p.AddChild(PeopleUi.L("  " + eff, UiTheme.FontSizeSmall, UiTheme.TextDim, wrap: true));
        }

        // ③ 몸
        p = _pages[2]; Clear(p);
        foreach (var line in c.Body) p.AddChild(PeopleUi.L(line, UiTheme.FontSizeSmall + 1, null, wrap: true));
        p.AddChild(new HSeparator());
        p.AddChild(PeopleUi.L("욕구", UiTheme.FontSizeSmall + 1, UiTheme.Accent, bold: true));
        foreach (var (name, v, note) in c.Needs)
        {
            var row = new HBoxContainer();
            row.AddChild(PeopleUi.L(name, UiTheme.FontSizeSmall + 1)); row.GetChild<Label>(0).CustomMinimumSize = new Vector2(72, 0);
            row.AddChild(PeopleUi.Bar(v, 100, v < 20 ? new Color(0.95f, 0.4f, 0.35f) : v < 40 ? new Color(0.95f, 0.75f, 0.35f) : new Color(0.5f, 0.8f, 0.5f), 150));
            row.AddChild(PeopleUi.L($"{v}% {note}", UiTheme.FontSizeSmall, UiTheme.TextDim, wrap: true));
            p.AddChild(row);
        }

        // ④ 마음
        p = _pages[3]; Clear(p);
        if (c.Alive)
        {
            p.AddChild(PeopleUi.L($"기분 {c.Mood} = 아래 생각들을 모두 더한 것 (0~100)", UiTheme.FontSizeSmall + 1, UiTheme.Accent, bold: true, wrap: true));
            foreach (var (name, mood, note) in c.Thoughts)
            {
                var row = new HBoxContainer();
                row.AddChild(PeopleUi.L($"{mood:+0;−0;0}", UiTheme.FontSizeSmall + 2, mood > 0 ? new Color(0.55f, 0.9f, 0.5f) : mood < 0 ? new Color(1f, 0.5f, 0.45f) : UiTheme.TextDim, bold: true)); row.GetChild<Label>(0).CustomMinimumSize = new Vector2(44, 0);
                row.AddChild(PeopleUi.L(name, UiTheme.FontSizeSmall + 1)); row.GetChild<Label>(1).CustomMinimumSize = new Vector2(170, 0);
                row.AddChild(PeopleUi.L(note, UiTheme.FontSizeSmall, UiTheme.TextDim, wrap: true));
                p.AddChild(row);
            }
            p.AddChild(PeopleUi.L($"기분이 {c.BreakThreshold} 아래면 무너질 수 있다(울며 헤매기 · 화풀이 · 혼자 떠나기).", UiTheme.FontSizeSmall, UiTheme.TextDim, wrap: true));
        }
        else p.AddChild(PeopleUi.L("죽은 사람은 생각이 없다.", UiTheme.FontSizeSmall + 1, UiTheme.TextDim));

        // ⑤ 관계
        p = _pages[4]; Clear(p);
        if (c.Relations.Count == 0) p.AddChild(PeopleUi.L("아직 아는 사람이 없다.", UiTheme.FontSizeSmall + 1, UiTheme.TextDim));
        foreach (var r in c.Relations)
        {
            var row = new HBoxContainer();
            var b = new Button { Text = r.Name + (r.Alive ? "" : " †"), FocusMode = FocusModeEnum.None, TooltipText = "눌러서 이 사람의 카드", CustomMinimumSize = new Vector2(120, 0) };
            int id = r.Id;
            b.Pressed += () => PersonRequested?.Invoke(id);
            row.AddChild(b);
            row.AddChild(PeopleUi.L(r.Kind, UiTheme.FontSizeSmall + 1, UiTheme.Accent)); row.GetChild<Label>(1).CustomMinimumSize = new Vector2(70, 0);
            row.AddChild(PeopleUi.L($"호감 {r.Opinion:+0;−0;0}" + (r.Grudge > 0 ? $" · 원한 {r.Grudge}" : "") + (r.Favor > 0 ? $" · 은혜 {r.Favor}" : "") + $" · 알아듣기 {r.Understand}%", UiTheme.FontSizeSmall, r.Opinion >= 30 ? new Color(0.6f, 0.9f, 0.55f) : r.Opinion <= -20 || r.Grudge >= 20 ? new Color(1f, 0.55f, 0.45f) : UiTheme.TextDim, wrap: true));
            p.AddChild(row);
        }

        // ⑥ 지식 (E4: 아는 것 · 바람 · 단서 · 새 낱말)
        p = _pages[5]; Clear(p);
        if (c.Wishes.Count > 0)
        {
            p.AddChild(PeopleUi.L("바람 — \"~가 있었으면\" (이것 때문에 실험한다)", UiTheme.FontSizeSmall + 1, UiTheme.Accent, bold: true, wrap: true));
            foreach (var (name, v) in c.Wishes.OrderByDescending(x => x.Strength))
            {
                var row = new HBoxContainer();
                row.AddChild(PeopleUi.L(name, UiTheme.FontSizeSmall + 1)); row.GetChild<Label>(0).CustomMinimumSize = new Vector2(110, 0);
                row.AddChild(PeopleUi.Bar(v, 100, new Color(0.95f, 0.72f, 0.35f), 200));
                row.AddChild(PeopleUi.L($"{v}", UiTheme.FontSizeSmall, UiTheme.TextDim));
                p.AddChild(row);
            }
            p.AddChild(new HSeparator());
        }
        p.AddChild(PeopleUi.L($"아는 것 ({c.Knowledge.Count}가지) — 무엇을 · 어떻게 · 언제 · 왜", UiTheme.FontSizeSmall + 1, UiTheme.Accent, bold: true, wrap: true));
        if (c.Knowledge.Count == 0) p.AddChild(PeopleUi.L("아직 아는 것이 없다. 뿌려진 사람은 아무것도 모른다 — 아쉬운 것이 생기면 실험하고, 곁에서 본 것은 따라 할 수 있다.", UiTheme.FontSizeSmall, UiTheme.TextDim, wrap: true));
        foreach (var k in c.Knowledge) p.AddChild(PeopleUi.L("• " + k, UiTheme.FontSizeSmall + 1, null, wrap: true));
        if (c.Clues.Count > 0)
        {
            p.AddChild(new HSeparator());
            p.AddChild(PeopleUi.L("단서 — 거의 될 듯했던 일(다음 실험이 따라간다)", UiTheme.FontSizeSmall + 1, UiTheme.Accent, bold: true, wrap: true));
            foreach (var k in c.Clues.Take(8)) p.AddChild(PeopleUi.L("• " + k, UiTheme.FontSizeSmall, UiTheme.TextDim, wrap: true));
        }
        if (c.NewWords.Count > 0)
        {
            p.AddChild(new HSeparator());
            p.AddChild(PeopleUi.L("새로 생긴 낱말 — 어디서 왔나", UiTheme.FontSizeSmall + 1, UiTheme.Accent, bold: true, wrap: true));
            foreach (var (word, meaning, origin) in c.NewWords) p.AddChild(PeopleUi.L($"'{word}' = {meaning}" + (origin.Length > 0 ? $" — {origin}" : ""), UiTheme.FontSizeSmall + 1, null, wrap: true));
        }
        p.AddChild(new HSeparator());
        p.AddChild(PeopleUi.L("아는 낱말 (이 사람의 말 — 왼쪽이 그 말, 오른쪽이 뜻)", UiTheme.FontSizeSmall + 1, UiTheme.Accent, bold: true, wrap: true));
        if (c.Speech.Length > 0) p.AddChild(PeopleUi.L(c.Speech, UiTheme.FontSizeSmall, UiTheme.TextDim, wrap: true));
        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 12);
        foreach (var (word, meaning) in c.Vocabulary)
        {
            grid.AddChild(PeopleUi.L(word, UiTheme.FontSizeSmall + 1, null, bold: true));
            grid.AddChild(PeopleUi.L(meaning, UiTheme.FontSizeSmall, UiTheme.TextDim));
        }
        p.AddChild(grid);
        p.AddChild(PeopleUi.L("문법은 없고 소리와 낱말만 있다. 가르치기 · 잊기 · 기록은 다음 단계(지식)에서.", UiTheme.FontSizeSmall - 1, UiTheme.TextDim, wrap: true));

        // ⑦ 이야기
        p = _pages[6]; Clear(p);
        foreach (var line in c.Story.AsEnumerable().Reverse().Take(60)) p.AddChild(PeopleUi.L(line, UiTheme.FontSizeSmall + 1, null, wrap: true));
        if (c.Story.Count > 60) p.AddChild(PeopleUi.L($"… 그 앞으로 {c.Story.Count - 60}줄 더", UiTheme.FontSizeSmall, UiTheme.TextDim));
    }
}

/// <summary>지방 화면의 사람 목록(이름 · 나이 · 지금 하는 일). 누르면 그 사람으로. 제목을 누르면 접힌다.</summary>
public partial class PeopleListPanel : PanelContainer
{
    public const int Width = 360;
    public event Action<int>? PersonPicked;
    private Button _head = null!;
    private ScrollContainer _scroll = null!;
    private VBoxContainer _rows = null!;
    private bool _collapsed = true; // 처음엔 접힌 채로(제목만)
    private string _sig = "";

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;
        AnchorLeft = 1; AnchorRight = 1; AnchorTop = 0; AnchorBottom = 0;
        OffsetLeft = -Width - 12; OffsetRight = -12;
        OffsetTop = UiTheme.TopBarHeight + 12; OffsetBottom = UiTheme.TopBarHeight + 12 + 380;
        AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Panel, 8, 12, 8));
        var box = new VBoxContainer();
        AddChild(box);
        _head = new Button { Flat = true, FocusMode = FocusModeEnum.None, Alignment = HorizontalAlignment.Left, TooltipText = "눌러서 접기 · 펼치기" };
        _head.AddThemeFontOverride("font", UiTheme.Bold);
        _head.AddThemeColorOverride("font_color", UiTheme.Accent);
        _head.Pressed += () => { _collapsed = !_collapsed; _scroll.Visible = !_collapsed; OffsetBottom = OffsetTop + (_collapsed ? 50 : 380); };
        box.AddChild(_head);
        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill, Visible = false };
        box.AddChild(_scroll);
        _rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _rows.AddThemeConstantOverride("separation", 2);
        _scroll.AddChild(_rows);
        OffsetBottom = OffsetTop + 50;
    }

    public void Fill(List<PersonRow> rows)
    {
        var alive = rows.Where(r => r.Alive).ToList();
        int dead = rows.Count - alive.Count;
        _head.Text = (_collapsed ? "▶ " : "▼ ") + $"사람 {alive.Count}명" + (dead > 0 ? $" (죽음 {dead})" : "");
        string sig = string.Join("|", alive.Select(r => $"{r.Id}:{r.Activity}:{r.Mood / 5}:{r.Age}"));
        if (sig == _sig) return;
        _sig = sig;
        foreach (var n in _rows.GetChildren()) { _rows.RemoveChild(n); n.QueueFree(); }
        if (alive.Count == 0) _rows.AddChild(PeopleUi.L("아무도 없습니다. 아래 [사람 뿌리기] · [가족 뿌리기]를 눌러 땅을 누르세요.", UiTheme.FontSizeSmall + 1, UiTheme.TextDim, wrap: true));
        foreach (var r in alive)
        {
            var b = new Button { FocusMode = FocusModeEnum.None, Alignment = HorizontalAlignment.Left, Flat = true, ClipText = true, CustomMinimumSize = new Vector2(Width - 40, 0) };
            b.Text = $"{r.Name} · {PeopleText.StageName(r.Stage, r.Female)} {r.Age} — {r.Activity}";
            b.AddThemeFontSizeOverride("font_size", UiTheme.FontSizeSmall + 1);
            b.AddThemeColorOverride("font_color", PeopleUi.MoodColor(r.Mood).Lerp(UiTheme.Text, 0.55f));
            b.TooltipText = $"기분 {r.Mood}";
            int id = r.Id;
            b.Pressed += () => PersonPicked?.Invoke(id);
            _rows.AddChild(b);
        }
    }
}

/// <summary>알림(아이가 태어남 · 죽음 · 짝 · 정신 붕괴): 오른쪽 아래에 쌓인다. 누르면 그 사람으로.</summary>
public partial class AlertsPanel : VBoxContainer
{
    public event Action<int, int>? AlertPicked; // 사람, 칸
    private long _seenSeq;
    private bool _primed;
    private readonly List<(Button B, double Born)> _items = new();
    public float RightMargin = 12;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeConstantOverride("separation", 4);
        Alignment = AlignmentMode.End;
    }

    public void Update(WorldSnapshot snap, double now, Vector2 vp)
    {
        if (!_primed) { _primed = true; _seenSeq = snap.RecentEvents.Length > 0 ? snap.RecentEvents[^1].Seq : 0; }
        foreach (var e in snap.RecentEvents)
        {
            if (e.Seq <= _seenSeq) continue;
            _seenSeq = e.Seq;
            if (e.Kind == WorldEventKind.Spawn) continue;
            var b = new Button { Text = $"{Icon(e.Kind)} {e.Text}  · {GameTime.Format(e.Tick)}", FocusMode = FocusModeEnum.None, Alignment = HorizontalAlignment.Left, TooltipText = "눌러서 그 사람으로" };
            b.AddThemeFontSizeOverride("font_size", UiTheme.FontSizeSmall + 1);
            b.AddThemeColorOverride("font_color", e.Kind switch { WorldEventKind.Death => new Color(1f, 0.6f, 0.55f), WorldEventKind.Birth => new Color(0.6f, 0.95f, 0.6f), WorldEventKind.Pair => new Color(1f, 0.75f, 0.85f), WorldEventKind.Discovery => new Color(1f, 0.85f, 0.35f), WorldEventKind.DiscoveryLost => new Color(1f, 0.45f, 0.3f), WorldEventKind.Wildfire => new Color(1f, 0.6f, 0.3f), _ => new Color(0.75f, 0.8f, 1f) });
            if (e.Kind is WorldEventKind.Discovery or WorldEventKind.DiscoveryLost) b.AddThemeFontOverride("font", UiTheme.Bold);
            int pid = e.Kind == WorldEventKind.Birth ? e.Other : e.Person, cell = e.Cell;
            double life = e.Kind is WorldEventKind.Discovery or WorldEventKind.DiscoveryLost ? 90 : 40;
            b.Pressed += () => AlertPicked?.Invoke(pid, cell);
            AddChild(b);
            _items.Add((b, now + life - 40)); // 발견 알림은 더 오래 남는다
        }
        while (_items.Count > 6) { var (b, _) = _items[0]; _items.RemoveAt(0); RemoveChild(b); b.QueueFree(); }
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            var (b, born) = _items[i];
            double age = now - born;
            if (age > 40) { _items.RemoveAt(i); RemoveChild(b); b.QueueFree(); continue; }
            b.Modulate = new Color(1, 1, 1, (float)Math.Clamp((40 - age) / 5, 0.25, 1));
        }
        ResetSize();
        Position = new Vector2(vp.X - Size.X - RightMargin, vp.Y - UiTheme.BottomBarHeight - 12 - Size.Y);
    }

    public void Reset() { _primed = false; foreach (var (b, _) in _items) { RemoveChild(b); b.QueueFree(); } _items.Clear(); }

    private static string Icon(WorldEventKind k) => k switch { WorldEventKind.Birth => "＋", WorldEventKind.Death => "†", WorldEventKind.Pair => "♥", WorldEventKind.Break => "!", WorldEventKind.Discovery => "★", WorldEventKind.DiscoveryLost => "✕", WorldEventKind.Wildfire => "▲", _ => "·" };
}
