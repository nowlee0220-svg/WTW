using Wtw.Sim.Core;
using Wtw.Sim.Time;

namespace Wtw.Sim.People;

/// <summary>
/// (E6b) 그해 날씨 — 넓은 땅이 함께. 밭(E5c)과 들의 열매 · 도토리 · 낟알 풀 · 콩 · 덩이뿌리(E6b)가 같은 값을 쓴다.
///   - 날씨 땅: 해마다 행성 위에 지름 700km 안팎의 땅 조각(보로노이 — 단위 구에서 간격 0.1, 칸 6~7개 너비)을 새로 나누고, 조각마다 그해 날씨를 정한다.
///     이웃 지방은 대개 같은 조각이라 함께 가뭄을 겪고, 흩어져도 이웃 땅까지 모자란 해가 있다. (현실의 큰 가뭄은 수백~수천 km — 1930년대 북미 Dust Bowl,
///     중국 · 인도의 큰 가뭄. 칸 하나는 약 100km.)
///   - 도토리 해거리: 참나무는 넓은 땅이 함께(같은 조각) 몇 해에 한 번 크게 열고(네 해에 한 번꼴 두 배) 다른 해는 적게(⅔) — 평균은 보통과 같다.
///     (현실: 참나무류의 해거리는 수백 km 에 걸쳐 함께 일어나고 큰 해는 2~5년마다 — Koenig &amp; Knops.)
/// 모두 시드 · 칸 · 해로만 정해진다(저장하지 않는다).
/// </summary>
public sealed partial class PeopleSim
{
    /// <summary>날씨 조각의 간격(단위 구) — 약 640km(지구 크기라면).</summary>
    public const double WeatherRegionSize = 0.1;

    private long[]? _luckCache;

    /// <summary>
    /// 그 해 그 땅의 비 · 날씨(보이지 않는 작은 계절 — 기획서 5-2): 70~115%, 열 해에 한 번꼴로 가뭄 35%, 일곱 해에 한 번꼴로 모자란 해 50~69%.
    /// (T1) 전에는 가뭄만 열두 해에 한 번(8%) — 옛 농사에서 "모자란 해"는 네댓 해에 한 번, 크게 망치는 해는 열 해에 한 번꼴이었다(Halstead &amp; O'Shea, Bad Year Economics, 1989).
    /// (E6b) 칸마다 따로가 아니라 날씨 조각(넓은 땅)마다 — 이웃 지방이 함께 가뭄을 겪는다.
    /// </summary>
    public int YearLuck(int cell, long year)
    {
        _luckCache ??= new long[_w.Planet.CellCount];
        long c = _luckCache[cell];
        if (c != 0 && (c >> 8) == year + 1) return (int)(c & 0xFF);
        ulong h = WeatherRegion(cell, year, 0) % 1000;
        int luck = h < 100 ? 35 : h < 250 ? 50 + (int)(h % 20) : 70 + (int)(h % 46);
        _luckCache[cell] = ((year + 1) << 8) | (long)luck; // 같은 값이라 여러 레인이 함께 써도 결과는 같다
        return luck;
    }

    /// <summary>(E6b) 도토리 해거리(100 분의): 네 해에 한 번꼴 200, 다른 해 67 — 같은 날씨 조각의 참나무가 함께.</summary>
    public int MastPct(int cell, long year) => WeatherRegion(cell, year, 1) % 4 == 0 ? 200 : 67;

    /// <summary>
    /// (E6b) 들의 먹을 것이 그해 열리는 몫(100 분의): 갈래 1 = 열매 · 버섯 · 이끼 · 낟알 풀 · 콩 · 덩이뿌리(그해 날씨), 갈래 2 = 도토리(날씨 × 해거리).
    /// </summary>
    public int GrowPct(int cell, int cls, long year) => cls switch
    {
        1 => YearLuck(cell, year),
        2 => YearLuck(cell, year) * MastPct(cell, year) / 100,
        _ => 100,
    };

