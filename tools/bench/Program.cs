using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Wtw.Sim.Persistence;
using Wtw.Sim.Time;
using Wtw.Sim.World;

// WTW 측정 도구: 헤드리스로 "점 N 개 × D 일" 을 돌려 게임 하루/초 · 메모리 · 예약 한 번당 평균 시간을 잰다.
// 사용: dotnet run -c Release --project tools/bench -- [--dots 500] [--days 100] [--seeds 1] [--seed 1]
//        [--parallel N] [--out result.json] [--baseline tools/bench/baseline.json] [--save-baseline]

var opt = Options.Parse(args);
Console.WriteLine($"WTW bench: 점 {opt.Dots}개 × {opt.Days}일, 시드 {opt.Seeds}개(첫 시드 {opt.FirstSeed}), 동시 {opt.Parallel}");

// 데우기 (JIT)
RunOne(opt.FirstSeed, opt.Dots, 2);

var results = new SeedResult[opt.Seeds];
var wall = Stopwatch.StartNew();
Parallel.For(0, opt.Seeds, new ParallelOptions { MaxDegreeOfParallelism = opt.Parallel },
    i => results[i] = RunOne(opt.FirstSeed + (ulong)i, opt.Dots, opt.Days));
wall.Stop();

var sortedDps = results.Select(r => r.DaysPerSecond).OrderBy(x => x).ToArray();
var summary = new BenchResult
{
    Dots = opt.Dots,
    Days = opt.Days,
    Seeds = opt.Seeds,
    Parallel = opt.Parallel,
    MedianDaysPerSecond = Median(sortedDps),
    MedianMicrosPerEvent = Median(results.Select(r => r.MicrosPerEvent).OrderBy(x => x).ToArray()),
    MedianEventsPerDay = Median(results.Select(r => r.EventsPerDay).OrderBy(x => x).ToArray()),
    PeakWorkingSetMb = Math.Round(Process.GetCurrentProcess().PeakWorkingSet64 / 1048576.0, 1),
    ManagedHeapMb = Math.Round(GC.GetTotalMemory(false) / 1048576.0, 1),
    WallSeconds = Math.Round(wall.Elapsed.TotalSeconds, 3),
    Machine = $"{Environment.ProcessorCount} cores, {System.Runtime.InteropServices.RuntimeInformation.OSDescription}",
    PerSeed = results,
};

foreach (var r in results)
    Console.WriteLine($"  시드 {r.Seed}: 하루/초 {r.DaysPerSecond:F1} · 예약 {r.Events:N0}개 · 예약당 {r.MicrosPerEvent:F3}µs · 지문 {r.Fingerprint:X16}");
Console.WriteLine($"중앙값: 게임 하루/초 {summary.MedianDaysPerSecond:F1} · 예약당 {summary.MedianMicrosPerEvent:F3}µs · 하루당 예약 {summary.MedianEventsPerDay:F0} · 최대 메모리 {summary.PeakWorkingSetMb}MB · 걸린 시간 {summary.WallSeconds}s");

var json = JsonSerializer.Serialize(summary, BenchJson.Default.BenchResult);
if (opt.Out is not null) { File.WriteAllText(opt.Out, json); Console.WriteLine($"저장: {opt.Out}"); }

if (opt.SaveBaseline)
{
    File.WriteAllText(opt.Baseline, json);
    Console.WriteLine($"기준 파일로 저장: {opt.Baseline}");
}
else if (File.Exists(opt.Baseline))
{
    var b = JsonSerializer.Deserialize(File.ReadAllText(opt.Baseline), BenchJson.Default.BenchResult)!;
    if (b.Dots != summary.Dots || b.Days != summary.Days)
        Console.WriteLine($"기준 비교: 조건이 다름(기준 점 {b.Dots}개 × {b.Days}일) — 비교하지 않음");
    else
    {
        double ratio = summary.MedianDaysPerSecond / b.MedianDaysPerSecond;
        string verdict = ratio > 1.20 ? "빨라짐" : ratio < 0.80 ? "느려짐" : "비슷함(±20% 안 — 클라우드 측정은 이 정도 흔들린다)";
        Console.WriteLine($"기준 비교: 하루/초 {b.MedianDaysPerSecond:F1} → {summary.MedianDaysPerSecond:F1} ({ratio * 100 - 100:+0;-0}%) → {verdict}");
    }
}
else Console.WriteLine($"기준 파일이 없습니다: {opt.Baseline} (--save-baseline 으로 만든다)");

static SeedResult RunOne(ulong seed, int dots, int days)
{
    var w = GameWorld.Create(seed, new WorldConfig { DotCount = dots });
    var sw = Stopwatch.StartNew();
    w.AdvanceTo(days * GameTime.TicksPerDay);
    sw.Stop();
    double secs = Math.Max(sw.Elapsed.TotalSeconds, 1e-9);
    return new SeedResult
    {
        Seed = seed,
        Seconds = Math.Round(secs, 4),
        DaysPerSecond = Math.Round(days / secs, 1),
        Events = w.EventsProcessed,
        EventsPerDay = Math.Round(w.EventsProcessed / (double)days, 1),
        MicrosPerEvent = Math.Round(secs * 1e6 / Math.Max(1, w.EventsProcessed), 4),
        Fingerprint = SaveGame.Fingerprint(w),
    };
}

static double Median(double[] sorted) =>
    sorted.Length == 0 ? 0 : sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;

sealed class Options
{
    public int Dots = 500, Days = 100, Seeds = 1, Parallel = Environment.ProcessorCount;
    public ulong FirstSeed = 1;
    public string? Out;
    public string Baseline = "tools/bench/baseline.json";
    public bool SaveBaseline;

    public static Options Parse(string[] a)
    {
        var o = new Options();
        for (int i = 0; i < a.Length; i++)
        {
            string Next() => i + 1 < a.Length ? a[++i] : throw new ArgumentException($"{a[i]} 뒤에 값이 필요합니다");
            switch (a[i])
            {
                case "--dots": o.Dots = int.Parse(Next()); break;
                case "--days": o.Days = int.Parse(Next()); break;
                case "--seeds": o.Seeds = int.Parse(Next()); break;
                case "--seed": o.FirstSeed = ulong.Parse(Next()); break;
                case "--parallel": o.Parallel = Math.Max(1, int.Parse(Next())); break;
                case "--out": o.Out = Next(); break;
                case "--baseline": o.Baseline = Next(); break;
                case "--save-baseline": o.SaveBaseline = true; break;
                default: throw new ArgumentException($"모르는 옵션: {a[i]}");
            }
        }
        return o;
    }
}

public sealed class SeedResult
{
    public ulong Seed { get; set; }
    public double Seconds { get; set; }
    public double DaysPerSecond { get; set; }
    public long Events { get; set; }
    public double EventsPerDay { get; set; }
    public double MicrosPerEvent { get; set; }
    public ulong Fingerprint { get; set; }
}

public sealed class BenchResult
{
    public int Dots { get; set; }
    public int Days { get; set; }
    public int Seeds { get; set; }
    public int Parallel { get; set; }
    public double MedianDaysPerSecond { get; set; }
    public double MedianMicrosPerEvent { get; set; }
    public double MedianEventsPerDay { get; set; }
    public double PeakWorkingSetMb { get; set; }
    public double ManagedHeapMb { get; set; }
    public double WallSeconds { get; set; }
    public string Machine { get; set; } = "";
    public SeedResult[] PerSeed { get; set; } = Array.Empty<SeedResult>();
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(BenchResult))]
partial class BenchJson : JsonSerializerContext { }
