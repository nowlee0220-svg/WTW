using Godot;
using Wtw.Sim.Persistence;
using Wtw.Sim.Planet;
using Wtw.Sim.Provinces;
using Wtw.Sim.Runtime;
using Wtw.Sim.World;

namespace Wtw.Game;

/// <summary>
/// 시작점. 켜면 새 세계 화면 → [만들기] → (다른 스레드에서 행성 만들기, 진행 표시) → 지구본.
/// 시뮬(SimHost, 별도 스레드)은 첫 세계가 생길 때 띄운다. 매 프레임 스냅샷을 받아 지구본과 막대를 그린다.
/// 지구본에서 땅 칸을 두 번 누르거나 칸 정보의 [들어가기] → 2D 지방 화면(ProvinceView). [지구본으로] · Esc → 들어갔던 칸을 보는 지구본.
/// 명령줄(사용자 인자, -- 뒤): AutoPilot 참고.
/// </summary>
public partial class Main : Node
{
    private SimHost? _host;
    private GlobeView _globe = null!;
    private MapView _map = null!;
    private Hud _hud = null!;
    private CellPanel _panel = null!;
    private Legend _legend = null!;
    private NewWorldScreen _newWorld = null!;
    private ProvinceView _province = null!;
    private TileCard _card = null!;
    private bool _neighborsPref;
    private readonly AutoPilot _auto = new();

    private Task<GameWorld>? _making;
    private string _makingNotice = "";
    private double _progress;
    private string _progressText = "";
    private bool _devDots;
    private WorldConfig _lastConfig = new();

    private static string SavePath => ProjectSettings.GlobalizePath("user://saves/quicksave.wtw");

    public override void _Ready()
    {
        var theme = UiTheme.Build();
        _auto.Parse(OS.GetCmdlineUserArgs());

        _globe = new GlobeView();
        AddChild(_globe);
        _globe.CellSelected += OnCellSelected;
        _globe.CellActivated += c => EnterProvince(c);

        _province = new ProvinceView();
        AddChild(_province);
        _province.TileClicked += OnTileClicked;
        _province.HoverChanged += OnTileHover;
        _province.Notice += n => _hud.ShowNotice(n);

        _map = new MapView { Visible = false }; // E0 의 점 시험 화면 — 개발용(F12)
        AddChild(_map);

        var ui = new CanvasLayer();
        AddChild(ui);
        _hud = new Hud { Theme = theme, Visible = false };
        ui.AddChild(_hud);
        _hud.NewRequested += OpenNewWorld;
        _hud.SaveRequested += Save;
        _hud.LoadRequested += Load;
        _hud.SpeedRequested += s => { if (_host is null) return; if (s == 0) _host.SetPaused(true); else _host.SetSpeed(s); };
        _hud.ModeRequested += SetMode;
        _hud.PlaceholderPressed += why => _hud.ShowNotice(why);
        _hud.ScopeRequested += SetScope;
        _hud.ExitProvinceRequested += ExitProvince;

        _legend = new Legend { Theme = theme, Visible = false };
        ui.AddChild(_legend);
        _legend.ResourcePicked += r => SetMode(MapMode.Resource, r);

        _panel = new CellPanel { Theme = theme, Visible = false };
        ui.AddChild(_panel);
        _panel.CloseRequested += () => { if (_province.IsOpen) _panel.Visible = false; else _globe.Select(-1); };
        _panel.EnterRequested += () => EnterProvince(_globe.Selected);

        _card = new TileCard { Theme = theme };
        ui.AddChild(_card);

        _newWorld = new NewWorldScreen { Theme = theme };
        ui.AddChild(_newWorld);
        _newWorld.CreateRequested += (seed, land, climate) => StartMaking(seed, new WorldConfig { DotCount = _auto.Dots ?? 0, LandCells = land, Climate = climate });
        _newWorld.LoadRequested += Load;
        _newWorld.CloseRequested += () => { if (_host is not null) _newWorld.Visible = false; };
        _newWorld.Open(false, File.Exists(SavePath));

        _auto.Start(this);
        if (_auto.Seed is ulong s0 && !_auto.StayOnNewWorld)
        {
            var cfg = new WorldConfig { DotCount = _auto.Dots ?? 0, LandCells = _auto.Land ?? 16_000, Climate = _auto.Climate ?? ClimateBias.Normal };
            _newWorld.SetValues(s0, cfg.LandCells, cfg.Climate);
            StartMaking(s0, cfg);
        }
        _devDots = _auto.DevDots;
        SetMode(_auto.Mode ?? MapMode.Natural, _auto.Resource);
    }