    /// <summary>그해 그 칸이 든 날씨 조각의 안정 해시(salt 로 갈래를 나눈다). 조각의 경계는 해마다 옮겨 간다.</summary>
    private ulong WeatherRegion(int cell, long year, ulong salt)
    {
        var pl = _w.Planet;
        ulong ys = StableHash.Combine(_w.Seed ^ 0x57EA7E2UL, (ulong)year);
        // 해마다 격자를 조금 옮긴다(조각의 경계가 해마다 다르게)
        double ox = (ys % 1000) / 1000.0 * WeatherRegionSize, oy = (ys / 1000 % 1000) / 1000.0 * WeatherRegionSize, oz = (ys / 1_000_000 % 1000) / 1000.0 * WeatherRegionSize;
        double x = pl.X[cell] + ox, y = pl.Y[cell] + oy, z = pl.Z[cell] + oz;
        long gx = (long)Math.Floor(x / WeatherRegionSize), gy = (long)Math.Floor(y / WeatherRegionSize), gz = (long)Math.Floor(z / WeatherRegionSize);
        double best = double.MaxValue; ulong bestKey = 0;
        for (long dz = -1; dz <= 1; dz++)
            for (long dy = -1; dy <= 1; dy++)
                for (long dx = -1; dx <= 1; dx++)
                {
                    long ax = gx + dx, ay = gy + dy, az = gz + dz;
                    ulong key = StableHash.Combine(ys, StableHash.Combine((ulong)ax, (ulong)ay, (ulong)az));
                    // 격자 칸 안의 흔들린 점(조각의 가운데)
                    double px = (ax + (key % 997) / 997.0) * WeatherRegionSize, py = (ay + (key / 997 % 991) / 991.0) * WeatherRegionSize, pz = (az + (key / 988_027 % 983) / 983.0) * WeatherRegionSize;
                    double d = (px - x) * (px - x) + (py - y) * (py - y) + (pz - z) * (pz - z);
                    if (d < best) { best = d; bestKey = key; }
                }
        return StableHash.Combine(bestKey, salt);
    }

    /// <summary>(E6b) 날씨 조각 번호(같으면 같은 날씨를 겪는다 — 측정 · 시험용).</summary>
    public ulong WeatherRegionId(int cell, long year) => WeatherRegion(cell, year, 0);

    /// <summary>
    /// (E6b) 들의 먹을 것의 "자라는 시계"(틱): 그해 열리는 몫만큼 빠르게 간다(보통 해 100% 면 세계 시각과 같은 빠르기). 시드 · 칸 · 시각으로만 정해진다.
    /// 해마다의 누적은 지방마다 기억해 둔다(ProvinceLife.GrowPrefix — 다시 셈할 수 있어 저장하지 않는다).
    /// </summary>
    internal long GrowClock(ProvinceLife life, int cls, long t)
    {
        if (cls == 0) return t;
        long year = Math.Max(0, t / GameTime.TicksPerYear);
        long start = year * GameTime.TicksPerYear;
        return GrowPrefix(life, cls, year) + GrowPct(life.Cell, cls, year) * (t - start) / 100;
    }

    /// <summary>지방 기억 없이 자라는 시계(옛 저장 파일을 바꿀 때).</summary>
    public long GrowClockAt(int cell, int cls, long t)
    {
        if (cls == 0 || t <= 0) return t;
        long year = t / GameTime.TicksPerYear, g = 0;
        for (long y = 0; y < year; y++) g += GrowPct(cell, cls, y) * GameTime.TicksPerYear / 100;
        return g + GrowPct(cell, cls, year) * (t - year * GameTime.TicksPerYear) / 100;
    }

    /// <summary>해 year 가 시작될 때까지의 자라는 시계.</summary>
    internal long GrowPrefix(ProvinceLife life, int cls, long year)
    {
        var list = life.GrowPrefix[cls] ??= new List<long> { 0 };
        while (list.Count <= year)
        {
            long y = list.Count - 1;
            list.Add(list[^1] + GrowPct(life.Cell, cls, y) * GameTime.TicksPerYear / 100);
        }
        return list[(int)year];
    }

    /// <summary>자라는 시계가 g 에 닿는 세계 시각(이미 지났으면 from). 닿지 않으면 long.MaxValue.</summary>
    internal long GrowClockInverse(ProvinceLife life, int cls, long g, long from)
    {
        if (cls == 0) return Math.Max(g, from);
        if (GrowClock(life, cls, from) >= g) return from;
        long year = Math.Max(0, from / GameTime.TicksPerYear);
        for (int guard = 0; guard < 400; guard++, year++)
        {
            long next = GrowPrefix(life, cls, year + 1);
            if (next < g) continue;
            long pct = GrowPct(life.Cell, cls, year);
            if (pct <= 0) continue;
            long need = g - GrowPrefix(life, cls, year);
            return year * GameTime.TicksPerYear + (need * 100 + pct - 1) / pct;
        }
        return long.MaxValue;
    }

