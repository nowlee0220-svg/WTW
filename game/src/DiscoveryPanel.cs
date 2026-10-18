using Godot;
using Wtw.Sim.Runtime;
using Wtw.Sim.Time;

namespace Wtw.Game;

/// <summary>
/// (E4) 발견 목록 창(아래 막대의 [발견]): 이 세계에서 처음 나온 것들 — 무엇 · 누가 · 언제 · 어디서 · 왜(어떤 바람에서) · 그 전의 부분 성공 · 지금 아는 사람 수 · 그 말의 낱말.
/// 이름을 누르면 그 사람(그 땅)으로. 아래에 "아직 거의 될 듯하기만 한 것"(부분 성공만 있는 것). 오른쪽 한 칸(다른 창과 겹치지 않게).
/// </summary>
public partial class DiscoveryPanel : PanelContainer
{
    public const int Width = 520;
    public event Action<int, int>? PersonPicked; // 사람, 칸
    public event Action? CloseRequested;
    private VBoxContainer _rows = null!;
    private Label _title = null!;
    private long _sig = -1;

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
        var head = new HBoxContainer();
        box.AddChild(head);
        _title = PeopleUi.L("발견", UiTheme.FontSizeBig, UiTheme.Accent, bold: true);
        _title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(_title);
        var close = new Button { Text = "닫기", FocusMode = FocusModeEnum.None, TooltipText = "Esc" };
        close.Pressed += () => CloseRequested?.Invoke();
        head.AddChild(close);
        box.AddChild(PeopleUi.L("이 세계에서 처음 나온 것들. 사람들이 추위 · 배고픔 같은 \"~가 있었으면\" 때문에 재료를 이것저것 다뤄 보다 알아냈다. 아는 사람이 모두 죽으면 사라진다(다시 알아내야 한다).", UiTheme.FontSizeSmall, UiTheme.TextDim, wrap: true));
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill };
        box.AddChild(scroll);
        _rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _rows.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_rows);
    }

    public void Fill(WorldSnapshot snap)
    {
        long sig = snap.Discoveries.Length * 1000L + snap.Discoveries.Sum(d => d.Knowers * 7 + (d.Lost ? 3 : 0) + d.RefoundCount * 11) + snap.PendingPartials.Sum(p => p.Count);
        if (sig == _sig) return;
        _sig = sig;
        foreach (var n in _rows.GetChildren()) { _rows.RemoveChild(n); n.QueueFree(); }
        _title.Text = $"발견 — {snap.Discoveries.Length}가지";
        if (snap.Discoveries.Length == 0)
            _rows.AddChild(PeopleUi.L("아직 아무것도 발견되지 않았습니다. 사람들은 아쉬운 것이 생기면(추위 · 배탈 · 딱딱한 열매 · 먼 물가 …) 들고 있는 것과 둘레의 것으로 실험합니다.", UiTheme.FontSizeSmall + 1, UiTheme.TextDim, wrap: true));
        foreach (var d in snap.Discoveries)
        {
            var card = new PanelContainer();
            card.AddThemeStyleboxOverride("panel", UiTheme.Box(d.Lost ? new Color(0.16f, 0.12f, 0.12f, 0.95f) : new Color(0.15f, 0.15f, 0.17f, 0.95f), 6, 10, 8));
            var v = new VBoxContainer();
            v.AddThemeConstantOverride("separation", 2);
            card.AddChild(v);
            var top = new HBoxContainer();
            v.AddChild(top);
            top.AddChild(PeopleUi.L(d.Item, UiTheme.FontSizeSmall + 4, d.Lost ? new Color(0.75f, 0.55f, 0.5f) : UiTheme.Accent, bold: true));
            if (d.Word.Length > 0) top.AddChild(PeopleUi.L($"'{d.Word}'(그 말로 {d.WordMeaning})", UiTheme.FontSizeSmall, UiTheme.TextDim));
            top.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
            top.AddChild(PeopleUi.L(d.Lost ? $"사라짐 — 아는 사람 0명" : $"아는 사람 {d.Knowers}명", UiTheme.FontSizeSmall + 1, d.Lost ? new Color(1f, 0.5f, 0.45f) : new Color(0.6f, 0.9f, 0.55f), bold: true));
            v.AddChild(PeopleUi.L($"어떻게: {d.RuleName}" + (d.Heat.Length > 0 ? $" ({d.Heat}에서)" : ""), UiTheme.FontSizeSmall + 1, null, wrap: true));
            var who = new HBoxContainer();
            v.AddChild(who);
            who.AddChild(PeopleUi.L("누가:", UiTheme.FontSizeSmall + 1, UiTheme.TextDim));
            var b = new Button { Text = d.PersonName, FocusMode = FocusModeEnum.None, Flat = true, TooltipText = "눌러서 그 사람 · 그 땅으로" };
            b.AddThemeColorOverride("font_color", UiTheme.Accent.Lightened(0.2f));
            int pid = d.Person, cell = d.Cell;
            b.Pressed += () => PersonPicked?.Invoke(pid, cell);
            who.AddChild(b);
            who.AddChild(PeopleUi.L($"· {GameTime.Format(d.Tick)} · {d.Place}", UiTheme.FontSizeSmall, UiTheme.TextDim));
            v.AddChild(PeopleUi.L($"왜: {Wtw.Sim.People.Hangul.I(d.Wish)} 아쉬워서", UiTheme.FontSizeSmall + 1, null, wrap: true));
            if (d.PartialsBefore > 0)
                v.AddChild(PeopleUi.L($"그 전에 거의 될 듯한 일 {d.PartialsBefore}번: " + string.Join(" / ", d.PartialTexts), UiTheme.FontSizeSmall, UiTheme.TextDim, wrap: true));
            if (d.LostCount > 0) v.AddChild(PeopleUi.L($"사라진 적 {d.LostCount}번 · 다시 찾아냄 {d.RefoundCount}번", UiTheme.FontSizeSmall, new Color(1f, 0.7f, 0.5f)));
            _rows.AddChild(card);
        }
        if (snap.PendingPartials.Length > 0)
        {
            _rows.AddChild(new HSeparator());
            _rows.AddChild(PeopleUi.L("아직 되지 않은 것 — 거의 될 듯했던 일(다음 실험의 단서)", UiTheme.FontSizeSmall + 2, UiTheme.Accent, bold: true, wrap: true));
            foreach (var (rule, count, texts) in snap.PendingPartials)
                _rows.AddChild(PeopleUi.L($"• {rule} — {count}번: {string.Join(" / ", texts)}", UiTheme.FontSizeSmall, UiTheme.TextDim, wrap: true));
        }
    }
}
