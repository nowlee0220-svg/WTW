using Godot;
using Wtw.Sim.Planet;
using Wtw.Sim.Runtime;
using Wtw.Sim.Time;

namespace Wtw.Game;

/// <summary>
/// 자동 확인용(클라우드의 가상 화면에서 쓴다). 사람이 게임할 때는 아무 일도 하지 않는다.
///   --seed=N             새 세계 화면을 건너뛰고 바로 이 시드로 만든다 (--land=16000|20000|24000 --climate=cold|normal|warm)
///   --newworld           새 세계 화면에 머문다(스크린샷용)
///   --mode=terrain|biome|temp|rain|res  --res=copper   지도 모드
///   --select=auto|칸번호  칸을 골라 정보 창을 연다(auto = 비그늘이 있는 산 근처 땅)    --zoom=거리   --spin  지구본을 계속 돌린다
///   --screenshot=경로 --shot-after=초 : 세계가 생기고 그 시간 뒤 화면을 찍고 끝낸다.
///   --selftest : 달리는 중에 [저장] → 멈춘 화면 = 저장된 시각인지 → 더 돌림 → 불러오기 → 저장한 시각·점 위치로 돌아왔는지.
///   --dots=N --devdots : E0 점 시험(개발용 화면)
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
    private bool _spin;
    private string? _shotPath;
    private double _shotAfter = 4;
    private bool _selftest;

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

    public bool Active => _shotPath is not null || _selftest;

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
                    Mode = v switch { "biome" => MapMode.Biome, "temp" => MapMode.Temperature, "rain" => MapMode.Rain, "res" => MapMode.Resource, _ => MapMode.Terrain };
                    break;
                case "--res": Resource = Array.IndexOf(ResourceKinds.Ids, v) is int r && r >= 0 ? r : null; break;
                case "--newworld": StayOnNewWorld = true; break;
                case "--select": _select = v; break;
                case "--zoom": _zoom = float.Parse(v, inv); break;
                case "--spin": _spin = true; break;
                case "--screenshot": _shotPath = v; break;
                case "--shot-after": _shotAfter = double.Parse(v, inv); break;
                case "--selftest": _selftest = true; break;
            }
        }
    }

    public void Start(Main main)
    {
        _main = main;
        if (_selftest && Seed is null) Seed = 7;
    }

    public void OnNotice(string n) => _lastNotice = n;

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
    }

    public void Tick(double delta, WorldSnapshot? snap)
    {
        if (!Active && !_selectPending) return;
        _elapsed += delta;
        _sinceReady += delta;
        if (_selectPending && _ready && _main.Globe.Planet is not null) { _selectPending = false; DoSelect(); }
        if (!Active) return;
        if (_elapsed > 1.0 && (_ready || StayOnNewWorld)) // 시작 직후는 빼고 잰다
        {
            _frames++;
            _maxFrame = Math.Max(_maxFrame, delta);
            _frameTimes.Add(delta);
        }
        if (_elapsed > 120) { Finish(false, "AUTOPILOT 시간 초과 (세계가 만들어지지 않음?)"); return; }
        if (_selftest) { if (_ready && snap is not null) SelfTest(delta, snap); }
        else if (_shotPath is not null && (StayOnNewWorld ? _elapsed : (_ready ? _sinceReady : -1)) >= _shotAfter) Finish(true, "");
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
