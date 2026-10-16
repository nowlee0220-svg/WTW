using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Wtw.Sim.Persistence;
using Wtw.Sim.Planet;
using Wtw.Sim.Time;
using Wtw.Sim.World;

// WTW 측정 도구: 헤드리스로 "점 N 개 × D 일" 을 돌려 게임 하루/초 · 메모리 · 예약 한 번당 평균 시간을 잰다.
// 행성: --planet 이면 행성 만들기 시간 · 땅 칸 수 · 대륙 비율 · 섬 수 · 오각형 · 호수 · 강을 잰다(--land N 으로 땅 하나만).
// 사용: dotnet run -c Release --project tools/bench -- [--dots 500] [--days 100] [--seeds 1] [--seed 1]
//        [--planet] [--land 16000] [--parallel N] [--out result.json] [--baseline tools/bench/baseline.json] [--save-baseline]
// 기준 파일은 잰 컴퓨터(코어 수 + CPU 이름)가 같을 때만 비교한다.

var opt = Options.Parse(args);
var machine = MachineInfo.Current();
Console.WriteLine($"컴퓨터: {machine}");
var summary = new BenchResult { Machine = machine.ToString(), Cpu = machine.Cpu, Cores = machine.Cores };

if (opt.Dump is not null) { DumpPlanet(opt); return; }
if (opt.Planet) summary.Planet = RunPlanets(opt);
else RunDots(opt, summary);

var json = JsonSerializer.Serialize(summary, BenchJson.Default.BenchResult);
if (opt.Out is not null) { File.WriteAllText(opt.Out, json); Console.WriteLine($"저장: {opt.Out}"); }

if (opt.SaveBaseline)
{
    // 기준 파일 하나에 점 측정과 행성 측정을 같이 둔다(이번에 잰 쪽만 바꾼다)
    BenchResult b = File.Exists(opt.Baseline) ? JsonSerializer.Deserialize(File.ReadAllText(opt.Baseline), BenchJson.Default.BenchResult)! : new BenchResult();
    if (b.Cpu != summary.Cpu || b.Cores != summary.Cores) b = new BenchResult(); // 다른 컴퓨터의 값은 섞지 않는다
    b.Machine = summary.Machine; b.Cpu = summary.Cpu; b.Cores = summary.Cores;
    if (opt.Planet) b.Planet = summary.Planet;
    else
    {
        b.Dots = summary.Dots; b.Days = summary.Days; b.Seeds = summary.Seeds; b.Parallel = summary.Parallel;
        b.MedianDaysPerSecond = summary.MedianDaysPerSecond; b.MedianMicrosPerEvent = summary.MedianMicrosPerEvent;
        b.MedianEventsPerDay = summary.MedianEventsPerDay; b.PeakWorkingSetMb = summary.PeakWorkingSetMb;
        b.ManagedHeapMb = summary.ManagedHeapMb; b.WallSeconds = summary.WallSeconds; b.PerSeed = summary.PerSeed;
    }
    File.WriteAllText(opt.Baseline, JsonSerializer.Serialize(b, BenchJson.Default.BenchResult));
    Console.WriteLine($"기준 파일로 저장: {opt.Baseline}");
}
else if (File.Exists(opt.Baseline))
{
    var b = JsonSerializer.Deserialize(File.ReadAllText(opt.Baseline), BenchJson.Default.BenchResult)!;
    if (string.IsNullOrEmpty(b.Cpu) || b.Cpu != summary.Cpu || b.Cores != summary.Cores)
        Console.WriteLine($"기준 비교: 다른 컴퓨터 — 비교 안 함 (기준: {(string.IsNullOrEmpty(b.Machine) ? "모름" : b.Machine)}). 이 컴퓨터의 기준을 만들려면 --save-baseline");
    else if (opt.Planet)
    {
        if (b.Planet is null) Console.WriteLine("기준 비교: 기준 파일에 행성 측정이 없음");
        else Compare("행성 만들기 시간(초, 작을수록 좋음)", b.Planet.MedianSeconds, summary.Planet!.MedianSeconds, lowerIsBetter: true);
    }
    else if (b.Dots != summary.Dots || b.Days != summary.Days)
        Console.WriteLine($"기준 비교: 조건이 다름(기준 점 {b.Dots}개 × {b.Days}일) — 비교하지 않음");
    else Compare("하루/초", b.MedianDaysPerSecond, summary.MedianDaysPerSecond, lowerIsBetter: false);
}
else Console.WriteLine($"기준 파일이 없습니다: {opt.Baseline} (--save-baseline 으로 만든다)");

