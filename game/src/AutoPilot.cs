using Godot;
using Wtw.Sim.Planet;
using Wtw.Sim.Provinces;
using Wtw.Sim.Runtime;
using Wtw.Sim.Time;

namespace Wtw.Game;

/// <summary>
/// 자동 확인용(클라우드의 가상 화면에서 쓴다). 사람이 게임할 때는 아무 일도 하지 않는다.
///   --seed=N             새 세계 화면을 건너뛰고 바로 이 시드로 만든다 (--land=16000|20000|24000 --climate=cold|normal|warm)
///   --newworld           새 세계 화면에 머문다(스크린샷용)
///   --mode=natural|elev|biome|temp|rain|res  --res=copper   지도 모드
///   --select=auto|칸번호  칸을 골라 정보 창을 연다(auto = 비그늘이 있는 산 근처 땅)  --why  "왜?" 를 펼친다    --zoom=거리   --spin  지구본을 계속 돌린다
///   --screenshot=경로 --shot-after=초 : 세계가 생기고 그 시간 뒤 화면을 찍고 끝낸다.
///   --selftest : 달리는 중에 [저장] → 멈춘 화면 = 저장된 시각인지 → 더 돌림 → 불러오기 → 저장한 시각·점 위치로 돌아왔는지.
///   --dots=N --devdots : E0 점 시험(개발용 화면)
///   --enter=forest|river|coast|mountain|desert|tundra|pentagon|칸번호 : 그 지방으로 들어간다  --neighbors : [이웃까지]
///   --pzoom=배율  지방 화면 확대   --focus=river|coast|edge|object|stone|ore|cave|gravel  --resview 자원 보기  --localhour=H 그 지방 시각을 H시로 보여 줌(개발용)  그곳을 화면 가운데로   --hover  오브젝트 하나에 마우스를 올린다(카드)   --tileclick  그 타일 정보 창
///   --enterexit=N : 지방을 N 번 드나들며 메모리를 잰다(지구본 ↔ 지방)
///   (E3 사람) --people=N  들어간 지방에 가족 2 + 혼자 N 명을 뿌린다 (--enter=home|cave|cold 로 칸)   --families=K 가족 수
///   --card=0~6  첫 사람의 큰 카드를 그 슬롯으로   --hoverperson  첫 어른에 마우스   --follow  첫 사람 따라가기
///   --untilhour=H  4배속으로 돌리다 그 지방 시각 H시가 되면 1배속(밤 · 잠 확인)   --globeafter=초  그 시간 뒤 지구본으로 나와 사람 사는 칸을 본다
///   --waitdays=D  뿌린 뒤 D 일을 5배속으로 돌린 다음 찍는다
///   (E3b) --prerun=Y  세계를 만들 때 따뜻한 땅에 가족 K 집(--prefamilies=K, 기본 3)을 뿌리고 Y 년을 미리 돌린다(화면 없이 — 퍼져 나간 모습 · 지명 · 말)
///         --select=home  그 땅 칸을 고른다   --mode=lang  언어 지도   --enter=dry  물이 없고 이웃에 물이 있는 땅   --enter=spring  샘 · 개울이 있는 땅
///         --focus=spring|stream|pool  그것을 화면 가운데로   --waitcross  누가 경계를 넘으러 가면 따라가고, 넘은 뒤 찍는다
/// 프레임 시간 통계(평균 fps, 가장 긴 프레임)도 출력한다.
/// </summary>
public sealed class AutoPilot
{
    public ulong? Seed;
    public int? Dots, Speed, Land;
    public ClimateBias? Climate;
    public MapMode? Mode;
    public int? Resource;
    public bool StayOnNewWorld, DevDots;
    private string? _select;
    private float? _zoom;
    private bool _spin, _whyOpen;
    private string? _shotPath;
    private double _shotAfter = 4;
    private bool _selftest;
    private string? _enter, _focus;
    private bool _neighbors, _hover, _tileClick, _resView;
    private double? _localHour;
    private float? _pzoom;
    private int _enterExit;
    private int? _people, _cardTab, _untilHour;
    private int _families = 2;
    private bool _hoverPerson, _follow, _spawned, _peopleReady;
    private double? _globeAfter, _waitDays;
    public int? PreRunYears;
    public int PreFamilies = 3, PreHomes = 1;
    private bool _waitCross, _crossNoFollow;
    private int? _existingCard;
    private bool _existingDone;
    private int _crossTarget = -1, _crossFrom = -1;
    private double _sinceCross = -1;
    private double _sincePeople;
    private long _spawnTick;
    private bool _globeDone;
    private bool _provinceOpened, _provinceReady;
    private double _sinceProvince;

    private Main _main = null!;
    private bool _ready;
    private double _elapsed, _sinceReady;
    private int _frames;
    private double _maxFrame;
    private readonly List<double> _frameTimes = new();

    // 자체 시험 상태
    private int _stage;
    private double _stageTime;
    private long _savedTick;
    private double[] _savedPos = Array.Empty<double>();
    private string? _lastNotice;
    private string _saveCheck = "";

    public bool Active => _shotPath is not null || _selftest || _enterExit > 0;

