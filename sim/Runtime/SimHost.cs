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
            long target = (long)Math.Ceiling(wanted + tps * Math.Max(LeadSeconds, dtSeconds * 2)) + 1; // 화면이 느리면(프레임이 길면) 그만큼 더 앞서 계산
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

    // ───────── (E4) 넘기기 ─────────

    /// <summary>넘기는 중의 모습(화면 스레드가 읽는다).</summary>
    public sealed record SkipInfo(string Goal, double Years, double Progress);

    private volatile SkipInfo? _skipInfo;
    private volatile List<string>? _skipSummary;
    private volatile bool _skipping;

    /// <summary>넘기는 중이면 그 모습, 아니면 null.</summary>
    public SkipInfo? Skipping => _skipping ? _skipInfo : null;
    public bool IsSkipping => _skipping;

    /// <summary>목표까지 화면 없이 최대 속도로 넘긴다. 끝나면 TryTakeSkipSummary 로 "그동안 일어난 일".</summary>
    public void StartSkip(SkipGoal goal)
    {
        _skipping = true;
        _skipInfo = new SkipInfo(goal.Name, 0, 0);
        Post(() => { _skip = new SkipRun(_world, goal); });
    }

    public void StopSkip() => Post(() => _skip?.Cancel());

    public bool TryTakeSkipSummary(out List<string> lines)
    {
        lines = _skipSummary!;
        if (lines is null) return false;
        _skipSummary = null;
        return true;
    }

    /// <summary>
    /// 멈춘 다음 저장한다. 시뮬은 이미 정한 목표(화면보다 조금 앞)까지만 마저 계산하고 서며, 화면도 그 시각까지 와서 선다.
    /// 그 상태를 저장하므로 "멈춘 화면에 보이는 것 = 저장된 것".
    /// </summary>
    public void Save(string path)
    {
        _paused = true;
        Post(() => _pendingSave = path);
    }

    /// <summary>마지막으로 저장한 세계 시각(확인용).</summary>
    public long LastSavedTick => Interlocked.Read(ref _lastSavedTick);
    private long _lastSavedTick = -1;

    private void DoSave(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            SaveGame.SaveFile(_world, path);
            Interlocked.Exchange(ref _lastSavedTick, _world.Now);
            _notices.Enqueue($"멈추고 저장했습니다 ({GameTime.Format(_world.Now)})");
        }
        catch (Exception e) { _notices.Enqueue("저장하지 못했습니다: " + e.Message); }
    }

    public void Load(string path) => Post(() =>
    {
        try
        {
            if (!File.Exists(path)) { _notices.Enqueue("저장 파일이 없습니다. 먼저 저장하세요."); return; }
            var w = SaveGame.LoadFile(path);
            w.EnsurePlanet(null); // 행성은 시드 + 설정으로 다시 만든다(같은 행성이 기억에 있으면 그것)
            ReplaceWorld(w);
            _notices.Enqueue($"불러왔습니다 ({GameTime.Format(_world.Now)})");
        }
        catch (Exception e) { _notices.Enqueue("불러오지 못했습니다: " + e.Message); }
    });

    /// <summary>다른 스레드에서 다 만든 세계(행성 포함)로 바꾼다.</summary>
    public void Adopt(GameWorld world, string notice) => Post(() =>
    {
        _pendingSave = null;
        ReplaceWorld(world);
        _notices.Enqueue(notice);
    });

    public void Intervene(string kind, params long[] values) => Post(() =>
    {
        _world.Apply(new Intervention(_world.Now, kind, values));
        PublishSnapshot(_lastDaysPerSecond, _lastLoad);
    });

    private void Post(Action a) { _commands.Enqueue(a); _wake.Set(); }

    // ── 인물 카드 · 사람 목록 (시뮬 스레드가 만들어 건넨다 — 보기만 해도 세계는 바뀌지 않는다) ──
    private readonly People.PersonCard?[] _cards = new People.PersonCard?[2];
    private readonly bool[] _cardBusy = new bool[2];
    private volatile List<People.PersonRow>? _rows;
    private volatile bool _rowsBusy;

    /// <summary>카드 자리: 0 = 큰 카드, 1 = 마우스를 올린 작은 카드.</summary>
    public People.PersonCard? Card(int slot) => Volatile.Read(ref _cards[slot]);
    public List<People.PersonRow>? Rows => _rows;

    public void RequestCard(int id, int slot = 0)
    {
        if (Volatile.Read(ref _cardBusy[slot])) return;
        Volatile.Write(ref _cardBusy[slot], true);
        Post(() => { try { Volatile.Write(ref _cards[slot], _world.People.Card(id)); } finally { Volatile.Write(ref _cardBusy[slot], false); } });
    }

    public void RequestRows(int cell)
    {
        if (_rowsBusy) return;
        _rowsBusy = true;
        Post(() => { try { _rows = _world.People.Rows(cell); } finally { _rowsBusy = false; } });
    }

    // ───────── 시뮬 스레드 쪽 ─────────

    private double _lastDaysPerSecond, _lastLoad;
    private string? _pendingSave;
    private SkipRun? _skip;

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

            if (_skip is not null)
            {
                // 넘기기: 목표 · 위급 · 그만까지 하루씩, 화면 시계를 기다리지 않고. 0.25초마다 스냅샷.
                double t0 = wall.Elapsed.TotalSeconds;
                bool done = false;
                while (!done && wall.Elapsed.TotalSeconds - t0 < 0.25 && _commands.IsEmpty) done = _skip.Step(_world);
                busy += wall.Elapsed.TotalSeconds - t0;
                _skipInfo = new SkipInfo(_skip.Goal.Name, _skip.YearsPassed(_world), _skip.Goal.Progress(_world));
                if (_skip.Done)
                {
                    _skipSummary = _skip.Summary(_world);
                    _skip = null;
                    lock (_targetLock) { _epoch++; _targetEpoch = _epoch; _target = _world.Now; } // 화면 시계가 새 시각으로 건너뛴다
                    _skipping = false;
                }
                PublishSnapshot(_lastDaysPerSecond, _lastLoad);
                continue;
            }
            if (_pendingSave is not null && _world.Now >= target)
            {
                DoSave(_pendingSave);
                _pendingSave = null;
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
