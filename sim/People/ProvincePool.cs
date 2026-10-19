namespace Wtw.Sim.People;

/// <summary>
/// (S1) 지방 계산용 일꾼 스레드 묶음. 맞추는 시각(하루 240번)마다 "이 지방들을 나눠 해" → 모두 끝날 때까지 기다린다.
/// Parallel.ForEach 는 부를 때마다 일을 만들고 거두는 비용이 있어서, 일꾼을 세워 두고 잠깐 돌며 기다리다(바로 이어지는 맞추는 시각에 빠르게)
/// 오래 일이 없으면 잠든다. 누가 어느 지방을 맡든 결과는 같다(지방끼리 만지지 않으므로) — 빠르기만 바뀐다.
/// </summary>
internal sealed class ProvincePool : IDisposable
{
    /// <summary>한 번의 일: 0 .. Count−1 을 앞 번호부터 하나씩 집어 간다(늦게 깬 일꾼이 지난 일을 집어도 남은 것이 없다).</summary>
    private sealed class Job
    {
        public readonly Action<int> Body;
        public readonly int Count;
        public int Next, Pending;
        public Exception? Error;
        public Job(Action<int> body, int count) { Body = body; Count = count; Pending = count; }
    }

    private readonly Thread[] _threads;
    private readonly object _gate = new();
    private Job? _job;
    private long _generation;
    private volatile bool _stop;

    public int Workers => _threads.Length;

    // 세계마다 묶음을 만들면 측정 도구(시드 여럿)가 일꾼을 너무 많이 세운다 — 쓰고 돌려주는 묶음(일꾼 수별)
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, System.Collections.Concurrent.ConcurrentBag<ProvincePool>> Idle = new();
    public static ProvincePool Rent(int workers) => Idle.GetOrAdd(workers, _ => new()).TryTake(out var p) ? p : new ProvincePool(workers);
    public static void Return(ProvincePool p) => Idle.GetOrAdd(p.Workers, _ => new()).Add(p);

    public ProvincePool(int workers)
    {
        _threads = new Thread[Math.Max(0, workers)];
        for (int i = 0; i < _threads.Length; i++)
        {
            _threads[i] = new Thread(Loop) { IsBackground = true, Name = $"WTW-Province-{i + 1}" };
            _threads[i].Start();
        }
    }

    /// <summary>body(0 .. count−1) 를 일꾼들과 이 스레드가 나눠 한다. 모두 끝나면 돌아온다.</summary>
    public void Run(int count, Action<int> body)
    {
        if (count <= 0) return;
        var job = new Job(body, count);
        lock (_gate) { _job = job; _generation++; Monitor.PulseAll(_gate); }
        Work(job);
        var spin = new SpinWait();
        while (Volatile.Read(ref job.Pending) > 0) spin.SpinOnce(sleep1Threshold: -1);
        lock (_gate) if (ReferenceEquals(_job, job)) _job = null;
        if (job.Error is { } e) throw new AggregateException(e);
    }

    private static void Work(Job job)
    {
        while (true)
        {
            int i = Interlocked.Increment(ref job.Next) - 1;
            if (i >= job.Count) return;
            try { job.Body(i); }
            catch (Exception e) { job.Error ??= e; }
            finally { Interlocked.Decrement(ref job.Pending); }
        }
    }

    private void Loop()
    {
        long seen = 0;
        while (!_stop)
        {
            // 잠깐 돌며 다음 일을 기다리고(맞추는 시각은 잇달아 온다), 오래 없으면 잠든다
            var spin = new SpinWait();
            while (Interlocked.Read(ref _generation) == seen && !_stop && spin.Count < 100) spin.SpinOnce(sleep1Threshold: -1);
            Job? job;
            lock (_gate)
            {
                while (_generation == seen && !_stop) Monitor.Wait(_gate);
                seen = _generation;
                job = _job;
            }
            if (_stop) return;
            if (job is not null) Work(job);
        }
    }

    public void Dispose()
    {
        _stop = true;
        lock (_gate) Monitor.PulseAll(_gate);
    }
}
