using System.Diagnostics;
using Wtw.Sim.Persistence;
using Wtw.Sim.Time;
using Wtw.Sim.World;

/// <summary>
/// (S1) 속도 측정 도우미.
///   skip  [--scene game/scenes/village-50y.wtw] [--years 50]
///         : 시험용 장면을 불러와 [넘기기]처럼 Y 년을 하루씩 돌린다 → 걸린 초 · 사람 하루당 · 메모리 정리(GC) 시간
/// </summary>
public static class SpeedBench
{
    /// <summary>
    /// (S1) --threads auto: 시드를 나눠 돌릴 때 코어를 놀리지 않게 — 돌고 있는 세계 수로 코어를 나눠 세계마다 CPU 수를 날마다 다시 정한다
    /// (끝 무렵 시드가 몇 개만 남으면 남은 세계가 코어를 더 쓴다). CPU 수는 결과와 상관없다.
    /// </summary>
    public static bool AutoThreads;
    private static int _active;
    public static void WorldStarted() => Interlocked.Increment(ref _active);
    public static void WorldDone() => Interlocked.Decrement(ref _active);
    public static void Pace(GameWorld w)
    {
        if (AutoThreads) w.Threads = Math.Max(1, Environment.ProcessorCount / Math.Max(1, Volatile.Read(ref _active)));
    }

    /// <summary>메모리 정리 · 할당 셈(측정 앞뒤 차이).</summary>
    public readonly record struct GcMark(TimeSpan Pause, long Allocated, int Gen0, int Gen1, int Gen2)
    {
        public static GcMark Now() => new(GC.GetTotalPauseDuration(), GC.GetTotalAllocatedBytes(false), GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
        public string Since(GcMark a, double seconds) =>
            $"메모리 정리(GC) 멈춤 {(Pause - a.Pause).TotalSeconds:0.0}초({(Pause - a.Pause).TotalSeconds * 100 / Math.Max(1e-9, seconds):0.0}%) · 할당 {(Allocated - a.Allocated) / 1048576.0:N0}MB · 정리 횟수 0세대 {Gen0 - a.Gen0} · 1세대 {Gen1 - a.Gen1} · 2세대 {Gen2 - a.Gen2}";
    }

    /// <summary>(E5c 개발용) --detail: 넘기는 동안 고른 일(일 · 까닭)마다 몇 번인지 · 발견 요약을 찍는다(시뮬 결과에는 영향 없음).</summary>
    public static bool Detail;

    public static void Skip(string scene, int years)
    {
        var acts = new System.Collections.Concurrent.ConcurrentDictionary<string, long>();
        if (Detail) Wtw.Sim.People.PeopleSim.ActHook = (_, p) => acts.AddOrUpdate($"{p.Act}/{p.Why}", 1, (_, v) => v + 1);
        var w = SaveGame.LoadFile(scene);
        w.EnsurePlanet(null);
        long start = w.Now, end = start + years * GameTime.TicksPerYear;
        long personDays = 0;
        w.DayBoundary += (world, day) => personDays += world.People.AliveCount;
        int alive0 = w.People.AliveCount;
        // 데우기(JIT) 없이 잰다 — 게임에서 [넘기기]를 누를 때와 같게
        var gc = GcMark.Now();
        var sw = Stopwatch.StartNew();
        for (long t = start; t < end; t += GameTime.TicksPerDay) w.AdvanceTo(Math.Min(end, t + GameTime.TicksPerDay));
        double sec = sw.Elapsed.TotalSeconds;
        Console.WriteLine($"장면 {Path.GetFileName(scene)} → {years}년 넘기기: {sec:0.0}초 · 처음 {alive0}명 → 끝 {w.People.AliveCount}명 · 사람 하루 {personDays:N0}개 · 사람 하루당 {sec * 1e6 / Math.Max(1, personDays):0}µs");
        Console.WriteLine("  " + GcMark.Now().Since(gc, sec));
        Console.WriteLine($"  지문 {SaveGame.Fingerprint(w):X16}");
        if (Detail)
        {
            Console.WriteLine("  고른 일(많은 순): " + string.Join(" · ", acts.OrderByDescending(kv => kv.Value).Take(40).Select(kv => $"{kv.Key} {kv.Value:N0}")));
            Console.WriteLine(DiscoverBench.Summary(w));
            var st = w.People.Stats; var cr = w.People.Craft;
            Console.WriteLine("  계획(바람 · 실험/아는 법): " + string.Join(" · ", cr.Wishes.Select(x => $"{x.Name} {st.PlanByWish[x.Index * 2]}/{st.PlanByWish[x.Index * 2 + 1]}")));
            Console.WriteLine("  아는 법 계획(규칙): " + string.Join(" · ", cr.Rules.Where(r => st.PlanKnownRule[r.Index] > 500).OrderByDescending(r => st.PlanKnownRule[r.Index]).Select(r => $"{r.Id} {st.PlanKnownRule[r.Index]}")));
            Console.WriteLine("  재료 구하기(바람 · 실험/아는 법): " + string.Join(" · ", cr.Wishes.Select(x => $"{x.Name} {st.MatByWish[x.Index * 2]}/{st.MatByWish[x.Index * 2 + 1]}")));
        }
    }
}
