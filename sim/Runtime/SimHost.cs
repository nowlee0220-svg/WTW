using System.Collections.Concurrent;
using System.Diagnostics;
using Wtw.Sim.Persistence;
using Wtw.Sim.Time;
using Wtw.Sim.World;

namespace Wtw.Sim.Runtime;

/// <summary>
/// 시뮬을 별도 스레드에서 돌리고 화면에 스냅샷을 건넨다.
/// 화면 시계(RenderTick)는 화면 쪽이 매 프레임 dt × 속도로 늘리고, 시뮬에게 "여기 조금 앞까지 계산해 둬" 라고 목표를 준다.
/// 시뮬이 늦으면 화면 시계가 시뮬이 계산한 곳에서 기다린다(뛰어넘지 않음 → 끊김 없이 느려질 뿐).
/// 세계(GameWorld)는 시뮬 스레드만 만진다. 저장·불러오기·새로 시작도 명령으로 넘겨 시뮬 스레드가 한다.
/// </summary>
public sealed class SimHost : IDisposable
{
    /// <summary>시뮬이 화면보다 앞서 계산해 두는 양(실제 시간 기준 초).</summary>
    public const double LeadSeconds = 0.05;
    /// <summary>한 번에 계산하는 최대 틱(이만큼마다 스냅샷을 내놓는다).</summary>
    public const long ChunkTicks = GameTime.TicksPerHour * 2;

    private readonly SnapshotBuffer _buffer = new();
    private readonly ConcurrentQueue<Action> _commands = new();
    private readonly ConcurrentQueue<string> _notices = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly object _targetLock = new();
    private readonly Thread _thread;
    private volatile bool _running = true;

    // 시뮬 스레드 전용
    private GameWorld _world;
    private long _epoch;

    // 목표 (잠금 안에서만)
    private long _target;
    private long _targetEpoch;

    // 화면 스레드 전용
    private double _renderTick;
    private long _renderEpoch = -1;
    private int _speed = 1;
    private bool _paused;

    public SimHost(GameWorld world)
    {
        _world = world;
        _target = world.Now;
        PublishSnapshot(0, 0);
        _thread = new Thread(Loop) { IsBackground = true, Name = "WTW-Sim" };
        _thread.Start();
    }

    // ───────── 화면 스레드 쪽 ─────────

    public int SpeedLevel => _speed;
    public bool Paused => _paused;
    public double RenderTick => _renderTick;
    /// <summary>시뮬이 늦어서 화면 시계가 기다린 프레임 수(끊김 측정용).</summary>
    public long StalledFrames { get; private set; }

    public void SetSpeed(int level) { _speed = Math.Clamp(level, 1, Speed.Max); _paused = false; }
    public void TogglePause() => _paused = !_paused;
    public void SetPaused(bool paused) => _paused = paused;

    /// <summary>
    /// 매 프레임 부른다. 화면 시계를 dt 만큼 움직이고, 그릴 스냅샷을 돌려준다.
    /// </summary>
    public WorldSnapshot Frame(double dtSeconds)
    {
        var snap = _buffer.Acquire();
        if (snap.Epoch != _renderEpoch)
        {
            _renderEpoch = snap.Epoch;
            _renderTick = snap.Now;
        }
        double tps = Speed.Tps(_speed);
        dtSeconds = Math.Clamp(dtSeconds, 0, 0.25); // 창을 끌다 놓는 등 긴 멈춤 뒤에 뛰지 않게
        if (!_paused)
        {
            double wanted = _renderTick + dtSeconds * tps;
            long target = (long)Math.Ceiling(wanted + tps * LeadSeconds) + 1;
            lock (_targetLock)
            {
                if (_targetEpoch == _renderEpoch && target > _target) _target = target;
            }
            _wake.Set();
            if (wanted > snap.Now) StalledFrames++;
            _renderTick = Math.Min(wanted, snap.Now);
        }
        else if (_renderTick < snap.Now)
        {
            // 멈춤: 시뮬이 미리 계산해 둔 곳까지만 마저 가서 선다 → 멈춘 화면 = 실제 세계 상태(저장과 일치)
            _renderTick = Math.Min(_renderTick + dtSeconds * tps, snap.Now);
        }
        return snap;
    }

    public bool TryGetNotice(out string notice) => _notices.TryDequeue(out notice!);

    public void Save(string path) => Post(() =>
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            SaveGame.SaveFile(_world, path);
            _notices.Enqueue($"저장했습니다 ({GameTime.Format(_world.Now)})");
        }
        catch (Exception e) { _notices.Enqueue("저장하지 못했습니다: " + e.Message); }
    });

    public void Load(string path) => Post(() =>
    {
        try
        {
            if (!File.Exists(path)) { _notices.Enqueue("저장 파일이 없습니다. 먼저 저장하세요."); return; }
            ReplaceWorld(SaveGame.LoadFile(path));
            _notices.Enqueue($"불러왔습니다 ({GameTime.Format(_world.Now)})");
        }
        catch (Exception e) { _notices.Enqueue("불러오지 못했습니다: " + e.Message); }
    });

    public void NewWorld(ulong seed, WorldConfig config) => Post(() =>
    {
        ReplaceWorld(GameWorld.Create(seed, config));
        _notices.Enqueue($"새 세계를 시작했습니다 (시드 {seed})");
    });

    public void Intervene(string kind, params long[] values) => Post(() =>
    {
        _world.Apply(new Intervention(_world.Now, kind, values));
        PublishSnapshot(_lastDaysPerSecond, _lastLoad);
    });

    private void Post(Action a) { _commands.Enqueue(a); _wake.Set(); }

    // ───────── 시뮬 스레드 쪽 ─────────

    private double _lastDaysPerSecond, _lastLoad;

    private void ReplaceWorld(GameWorld w)
    {
        _world = w;
        lock (_targetLock)
        {
            _epoch++;
            _targetEpoch = _epoch;
            _target = w.Now;
        }
        PublishSnapshot(0, 0);
    }

    private void PublishSnapshot(double daysPerSecond, double load)
    {
        _buffer.Back.Fill(_world, _epoch, daysPerSecond, load);
        _buffer.Publish();
    }

    private void Loop()
    {
        var wall = Stopwatch.StartNew();
        double windowStart = 0, busy = 0;
        long windowStartTick = _world.Now, windowEpoch = _epoch;
        while (_running)
        {
            while (_commands.TryDequeue(out var cmd)) cmd();

            long target;
            lock (_targetLock) target = _targetEpoch == _epoch ? _target : _world.Now;

            if (windowEpoch != _epoch) { windowEpoch = _epoch; windowStartTick = _world.Now; windowStart = wall.Elapsed.TotalSeconds; busy = 0; }
            double nowSec = wall.Elapsed.TotalSeconds;
            if (nowSec - windowStart >= 1.0)
            {
                double span = nowSec - windowStart;
                _lastDaysPerSecond = GameTime.Days(_world.Now - windowStartTick) / span;
                _lastLoad = Math.Min(1.0, busy / span);
                windowStart = nowSec; windowStartTick = _world.Now; busy = 0;
            }

            if (_world.Now < target)
            {
                double t0 = wall.Elapsed.TotalSeconds;
                _world.AdvanceTo(Math.Min(target, _world.Now + ChunkTicks));
                busy += wall.Elapsed.TotalSeconds - t0;
                PublishSnapshot(_lastDaysPerSecond, _lastLoad);
            }
            else
            {
                _wake.WaitOne(4);
            }
        }
    }

    public void Dispose()
    {
        _running = false;
        _wake.Set();
        _thread.Join(2000);
        _wake.Dispose();
    }
}
