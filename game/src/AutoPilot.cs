using Godot;
using Wtw.Sim.Runtime;
using Wtw.Sim.Time;

namespace Wtw.Game;

/// <summary>
/// 자동 확인용(클라우드의 가상 화면에서 쓴다). 사람이 게임할 때는 아무 일도 하지 않는다.
/// --screenshot=경로 --shot-after=초 : 그 시간 뒤 화면을 찍고 끝낸다.
/// --selftest : 게임 화면 안에서 저장 → 더 돌림 → 불러오기 → 저장한 시각·점 위치로 돌아왔는지 확인하고 결과를 출력.
/// 프레임 시간 통계(평균 fps, 가장 긴 프레임)도 출력한다.
/// </summary>
public sealed class AutoPilot
{
    public ulong? Seed;
    public int? Dots;
    public int? Speed;
    private string? _shotPath;
    private double _shotAfter = 4;
    private bool _selftest;

    private Main _main = null!;
    private double _elapsed;
    private int _frames;
    private double _maxFrame;
    private readonly List<double> _frameTimes = new();

    // 자체 시험 상태
    private int _stage;
    private double _stageTime;
    private long _savedTick;
    private double[] _savedPos = Array.Empty<double>();
    private string? _lastNotice;

    public bool Active => _shotPath is not null || _selftest;

    public void Parse(string[] args)
    {
        foreach (var a in args)
        {
            var kv = a.Split('=', 2);
            string v = kv.Length > 1 ? kv[1] : "";
            switch (kv[0])
            {
                case "--seed": Seed = ulong.Parse(v); break;
                case "--dots": Dots = int.Parse(v); break;
                case "--speed": Speed = int.Parse(v); break;
                case "--screenshot": _shotPath = v; break;
                case "--shot-after": _shotAfter = double.Parse(v, System.Globalization.CultureInfo.InvariantCulture); break;
                case "--selftest": _selftest = true; break;
            }
        }
    }

    public void Start(Main main) { _main = main; }

    public void OnNotice(string n) => _lastNotice = n;

    public void Tick(double delta, WorldSnapshot snap)
    {
        if (!Active) return;
        _elapsed += delta;
        if (_elapsed > 1.0) // 시작 직후 1초는 빼고 잰다
        {
            _frames++;
            _maxFrame = Math.Max(_maxFrame, delta);
            _frameTimes.Add(delta);
        }
        if (_selftest) SelfTest(delta, snap);
        else if (_shotPath is not null && _elapsed >= _shotAfter) Finish(true, "");
    }

    private void SelfTest(double delta, WorldSnapshot snap)
    {
        var host = _main.Host;
        _stageTime += delta;
        switch (_stage)
        {
            case 0 when _stageTime > 1.5:
                host.SetPaused(true); Next(); break;
            case 1 when _stageTime > 0.5:
                _savedTick = (long)host.RenderTick;
                _savedPos = Positions(snap, host.RenderTick);
                _lastNotice = null;
                host.Save(_main.QuickSavePath); Next(); break;
            case 2 when _lastNotice is not null:
                host.SetSpeed(4); Next(); break;
            case 3 when _stageTime > 1.5:
                host.SetPaused(true); _lastNotice = null; host.Load(_main.QuickSavePath); Next(); break;
            case 4 when _lastNotice is not null && _stageTime > 0.5:
            {
                long now = (long)host.RenderTick;
                var pos = Positions(snap, host.RenderTick);
                double maxDiff = 0;
                for (int i = 0; i < Math.Min(pos.Length, _savedPos.Length); i++) maxDiff = Math.Max(maxDiff, Math.Abs(pos[i] - _savedPos[i]));
                bool ok = now == _savedTick && pos.Length == _savedPos.Length && maxDiff < 1e-6;
                Finish(ok, $"SELFTEST saveload {(ok ? "OK" : "FAIL")}: 저장 시각 {GameTime.FormatWithMinutes(_savedTick)}({_savedTick}) / 불러온 뒤 {GameTime.FormatWithMinutes(now)}({now}) / 점 위치 최대 차이 {maxDiff}");
                break;
            }
            case 4 when _stageTime > 10:
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
        GD.Print($"FRAMES n={_frames} avg_fps={(avg > 0 ? 1 / avg : 0):F1} p99_ms={p99 * 1000:F1} max_ms={_maxFrame * 1000:F1} stalled_frames={_main.Host.StalledFrames}");
        if (_shotPath is not null)
        {
            var img = _main.GetViewport().GetTexture().GetImage();
            var err = img.SavePng(_shotPath);
            GD.Print($"SCREENSHOT {_shotPath} {err}");
        }
        _main.GetTree().Quit(ok ? 0 : 1);
    }
}