    // ───────── 세계 만들기 · 불러오기 (다른 스레드) ─────────

    private void StartMaking(ulong seed, WorldConfig config)
    {
        if (_making is not null) return;
        _lastConfig = config;
        _makingNotice = $"새 세계를 만들었습니다 (시드 {seed})";
        RunWorldTask(() =>
        {
            var w = GameWorld.Create(seed, config);
            w.EnsurePlanet(Progress);
            return w;
        });
    }

    private void Load()
    {
        if (_making is not null) return;
        if (_province.IsOpen) ExitProvince();
        if (!File.Exists(SavePath)) { _hud.ShowNotice("저장 파일이 없습니다. 먼저 저장하세요."); return; }
        _host?.SetPaused(true);
        _makingNotice = "불러왔습니다";
        if (_newWorld.Visible) _newWorld.SetBusy(true, 0, "불러오는 중…");
        RunWorldTask(() =>
        {
            var w = SaveGame.LoadFile(SavePath);
            w.EnsurePlanet(Progress); // 행성은 시드 + 설정으로 다시 만든다
            return w;
        });
    }

    private void RunWorldTask(Func<GameWorld> make)
    {
        _progress = 0; _progressText = "준비";
        if (_newWorld.Visible) _newWorld.SetBusy(true, 0, "준비");
        _making = Task.Run(make);
    }

    private void Progress(double f, string what) { _progress = f; _progressText = what; }

    private void PollMaking()
    {
        if (_making is null) return;
        if (!_making.IsCompleted)
        {
            if (_newWorld.Visible) _newWorld.SetBusy(true, _progress, $"행성 만드는 중: {_progressText}…");
            return;
        }
        var task = _making;
        _making = null;
        if (task.IsFaulted)
        {
            string msg = task.Exception?.GetBaseException().Message ?? "알 수 없는 문제";
            _newWorld.SetBusy(false, 0, "");
            _hud.ShowNotice("세계를 만들지/불러오지 못했습니다: " + msg);
            GD.PrintErr(task.Exception);
            return;
        }
        var w = task.Result;
        string notice = _makingNotice + $" ({w.Planet.GenerationSeconds:0.0}초)";
        if (_makingNotice.StartsWith("불러")) notice = $"불러왔습니다 ({Wtw.Sim.Time.GameTime.Format(w.Now)})";
        if (_host is null)
        {
            _host = new SimHost(w);
            _host.SetSpeed(_auto.Speed ?? 1);
            if (_auto.Speed is null) _host.SetPaused(false);
            _hud.ShowNotice(notice);
            _auto.OnNotice(notice);
        }
        else _host.Adopt(w, notice);
        _lastConfig = w.Config;
        _newWorld.SetBusy(false, 1, "");
        _newWorld.Visible = false;
        _hud.Visible = true;
        _legend.Visible = true;
        _auto.OnWorldReady();
    }

    private void OpenNewWorld()
    {
        if (_making is not null) return;
        if (_province.IsOpen) ExitProvince();
        _newWorld.SetValues(NewWorldScreen.RandomSeed(), _lastConfig.LandCells, _lastConfig.Climate);
        _newWorld.Open(_host is not null, File.Exists(SavePath));
    }

    private void Save()
    {
        if (_host is null || _making is not null) return;
        _host.Save(SavePath); // 멈춘 다음 저장
    }

    // ───────── 지도 ─────────

    private void SetMode(MapMode mode) => SetMode(mode, null);

    private void SetMode(MapMode mode, int? resource)
    {
        _globe.SetMode(mode, resource);
        _hud.SetMode(mode);
        _legend.Show(mode, _globe.Resource);
    }

    // ───────── 지방 화면 ─────────

    private void EnterProvince(int c)
    {
        var p = _globe.Planet;
        if (p is null || c < 0) return;
        if (!p.IsLand(c)) { _hud.ShowNotice(p.IsOcean(c) ? "바다에는 들어갈 수 없습니다 — 땅 칸을 고르세요." : "호수에는 들어갈 수 없습니다 — 땅 칸을 고르세요."); return; }
        _panel.Visible = false;
        _legend.Visible = false;
        _globe.Visible = false;
        _province.Open(p, c, _neighborsPref);
        _hud.SetProvinceMode(true, TileDescriber.ProvinceName(p, c), _neighborsPref);
        _auto.OnProvinceOpened();
    }