    // ───────── (E6b) 짐승도 먹는다 ─────────

    /// <summary>들고 다니는 먹을 것(도토리 · 낟알 · 콩 · 덩이뿌리) 한 몫이 짐승에게 주는 배부름 — 짐승은 날로도 다 먹는다(사람이 익혀 먹을 때의 값 어림, 도토리 한 몫 ≈ 20).</summary>
    public const int AnimalCarryValue = 20_000;

    /// <summary>
    /// 짐승이 다 살 때(살 수 있는 수만큼) 보통 해에 새로 열리는 들의 먹을 것 가운데 먹는 몫(100 분의). 현실 어림: 도토리는 짐승(새 · 쥐 · 사슴 · 멧돼지)이
    /// 40~100%를, 들의 열매는 새 · 짐승이 30~90%를 먹는다 — 그 가운데쯤인 절반.
    /// 지방 지도의 짐승 수는 상징적(300m 땅에 사슴 스물여섯)이라 한 마리가 먹는 양(kcal)을 그대로 쓰면 땅이 내는 것보다 많아진다 — 그래서 종마다의 먹는 양(forage)은
    /// 서로의 무게로만 쓰고, 전체는 그 땅이 내는 것에 맞춘다.
    /// </summary>
    public const int AnimalSharePct = 50;

    /// <summary>
    /// 하루 경계(지방마다 한 번): 풀 먹는 짐승이 그 지방의 들의 열매 · 도토리 · 낟알 풀 · 덩이뿌리 · 이끼를 먹는다 — 먹을 양 = 그 땅이 보통 해에 하루 내는 몫 × 절반 ×
    /// (지금 무리 마릿수 × 종마다 무게) ÷ (살 수 있는 수 × 무게). 지방 안 아무 데서나(사람이 닿지 않는 곳도) 한 몫씩 — 찾아본 곳이 비어 있으면 그만큼 덜 먹는다
    /// (찾아보는 수는 먹을 양의 세 배쯤까지). 먹은 몫 / 먹으려던 몫(1000 분의)을 낸다 — 짐승이 살 수 있는 수에 잇는다. 난수는 ("animals", 지방) 스트림.
    /// </summary>
    /// <summary>그 땅이 보통 해에 하루 새로 내는 들의 먹을 것(짐승에게의 값, 배부름 1000 = 1) — 지도로만 정해진다.</summary>
    public long ForageSupply(ProvinceLife life)
    {
        if (life.ForageSupplyCache >= 0) return life.ForageSupplyCache;
        long sum = 0;
        foreach (int t in life.ForageTiles()) { var h = life.HarvestAt(t)!; sum += (h.Food ? h.Nutrition : AnimalCarryValue) * GameTime.TicksPerDay / h.RegrowTicks; }
        return life.ForageSupplyCache = sum;
    }

    private int AnimalsForage(ProvinceLife life, Pcg32 rng)
    {
        long now = 0, full = 0;
        foreach (var (_, h) in life.Herds) if (!h.Tame && h.Species < Fauna.Animals.Count) now += (long)h.Count * Fauna.Animals[h.Species].Forage;
        for (int s = 0; s < Fauna.Animals.Count && s < life.AnimalCap.Length; s++) full += (long)life.AnimalCap[s] * Fauna.Animals[s].Forage;
        if (now <= 0 || full <= 0) return 1000;
        long want = ForageSupply(life) * AnimalSharePct / 100 * now / full;
        if (want <= 0) return 1000;
        var tiles = life.ForageTiles();
        if (tiles.Length == 0) return 0;
        long got = 0;
        long tries = Math.Min(100_000, want / 5_000 + 20); // 한 몫의 값을 15 쯤으로 보면 먹을 양의 세 배
        for (long i = 0; i < tries && got < want; i++)
        {
            int t = tiles[rng.NextInt(tiles.Length)];
            var h = life.HarvestAt(t)!;
            if (life.Take(t, 1, Now) <= 0) continue;
            got += h.Food ? h.Nutrition : AnimalCarryValue;
        }
        St.AnimalForage += got / 1000; St.AnimalForageWant += want / 1000;
        life.AnimalAteYear = life.AnimalYear == Now / GameTime.TicksPerYear ? life.AnimalAteYear + got / 1000 : got / 1000;
        life.AnimalYear = Now / GameTime.TicksPerYear;
        return (int)Math.Min(1000, got * 1000 / want);
    }
}
