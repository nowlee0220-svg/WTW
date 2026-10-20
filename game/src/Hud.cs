using Godot;
using Wtw.Sim.Planet;
using Wtw.Sim.Runtime;
using Wtw.Sim.Time;

namespace Wtw.Game;

/// <summary>위 막대(날짜 · 속도 · 측정값)와 아래 메뉴 막대(림월드식). 읽기만 하고, 누르면 Main 에 알린다.</summary>
public partial class Hud : Control
{
    public event Action? SaveRequested;
    public event Action? LoadRequested;
    public event Action? NewRequested;
    public event Action<int>? SpeedRequested; // 0 = 멈춤
    public event Action<MapMode>? ModeRequested;
    public event Action<string>? PlaceholderPressed;
    public event Action<bool>? ScopeRequested;     // 지방 화면: false = 이 지방만, true = 이웃까지
    public event Action? ExitProvinceRequested;    // 지방 화면 → 지구본
    public event Action<bool>? ResourceViewRequested; // 지방 화면: 자원 보기 켜기/끄기
    public event Action<int>? SpawnToolRequested;   // 지방 화면: 0 끔 · 1 사람 뿌리기 · 2 가족 뿌리기
    public event Action? DiscoveriesRequested;      // (E4) 발견 목록
    public event Action? SkipRequested;             // (E4) 넘기기
    public event Action? ThreadsRequested;          // (S1) 사람 계산에 쓰는 CPU 수 바꾸기

    private Label _date = null!, _speed = null!, _metrics = null!, _notice = null!;
    private readonly Button[] _speedButtons = new Button[Speed.Max + 1];
    private readonly Dictionary<MapMode, Button> _modeButtons = new();
    private double _noticeTime;
    private double _metricsTimer;
    private PanelContainer _provBar = null!;
    private Label _provName = null!;
    private Button _scopeOne = null!, _scopeNb = null!, _resView = null!;
    private readonly List<Control> _globeOnly = new();
    private readonly List<Control> _provinceOnly = new();
    private Button _spawnOne = null!, _spawnFamily = null!;
    private Button _cpu = null!;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        // 위 막대
        var top = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
        top.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        top.CustomMinimumSize = new Vector2(0, UiTheme.TopBarHeight);
        AddChild(top);
        var topRow = new HBoxContainer();
        top.AddChild(topRow);
        _date = new Label { VerticalAlignment = VerticalAlignment.Center };
        _date.AddThemeFontOverride("font", UiTheme.Bold);
        _date.AddThemeFontSizeOverride("font_size", UiTheme.FontSizeBig);
        _date.AddThemeColorOverride("font_color", UiTheme.Accent);
        topRow.AddChild(_date);
        _speed = new Label { VerticalAlignment = VerticalAlignment.Center, CustomMinimumSize = new Vector2(120, 0) };
        topRow.AddChild(_speed);
        topRow.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        _metrics = new Label { VerticalAlignment = VerticalAlignment.Center };
        _metrics.AddThemeFontSizeOverride("font_size", UiTheme.FontSizeSmall);
        _metrics.AddThemeColorOverride("font_color", UiTheme.TextDim);
        topRow.AddChild(_metrics);
        // (S1) 사람 계산에 쓰는 CPU 수(누를 때마다 하나씩 — 다 쓰면 1로). 결과는 같고 빠르기만 바뀐다.
        _cpu = MakeButton("CPU", () => ThreadsRequested?.Invoke(), "사람 계산에 쓰는 CPU 수 — 누를 때마다 바뀐다. 기본은 코어 수 − 1(화면 몫을 남김). 몇 개를 쓰든 같은 세계가 된다(빠르기만 다름).");
        _cpu.AddThemeFontSizeOverride("font_size", UiTheme.FontSizeSmall);
        topRow.AddChild(_cpu);

