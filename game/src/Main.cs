using Godot;
using Wtw.Sim.Persistence;
using Wtw.Sim.Planet;
using Wtw.Sim.Provinces;
using Wtw.Sim.Runtime;
using Wtw.Sim.World;
using Wtw.Sim.People;

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
    private ProvinceInfoPanel _info = null!;
    private PersonHoverCard _personHover = null!;
    private PersonCardPanel _cardPanel = null!;
    private PeopleListPanel _list = null!;
    private AlertsPanel _alerts = null!;
    private int _hoverPerson = -1;
    private double _cardTimer, _listTimer, _hoverTimer;
    private string _cardSig = "";
    private (int Person, int Cell)? _pendingOpen;
    private double _infoTimer;
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
        _province.Loaded += UpdateResourceLegend;
        _province.Loaded += () => { if (_pendingOpen is { } po && po.Cell == _province.Center) { _pendingOpen = null; OpenCard(po.Person); } };
        _province.PersonHover += id => { _hoverPerson = id; _hoverTimer = 0; if (id < 0) _personHover.Hide2(); else _card.Visible = false; };
        _province.PersonClicked += OpenCard;
        _province.SpawnClicked += OnSpawnClicked;

        _map = new MapView { Visible = false }; // E0 의 점 시험 화면 — 개발용(F12)
        AddChild(_map);

        var ui = new CanvasLayer { Layer = 2 }; // 지도 위 이름표 · 말풍선(층 1)보다 위
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
        _hud.ResourceViewRequested += SetResourceView;
        _hud.SpawnToolRequested += SetSpawnTool;

        _legend = new Legend { Theme = theme, Visible = false };
        ui.AddChild(_legend);
        _legend.ResourcePicked += r => SetMode(MapMode.Resource, r);

        _panel = new CellPanel { Theme = theme, Visible = false };
        ui.AddChild(_panel);
        _panel.CloseRequested += () => { if (_province.IsOpen) _panel.Visible = false; else _globe.Select(-1); };
        _panel.EnterRequested += () => EnterProvince(_globe.Selected);

        _info = new ProvinceInfoPanel { Theme = theme, Visible = false };
        ui.AddChild(_info);
        _card = new TileCard { Theme = theme };
        ui.AddChild(_card);
        _list = new PeopleListPanel { Theme = theme };
        ui.AddChild(_list);
        _list.PersonPicked += id => { OpenCard(id); _province.Follow(id); _cardPanel.SetFollowing(true); };
        _cardPanel = new PersonCardPanel { Theme = theme };
        ui.AddChild(_cardPanel);
        _cardPanel.CloseRequested += CloseCard;
        _cardPanel.FollowRequested += id => _province.Follow(id);
        _cardPanel.PersonRequested += OpenCard;
        _alerts = new AlertsPanel { Theme = theme };
        ui.AddChild(_alerts);
        _alerts.AlertPicked += OnAlertPicked;
        _personHover = new PersonHoverCard { Theme = theme };
        ui.AddChild(_personHover);
        _province.LabelBlockers = LabelBlockers;

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
        else { _host.Adopt(w, notice); _alerts.Reset(); }
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

    /// <summary>가장자리 이름이 피할 곳: 막대 · 단추 줄 · 지방 정보 칸 · 칸 정보 창 · 범례 · 카드.</summary>
    private IEnumerable<Rect2> LabelBlockers()
    {
        foreach (var r in _hud.Blockers()) yield return r;
        if (_info.Visible) yield return _info.GetGlobalRect();
        if (_cardPanel.Visible) yield return _cardPanel.GetGlobalRect();
        if (_list.Visible) yield return _list.GetGlobalRect();
        if (_alerts.Visible && _alerts.GetChildCount() > 0) yield return _alerts.GetGlobalRect();
        if (_panel.Visible) yield return _panel.GetGlobalRect();
        if (_legend.Visible) yield return _legend.GetGlobalRect();
    }

    /// <summary>지방 정보 칸: 그 지방의 시각 · 기온 등. 0.5초마다 다시 채운다(시각이 흐르므로).</summary>
    private void UpdateProvinceInfo(double delta)
    {
        _info.Visible = _province.IsOpen && _province.ShownCount > 0;
        if (!_info.Visible || _host is null || _globe.Planet is not { } p) return;
        // 아래 끝: 범례(자원 보기)가 있으면 그 위까지
        var vp = GetViewport().GetVisibleRect().Size;
        float bottom = _legend.Visible ? _legend.GetGlobalRect().Position.Y - 12 : vp.Y - UiTheme.BottomBarHeight - 12;
        _info.SetBottom(bottom, vp.Y);
        _infoTimer -= delta;
        if (_infoTimer > 0) return;
        _infoTimer = 0.5;
        var m = _province.MapOf(_province.Center);
        if (m is null) return;
        _info.Show($"지방 정보 — {TileDescriber.ProvinceName(p, m.Cell)}", ProvinceReport.Describe(p, m, _host.RenderTick + ShowTickOffset));
    }

    /// <summary>지방 화면의 자원 보기: 바위 · 광맥 · 돌만 밝게 + 범례(이 화면에 있는 것만).</summary>
    private void SetResourceView(bool on)
    {
        if (!_province.IsOpen) return;
        _province.SetResourceView(on);
        _hud.SetResourceViewPressed(on);
        UpdateResourceLegend();
    }

    private void UpdateResourceLegend()
    {
        if (!_province.IsOpen || !_province.ResourceView || _province.ShownCount == 0) { if (_province.IsOpen) _legend.Visible = false; return; }
        var rocks = Wtw.Sim.Data.RockCatalog.Default;
        var cat = Wtw.Sim.Data.ObjectCatalog.Default;
        var (rk, ores, objs, gravel, hidden) = _province.ResourceSummary();
        var items = new List<(Color, string)>();
        for (int r = 0; r < rk.Length; r++)
            if (rk[r] > 0) items.Add((Art.RockColor(Wtw.Sim.Provinces.Ground.Stone, r), $"{rocks.Rocks[r].Name} — 캐면 {string.Join(", ", rocks.Rocks[r].Yields.Select(y => y.Name))} ({rk[r]:N0}칸)"));
        for (int o = 0; o < ores.Length; o++)
            if (ores[o] > 0) { var oc = rocks.Ores[o].Color; items.Add((new Color(oc.R / 255f, oc.G / 255f, oc.B / 255f), $"{rocks.Ores[o].Name} — 캐면 {string.Join(", ", rocks.Ores[o].Yields.Select(y => y.Name))} ({ores[o]:N0}칸)")); }
        if (gravel > 0) items.Add((new Color(0.78f, 0.76f, 0.7f), $"자갈 바닥 — 줍기 → 돌 ({gravel:N0}칸)"));
        foreach (var (o, n) in objs.OrderByDescending(kv => kv.Value)) items.Add((new Color(1, 1, 1, 0), $"• {cat.Objects[o].Name} — {cat.Objects[o].UsesText} ({n:N0}개)"));
        if (hidden > 0) items.Add((new Color(0.25f, 0.25f, 0.25f), $"바위 속(어둡게) — 안쪽 광맥 · 동굴은 아직 아무도 모름"));
        if (items.Count == 0) items.Add((new Color(0, 0, 0, 0), "이 화면에는 바위 · 광맥이 없습니다"));
        _legend.ShowItems("자원 보기 — 돌 · 광물", items);
        _legend.Visible = true;
    }

    private void ExitProvince()
    {
        if (!_province.IsOpen) return;
        int c = _province.Center;
        if (_province.ResourceView) { _province.SetResourceView(false); _hud.SetResourceViewPressed(false); }
        SetMode(_globe.Mode, null); // 지구본 범례로 되돌림
        _province.Close();
        CloseCard();
        _list.Visible = false;
        _personHover.Hide2();
        _card.Visible = false;
        _info.Visible = false;
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
        CloseCard();
        _panel.ShowTile(TileDescriber.Describe(p, m, x, y));
    }

    // ───────── 사람 ─────────

    private void SetSpawnTool(int tool)
    {
        if (!_province.IsOpen) tool = 0;
        _province.SetSpawnTool(tool);
        _hud.SetSpawnTool(tool);
        if (tool != 0) _hud.ShowNotice(tool == 1 ? "땅을 누르면 그 자리에 한 명을 뿌립니다 (오른쪽 단추 · Esc = 그만)" : "땅을 누르면 가족(짝 + 아이)을 뿌립니다 (오른쪽 단추 · Esc = 그만)");
    }

    private void OnSpawnClicked(int cell, int x, int y, bool family)
    {
        if (_host is null) return;
        var m = _province.MapOf(cell);
        if (m is null) return;
        int i = y * HexFrame.Size + x;
        if (!HexFrame.Inside[i] || !GroundInfo.IsWalkable(m.Ground[i])) { _hud.ShowNotice($"여기는 {GroundInfo.Name(m.Ground[i])} — 걸을 수 있는 땅에만 뿌릴 수 있습니다"); return; }
        _host.Intervene(family ? Intervention.Kinds.SpawnFamily : Intervention.Kinds.SpawnPerson, cell, x, y);
        _auto.OnSpawned();
    }

    private void OpenCard(int id)
    {
        if (_host is null || id < 0) return;
        _panel.Visible = false;
        _cardPanel.Open(id);
        _cardPanel.SetFollowing(_province.FollowPerson == id);
        _province.Select(id);
        _cardSig = "";
        _cardTimer = 0;
        _host.RequestCard(id, 0);
    }

    private void CloseCard()
    {
        _cardPanel.Close();
        _province.Select(-1);
        _province.Follow(-1);
    }

    private void OnAlertPicked(int person, int cell)
    {
        if (_globe.Planet is null) return;
        if (_province.IsOpen && _province.MapOf(cell) is not null) { OpenCard(person); _province.Follow(person); _cardPanel.SetFollowing(true); return; }
        _pendingOpen = (person, cell);
        if (_province.IsOpen) ExitProvince();
        _globe.Select(cell);
        EnterProvince(cell);
    }

    /// <summary>카드 · 목록 · 알림을 매 프레임 맞춘다(카드 내용은 시뮬에게 부탁해 받는다).</summary>
    private void UpdatePeopleUi(WorldSnapshot snap, double delta)
    {
        var vp = GetViewport().GetVisibleRect().Size;
        _alerts.RightMargin = _cardPanel.Visible ? PersonCardPanel.Width + 24 : _panel.Visible ? CellPanel.Width + 24 : 12;
        _alerts.Update(snap, Time.GetTicksMsec() / 1000.0, vp);
        if (_host is null) return;
        // 큰 카드
        if (_cardPanel.Visible)
        {
            _cardTimer -= delta;
            var c = _host.Card(0);
            if (c is not null && c.Id == _cardPanel.PersonId)
            {
                string sig = $"{c.Activity}|{c.Why}|{c.Mood}|{c.Age}|{string.Join(",", c.Needs.Select(n => n.Value / 2))}|{c.Thoughts.Count}|{c.Relations.Count}|{c.Story.Count}|{c.Body.Count}|{string.Join(",", c.Relations.Select(r => r.Opinion / 3))}|{c.Alive}";
                if (sig != _cardSig) { _cardSig = sig; _cardPanel.Fill(c); }
            }
            if (_cardTimer <= 0) { _cardTimer = 0.35; _host.RequestCard(_cardPanel.PersonId, 0); }
            _cardPanel.SetFollowing(_province.FollowPerson == _cardPanel.PersonId && _province.FollowPerson >= 0);
        }
        // 작은 카드
        if (_hoverPerson >= 0 && _province.IsOpen)
        {
            _hoverTimer -= delta;
            if (_hoverTimer <= 0) { _hoverTimer = 0.3; _host.RequestCard(_hoverPerson, 1); }
            var h = _host.Card(1);
            if (h is not null && h.Id == _hoverPerson) _personHover.Show(h, GetViewport().GetMousePosition());
            else _personHover.Follow(GetViewport().GetMousePosition());
        }
        else if (_personHover.Visible) _personHover.Hide2();
        // 사람 목록: 오른쪽 칸이 비어 있을 때만(창끼리 겹치지 않게)
        _list.Visible = _province.IsOpen && _province.ShownCount > 0 && !_cardPanel.Visible && !_panel.Visible;
        if (_list.Visible)
        {
            _listTimer -= delta;
            if (_listTimer <= 0) { _listTimer = 0.8; _host.RequestRows(_province.Center); }
            if (_host.Rows is { } rows && (rows.Count == 0 || rows[0].Cell == _province.Center)) _list.Fill(rows);
        }
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
        _province.SetTime(_host.RenderTick + ShowTickOffset);
        _province.SetPeople(snap, _host.RenderTick);
        _globe.SetPeople(snap.CellPeople, snap.OpenedCells);
        UpdatePeopleUi(snap, delta);
        UpdateProvinceInfo(delta);
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
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } && _province.SpawnTool != 0) { SetSpawnTool(0); GetViewport().SetInputAsHandled(); return; }
        if (e is not InputEventKey { Pressed: true, Echo: false } k) return;
        if (k.Keycode == Key.Escape)
        {
            if (_newWorld.Visible) { if (_host is not null && !_newWorld.Busy) _newWorld.Visible = false; }
            else if (_province.SpawnTool != 0) SetSpawnTool(0);
            else if (_cardPanel.Visible) CloseCard();
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
            case Key.Y when _province.IsOpen: SetResourceView(!_province.ResourceView); break;
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
    /// <summary>개발용(자동 확인): 지방 화면의 시각만 앞당겨 보여 준다(시뮬 시간은 그대로).</summary>
    internal double ShowTickOffset;
    internal TileCard Card => _card;
    internal PersonCardPanel CardPanel => _cardPanel;
    internal PersonHoverCard PersonHover => _personHover;
    internal void OpenCardForTest(int id) => OpenCard(id);
    internal void HoverPersonForTest(int id) { _hoverPerson = id; _hoverTimer = 0; }
    internal void SpawnForTest(int cell, int x, int y, bool family) => OnSpawnClicked(cell, x, y, family);
    internal void EnterForTest(int c) => EnterProvince(c);
    internal void ExitForTest() => ExitProvince();
    internal void ScopeForTest(bool nb) => SetScope(nb);
    internal void HoverForTest(int cell, int x, int y) => _province.SetHover(cell, x, y);
    internal void ClickForTest(int cell, int x, int y) => OnTileClicked(cell, x, y);
    internal void ResourceViewForTest(bool on) => SetResourceView(on);
    internal void SaveForTest() => Save();
    internal void LoadForTest() => Load();
}