    private void ExitProvince()
    {
        if (!_province.IsOpen) return;
        int c = _province.Center;
        _province.Close();
        _card.Visible = false;
        _globe.Visible = true;
        _legend.Visible = _host is not null;
        _hud.SetProvinceMode(false, "", _neighborsPref);
        // 들어갔던 칸을 보는 지구본으로
        _globe.LookAtCell(c, Math.Min(_globe.Distance, 1.9f));
        _globe.Select(c);
    }

    private void SetScope(bool neighbors)
    {
        _neighborsPref = neighbors;
        _province.SetNeighbors(neighbors);
        if (_globe.Planet is { } p && _province.IsOpen) _hud.SetProvinceMode(true, TileDescriber.ProvinceName(p, _province.Center), neighbors);
    }

    private void OnTileHover(int cell, int x, int y)
    {
        var p = _globe.Planet;
        var m = cell >= 0 ? _province.MapOf(cell) : null;
        if (p is null || m is null) { _card.Visible = false; return; }
        _card.ShowAt(TileDescriber.Describe(p, m, x, y), GetViewport().GetMousePosition());
    }

    private void OnTileClicked(int cell, int x, int y)
    {
        var p = _globe.Planet;
        var m = _province.MapOf(cell);
        if (p is null || m is null) return;
        _panel.ShowTile(TileDescriber.Describe(p, m, x, y));
    }

    private void OnCellSelected(int c)
    {
        if (c < 0 || _globe.Planet is null) { _panel.Visible = false; return; }
        _panel.ShowCell(_globe.Planet, c);
    }

    // ───────── 프레임 ─────────

    public override void _Process(double delta)
    {
        PollMaking();
        if (_host is null) { _auto.Tick(delta, null); return; }
        var snap = _host.Frame(delta);
        if (snap.Planet is not null && !ReferenceEquals(snap.Planet, _globe.Planet) && _province.IsOpen) ExitProvince();
        if (snap.Planet is not null) _globe.SetPlanet(snap.Planet);
        if (_card.Visible) _card.Follow(GetViewport().GetMousePosition());
        _map.Visible = _devDots;
        if (_devDots) _map.Render(snap, _host.RenderTick);
        _hud.UpdateInfo(snap, _host, delta);
        while (_host.TryGetNotice(out var n))
        {
            _hud.ShowNotice(n);
            _auto.OnNotice(n);
        }
        _auto.Tick(delta, snap);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } k) return;
        if (k.Keycode == Key.Escape)
        {
            if (_newWorld.Visible) { if (_host is not null && !_newWorld.Busy) _newWorld.Visible = false; }
            else if (_province.IsOpen) { _panel.Visible = false; ExitProvince(); }
            else if (_panel.Visible) _globe.Select(-1);
            GetViewport().SetInputAsHandled();
            return;
        }
        if (_host is null || _newWorld.Visible) return;
        switch (k.Keycode)
        {
            case Key.Space: _host.TogglePause(); break;
            case Key.Key1 or Key.Kp1: _host.SetSpeed(1); break;
            case Key.Key2 or Key.Kp2: _host.SetSpeed(2); break;
            case Key.Key3 or Key.Kp3: _host.SetSpeed(3); break;
            case Key.Key4 or Key.Kp4: _host.SetSpeed(4); break;
            case Key.Key5 or Key.Kp5: _host.SetSpeed(5); break;
            case Key.F5: Save(); break;
            case Key.F9: Load(); break;
            case Key.F12: _devDots = !_devDots; break; // 개발용: E0 점 시험 화면
            case Key.Enter or Key.KpEnter when !_province.IsOpen && _globe.Selected >= 0: EnterProvince(_globe.Selected); break;
            default:
                if (_province.IsOpen) return;
                var mode = MapModes.All.FirstOrDefault(m => MapModes.Hotkey(m) == k.Keycode, (MapMode)(-1));
                if ((int)mode < 0) return;
                SetMode(mode);
                break;
        }
        GetViewport().SetInputAsHandled();
    }

    public override void _ExitTree() => _host?.Dispose();

    // AutoPilot 이 쓰는 문
    internal SimHost? Host => _host;
    internal GlobeView Globe => _globe;
    internal CellPanel Panel => _panel;
    internal ProvinceView Province => _province;
    internal TileCard Card => _card;
    internal void EnterForTest(int c) => EnterProvince(c);
    internal void ExitForTest() => ExitProvince();
    internal void ScopeForTest(bool nb) => SetScope(nb);
    internal void HoverForTest(int cell, int x, int y) => _province.SetHover(cell, x, y);
    internal void ClickForTest(int cell, int x, int y) => OnTileClicked(cell, x, y);
    internal void SaveForTest() => Save();
    internal void LoadForTest() => Load();
}
