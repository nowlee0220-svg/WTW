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
    /// <summary>메모리 정리 · 할당 셈(측정 앞뒤 차이).</summary>
    public readonly record struct GcMark(TimeSpan Pause, long Allocated, int Gen0, int Gen1, int Gen2)
    {
        public static GcMark Now() => new(GC.GetTotalPauseDuration(), GC.GetTotalAllocatedBytes(false), GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
        public string Since(GcMark a, double seconds) =>
            $"메모리 정리(GC) 멈춤 {(Pause - a.Pause).TotalSeconds:0.0}초({(Pause - a.Pause).TotalSeconds * 100 / Math.Max(1e-9, seconds):0.0}%) · 할당 {(Allocated - a.Allocated) / 1048576.0:N0}MB · 정리 횟수 0세대 {Gen0 - a.Gen0} · 1세대 {Gen1 - a.Gen1} · 2세대 {Gen2 - a.Gen2}";
    }

    public static void Skip(string scene, int years)
    {
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
    }
}
