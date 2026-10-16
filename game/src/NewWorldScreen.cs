using Godot;
using Wtw.Sim.Planet;

namespace Wtw.Game;

/// <summary>
/// 새 세계 화면: 시드(직접 입력 / 무작위) · 땅의 양 · 기후 경향 → [만들기]. 만드는 동안 진행 표시.
/// 게임을 켜면 먼저 나오고, 아래 막대의 [새 세계] 로 다시 연다.
/// </summary>
public partial class NewWorldScreen : Control
{
    public event Action<ulong, int, ClimateBias>? CreateRequested;
    public event Action? LoadRequested;
    public event Action? CloseRequested;

    private LineEdit _seed = null!;
    private readonly Button[] _land = new Button[3];
    private readonly Button[] _climate = new Button[3];
    private Button _create = null!, _load = null!, _close = null!;
    private ProgressBar _bar = null!;
    private Label _status = null!, _error = null!;

    public bool Busy { get; private set; }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        var dim = new ColorRect { Color = UiTheme.Dim, MouseFilter = MouseFilterEnum.Ignore };
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(dim);

        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(center);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(700, 0) };
        panel.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Panel, 10, 36, 28));
        center.AddChild(panel);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 14);
        panel.AddChild(box);

        var title = new Label { Text = "새 세계 만들기", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontOverride("font", UiTheme.Bold);
        title.AddThemeFontSizeOverride("font_size", 36);
        title.AddThemeColorOverride("font_color", UiTheme.Accent);
        box.AddChild(title);
        var sub = new Label { Text = "빈 행성을 만듭니다. 사람은 아직 없습니다.", HorizontalAlignment = HorizontalAlignment.Center };
        sub.AddThemeColorOverride("font_color", UiTheme.TextDim);
        box.AddChild(sub);
        box.AddChild(new HSeparator());

        // 시드
        box.AddChild(Caption("시드 (같은 시드 = 같은 행성)"));
        var seedRow = new HBoxContainer();
        _seed = new LineEdit { SizeFlagsHorizontal = SizeFlags.ExpandFill, MaxLength = 18, PlaceholderText = "숫자" };
        _seed.TextChanged += _ => _error.Text = "";
        seedRow.AddChild(_seed);
        seedRow.AddChild(MakeButton("무작위", () => _seed.Text = RandomSeed().ToString()));
        box.AddChild(seedRow);

        // 땅의 양
        box.AddChild(Caption("땅의 양 (행성 크기는 같고, 땅 비율만 바뀝니다)"));
        var landRow = new HBoxContainer();
        var landGroup = new ButtonGroup();
        for (int i = 0; i < 3; i++)
        {
            int cells = PlanetSettings.LandChoices[i];
            _land[i] = MakeButton($"{cells:N0} 칸{(i == 0 ? " (기본)" : "")}", null);
            _land[i].ToggleMode = true; _land[i].ButtonGroup = landGroup; _land[i].SizeFlagsHorizontal = SizeFlags.ExpandFill;
            landRow.AddChild(_land[i]);
        }
        _land[0].ButtonPressed = true;
        box.AddChild(landRow);

        // 기후 경향
        box.AddChild(Caption("기후 경향"));
        var climRow = new HBoxContainer();
        var climGroup = new ButtonGroup();
        var climates = new[] { ClimateBias.Cold, ClimateBias.Normal, ClimateBias.Warm };
        for (int i = 0; i < 3; i++)
        {
            _climate[i] = MakeButton(PlanetNames.Of(climates[i]) + (climates[i] == ClimateBias.Normal ? " (기본)" : ""), null);
            _climate[i].ToggleMode = true; _climate[i].ButtonGroup = climGroup; _climate[i].SizeFlagsHorizontal = SizeFlags.ExpandFill;
            climRow.AddChild(_climate[i]);
        }
        _climate[1].ButtonPressed = true;
        box.AddChild(climRow);

        box.AddChild(new HSeparator());
        _create = MakeButton("만들기", OnCreate);
        _create.CustomMinimumSize = new Vector2(0, 58);
        _create.AddThemeFontOverride("font", UiTheme.Bold);
        _create.AddThemeFontSizeOverride("font_size", UiTheme.FontSizeBig);
        box.AddChild(_create);

        _bar = new ProgressBar { MinValue = 0, MaxValue = 100, Visible = false, CustomMinimumSize = new Vector2(0, 26), ShowPercentage = false };
        box.AddChild(_bar);
        _status = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _status.AddThemeColorOverride("font_color", UiTheme.TextDim);
        box.AddChild(_status);
        _error = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _error.AddThemeColorOverride("font_color", new Color(1f, 0.5f, 0.4f));
        box.AddChild(_error);

        var bottom = new HBoxContainer();
        _load = MakeButton("저장한 세계 불러오기", () => LoadRequested?.Invoke());
        bottom.AddChild(_load);
        bottom.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        _close = MakeButton("닫기", () => CloseRequested?.Invoke());
        _close.TooltipText = "Esc";
        bottom.AddChild(_close);
        box.AddChild(bottom);

        _seed.Text = RandomSeed().ToString();
    }

    private static Label Caption(string text)
    {
        var l = new Label { Text = text };
        l.AddThemeFontOverride("font", UiTheme.Bold);
        return l;
    }

    private static Button MakeButton(string text, Action? onPressed)
    {
        var b = new Button { Text = text, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 44) };
        if (onPressed is not null) b.Pressed += onPressed;
        return b;
    }

    public static ulong RandomSeed() => (ulong)Random.Shared.NextInt64(1, 1_000_000_000);

    /// <summary>열 때: 닫기 단추는 이미 세계가 있을 때만, 불러오기는 저장 파일이 있을 때만.</summary>
    public void Open(bool canClose, bool canLoad)
    {
        Visible = true;
        _close.Visible = canClose;
        _load.Disabled = !canLoad;
        SetBusy(false, 0, "");
    }

    public void SetValues(ulong seed, int land, ClimateBias climate)
    {
        _seed.Text = seed.ToString();
        for (int i = 0; i < 3; i++) _land[i].ButtonPressed = PlanetSettings.LandChoices[i] == land;
        for (int i = 0; i < 3; i++) _climate[i].ButtonPressed = i == (int)climate + 1;
    }

    public void SetBusy(bool busy, double progress, string status)
    {
        Busy = busy;
        _create.Disabled = busy;
        _load.Disabled = busy || _load.Disabled;
        _close.Disabled = busy;
        _bar.Visible = busy;
        _bar.Value = progress * 100;
        _status.Text = status;
    }

    private void OnCreate()
    {
        if (!ulong.TryParse(_seed.Text.Trim(), out ulong seed))
        {
            _error.Text = "시드는 숫자(0 이상)로 넣어 주세요.";
            return;
        }
        int land = PlanetSettings.LandChoices[0];
        for (int i = 0; i < 3; i++) if (_land[i].ButtonPressed) land = PlanetSettings.LandChoices[i];
        var climate = ClimateBias.Normal;
        for (int i = 0; i < 3; i++) if (_climate[i].ButtonPressed) climate = (ClimateBias)(i - 1);
        CreateRequested?.Invoke(seed, land, climate);
    }
}