static void Compare(string what, double before, double after, bool lowerIsBetter)
{
    double ratio = after / before;
    bool better = lowerIsBetter ? ratio < 0.80 : ratio > 1.20;
    bool worse = lowerIsBetter ? ratio > 1.20 : ratio < 0.80;
    string verdict = better ? "빨라짐" : worse ? "느려짐" : "비슷함(±20% 안 — 클라우드 측정은 이 정도 흔들린다)";
    int pct = (int)Math.Round(ratio * 100 - 100);
    Console.WriteLine($"기준 비교: {what} {before:0.###} → {after:0.###} ({(pct > 0 ? "+" : "")}{pct}%) → {verdict}");
}

static void RunDots(Options opt, BenchResult summary)
{
    Console.WriteLine($"WTW bench: 점 {opt.Dots}개 × {opt.Days}일, 시드 {opt.Seeds}개(첫 시드 {opt.FirstSeed}), 동시 {opt.Parallel}");
    RunOne(opt.FirstSeed, opt.Dots, 2); // 데우기 (JIT)
    var results = new SeedResult[opt.Seeds];
    var wall = Stopwatch.StartNew();
    Parallel.For(0, opt.Seeds, new ParallelOptions { MaxDegreeOfParallelism = opt.Parallel },
        i => results[i] = RunOne(opt.FirstSeed + (ulong)i, opt.Dots, opt.Days));
    wall.Stop();

    summary.Dots = opt.Dots; summary.Days = opt.Days; summary.Seeds = opt.Seeds; summary.Parallel = opt.Parallel;
    summary.MedianDaysPerSecond = Median(results.Select(r => r.DaysPerSecond));
    summary.MedianMicrosPerEvent = Median(results.Select(r => r.MicrosPerEvent));
    summary.MedianEventsPerDay = Median(results.Select(r => r.EventsPerDay));
    summary.PeakWorkingSetMb = Math.Round(Process.GetCurrentProcess().PeakWorkingSet64 / 1048576.0, 1);
    summary.ManagedHeapMb = Math.Round(GC.GetTotalMemory(false) / 1048576.0, 1);
    summary.WallSeconds = Math.Round(wall.Elapsed.TotalSeconds, 3);
    summary.PerSeed = results;

    foreach (var r in results)
        Console.WriteLine($"  시드 {r.Seed}: 하루/초 {r.DaysPerSecond:F1} · 예약 {r.Events:N0}개 · 예약당 {r.MicrosPerEvent:F3}µs · 지문 {r.Fingerprint:X16}");
    Console.WriteLine($"중앙값: 게임 하루/초 {summary.MedianDaysPerSecond:F1} · 예약당 {summary.MedianMicrosPerEvent:F3}µs · 하루당 예약 {summary.MedianEventsPerDay:F0} · 최대 메모리 {summary.PeakWorkingSetMb}MB · 걸린 시간 {summary.WallSeconds}s");
}

// 개발용: 행성 하나를 칸별 CSV + 경위도 그림용 칸 번호(1024×512, int32)로 쏟아 낸다 (tools/planet_map.py 가 그림으로)
static void DumpPlanet(Options opt)
{
    var p = PlanetGenerator.Generate(opt.FirstSeed, new PlanetSettings(opt.Land > 0 ? opt.Land : 16_000, opt.Climate));
    using (var w = new StreamWriter(opt.Dump + ".csv"))
    {
        w.WriteLine("id,x,y,z,elev,terrain,biome,temp10,precip,river,down,flags,shadow");
        for (int c = 0; c < p.CellCount; c++)
            w.WriteLine(FormattableString.Invariant($"{c},{p.X[c]:F5},{p.Y[c]:F5},{p.Z[c]:F5},{p.Elevation[c]},{(int)p.Terrain[c]},{(int)p.Biome[c]},{p.Temp10[c]},{p.PrecipMm[c]},{p.RiverSize[c]},{p.Downstream[c]},{(int)p.Flags[c]},{p.ShadowPct[c]}"));
    }
    const int W = 1024, H = 512;
    var ids = new int[W * H];
    int cur = 0;
    var grid = p.Grid;
    for (int py = 0; py < H; py++)
        for (int px = 0; px < W; px++)
        {
            double lat = Math.PI / 2 - (py + 0.5) / H * Math.PI, lon = (px + 0.5) / W * 2 * Math.PI - Math.PI;
            double x = Math.Cos(lat) * Math.Cos(lon), y = Math.Cos(lat) * Math.Sin(lon), z = Math.Sin(lat);
            while (true) // 이웃 쪽으로 더 가까워지는 동안 걷기
            {
                double best = x * p.X[cur] + y * p.Y[cur] + z * p.Z[cur];
                int next = -1;
                foreach (int nb in grid.Neighbors(cur))
                {
                    double d = x * p.X[nb] + y * p.Y[nb] + z * p.Z[nb];
                    if (d > best) { best = d; next = nb; }
                }
                if (next < 0) break;
                cur = next;
            }
            ids[py * W + px] = cur;
        }
    using (var f = File.Create(opt.Dump + ".ids")) f.Write(System.Runtime.InteropServices.MemoryMarshal.AsBytes(ids.AsSpan()));
    Console.WriteLine($"{opt.Dump}.csv / .ids — 땅 {p.LandCount} · 호수 {p.LakeCount} · 대륙 {p.ContinentCells} · 섬 {p.IslandCount} · 오각형-땅 {p.PentagonsOnLand}");
}

