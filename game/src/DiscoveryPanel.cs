using Godot;
using Wtw.Sim.Runtime;
using Wtw.Sim.Time;

namespace Wtw.Game;

/// <summary>
/// (E4) 발견 목록 창(아래 막대의 [발견]): 이 세계에서 처음 나온 것들 — 무엇 · 누가 · 언제 · 어디서 · 왜(어떤 바람에서) · 그 전의 부분 성공 · 지금 아는 사람 수 · 그 말의 낱말.
/// 이름을 누르면 그 사람(그 땅)으로. 아래에 "아직 거의 될 듯하기만 한 것"(부분 성공만 있는 것). 오른쪽 한 칸(다른 창과 겹치지 않게).
/// (E5) "지식의 행방" 백과: 발견마다 [자세히]를 펼치면 지금 누가 아나(무리 · 아는 정도) · 어디에 남았나(그림) · 잃음과 되찾음의 역사 · 낱말과 빌려 온 곳 · [지식 지도로].
/// </summary>
public partial class DiscoveryPanel : PanelContainer
{
    public const int Width = 520;
    public event Action<int, int>? PersonPicked; // 사람, 칸
    public event Action? CloseRequested;
    /// <summary>(E5) 지구본 [지식] 지도로 그 발견을 본다(규칙 번호).</summary>
    public event Action<int>? MapRequested;
    private readonly HashSet<int> _open = new();
    private VBoxContainer _rows = null!;
    private Label _title = null!;
    private long _sig = -1;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;
        AnchorLeft = 1; AnchorRight = 1; AnchorTop = 0; AnchorBottom = 1;
        OffsetLeft = -Width - 12; OffsetRight = -12;
        OffsetTop = UiTheme.TopBarHeight + 72; OffsetBottom = -UiTheme.BottomBarHeight - 12; // (E5) 지방 단추 줄 아래(겹치지 않게)
        AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Panel, 8, 14, 12));
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        AddChild(box);
        var head = new HBoxContainer();
        box.AddChild(head);
        _title = PeopleUi.L("지식의 행방", UiTheme.FontSizeBig, UiTheme.Accent, bold: true);
        _title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(_title);
        var close = new Button { Text = "닫기", FocusMode = FocusModeEnum.None, TooltipText = "Esc" };
        close.Pressed += () => CloseRequested?.Invoke();
        head.AddChild(close);
        box.AddChild(PeopleUi.L("이 세계에서 처음 나온 것들. 아는 법은 사람 머릿속에만 있다 — 가르치고, 보고 배우고, 안 쓰면 잊고, 그림으로 남긴다. 아는 사람이 모두 죽으면 사라지고, 남은 그림이나 이웃을 보고 되찾는다. [자세히]를 누르면 지금 누가 아는지 · 어디에 남았는지 · 잃음과 되찾음의 역사.", UiTheme.FontSizeSmall, UiTheme.TextDim, wrap: true));
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill };
        box.AddChild(scroll);
        _rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _rows.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_rows);
    }

    public void Fill(WorldSnapshot snap)
    {
        long sig = snap.Discoveries.Length * 1000L + snap.Discoveries.Sum(d => d.Knowers * 7 + (d.Lost ? 3 : 0) + d.RefoundCount * 11 + d.History.Length * 13 + d.Paintings.Length * 17 + d.Groups * 19) + snap.PendingPartials.Sum(p => p.Count)
                   + _open.Sum(x => x * 31L + 1) * 7;
        if (sig == _sig) return;
        _sig = sig;
        foreach (var n in _rows.GetChildren()) { _rows.RemoveChild(n); n.QueueFree(); }
        _title.Text = $"지식의 행방 — 발견 {snap.Discoveries.Length}가지";
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
            // (E5) 요약 한 줄 + 펼치기
            var sum = new HBoxContainer();
            v.AddChild(sum);
            sum.AddChild(PeopleUi.L($"아는 무리 {d.Groups}곳" + (d.GroupsLost > 0 ? $" · 끊긴 무리 {d.GroupsLost}곳" : "") + $" · 그림 {d.Paintings.Length}점", UiTheme.FontSizeSmall, UiTheme.TextDim));
            sum.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
            bool open = _open.Contains(d.Rule);
            var more = new Button { Text = open ? "접기 ▲" : "자세히 ▼", FocusMode = FocusModeEnum.None, Flat = true };
            int rule = d.Rule;
            more.Pressed += () => { if (!_open.Remove(rule)) _open.Add(rule); _sig = -1; Fill(snap); };
            sum.AddChild(more);
            if (open) AddDetails(v, d);
            _rows.AddChild(card);
        }
        if (snap.Paintings.Count > 0)
        {
            int n = snap.Paintings.Values.Sum(l => l.Length);
            _rows.AddChild(new HSeparator());
            _rows.AddChild(PeopleUi.L($"남아 있는 그림 {n}점 — 무엇을 그렸나 · 어디에", UiTheme.FontSizeSmall + 2, UiTheme.Accent, bold: true, wrap: true));
            foreach (var g in snap.Paintings.Values.SelectMany(l => l).OrderBy(g => g.Tick).Take(30))
                _rows.AddChild(PeopleUi.L($"• {g.What} — {g.Place}의 {Wtw.Sim.People.Painting.MediumName(g.Medium)} · {g.Painter} · {GameTime.Format(g.Tick)} · 본 사람 {g.Seen}번 · 그림으로 배운 사람 {g.Taught}명", UiTheme.FontSizeSmall, UiTheme.TextDim, wrap: true));
        }
        if (snap.PendingPartials.Length > 0)
        {
            _rows.AddChild(new HSeparator());
            _rows.AddChild(PeopleUi.L("아직 되지 않은 것 — 거의 될 듯했던 일(다음 실험의 단서)", UiTheme.FontSizeSmall + 2, UiTheme.Accent, bold: true, wrap: true));
            foreach (var (rule, count, texts) in snap.PendingPartials)
                _rows.AddChild(PeopleUi.L($"• {rule} — {count}번: {string.Join(" / ", texts)}", UiTheme.FontSizeSmall, UiTheme.TextDim, wrap: true));
        }
    }

    /// <summary>(E5) 지식의 행방: 지금 누가 아나 · 어디에 남았나 · 잃음과 되찾음의 역사 · 낱말.</summary>
    private void AddDetails(VBoxContainer v, DiscoveryView d)
    {
        var dim = UiTheme.TextDim;
        v.AddChild(new HSeparator());
        if (d.Word.Length > 0) v.AddChild(PeopleUi.L($"낱말: '{d.Word}'(뜻: {d.WordMeaning}) — {d.WordOrigin}", UiTheme.FontSizeSmall, null, wrap: true));
        if (d.OtherWords.Length > 0) v.AddChild(PeopleUi.L("다른 말들의 낱말: " + string.Join(" · ", d.OtherWords), UiTheme.FontSizeSmall, dim, wrap: true));
        v.AddChild(PeopleUi.L($"지금 누가 아나 — {d.Knowers}명 · 무리 {d.Groups}곳", UiTheme.FontSizeSmall + 1, UiTheme.Accent, bold: true));
        if (d.People.Length == 0) v.AddChild(PeopleUi.L("아무도 모른다.", UiTheme.FontSizeSmall, dim));
        foreach (var k in d.People.Take(12))
        {
            var b = new Button { Text = $"{k.Name} ({k.Age}) — {k.Place} · {LevelWord(k.Level)} {k.Level} · {k.How}", FocusMode = FocusModeEnum.None, Flat = true, Alignment = HorizontalAlignment.Left, ClipText = true, TooltipText = "눌러서 그 사람 · 그 땅으로" };
            b.AddThemeFontSizeOverride("font_size", UiTheme.FontSizeSmall);
            b.AddThemeColorOverride("font_color", k.Level >= 60 ? new Color(0.75f, 0.92f, 0.7f) : new Color(0.9f, 0.78f, 0.6f));
            int pid = k.Id, cell = k.Cell;
            b.Pressed += () => PersonPicked?.Invoke(pid, cell);
            v.AddChild(b);
        }
        if (d.People.Length > 12) v.AddChild(PeopleUi.L($"… 그리고 {d.People.Length - 12}명", UiTheme.FontSizeSmall, dim));
        v.AddChild(PeopleUi.L("어디에 남았나 — 그림", UiTheme.FontSizeSmall + 1, UiTheme.Accent, bold: true));
        if (d.Paintings.Length == 0) v.AddChild(PeopleUi.L("그림으로 남은 곳이 없다. 아는 사람이 모두 죽으면 다시 알아내야 한다.", UiTheme.FontSizeSmall, dim, wrap: true));
        foreach (var t in d.Paintings) v.AddChild(PeopleUi.L("• " + t, UiTheme.FontSizeSmall, null, wrap: true));
        v.AddChild(PeopleUi.L("잃음과 되찾음의 역사", UiTheme.FontSizeSmall + 1, UiTheme.Accent, bold: true));
        foreach (var (tick, kind, text) in d.History)
        {
            var col = kind is 1 or 3 or 6 ? new Color(1f, 0.6f, 0.5f) : kind is 2 or 4 ? new Color(0.6f, 0.9f, 0.6f) : kind == 5 ? new Color(0.75f, 0.65f, 0.95f) : (Color?)null;
            v.AddChild(PeopleUi.L($"{GameTime.Format(tick)} — {text}", UiTheme.FontSizeSmall, col, wrap: true));
        }
        var map = new Button { Text = "지구본 [지식] 지도로 보기", FocusMode = FocusModeEnum.None };
        int rule = d.Rule;
        map.Pressed += () => MapRequested?.Invoke(rule);
        v.AddChild(map);
    }

    /// <summary>(자동 확인용) 역사가 가장 긴 발견을 펼친다.</summary>
    public void ExpandBest(WorldSnapshot snap)
    {
        var d = snap.Discoveries.OrderByDescending(x => x.History.Length + x.Paintings.Length * 3 + (x.Item == "모닥불" ? 2 : 0)).FirstOrDefault();
        if (d is null) return;
        _open.Add(d.Rule); _sig = -1; Fill(snap);
        _scrollTo = Array.IndexOf(snap.Discoveries, d);
    }

    private int _scrollTo = -1, _scrollWait;

    public override void _Process(double delta)
    {
        // (자동 확인용) 펼친 카드가 보이게 — 배치가 끝난 뒤(몇 프레임 뒤) 스크롤
        if (_scrollTo < 0 || ++_scrollWait < 4) return;
        if (_rows.GetParent() is ScrollContainer sc && _scrollTo < _rows.GetChildCount() && _rows.GetChild(_scrollTo) is Control c) sc.ScrollVertical = (int)c.Position.Y;
        _scrollTo = -1;
    }

    private static string LevelWord(int level) => level >= 80 ? "잘 앎" : level >= 60 ? "앎" : level >= 35 ? "흐려짐" : "거의 잊음";
}
