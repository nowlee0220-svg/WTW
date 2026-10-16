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
                    Mode = v switch { "elev" => MapMode.Elevation, "biome" => MapMode.Biome, "temp" => MapMode.Temperature, "rain" => MapMode.Rain, "res" => MapMode.Resource, _ => MapMode.Natural };
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
                case "--untilhour": _untilHour = int.Parse(v); break;
                case "--globeafter": _globeAfter = double.Parse(v, inv); break;
                case "--waitdays": _waitDays = double.Parse(v, inv); break;
            }
        }
    }

    public void Start(Main main)
    {
        _main = main;
        if (_selftest && Seed is null) Seed = 7;
    }

    public void OnNotice(string n) => _lastNotice = n;
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
        else cell = int.TryParse(_select, out int sc) ? sc : ProvinceSamples.Find(p, _select!);
        g.LookAtCell(cell, _zoom ?? 1.7f);
        g.Select(cell);
        if (_whyOpen) _main.Panel.SetWhyOpen(true);
    }

    public void Tick(double delta, WorldSnapshot? snap)
    {
        if (!Active && !_selectPending) return;
        _elapsed += delta;
        _sinceReady += delta;
        if (_selectPending && _ready && _main.Globe.Planet is not null) { _selectPending = false; DoSelect(); }
        if (!Active) return;
        if (_enter is not null && _ready && _main.Globe.Planet is not null && !_provinceOpened && _enterExit == 0)
        {
            var p = _main.Globe.Planet;
            int cell = int.TryParse(_enter, out int ec) ? ec : _enter switch
            {
                "home" => Wtw.Sim.People.PeopleSamples.Home(p),
                "cold" => Wtw.Sim.People.PeopleSamples.Cold(p),
                "cave" => CaveCell(p),
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
        if (_enterExit > 0) { if (_ready) EnterExit(delta); return; }
        if (_elapsed > 1.0 && (_ready || StayOnNewWorld)) // 시작 직후는 빼고 잰다
        {
            _frames++;
            _maxFrame = Math.Max(_maxFrame, delta);
            _frameTimes.Add(delta);
        }
        if (_elapsed > 120) { Finish(false, "AUTOPILOT 시간 초과 (세계가 만들어지지 않음?)"); return; }
        if (_selftest) { if (_ready && snap is not null) { if (_people is not null && !_spawned) { if (_provinceReady) PeopleSteps(delta, snap); } else SelfTest(delta, snap); } }
        else if (_people is not null) { if (_provinceReady) PeopleSteps(delta, snap); }

        else if (_shotPath is not null && (StayOnNewWorld ? _elapsed : _enter is not null ? (_provinceReady ? _sinceProvince : -1) : (_ready ? _sinceReady : -1)) >= _shotAfter) Finish(true, "");
    }

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
            _cave = _enter == "cave";
            GD.Print($"PEOPLE ready alive={snap.AliveCount} target={target} local_hour={ProvinceReport.LocalHour(p, pv.Center, host.RenderTick):F1}");
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

    private int _hoverTarget = -1;
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