        // 아래 메뉴 막대
        var bottom = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
        bottom.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide, LayoutPresetMode.Minsize);
        bottom.CustomMinimumSize = new Vector2(0, UiTheme.BottomBarHeight);
        bottom.GrowVertical = GrowDirection.Begin;
        AddChild(bottom);
        var row = new HBoxContainer();
        bottom.AddChild(row);
        row.AddChild(MakeButton("새 세계", () => NewRequested?.Invoke(), "새 행성을 만든다"));
        row.AddChild(MakeButton("저장", () => SaveRequested?.Invoke(), "멈춘 다음 저장한다 (F5)"));
        row.AddChild(MakeButton("불러오기", () => LoadRequested?.Invoke(), "F9"));
        row.AddChild(new VSeparator());
        for (int s = 0; s <= Speed.Max; s++)
        {
            int level = s;
            var b = MakeButton(s == 0 ? "멈춤" : Speed.Label(s), () => SpeedRequested?.Invoke(level), s == 0 ? "스페이스" : $"키 {s}");
            b.ToggleMode = true;
            _speedButtons[s] = b;
            row.AddChild(b);
        }
        row.AddChild(new VSeparator());
        row.AddChild(MakeButton("지식의 행방", () => DiscoveriesRequested?.Invoke(), "지식의 행방 — 이 세계에서 처음 나온 것들: 누가 아나 · 어디에 남았나 · 잃음과 되찾음 (키 D)"));
        row.AddChild(MakeButton("넘기기", () => SkipRequested?.Invoke(), "다음 발견 · 10년 · 50년 … 까지 화면 없이 빠르게 (키 N)"));
        row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        // 지방 화면: 플레이어의 첫 권능 — 사람 뿌리기
        _spawnOne = MakeButton("사람 뿌리기", () => SpawnToolRequested?.Invoke(_spawnOne.ButtonPressed ? 1 : 0), "누르고 땅을 누르면 그 자리에 한 명 (나이 15~40, 아무것도 모름). 걸을 수 있는 땅에만.");
        _spawnFamily = MakeButton("가족 뿌리기", () => SpawnToolRequested?.Invoke(_spawnFamily.ButtonPressed ? 2 : 0), "누르고 땅을 누르면 짝 + 아이 0~2명 (2~4명)");
        _spawnOne.ToggleMode = _spawnFamily.ToggleMode = true;
        _spawnOne.Visible = _spawnFamily.Visible = false;
        row.AddChild(_spawnOne); row.AddChild(_spawnFamily);
        _provinceOnly.Add(_spawnOne); _provinceOnly.Add(_spawnFamily);
        foreach (var m in MapModes.All)
        {
            var mode = m;
            var b = MakeButton(MapModes.Name(m), () => ModeRequested?.Invoke(mode), $"지도 모드 (키 {MapModes.Hotkey(m)})");
            b.ToggleMode = true;
            _modeButtons[m] = b;
            row.AddChild(b);
            _globeOnly.Add(b);
        }
        foreach (var (name, why) in MapModes.Placeholders)
        {
            var b = MakeButton(name, () => PlaceholderPressed?.Invoke(why), "아직 없음");
            b.Modulate = new Color(1, 1, 1, 0.45f);
            row.AddChild(b);
            _globeOnly.Add(b);
        }

        // 지방 화면의 위쪽 단추: [이 지방만] / [이웃까지] · [지구본으로]
        _provBar = new PanelContainer { MouseFilter = MouseFilterEnum.Stop, Visible = false };
        _provBar.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Panel, 8, 12, 6));
        _provBar.SetAnchorsPreset(LayoutPreset.CenterTop);
        AddChild(_provBar);
        var prow = new HBoxContainer();
        _provBar.AddChild(prow);
        _provName = new Label { VerticalAlignment = VerticalAlignment.Center };
        _provName.AddThemeFontOverride("font", UiTheme.Bold);
        _provName.AddThemeColorOverride("font_color", UiTheme.Accent);
        prow.AddChild(_provName);
        prow.AddChild(new VSeparator());
        _scopeOne = MakeButton("이 지방만", () => ScopeRequested?.Invoke(false), "들어간 지방 하나만 밝게");
        _scopeNb = MakeButton("이웃까지", () => ScopeRequested?.Invoke(true), "둘레 이웃 지방(바다가 아닌 칸)까지 이어 붙여 밝게");
        _scopeOne.ToggleMode = _scopeNb.ToggleMode = true;
        prow.AddChild(_scopeOne);
        prow.AddChild(_scopeNb);
        prow.AddChild(new VSeparator());
        _resView = MakeButton("자원 보기", () => ResourceViewRequested?.Invoke(_resView.ButtonPressed), "바위 · 광맥 · 돌이 어디 있는지 (키 Y)");
        _resView.ToggleMode = true;
        prow.AddChild(_resView);
        prow.AddChild(new VSeparator());
        prow.AddChild(MakeButton("지구본으로", () => ExitProvinceRequested?.Invoke(), "Esc"));

        // 알림
        _notice = new Label { HorizontalAlignment = HorizontalAlignment.Center, Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        _notice.SetAnchorsPreset(LayoutPreset.CenterTop);
        _notice.Position = new Vector2(0, UiTheme.TopBarHeight + 16);
        _notice.AddThemeFontSizeOverride("font_size", UiTheme.FontSizeBig);
        _notice.AddThemeStyleboxOverride("normal", UiTheme.Box(new Color(0, 0, 0, 0.7f), 8, 18, 8));
        AddChild(_notice);
    }

    private static Button MakeButton(string text, Action onPressed, string tooltip = "")
    {
        var b = new Button { Text = text, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 44), TooltipText = tooltip };
        b.Pressed += onPressed;
        return b;
    }

    /// <summary>지방 화면이면 위쪽 단추를 보이고 지도 모드 단추는 감춘다.</summary>
    public void SetResourceViewPressed(bool on) => _resView.SetPressedNoSignal(on);

    /// <summary>화면에서 차지하는 자리(가장자리 이름이 피할 곳): 위 · 아래 막대, 지방 화면의 위쪽 단추 줄, 알림.</summary>
    public IEnumerable<Rect2> Blockers()
    {
        var vp = GetViewportRect().Size;
        yield return new Rect2(0, 0, vp.X, UiTheme.TopBarHeight);
        yield return new Rect2(0, vp.Y - UiTheme.BottomBarHeight, vp.X, UiTheme.BottomBarHeight);
        if (_provBar.Visible) yield return _provBar.GetGlobalRect();
        if (_notice.Visible) yield return _notice.GetGlobalRect();
    }

    public string ProvinceName => _provName.Text;

    public void SetProvinceMode(bool on, string name, bool neighbors)
    {
        _provBar.Visible = on;
        foreach (var b in _globeOnly) b.Visible = !on;
        foreach (var b in _provinceOnly) b.Visible = on;
        if (!on) SetSpawnTool(0);
        _provName.Text = name;
        _scopeOne.SetPressedNoSignal(!neighbors);
        _scopeNb.SetPressedNoSignal(neighbors);
        _provBar.ResetSize();
        _provBar.Position = new Vector2((GetViewportRect().Size.X - _provBar.Size.X) / 2f, UiTheme.TopBarHeight + 10);
    }

    public void SetSpawnTool(int tool)
    {
        _spawnOne.SetPressedNoSignal(tool == 1);
        _spawnFamily.SetPressedNoSignal(tool == 2);
    }

    public void SetMode(MapMode mode)
    {
        foreach (var (m, b) in _modeButtons) b.SetPressedNoSignal(m == mode);
    }

    public void UpdateInfo(WorldSnapshot snap, SimHost host, double delta)
    {
        _date.Text = GameTime.Format((long)host.RenderTick);
        _cpu.Text = $"CPU {host.Threads}/{System.Environment.ProcessorCount}";
        int shown = host.Paused ? 0 : host.SpeedLevel;
        _speed.Text = host.Paused ? $"멈춤 ({Speed.Label(host.SpeedLevel)})" : Speed.Label(host.SpeedLevel);
        for (int s = 0; s <= Speed.Max; s++) _speedButtons[s].SetPressedNoSignal(s == shown);

        _metricsTimer -= delta;
        if (_metricsTimer <= 0)
        {
            _metricsTimer = 0.25;
            string dots = (snap.DotCount > 0 ? $"  ·  점 {snap.DotCount}개" : "") + (snap.PeopleCount > 0 ? $"  ·  사람 {snap.AliveCount}명 (상한 {snap.PeopleCap})" : "");
            _metrics.Text = $"화면 {Engine.GetFramesPerSecond():0} fps  ·  시뮬 하루/초 {FormatRate(snap.SimDaysPerSecond)}  ·  시뮬 부하 {snap.SimLoad * 100:0}%{dots}  ·  시드 {snap.Seed} · 땅 {snap.Config.LandCells:N0} · 기후 {PlanetNames.Of(snap.Config.Climate)}";
        }

        if (_notice.Visible)
        {
            _noticeTime -= delta;
            var c = _notice.Modulate;
            c.A = (float)Mathf.Clamp(_noticeTime, 0, 1);
            _notice.Modulate = c;
            if (_noticeTime <= 0) _notice.Visible = false;
            PlaceNotice(); // (E5) 지방 단추 줄이 뒤늦게 나타나도 겹치지 않게 날마다 자리를 다시 잡는다
        }
    }

    private static string FormatRate(double v) => v >= 10 ? v.ToString("0") : v >= 0.1 ? v.ToString("0.00") : v.ToString("0.0000");

    /// <summary>알림 자리: 위 막대 아래, 지방 단추 줄이 보이면 그 아래(실제 크기로).</summary>
    private void PlaceNotice()
    {
        float y = UiTheme.TopBarHeight + 16;
        if (_provBar.Visible) y = Math.Max(y, _provBar.GetGlobalRect().End.Y + 12);
        _notice.Position = new Vector2((GetViewportRect().Size.X - _notice.Size.X) / 2f, y);
    }

    public void ShowNotice(string text)
    {
        _notice.Text = text;
        _notice.Visible = true;
        _notice.Modulate = Colors.White;
        _noticeTime = 3.0;
        _notice.ResetSize();
        PlaceNotice();
    }
}