static PlanetBench RunPlanets(Options opt)
{
    var lands = opt.Land > 0 ? new[] { opt.Land } : PlanetSettings.LandChoices;
    Console.WriteLine($"WTW bench: 행성 만들기, 시드 {opt.Seeds}개(첫 시드 {opt.FirstSeed}) × 땅 [{string.Join(", ", lands)}], 동시 {opt.Parallel}");
    var gsw = Stopwatch.StartNew();
    var grid = GeoGrid.Shared;
    gsw.Stop();
    Console.WriteLine($"격자: 칸 {grid.CellCount:N0} · 만드는 데 {gsw.Elapsed.TotalSeconds:0.000}초 (한 번만)");
    PlanetGenerator.Generate(opt.FirstSeed, new PlanetSettings(lands[0], ClimateBias.Normal)); // 데우기

    var jobs = new List<(ulong Seed, int Land)>();
    for (int i = 0; i < opt.Seeds; i++) foreach (var l in lands) jobs.Add((opt.FirstSeed + (ulong)i, l));
    var results = new PlanetSeedResult[jobs.Count];
    Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = opt.Parallel }, k =>
    {
        var (seed, land) = jobs[k];
        var p = PlanetGenerator.Generate(seed, new PlanetSettings(land, ClimateBias.Normal));
        var terr = new int[8];
        var bio = new int[11];
        for (int c = 0; c < p.CellCount; c++) { terr[(int)p.Terrain[c]]++; bio[(int)p.Biome[c]]++; }
        results[k] = new PlanetSeedResult
        {
            Seed = seed, Land = land, Seconds = Math.Round(p.GenerationSeconds, 3),
            LandCells = p.LandCount, LandErrorPct = Math.Round((p.LandCount - land) * 100.0 / land, 2),
            Lakes = p.LakeCount, ContinentPct = Math.Round(p.ContinentCells * 100.0 / Math.Max(1, p.LandCount), 1),
            Islands = p.IslandCount, IslandSizes = p.IslandSizes, PentagonsOnLand = p.PentagonsOnLand,
            RiverCells = p.RiverCells, RemovedIslets = p.RemovedIslets, OrientationTries = p.OrientationTries,
            Terrain = terr, Biomes = bio, Fingerprint = p.Fingerprint,
        };
    });

    foreach (var r in results)
        Console.WriteLine($"  시드 {r.Seed} 땅 {r.Land}: {r.Seconds:0.00}초 · 땅 {r.LandCells} ({r.LandErrorPct:+0.0;-0.0}%) · 호수 {r.Lakes} · 대륙 {r.ContinentPct}% · 섬 {r.Islands} [{string.Join(",", r.IslandSizes.Take(10))}] · 오각형-땅 {r.PentagonsOnLand} · 강 {r.RiverCells} · 지운 조각 {r.RemovedIslets} · 방향 {r.OrientationTries}번\n" +
                          $"      지형[깊은바다 얕은바다 호수 평야 언덕 고원 산 높은산] {string.Join(" ", r.Terrain)} · 생물군계[물 얼음 툰드라 침엽 온대 초원 사막 사바나 열대 습지 고산] {string.Join(" ", r.Biomes)}");

    var pb = new PlanetBench
    {
        Seeds = opt.Seeds, Lands = lands, GridSeconds = Math.Round(gsw.Elapsed.TotalSeconds, 3),
        MedianSeconds = Median(results.Select(r => r.Seconds)), MaxSeconds = results.Max(r => r.Seconds),
        MaxAbsLandErrorPct = results.Max(r => Math.Abs(r.LandErrorPct)),
        MedianContinentPct = Median(results.Select(r => r.ContinentPct)),
        ContinentInRange = results.Count(r => r.ContinentPct >= 70 && r.ContinentPct <= 85),
        MedianIslands = Median(results.Select(r => (double)r.Islands)),
        IslandsInRange = results.Count(r => r.Islands >= 3 && r.Islands <= 8),
        SeedsWithPentagonOnLand = results.Count(r => r.PentagonsOnLand > 0),
        MedianLakes = Median(results.Select(r => (double)r.Lakes)),
        MedianRiverCells = Median(results.Select(r => (double)r.RiverCells)),
        Runs = results.Length, PerSeed = results,
    };
    Console.WriteLine($"요약({pb.Runs}번): 만드는 시간 중앙값 {pb.MedianSeconds:0.00}초(최대 {pb.MaxSeconds:0.00}) · 땅 오차 최대 {pb.MaxAbsLandErrorPct:0.0}% · " +
                      $"대륙 중앙값 {pb.MedianContinentPct}% (70~85% 안: {pb.ContinentInRange}/{pb.Runs}) · 섬 중앙값 {pb.MedianIslands} (3~8개: {pb.IslandsInRange}/{pb.Runs}) · " +
                      $"오각형이 땅에 걸린 경우 {pb.SeedsWithPentagonOnLand}/{pb.Runs} · 호수 중앙값 {pb.MedianLakes} · 강 칸 중앙값 {pb.MedianRiverCells}");
    return pb;
}

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

