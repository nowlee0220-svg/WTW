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
///   --pzoom=배율  지방 화면 확대   --focus=river|coast|edge|object|stone|ore|cave|gravel  --resview 자원 보기  그곳을 화면 가운데로   --hover  오브젝트 하나에 마우스를 올린다(카드)   --tileclick  그 타일 정보 창
///   --enterexit=N : 지방을 N 번 드나들며 메모리를 잰다(지구본 ↔ 지방)
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
    private float? _pzoom;
    private int _enterExit;
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
                case "--speed": Speed = int.Parse(v); break;
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
            }
        }
    }

    public void Start(Main main)
    {
        _main = main;
        if (_selftest && Seed is null) Seed = 7;
    }

    public void OnNotice(string n) => _lastNotice = n;

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
        else cell = int.Parse(_select!);
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
            int cell = int.TryParse(_enter, out int ec) ? ec : ProvinceSamples.Find(p, _enter);
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
        if (_selftest) { if (_ready && snap is not null) SelfTest(delta, snap); }
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
                Finish(ok, $"SELFTEST saveload {(ok ? "OK" : "FAIL")}: {_saveCheck} / 저장 시각 {GameTime.FormatWithMinutes(_savedTick)}({_savedTick}) / 불러온 뒤 {GameTime.FormatWithMinutes(now)}({now}) / 점 {pos.Length / 2}개 위치 최대 차이 {maxDiff}");
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
        GD.Print($"PROVINCE cell={pv.Center} neighbors={pv.Neighbors} shown={pv.ShownCount} gen_s={m.GenerationSeconds:F3} focus=({fx},{fy}) zoom={pv.Zoom:F3}");
    }

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
        var r = new double[snap.DotCount * 2];
        for (int i = 0; i < snap.DotCount; i++) snap.DotPosition(i, tick, out r[2 * i], out r[2 * i + 1]);
        return r;
    }

    private void Finish(bool ok, string message)
    {
        if (message.Length > 0) GD.Print(message);
        _frameTimes.Sort();
        double p99 = _frameTimes.Count > 0 ? _frameTimes[(int)(_frameTimes.Count * 0.99)] : 0;
        double avg = _frameTimes.Count > 0 ? _frameTimes.Average() : 0;
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