    public void Parse(string[] args)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        foreach (var a in args)
        {
            var kv = a.Split('=', 2);
            string v = kv.Length > 1 ? kv[1] : "";
            switch (kv[0])
            {
                case "--seed": Seed = ulong.Parse(v); break;
                case "--dots": Dots = int.Parse(v); break;
                case "--devdots": DevDots = true; break;
                case "--speed": Speed = int.Parse(v); _speedAfter = Speed.Value; break;
                case "--land": Land = int.Parse(v); break;
                case "--climate": Climate = v switch { "cold" => ClimateBias.Cold, "warm" => ClimateBias.Warm, _ => ClimateBias.Normal }; break;
                case "--mode":
                    Mode = v switch { "elev" => MapMode.Elevation, "biome" => MapMode.Biome, "temp" => MapMode.Temperature, "rain" => MapMode.Rain, "res" => MapMode.Resource, "lang" => MapMode.Language, "know" => MapMode.Knowledge, _ => MapMode.Natural };
                    break;
                case "--res": Resource = Array.IndexOf(ResourceKinds.Ids, v) is int r && r >= 0 ? r : null; break;
                case "--newworld": StayOnNewWorld = true; break;
                case "--select": _select = v; break;
                case "--zoom": _zoom = float.Parse(v, inv); break;
                case "--spin": _spin = true; break;
                case "--why": _whyOpen = true; break;
                case "--screenshot": _shotPath = v; break;
                case "--shot-after": _shotAfter = double.Parse(v, inv); break;
                case "--selftest": _selftest = true; break;
                case "--enter": _enter = v; break;
                case "--neighbors": _neighbors = true; break;
                case "--pzoom": _pzoom = float.Parse(v, inv); break;
                case "--focus": _focus = v; break;
                case "--hover": _hover = true; break;
                case "--tileclick": _tileClick = true; break;
                case "--enterexit": _enterExit = int.Parse(v); break;
                case "--resview": _resView = true; break;
                case "--localhour": _localHour = double.Parse(v, inv); break;
                case "--people": _people = int.Parse(v); break;
                case "--families": _families = int.Parse(v); break;
                case "--card": _cardTab = int.Parse(v); break;
                case "--hoverperson": _hoverPerson = true; break;
                case "--follow": _follow = true; break;
                case "--focustalk": _focusTalk = true; break;
                case "--untilhour": _untilHour = int.Parse(v); break;
                case "--globeafter": _globeAfter = double.Parse(v, inv); break;
                case "--waitdays": _waitDays = double.Parse(v, inv); break;
                case "--prerun": PreRunYears = int.Parse(v); break;
                case "--prefamilies": PreFamilies = int.Parse(v); break;
                case "--prehomes": PreHomes = int.Parse(v); break;
                case "--waitcross": _waitCross = true; break;
                case "--existingcard": _existingCard = int.Parse(v); break;
                case "--crossnofollow": _waitCross = true; _crossNoFollow = true; break;
                case "--livebake": ProvinceView.BakeGround = v != "0"; break;
                case "--look": _look = v; break;
                case "--popups": Popups = true; break;
                case "--scene": Scene = v; break;
                case "--skip": _skip = v; break;
                case "--skippanel": _skipPanelShot = true; break;
                case "--skipmid": _skipMidShot = true; break;
                case "--loadpanel": _loadPanelShot = true; break;
                case "--discoveries": _showDiscoveries = true; _expandDiscovery = v == "expand"; break;
                case "--pausemeasure": _pauseMeasure = double.Parse(v, inv); break;
            }
        }
    }

    public void Start(Main main)
    {
        _main = main;
        if (_selftest && Seed is null) Seed = 7;
    }

    public void OnNotice(string n) { _lastNotice = n; if (n.StartsWith("SKIPDONE")) { GD.Print(n); _sinceProvince = 0; _sinceReady = 0; } }
    public void OnSpawned() { }

    public void OnProvinceOpened() { _provinceOpened = true; _provinceReady = false; }

    public void OnWorldReady()
    {
        if (_ready) return;
        _ready = true;
        _sinceReady = 0;
        var g = _main.Globe;
        g.AutoSpin = _spin;
        if (_zoom is float z) g.LookAtCell(0, z);
        if (_select is not null) _selectPending = true;
    }

    private bool _selectPending;

    private void DoSelect()
    {
        var g = _main.Globe;
        var p = g.Planet;
        if (p is null) return;
        int cell;
        if (_select == "auto")
        {
            // 보기 좋은 예: 비그늘이 뚜렷한 마른 땅 (없으면 산 옆 땅)
            cell = -1;
            int best = -1;
            for (int c = 0; c < p.CellCount; c++)
            {
                if (!p.IsLand(c) || p.Terrain[c] == Terrain.Lake) continue;
                int score = p.ShadowPct[c] * 4 + (p.RiverSize[c] > 0 ? 30 : 0) + (p.Terrain[c] == Terrain.Plain ? 20 : 0) - Math.Abs(p.Temp10[c] - 120) / 10;
                if (score > best) { best = score; cell = c; }
            }
        }
        else if (_select == "home") cell = Wtw.Sim.People.PeopleSamples.Home(p);
        else if (_select == "people")
        {
            // 사람 사는 칸들의 가운데(고르지 않고 보기만)
            var cells = _lastSnap?.CellPeople ?? Array.Empty<(int, int)>();
            double x = 0, y = 0, z = 0;
            foreach (var (c, _) in cells) { x += p.X[c]; y += p.Y[c]; z += p.Z[c]; }
            int best = -1; double bd = double.MinValue;
            for (int c = 0; c < p.CellCount; c++) { double d = p.X[c] * x + p.Y[c] * y + p.Z[c] * z; if (d > bd) { bd = d; best = c; } }
            g.LookAtCell(best, _zoom ?? 1.7f);
            return;
        }
        else cell = int.TryParse(_select, out int sc) ? sc : ProvinceSamples.Find(p, _select!);
        g.LookAtCell(cell, _zoom ?? 1.7f);
        g.Select(cell);
        if (_whyOpen) _main.Panel.SetWhyOpen(true);
    }

    private WorldSnapshot? _lastSnap;

    public void Tick(double delta, WorldSnapshot? snap)
    {
        _lastSnap = snap ?? _lastSnap;
        if (!Active && !_selectPending) return;
        _elapsed += delta;
        _sinceReady += delta;
        if (_selectPending && _ready && _main.Globe.Planet is not null && (_select != "people" || (_lastSnap?.CellPeople.Length ?? 0) > 0)) { _selectPending = false; DoSelect(); }
        if (!Active) return;
        if (_enter is not null && _ready && _main.Globe.Planet is not null && !_provinceOpened && _enterExit == 0)
        {
            var p = _main.Globe.Planet;
            int cell = int.TryParse(_enter, out int ec) ? ec : _enter switch
            {
                "home" => Wtw.Sim.People.PeopleSamples.Home(p),
                "cold" => Wtw.Sim.People.PeopleSamples.Cold(p),
                "cave" => CaveCell(p),
                "dry" => DryCell(p),
                "spring" => SpringCell(p),
                _ => ProvinceSamples.Find(p, _enter),
            };
            if (cell < 0) { Finish(false, $"AUTOPILOT: {_enter} 칸이 없음"); return; }
            if (_neighbors) _main.ScopeForTest(true);
            _main.Globe.Select(cell);
            _main.EnterForTest(cell);
        }
        if (_provinceOpened && !_provinceReady && _main.Province.ShownCount > 0 && !_main.Province.Loading)
        {
            _provinceReady = true;
            _sinceProvince = 0;
            ArrangeProvince();
        }
        if (_provinceReady) _sinceProvince += delta;
        if (_provinceReady && _showDiscoveries && _people is null && !_discShown) { _discShown = true; _main.ShowDiscoveriesForTest(); if (_expandDiscovery && _main.Snap is { } es) _main.Discoveries.ExpandBest(es); }
        if (_enterExit > 0) { if (_ready) EnterExit(delta); return; }
        if (_elapsed > 1.0 && (_ready || StayOnNewWorld)) // 시작 직후는 빼고 잰다
        {
            _frames++;
            _maxFrame = Math.Max(_maxFrame, delta);
            _frameTimes.Add(delta);
        }
        if (_elapsed > (PreRunYears is int pr ? 120 + pr * 6 : 120) && !_ready) { Finish(false, "AUTOPILOT 시간 초과 (세계가 만들어지지 않음?)"); return; }
        if (_selftest) { if (_ready && snap is not null) { if (_people is not null && !_spawned) { if (_provinceReady) PeopleSteps(delta, snap); } else SelfTest(delta, snap); } }
        else if (_people is not null) { if (_provinceReady) PeopleSteps(delta, snap); }
        else if (_look is not null && _provinceReady && snap is not null && !_lookDone)
        {
            _lookWait += delta;
            if (Look(snap) || _lookWait > 300) { _lookDone = true; _sinceProvince = 0; }
            return;
        }
        else if (_existingCard is int ec && _provinceReady && snap is not null && !_existingDone)
        {
            // 이미 사는 사람(미리 돌린 세계)의 큰 카드: 가운데 지방에서 옮겨 온 적이 많은 어른 — 화면은 이야기를 볼 수 있게
            _existingDone = true;
            var pv = _main.Province;
            int target = -1;
            for (int i = 0; i < snap.PeopleCount && target < 0; i++)
            {
                var v = snap.People[i];
                if (!v.Dead && v.Cell == pv.Center && v.Stage >= Wtw.Sim.People.LifeStage.Adult && v.Carrier < 0 && v.Named) target = v.Id;
            }
            if (target >= 0) { _main.OpenCardForTest(target); _main.CardPanel.SetTab(ec); }
            _sinceProvince = 0;
        }

        else if (_skip is not null && !_skipStarted && (_provinceReady || (_enter is null && _ready)) && _main.Host is { } sh)
        {
            _skipStarted = true;
            var goal = _skip switch { "discovery" => Wtw.Sim.Runtime.SkipGoals.Discovery(null), "pot" => Wtw.Sim.Runtime.SkipGoals.Discovery("pot"), "land" => Wtw.Sim.Runtime.SkipGoals.NewLand(), "lost" => Wtw.Sim.Runtime.SkipGoals.KnowledgeLost(), "grouplost" => Wtw.Sim.Runtime.SkipGoals.KnowledgeGroupLost(), "refound" => Wtw.Sim.Runtime.SkipGoals.KnowledgeRefound(), _ => Wtw.Sim.Runtime.SkipGoals.Years(int.Parse(_skip)) };
            _main.StartSkipForTest(goal);
            GD.Print($"SKIP start {goal.Name}");
        }
        else if (_loadPanelShot && !_loadPanelShown && _ready) { _loadPanelShown = true; _main.LoadPanelForTest(); }
        else if (_skipPanelShot && !_skipPanelShown && (_provinceReady || (_enter is null && _ready))) { _skipPanelShown = true; _main.ShowSkipPanelForTest(); }
        else if (_shotPath is not null && (_look is null || _lookDone) && (_skip is null || (_lastNotice?.StartsWith("SKIPDONE") ?? false) || _skipMidShot) && (StayOnNewWorld ? _elapsed : _enter is not null ? (_provinceReady ? _sinceProvince : -1) : (_ready ? _sinceReady : -1)) >= _shotAfter) Finish(true, "");
    }

    /// <summary>(E4) 스크린샷: fire 불 둘레 · tool 도구를 든 사람 · pot 그릇 · experiment 실험하는 사람(큰 카드) — 찾을 때까지 2배속으로 돌린다.</summary>
    private bool Look(WorldSnapshot snap)
    {
        var pv = _main.Province;
        var host = _main.Host!;
        int center = pv.Center;
        if (_look == "fire")
        {
            double lh = ProvinceReport.LocalHour(_main.Globe.Planet!, center, host.RenderTick);
            bool night = lh >= 20 || lh < 4;
            if (night && snap.Fires.TryGetValue(center, out var fires) && fires.FirstOrDefault(f => f.Lit) is { Kind: > 0 } f)
            {
                var lp = pv.LayoutOf(center, f.Tile % HexFrame.Size, f.Tile / HexFrame.Size)!.Value;
                pv.LookAt(lp.X, lp.Y, _pzoom ?? 1.2f);
                host.SetSpeed(1);
                return true;
            }
            if (host.SpeedLevel != 5) host.SetSpeed(5);
            return false;
        }
        if (_lookPerson >= 0)
        {
            // 멈춘 뒤의 새 스냅샷으로 다시 본다 — 그새 일이 바뀌었으면 다시 찾는다
            if (++_lookFrames < 3) return false;
            var lv = snap.People.Take(snap.PeopleCount).FirstOrDefault(q => q.Id == _lookPerson);
            if (_look == "experiment" ? lv.Act is Wtw.Sim.People.Act.Experiment or Wtw.Sim.People.Act.Craft : CardLookOk(lv)) return true;
            _lookPerson = -1; _main.CloseCardForTest(); host.SetPaused(false);
        }
        // (E5) 벽 그림: 가운데 지방의 그림을 화면 가운데로 · 눌러 정보 창
        if (_look == "painting")
        {
            if (snap.Paintings.TryGetValue(center, out var pics) && pics.Length > 0)
            {
                var g = pics.OrderByDescending(q => q.Rule >= 0 ? 1 : 0).ThenByDescending(q => q.Medium).First();
                var lp = pv.LayoutOf(center, g.Tile % HexFrame.Size, g.Tile / HexFrame.Size)!.Value;
                pv.LookAt(lp.X - (CellPanel.Width / 2.0) / (_pzoom ?? 2f) / ProvinceView.TilePx, lp.Y, _pzoom ?? 2f);
                _main.ClickForTest(center, g.Tile % HexFrame.Size, g.Tile / HexFrame.Size);
                host.SetPaused(true);
                GD.Print($"LOOK painting {g.What} medium={g.Medium} tile={g.Tile}");
                return true;
            }
            if (host.SpeedLevel != 5) host.SetSpeed(5);
            return false;
        }
        bool cardLook = _look is "experiment" or "teach" or "study" or "paintact";
        for (int i = 0; i < snap.PeopleCount; i++)
        {
            var v = snap.People[i];
            if (v.Dead || v.Cell != center || v.Carrier >= 0) continue;
            if (_look == "experiment" && v.Stage == Wtw.Sim.People.LifeStage.Child) continue;
            bool ok = _look switch
            {
                "tool" => v.Tool >= 0 && !v.Asleep,
                "pot" => v.Vessel >= 0 && Wtw.Sim.Data.CraftData.Default.Catalog.Items[v.Vessel].Id.EndsWith("pot") && !v.Asleep,
                "experiment" => v.Act is Wtw.Sim.People.Act.Experiment or Wtw.Sim.People.Act.Craft && !v.Walking,
                "teach" or "study" or "paintact" => CardLookOk(v),
                _ => false,
            };
            if (!ok || pv.PersonWorld(v.Id) is not { } wp) continue;
            float z = _pzoom ?? 1.6f;
            double shift = cardLook || _cardTab is not null ? (PersonCardPanel.Width / 2.0) / z / ProvinceView.TilePx : 0;
            pv.LookAt(wp.X / ProvinceView.TilePx + shift, wp.Y / ProvinceView.TilePx - 0.6, z);
            if (cardLook || _cardTab is not null) { _main.OpenCardForTest(v.Id); _main.CardPanel.SetTab(_cardTab ?? 0); }
            if (_follow) pv.Follow(v.Id);
            host.SetSpeed(1);
            GD.Print($"LOOK {_look} person={v.Id} {v.Name}");
            if (cardLook) { host.SetPaused(true); _lookPerson = v.Id; _lookFrames = 0; return false; }
            return true;
        }
        if (host.SpeedLevel != 4) host.SetSpeed(4);
        return false;
    }

    /// <summary>(E5) 가르치는 사람(배우는 사람이 곁에 있음) · 그림을 보는 사람 · 그림을 그리는 사람.</summary>
    private bool CardLookOk(PersonView v) => _look switch
    {
        "teach" => v.Teaching,
        "study" => v.Studying,
        "paintact" => v.Painting,
        _ => false,
    };

    private void SelfTest(double delta, WorldSnapshot snap)
    {
        var host = _main.Host!;
        _stageTime += delta;
        switch (_stage)
        {
            case 0 when _stageTime > 0.5:
                host.SetSpeed(4); Next(); break;
            case 1 when _stageTime > 1.5:
                // 달리는 중에 [저장]: 게임이 스스로 멈춘 뒤 저장해야 한다
                _lastNotice = null;
                _main.SaveForTest(); Next(); break;
            case 2 when _lastNotice is not null && _stageTime > 0.5:
            {
                _savedTick = (long)host.RenderTick;
                _savedPos = Positions(snap, host.RenderTick);
                bool same = host.Paused && host.LastSavedTick == _savedTick;
                _saveCheck = $"저장 뒤 멈춤 {host.Paused} · 화면 시각 {_savedTick} · 저장된 시각 {host.LastSavedTick} → {(same ? "같음" : "다름")}";
                if (!same) { Finish(false, "SELFTEST saveload FAIL: " + _saveCheck); return; }
                host.SetSpeed(4); Next(); break;
            }
            case 3 when _stageTime > 1.5:
                _lastNotice = null; _main.LoadForTest(); Next(); break;
            case 4 when _lastNotice is not null && _lastNotice.StartsWith("불러") && _stageTime > 0.5:
            {
                long now = (long)host.RenderTick;
                var pos = Positions(snap, host.RenderTick);
                double maxDiff = 0;
                for (int i = 0; i < Math.Min(pos.Length, _savedPos.Length); i++) maxDiff = Math.Max(maxDiff, Math.Abs(pos[i] - _savedPos[i]));
                bool ok = now == _savedTick && pos.Length == _savedPos.Length && maxDiff < 1e-6;
                Finish(ok, $"SELFTEST saveload {(ok ? "OK" : "FAIL")}: {_saveCheck} / 저장 시각 {GameTime.FormatWithMinutes(_savedTick)}({_savedTick}) / 불러온 뒤 {GameTime.FormatWithMinutes(now)}({now}) / 점 · 사람 {pos.Length / 2}개(사람 {snap.PeopleCount}) 위치 최대 차이 {maxDiff}");
                break;
            }
            case 4 when _stageTime > 20:
                Finish(false, "SELFTEST saveload FAIL: 불러오기 알림이 오지 않음"); break;
        }
    }

    private void Next() { _stage++; _stageTime = 0; }

    // ───────── 지방 화면 ─────────

    private void ArrangeProvince()
    {
        var pv = _main.Province;
        var p = _main.Globe.Planet!;
        var m = pv.MapOf(pv.Center)!;
        int fx = 75, fy = 75;
        bool found = false;
        void FindTile(Func<int, bool> pred)
        {
            // 가운데에서 가까운 순서로
            for (int r = 0; r < 75 && !found; r++)
                for (int dy = -r; dy <= r && !found; dy++)
                    for (int dx = -r; dx <= r && !found; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                        int x = 75 + dx, y = 75 + dy;
                        if (x < 0 || y < 0 || x >= HexFrame.Size || y >= HexFrame.Size) continue;
                        int i = y * HexFrame.Size + x;
                        if (HexFrame.Inside[i] && pred(i)) { fx = x; fy = y; found = true; }
                    }
        }
        switch (_focus)
        {
            case "river": FindTile(i => (m.Flags[i] & TileFlags.River) != 0); break;
            case "coast": FindTile(i => (m.Flags[i] & TileFlags.Sea) != 0); break;
            case "edge": fx = 75; fy = 14; found = true; break;
            case "blocked": FindTile(i => m.Ground[i] == Ground.Cliff); break;
            case "stone": FindTile(i => m.Ground[i] == Ground.Stone && !m.IsHidden(i)); break;
            case "ore": FindTile(i => m.VisibleOreAt(i) >= 0); break;
            case "cave": FindTile(i => (m.Flags[i] & TileFlags.Roof) != 0); break;
            case "mouth": FindTile(i => (m.Flags[i] & TileFlags.Roof) != 0 && !m.IsHidden(i)); break;
            case "gravel": FindTile(i => m.Ground[i] == Ground.Gravel); break;
            case "spring": FindTile(i => (m.Flags[i] & TileFlags.Spring) != 0); break;
            case "stream": FindTile(i => (m.Flags[i] & TileFlags.Stream) != 0); break;
            case "pool": FindTile(i => (m.Flags[i] & TileFlags.Pool) != 0 && (m.Flags[i] & TileFlags.Spring) == 0); break;
        }
        if ((_hover && _focus is null or "object" or "river" or "coast" or "edge") || _focus == "object")
        {
            found = false;
            var cat = Wtw.Sim.Data.ObjectCatalog.Default;
            int fx0 = fx, fy0 = fy;
            // 다루는 법이 둘 이상인 오브젝트(사과나무 등) → 아니면 아무 오브젝트
            FindTile(i => m.Object[i] > 0 && cat.Objects[m.Object[i] - 1].Uses.Count >= 2 && Math.Abs(i % HexFrame.Size - fx0) + Math.Abs(i / HexFrame.Size - fy0) < 60);
            if (!found) FindTile(i => m.Object[i] > 0);
        }
        var lp = pv.LayoutOf(pv.Center, fx, fy)!.Value;
        if (_pzoom is float z || _focus is not null || _hover) pv.LookAt(lp.X, lp.Y, _pzoom ?? pv.Zoom);
        if (_hover)
        {
            var sp = pv.ScreenOf(pv.Center, fx, fy)!.Value;
            Input.WarpMouse(sp);
            _main.HoverForTest(pv.Center, fx, fy);
            _main.Card.ShowAt(TileDescriber.Describe(p, m, fx, fy), sp);
        }
        if (_tileClick) _main.ClickForTest(pv.Center, fx, fy);
        if (_resView) _main.ResourceViewForTest(true);
        if (_localHour is double lh)
        {
            double now = ProvinceReport.LocalHour(p, pv.Center, _main.Host!.RenderTick);
            _main.ShowTickOffset = ((lh - now) % 24 + 24) % 24 * Wtw.Sim.Time.GameTime.TicksPerHour;
        }
        GD.Print($"PROVINCE cell={pv.Center} neighbors={pv.Neighbors} shown={pv.ShownCount} gen_s={m.GenerationSeconds:F3} focus=({fx},{fy}) zoom={pv.Zoom:F3}");
    }

    // ───────── (E3b) ─────────

    /// <summary>세계를 만든 직후(다른 스레드): 따뜻한 땅에 가족 K 집을 뿌리고 Y 년을 미리 돌린다. 개입 기록에 남으므로 저장 · 다시 돌리기와 같다.</summary>
    public static void PreRun(Wtw.Sim.World.GameWorld w, int years, int families, Action<double, string> progress, int homes = 1)
    {
        var p = w.Planet;
        var list = new List<int> { Wtw.Sim.People.PeopleSamples.Home(p) };
        // 두 번째 땅부터: 앞의 땅들에서 8칸 이상 떨어진 따뜻한 땅(서로 떨어져 사는 무리)
        foreach (int c in Wtw.Sim.People.PeopleSamples.Ranked(p, (pl, c) => pl.Biome[c] is Biome.Temperate or Biome.Rainforest or Biome.Grassland or Biome.Savanna && pl.RiverSize[c] > 0 ? pl.Temp10[c] : int.MinValue))
        {
            if (list.Count >= homes) break;
            if (list.All(h => Hops(p, h, c, 8) >= 8)) list.Add(c);
        }
        foreach (int home in list)
            foreach (var (x, y) in Wtw.Sim.People.PeopleSamples.SpawnSpots(w.People.Life(home), families))
                w.Apply(new Wtw.Sim.World.Intervention(0, Wtw.Sim.World.Intervention.Kinds.SpawnFamily, new long[] { home, x, y }));
        long end = years * GameTime.TicksPerYear;
        for (long t = 0; t < end; t += GameTime.TicksPerDay)
        {
            w.AdvanceTo(Math.Min(end, t + GameTime.TicksPerDay));
            if (t % GameTime.TicksPerYear == 0) progress(t / (double)end, $"미리 돌리는 중 {t / GameTime.TicksPerYear}/{years}년 (사람 {w.People.AliveCount})");
        }
        GD.Print($"PRERUN years={years} alive={w.People.AliveCount} inhabited={w.People.Provinces.Count(kv => kv.Value.People.Exists(q => !q.Dead))} opened={w.People.OpenedProvinces().Count()} crossings={w.People.Stats.Crossings} names={w.People.LandNames.Count} langs={w.People.All.Where(q => !q.Dead).Select(q => q.LangId).Distinct().Count()}");
    }

    /// <summary>a 에서 b 까지 칸 수(limit 넘으면 limit).</summary>
    private static int Hops(Planet p, int a, int b, int limit)
    {
        var dist = new Dictionary<int, int> { [a] = 0 };
        var q = new Queue<int>(); q.Enqueue(a);
        while (q.Count > 0)
        {
            int c = q.Dequeue();
            if (c == b) return dist[c];
            if (dist[c] >= limit) continue;
            foreach (int nb in p.Grid.Neighbors(c)) if (!dist.ContainsKey(nb)) { dist[nb] = dist[c] + 1; q.Enqueue(nb); }
        }
        return limit;
    }

    /// <summary>가운데 땅에 마실 물이 없고, 이웃 땅에는 있는 따뜻한 칸 — 사람이 물을 찾아 경계를 넘는 모습을 보려고.</summary>
    private static int DryCell(Planet p)
    {
        foreach (int c in Wtw.Sim.People.PeopleSamples.Ranked(p, (pl, c) => pl.Biome[c] is Biome.Grassland or Biome.Savanna or Biome.Temperate && pl.RiverSize[c] == 0 && pl.Temp10[c] > 120 ? 100 - Math.Abs(pl.Temp10[c] - 200) / 5 : int.MinValue).Take(400))
        {
            var life = new Wtw.Sim.People.ProvinceLife(p, ProvinceCache.Get(p, c), Wtw.Sim.People.PeopleData.Default);
            if (life.DrinkTiles > 0) continue;
            foreach (int nb in p.Grid.Neighbors(c))
                if (p.IsLand(nb) && new Wtw.Sim.People.ProvinceLife(p, ProvinceCache.Get(p, nb), Wtw.Sim.People.PeopleData.Default).DrinkTiles > 50) return c;
        }
        return Wtw.Sim.People.PeopleSamples.Home(p);
    }

    /// <summary>샘과 개울이 가운데 가까이 있는 따뜻한 칸.</summary>
    private static int SpringCell(Planet p)
    {
        foreach (int c in Wtw.Sim.People.PeopleSamples.Ranked(p, (pl, c) => pl.Biome[c] is Biome.Temperate or Biome.Grassland or Biome.Rainforest && pl.RiverSize[c] == 0 ? 100 - Math.Abs(pl.Temp10[c] - 170) / 5 : int.MinValue).Take(300))
        {
            var m = ProvinceCache.Get(p, c);
            int springs = 0, streams = 0;
            for (int i = 0; i < HexFrame.TileCount; i++)
            {
                if ((m.Flags[i] & TileFlags.Spring) != 0 && HexFrame.BorderDistance(i) > 25) springs++;
                if ((m.Flags[i] & TileFlags.Stream) != 0) streams++;
            }
            if (springs > 0 && streams > 60) return c;
        }
        return Wtw.Sim.People.PeopleSamples.Home(p);
    }

    private void WaitCross(WorldSnapshot snap, double delta)
    {
        var pv = _main.Province;
        var host = _main.Host!;
        if (_crossTarget < 0)
        {
            if (host.SpeedLevel != 5) host.SetSpeed(5);
            for (int i = 0; i < snap.PeopleCount; i++)
            {
                var v = snap.People[i];
                bool crossedJustNow = false;
                foreach (var cr in snap.RecentCrossings) if (cr.Person == v.Id && cr.From == pv.Center && cr.Tick > host.RenderTick - Wtw.Sim.Time.GameTime.TicksPerHour) crossedJustNow = true;
                if (!v.Dead && ((v.Act == Wtw.Sim.People.Act.Cross && v.Walking && v.Cell == pv.Center) || crossedJustNow))
                {
                    _crossTarget = v.Id; _crossFrom = pv.Center;
                    host.SetSpeed(2);
                    if (!_crossNoFollow) { pv.Follow(v.Id); _main.OpenCardForTest(v.Id); }
                    GD.Print($"CROSS following {v.Id} ({v.Name}) from {v.Cell}");
                    break;
                }
            }
            return;
        }
        for (int i = 0; i < snap.PeopleCount; i++)
        {
            var v = snap.People[i];
            if (v.Id != _crossTarget) continue;
            if (_crossNoFollow && _sinceCross < 0 && v.Cell == _crossFrom && pv.PersonWorld(v.Id) is { } wp)
                pv.LookAt(wp.X / ProvinceView.TilePx, wp.Y / ProvinceView.TilePx, pv.Zoom); // 따라가지 않고 화면만 그 사람 쪽으로(경계를 넘으면 사라지고 표시가 남는다)
            if (_sinceCross < 0 && v.Cell != _crossFrom) { _sinceCross = 0; host.SetSpeed(1); GD.Print($"CROSS crossed {_crossFrom} → {v.Cell}"); }
        }
        if (_sinceCross >= 0) _sinceCross += delta;
    }

    // ───────── 사람(E3) ─────────

    /// <summary>동굴(입구가 열린)이 있고 밤에 쌀쌀한 칸 — 사람이 동굴로 들어가 자는 모습을 보려고.</summary>
    private static int CaveCell(Planet p)
    {
        foreach (int c in Wtw.Sim.People.PeopleSamples.Ranked(p, (pl, c) => pl.Terrain[c] is Terrain.Hill or Terrain.Plateau && pl.Temp10[c] is > 20 and < 110 && pl.Biome[c] is Biome.Temperate or Biome.Boreal or Biome.Grassland ? 100 - Math.Abs(pl.Temp10[c] - 70) / 5 + (pl.RiverSize[c] > 0 ? 20 : 0) : int.MinValue).Take(60))
        {
            var m = ProvinceCache.Get(p, c);
            int mouths = 0, caves = 0;
            for (int i = 0; i < HexFrame.TileCount; i++) if ((m.Flags[i] & TileFlags.Roof) != 0) { caves++; if (!m.IsHidden(i)) mouths++; }
            if (mouths > 0 && caves > 30) return c;
        }
        return Wtw.Sim.People.PeopleSamples.Home(p);
    }

    private void PeopleSteps(double delta, WorldSnapshot? snap)
    {
        var host = _main.Host!;
        var pv = _main.Province;
        var p = _main.Globe.Planet!;
        if (!_spawned)
        {
            _spawned = true;
            var map = pv.MapOf(pv.Center)!;
            var life = new Wtw.Sim.People.ProvinceLife(p, map, Wtw.Sim.People.PeopleData.Default); // 화면 쪽 사본(자리 고르기만)
            List<(int X, int Y)> spots;
            if (_enter == "cave")
            {
                // 동굴 입구 둘레
                int mouth = Enumerable.Range(0, HexFrame.TileCount).Where(i => (map.Flags[i] & TileFlags.Roof) != 0 && !map.IsHidden(i) && life.Walk[i]).OrderBy(i => Math.Abs(i % 150 - 75) + Math.Abs(i / 150 - 75)).FirstOrDefault(-1);
                spots = new List<(int, int)>();
                if (mouth >= 0)
                    for (int r = 2; r < 20 && spots.Count < _people + _families; r++)
                        foreach (int t in Enumerable.Range(0, HexFrame.TileCount).Where(t => life.Walk[t] && life.Region[t] == life.Region[mouth] && (map.Flags[t] & TileFlags.Roof) == 0 && Math.Max(Math.Abs(t % 150 - mouth % 150), Math.Abs(t / 150 - mouth / 150)) == r).Take(3))
                            if (spots.Count < _people + _families) spots.Add((t % 150, t / 150));
                if (spots.Count == 0) spots = Wtw.Sim.People.PeopleSamples.SpawnSpots(life, _people!.Value + _families);
            }
            else spots = Wtw.Sim.People.PeopleSamples.SpawnSpots(life, _people!.Value + _families);
            for (int k = 0; k < spots.Count; k++) _main.SpawnForTest(pv.Center, spots[k].X, spots[k].Y, k < _families);
            _spawnTick = (long)host.RenderTick;
            if (spots.Count > 0)
            {
                var lp = pv.LayoutOf(pv.Center, spots[0].X, spots[0].Y)!.Value;
                pv.LookAt(lp.X, lp.Y, _pzoom ?? 1.3f);
            }
            if (_waitDays is not null || _untilHour is not null) host.SetSpeed(_waitDays is not null ? 5 : 4);
            GD.Print($"PEOPLE spawned {spots.Count} spots in cell {pv.Center}");
            return;
        }
        if (snap is null || snap.PeopleCount == 0) return;
        _sincePeople += delta;
        if (!_peopleReady)
        {
            if (_waitDays is double wd && host.RenderTick - _spawnTick < wd * Wtw.Sim.Time.GameTime.TicksPerDay) return;
            if (_untilHour is int uh)
            {
                double lh = ProvinceReport.LocalHour(p, pv.Center, host.RenderTick);
                if (host.RenderTick - _spawnTick < Wtw.Sim.Time.GameTime.TicksPerHour * 6 || Math.Abs(lh - uh) > 0.6) { if (host.SpeedLevel != 4 && _waitDays is null) host.SetSpeed(4); return; }
            }
            _peopleReady = true;
            _sincePeople = 0;
            host.SetSpeed(_speedAfter);
            int first = -1, adult = -1;
            for (int i = 0; i < snap.PeopleCount; i++)
            {
                var v = snap.People[i];
                if (v.Dead || v.Cell != pv.Center) continue;
                if (first < 0) first = v.Id;
                if (adult < 0 && v.Stage >= Wtw.Sim.People.LifeStage.Adult && v.Carrier < 0) adult = v.Id;
            }
            int target = adult >= 0 ? adult : first;
            if (_focusTalk)
            {
                int talker = -1;
                for (int i = 0; i < snap.PeopleCount; i++) if (snap.People[i].Talking && snap.People[i].Say.Length > 0 && snap.People[i].Cell == pv.Center) { talker = snap.People[i].Id; break; }
                if (talker < 0) { _peopleReady = false; return; } // 이야기하는 사람이 생길 때까지 기다린다
                target = talker;
            }
            if (_enter == "cave")
            {
                // 동굴 안에 있는 사람을 화면 가운데로
                var map = pv.MapOf(pv.Center)!;
                for (int i = 0; i < snap.PeopleCount; i++)
                {
                    var v = snap.People[i];
                    v.PositionAt(host.RenderTick, out double x, out double y, out _, out _);
                    int t = (int)(y / 1000) * 150 + (int)(x / 1000);
                    if (!v.Dead && (uint)t < HexFrame.TileCount && (map.Flags[t] & TileFlags.Roof) != 0) { target = v.Id; break; }
                }
            }
            float z = _pzoom ?? 1.3f;
            double shift = _cardTab is not null ? (PersonCardPanel.Width / 2.0) / z / ProvinceView.TilePx : 0; // 카드에 가리지 않게 왼쪽으로
            if (pv.PersonWorld(target) is { } wpos) pv.LookAt(wpos.X / ProvinceView.TilePx + shift, wpos.Y / ProvinceView.TilePx - 0.6, z);
            if (_follow) pv.Follow(target);
            if (_cardTab is int tab) { _main.OpenCardForTest(target); _main.CardPanel.SetTab(tab); }
            if (_hoverPerson) { _hoverTarget = target; }
            if (_showDiscoveries) _main.ShowDiscoveriesForTest();
            _cave = _enter == "cave";
            GD.Print($"PEOPLE ready alive={snap.AliveCount} target={target} local_hour={ProvinceReport.LocalHour(p, pv.Center, host.RenderTick):F1}");
            return;
        }
        if (_pauseMeasure is double pm)
        {
            MeasureFrames(delta, pm);
            return;
        }
        if (_waitCross)
        {
            if (snap is not null) WaitCross(snap, delta);
            if (_sinceCross >= _shotAfter) Finish(true, $"CROSS done target={_crossTarget}");
            else if (_sincePeople > 300) Finish(false, "CROSS 시간 초과 — 아무도 경계를 넘지 않음");
            return;
        }
        if (_hoverTarget >= 0 && pv.PersonScreen(_hoverTarget) is { } sp)
        {
            Input.WarpMouse(sp);
            pv.SetHoverPerson(_hoverTarget);
            _main.HoverPersonForTest(_hoverTarget);
        }
        if (_globeAfter is double ga && !_globeDone && _sincePeople >= ga)
        {
            _globeDone = true;
            int c = pv.Center;
            _main.ExitForTest();
            _main.Globe.LookAtCell(c, 1.6f);
            _sincePeople = 0;
            return;
        }
        if (_sincePeople >= _shotAfter && (_globeAfter is null || _globeDone)) Finish(true, $"PEOPLE alive={snap.AliveCount} total={snap.PeopleCount}");
    }

    // (E4) 화면 한 장 그리는 시간: 1배속 pm 초 → 멈추고 1.5초 기다린 뒤 pm 초
    private int _pmPhase;
    private double _pmT;
    private readonly List<double> _pmRun = new(), _pmPaused = new();

    private void MeasureFrames(double delta, double pm)
    {
        var host = _main.Host!;
        _pmT += delta;
        switch (_pmPhase)
        {
            case 0 when _pmT > 3 && !_main.Province.Baking: _pmPhase = 1; _pmT = 0; break;
            case 1: _pmRun.Add(delta); if (_pmT > pm) { _pmPhase = 2; _pmT = 0; host.SetPaused(true); } break;
            case 2 when _pmT > 2.0: _pmPhase = 3; _pmT = 0; break;
            case 3: _pmPaused.Add(delta); if (_pmT > pm) _pmPhase = 4; break;
            case 4:
                double Ms(List<double> l) => l.Count == 0 ? 0 : l.Average() * 1000;
                GD.Print($"FRAMEMEASURE bake={ProvinceView.BakeGround} run_frames={_pmRun.Count} run_ms={Ms(_pmRun):F2} run_fps={(_pmRun.Count > 0 ? 1000 / Ms(_pmRun) : 0):F1} paused_frames={_pmPaused.Count} paused_fps={(_pmPaused.Count / pm):F1} bakes={ProvinceView.BakesDone} people={_lastSnap?.AliveCount}");
                Finish(true, "");
                break;
        }
    }

    private int _hoverTarget = -1;
    private bool _showDiscoveries, _expandDiscovery;
    private string? _look;
    public bool Popups;
    public string? Scene;
    private string? _skip;
    private bool _skipStarted, _skipPanelShot, _skipPanelShown, _skipMidShot, _loadPanelShot, _loadPanelShown;
    private bool _lookDone, _discShown;
    private int _lookPerson = -1, _lookFrames;
    private double _lookWait;
    private double? _pauseMeasure;
    private bool _focusTalk;
    private bool _cave;
    private int _speedAfter = 1;

    private int _eeCount;
    private double _eeTimer;
    private long _memStart;
    private List<int>? _eeCells;

    /// <summary>지방을 여러 번 드나들며 메모리가 계속 늘지 않는지(관리 메모리 + 엔진 메모리).</summary>
    private void EnterExit(double delta)
    {
        var p = _main.Globe.Planet;
        if (p is null) return;
        _eeCells ??= Enumerable.Range(0, p.CellCount).Where(p.IsLand).Where((c, k) => k % 37 == 0).Take(40).ToList();
        _eeTimer += delta;
        if (_eeTimer < 0.25) return;
        _eeTimer = 0;
        var pv = _main.Province;
        if (pv.IsOpen)
        {
            if (pv.Loading || pv.ShownCount == 0) return;
            _main.ExitForTest();
            _eeCount++;
            if (_eeCount == 3) { GC.Collect(); GC.WaitForPendingFinalizers(); _memStart = (long)Performance.GetMonitor(Performance.Monitor.MemoryStatic) + GC.GetTotalMemory(true); }
            if (_eeCount >= _enterExit)
            {
                GC.Collect(); GC.WaitForPendingFinalizers();
                long mem = (long)Performance.GetMonitor(Performance.Monitor.MemoryStatic) + GC.GetTotalMemory(true);
                long obj = (long)Performance.GetMonitor(Performance.Monitor.ObjectCount);
                Finish(true, $"ENTEREXIT n={_eeCount} 메모리(3번째 뒤 → 끝) {_memStart / 1048576.0:F1}MB → {mem / 1048576.0:F1}MB · 남은 지도 기억 {ProvinceCache.Count}개 · 엔진 객체 {obj}");
            }
            return;
        }
        int cell = _eeCells[_eeCount % _eeCells.Count];
        _main.ScopeForTest(_eeCount % 2 == 1);
        _main.EnterForTest(cell);
    }

    private static double[] Positions(WorldSnapshot snap, double tick)
    {
        var r = new double[(snap.DotCount + snap.PeopleCount) * 2];
        for (int i = 0; i < snap.DotCount; i++) snap.DotPosition(i, tick, out r[2 * i], out r[2 * i + 1]);
        for (int i = 0; i < snap.PeopleCount; i++) snap.People[i].PositionAt(tick, out r[2 * (snap.DotCount + i)], out r[2 * (snap.DotCount + i) + 1], out _, out _);
        return r;
    }

    private void Finish(bool ok, string message)
    {
        if (message.Length > 0) GD.Print(message);
        _frameTimes.Sort();
        double p99 = _frameTimes.Count > 0 ? _frameTimes[(int)(_frameTimes.Count * 0.99)] : 0;
        double avg = _frameTimes.Count > 0 ? _frameTimes.Average() : 0;
        // (E5) 지구본 사람 표시 확인: 표시판이 붙은 칸 = 지금 사람이 사는 칸(사람이 다 떠난 칸에는 표시가 없어야 한다) · 넘기기 뒤 켜진 화면
        if (_main.Snap is { } ms)
        {
            var marked = _main.Globe.MarkerCells().OrderBy(c => c).ToList();
            var alive = ms.CellPeople.Select(c => c.Cell).OrderBy(c => c).ToList();
            var extra = marked.Except(alive).ToList(); var missing = alive.Except(marked).ToList();
            GD.Print($"MARKERCHECK {(extra.Count == 0 && missing.Count == 0 ? "ok" : "FAIL")} marked={marked.Count} alive_cells={alive.Count} extra=[{string.Join(",", extra)}] missing=[{string.Join(",", missing)}]");
            GD.Print($"VIEW globe={_main.Globe.Visible} province={_main.Province.Visible} province_open={_main.Province.IsOpen}");
        }
        if (ProvinceView.PeopleFrames > 0) GD.Print($"PEOPLE draw_ms_avg={ProvinceView.PeopleMsTotal / ProvinceView.PeopleFrames:F2} frames={ProvinceView.PeopleFrames}");
        GD.Print($"FRAMES n={_frames} avg_fps={(avg > 0 ? 1 / avg : 0):F1} p99_ms={p99 * 1000:F1} max_ms={_maxFrame * 1000:F1} stalled_frames={_main.Host?.StalledFrames ?? 0}");
        var p = _main.Globe.Planet;
        if (p is not null)
            GD.Print($"PLANET seed={p.Seed} land={p.LandCount} lakes={p.LakeCount} continent={p.ContinentCells} islands={p.IslandCount} pentagons_on_land={p.PentagonsOnLand} gen_s={p.GenerationSeconds:F2}");
        if (_shotPath is not null)
        {
            var img = _main.GetViewport().GetTexture().GetImage();
            var err = img.SavePng(_shotPath);
            GD.Print($"SCREENSHOT {_shotPath} {err}");
        }
        _main.GetTree().Quit(ok ? 0 : 1);
    }
}