static double Median(IEnumerable<double> values)
{
    var sorted = values.OrderBy(x => x).ToArray();
    return sorted.Length == 0 ? 0 : sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
}

sealed class Options
{
    public int Dots = 500, Days = 100, Seeds = 1, Parallel = Environment.ProcessorCount;
    public ulong FirstSeed = 1;
    public string? Out;
    public string Baseline = "tools/bench/baseline.json";
    public bool SaveBaseline;
    public bool Planet;
    public int Land;
    public string? Dump;
    public ClimateBias Climate = ClimateBias.Normal;

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
                case "--planet": o.Planet = true; break;
                case "--land": o.Land = int.Parse(Next()); break;
                case "--dump": o.Dump = Next(); break;
                case "--climate": o.Climate = Enum.Parse<ClimateBias>(Next(), true); break;
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
    public string Cpu { get; set; } = "";
    public int Cores { get; set; }
    public SeedResult[] PerSeed { get; set; } = Array.Empty<SeedResult>();
    public PlanetBench? Planet { get; set; }
}

public sealed class PlanetBench
{
    public int Seeds { get; set; }
    public int[] Lands { get; set; } = Array.Empty<int>();
    public int Runs { get; set; }
    public double GridSeconds { get; set; }
    public double MedianSeconds { get; set; }
    public double MaxSeconds { get; set; }
    public double MaxAbsLandErrorPct { get; set; }
    public double MedianContinentPct { get; set; }
    public int ContinentInRange { get; set; }
    public double MedianIslands { get; set; }
    public int IslandsInRange { get; set; }
    public int SeedsWithPentagonOnLand { get; set; }
    public double MedianLakes { get; set; }
    public double MedianRiverCells { get; set; }
    public PlanetSeedResult[] PerSeed { get; set; } = Array.Empty<PlanetSeedResult>();
}

public sealed class PlanetSeedResult
{
    public ulong Seed { get; set; }
    public int Land { get; set; }
    public double Seconds { get; set; }
    public int LandCells { get; set; }
    public double LandErrorPct { get; set; }
    public int Lakes { get; set; }
    public double ContinentPct { get; set; }
    public int Islands { get; set; }
    public int[] IslandSizes { get; set; } = Array.Empty<int>();
    public int PentagonsOnLand { get; set; }
    public int RiverCells { get; set; }
    public int RemovedIslets { get; set; }
    public int OrientationTries { get; set; }
    public int[] Terrain { get; set; } = Array.Empty<int>();
    public int[] Biomes { get; set; } = Array.Empty<int>();
    public ulong Fingerprint { get; set; }
}

/// <summary>이 컴퓨터: 코어 수 + CPU 이름. 기준 파일은 같은 컴퓨터에서 잰 것과만 비교한다.</summary>
sealed record MachineInfo(int Cores, string Cpu, string Os)
{
    public static MachineInfo Current()
    {
        string cpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "";
        try
        {
            if (File.Exists("/proc/cpuinfo"))
                foreach (var line in File.ReadLines("/proc/cpuinfo"))
                    if (line.StartsWith("model name")) { cpu = line[(line.IndexOf(':') + 1)..].Trim(); break; }
        }
        catch (IOException) { }
        if (cpu.Length == 0) cpu = "모름";
        return new MachineInfo(Environment.ProcessorCount, cpu, System.Runtime.InteropServices.RuntimeInformation.OSDescription);
    }

    public override string ToString() => $"{Cores} cores, {Cpu}, {Os}";
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(BenchResult))]
partial class BenchJson : JsonSerializerContext { }
