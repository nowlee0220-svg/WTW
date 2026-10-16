using Wtw.Sim.Data;
using Wtw.Sim.Planet;
using Wtw.Sim.Provinces;
using Wtw.Sim.Time;

namespace Wtw.Sim.People;

/// <summary>오브젝트 하나의 남은 몫: stamp 시각에 stock 몫, 그 뒤로 regrow 마다 한 몫씩 다시 난다.</summary>
public struct TileStock
{
    public int Stock;
    public long Stamp;
}

/// <summary>손댄 타일의 남은 몫(타일 번호 순으로 돈다 — 저장 순서가 정해져 있다).</summary>
public sealed class StockTable : IEnumerable<KeyValuePair<int, TileStock>>
{
    private readonly int[] _stock = new int[HexFrame.TileCount];
    private readonly long[] _stamp = new long[HexFrame.TileCount];
    private readonly bool[] _has = new bool[HexFrame.TileCount];
    public int Count { get; private set; }

    public bool TryGetValue(int tile, out TileStock s)
    {
        if (_has[tile]) { s = new TileStock { Stock = _stock[tile], Stamp = _stamp[tile] }; return true; }
        s = default; return false;
    }

    public TileStock this[int tile]
    {
        set { if (!_has[tile]) { _has[tile] = true; Count++; } _stock[tile] = value.Stock; _stamp[tile] = value.Stamp; }
    }

    public IEnumerator<KeyValuePair<int, TileStock>> GetEnumerator()
    {
        for (int i = 0; i < HexFrame.TileCount; i++)
            if (_has[i]) yield return new KeyValuePair<int, TileStock>(i, new TileStock { Stock = _stock[i], Stamp = _stamp[i] });
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// 사람이 사는 지방 하나의 살아 있는 상태(시뮬 스레드 전용).
///   - 바뀐 것(저장): 딴 · 주운 오브젝트의 남은 몫(Stocks), 사람이 들어가 걷힌 숨김(Revealed). 지도 자체는 행성 + 칸으로 다시 만든다.
///   - 다시 만들 수 있는 것(저장 안 함): 걸을 수 있는 타일 · 이어진 땅 번호(Region) · 먹을 것 · 물 마실 자리 · 그늘 · 지방 시각 · 기온 표.
/// 순서가 결과에 들어가는 컬렉션은 정렬된 것만 쓴다(SortedDictionary · SortedSet · 번호 순 목록).
/// </summary>
public sealed class ProvinceLife
{
    public const int Bucket = 15;
    public const int Buckets = HexFrame.Size / Bucket; // 10

    public int Cell { get; }
    public ProvinceMap Map { get; }
    public PeopleData Data { get; }
    public readonly StockTable Stocks = new();
    private readonly HarvestDef?[] _harvest = new HarvestDef?[HexFrame.TileCount];
    public readonly SortedSet<int> Revealed = new();
    /// <summary>이 지방에 있는 사람(살아 있는 · 죽은 모두, 번호 순).</summary>
    public readonly List<Person> People = new();

    // ── 다시 만들 수 있는 것 ──
    public readonly bool[] Walk = new bool[HexFrame.TileCount];
    public readonly int[] Region = new int[HexFrame.TileCount];
    /// <summary>0 없음 · 1 나무 그늘 · 2 바위 그늘 · 3 동굴(위가 바위).</summary>
    public readonly byte[] Shelter = new byte[HexFrame.TileCount];
    public readonly bool[] Drink = new bool[HexFrame.TileCount];
    private readonly List<int>[] _food = NewBuckets(), _carry = NewBuckets(), _drink = NewBuckets(), _shelter = NewBuckets();
    /// <summary>
    /// 칸 묶음마다 "이 시각 전에는 딸 것이 하나도 없다"(다 따 간 묶음을 다시 뒤지지 않게). 따 가는 일은 몫을 줄이기만 하므로 늘 맞는 아래 경계다.
    /// 결과에는 영향이 없고(이미 빈 것이 확실한 묶음만 건너뛴다) 저장하지 않는다.
    /// </summary>
    private readonly long[] _foodReady = new long[Buckets * Buckets], _carryReady = new long[Buckets * Buckets];
    public int FoodTiles { get; private set; }
    public int DrinkTiles { get; private set; }
    public int CaveTiles { get; private set; }
    /// <summary>지방 시각 = 세계 시각 + 이만큼(틱, 경도 ÷ 15° 시간).</summary>
    public long OffsetTicks { get; }
    public int Mean10 { get; }
    public int Range10 { get; }

    public readonly PathFinder Paths;

    private static List<int>[] NewBuckets() { var a = new List<int>[Buckets * Buckets]; for (int i = 0; i < a.Length; i++) a[i] = new List<int>(); return a; }

    public ProvinceLife(Planet.Planet planet, ProvinceMap map, PeopleData data)
    {
        Cell = map.Cell; Map = map; Data = data;
        var (_, lon) = planet.LatLon(map.Cell);
        OffsetTicks = (long)Math.Round(lon / 15.0 * GameTime.TicksPerHour);
        Mean10 = planet.Temp10[map.Cell];
        Range10 = (int)Math.Round(ProvinceReport.DailyRange(planet, map.Cell, out _) * 10);
        var g = map.Ground;
        for (int i = 0; i < HexFrame.TileCount; i++) Walk[i] = HexFrame.Inside[i] && GroundInfo.IsWalkable(g[i]);
        // 이어진 땅(4방향 = 모서리를 자르지 않는 8방향과 같다)
        Array.Fill(Region, -1);
        int region = 0;
        var q = new Queue<int>();
        for (int i = 0; i < HexFrame.TileCount; i++)
        {
            if (!Walk[i] || Region[i] >= 0) continue;
            Region[i] = region; q.Enqueue(i);
            while (q.Count > 0)
            {
                int a = q.Dequeue(); int ax = a % HexFrame.Size, ay = a / HexFrame.Size;
                if (ax > 0) Visit(a - 1); if (ax < HexFrame.Size - 1) Visit(a + 1);
                if (ay > 0) Visit(a - HexFrame.Size); if (ay < HexFrame.Size - 1) Visit(a + HexFrame.Size);
            }
            region++;
        }
        void Visit(int b) { if (Walk[b] && Region[b] < 0) { Region[b] = region; q.Enqueue(b); } }

        for (int i = 0; i < HexFrame.TileCount; i++)
        {
            if (!Walk[i]) continue;
            int x = i % HexFrame.Size, y = i / HexFrame.Size, b = Bk(x, y);
            int o = map.ObjectAt(i);
            if (o >= 0 && data.Harvest[o] is { } h) { _harvest[i] = h; (h.Food ? _food : _carry)[b].Add(i); if (h.Food) FoodTiles++; }
            bool fresh = false, nearStone = false, nearTree = o >= 0 && ObjectCatalog.Default.Objects[o].IsTree;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if ((uint)nx >= HexFrame.Size || (uint)ny >= HexFrame.Size) continue;
                    int j = ny * HexFrame.Size + nx;
                    if (GroundInfo.IsWater(g[j]) && (map.Flags[j] & TileFlags.Sea) == 0 && HexFrame.Inside[j]) fresh = true;
                    if (g[j] == Ground.Stone) nearStone = true;
                    int oj = map.ObjectAt(j);
                    if (oj >= 0 && ObjectCatalog.Default.Objects[oj].IsTree) nearTree = true;
                }
            if (fresh) { Drink[i] = true; _drink[b].Add(i); DrinkTiles++; }
            byte sh = (map.Flags[i] & TileFlags.Roof) != 0 ? (byte)3 : nearStone ? (byte)2 : nearTree ? (byte)1 : (byte)0;
            Shelter[i] = sh;
            if (sh >= 2) { _shelter[b].Add(i); if (sh == 3) CaveTiles++; }
        }
        Paths = new PathFinder(this);
    }

    public static int Bk(int x, int y) => Math.Min(Buckets - 1, y / Bucket) * Buckets + Math.Min(Buckets - 1, x / Bucket);

    // ── 시각 · 기온 ──

    private static readonly short[] Curve =
    {
        -334, -383, -431, -477, -522, -566, -609, -649, -688, -725, -760, -793, -824, -853, -879, -903, -924, -943, -959, -972, -983, -991, -997, -1000,
        -999, -991, -976, -954, -924, -887, -843, -793, -737, -676, -609, -537, -462, -383, -301, -216, -131, -44, 44, 131, 216, 301, 383, 462,
        537, 609, 676, 737, 793, 843, 887, 924, 954, 976, 991, 999, 1000, 997, 991, 983, 972, 959, 943, 924, 903, 879, 853, 824,
        793, 760, 725, 688, 649, 609, 566, 522, 477, 431, 383, 334, 284, 233, 182, 131, 78, 26, -26, -78, -131, -182, -233, -284,
    };

    /// <summary>그 지방 시각으로 하루 중 몇 틱(0 ~ 60,000).</summary>
    public long LocalTickOfDay(long tick)
    {
        long t = (tick + OffsetTicks) % GameTime.TicksPerDay;
        return t < 0 ? t + GameTime.TicksPerDay : t;
    }

    public int LocalHour(long tick) => (int)(LocalTickOfDay(tick) / GameTime.TicksPerHour);
    public bool IsNight(long tick) { int h = LocalHour(tick); return h < 6 || h >= 18; }

    /// <summary>바깥 기온(0.1°C): 연평균 + 낮밤 곡선(해 뜰 무렵 가장 차고 15시 가장 따뜻함).</summary>
    public int OutsideTemp10(long tick) => Mean10 + Range10 / 2 * Curve[LocalTickOfDay(tick) / (GameTime.TicksPerHour / 4)] / 1000;

    /// <summary>그 자리에서 느끼는 기온(0.1°C): 동굴은 늘 연평균 근처, 바위 · 나무 그늘은 밤에 덜 춥고 낮에 덜 덥다, 함께 자면 덜 춥다.</summary>
    public int FeltTemp10(long tick, int tile, bool huddle)
    {
        int t = OutsideTemp10(tick);
        int sh = (uint)tile < HexFrame.TileCount ? Shelter[tile] : 0;
        bool night = IsNight(tick);
        if (sh == 3) t = Mean10 + 20;
        else if (sh == 2) t += night ? 25 : (t > 280 ? -30 : 0);
        else if (sh == 1) t += night ? 15 : (t > 280 ? -40 : 0);
        if (huddle) t += 30;
        return t;
    }

    public static string ShelterName(int s) => s switch { 3 => "동굴", 2 => "바위 그늘", 1 => "나무 그늘", _ => "탁 트인 곳" };

    // ── 숨김 ──

    public bool IsHidden(int i) => Map.IsHidden(i) && !Revealed.Contains(i);

    /// <summary>동굴에 들어가면 둘레(이어진 동굴 바닥과 맞닿은 바위)의 숨김을 걷는다. 새로 걷힌 타일 수.</summary>
    public int RevealAround(int tile, int radius = 10)
    {
        int n = 0;
        int x0 = tile % HexFrame.Size, y0 = tile / HexFrame.Size;
        var q = new Queue<int>();
        var seen = new HashSet<int> { tile };
        q.Enqueue(tile);
        while (q.Count > 0)
        {
            int a = q.Dequeue();
            if (Map.IsHidden(a) && Revealed.Add(a)) n++;
            if ((Map.Flags[a] & TileFlags.Roof) == 0) continue; // 바위는 걷히기만 하고 그 너머로는 안 본다
            int ax = a % HexFrame.Size, ay = a / HexFrame.Size;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = ax + dx, ny = ay + dy;
                    if ((uint)nx >= HexFrame.Size || (uint)ny >= HexFrame.Size || Math.Abs(nx - x0) > radius || Math.Abs(ny - y0) > radius) continue;
                    int b = ny * HexFrame.Size + nx;
                    if (!HexFrame.Inside[b] || !seen.Add(b)) continue;
                    if ((Map.Flags[b] & TileFlags.Roof) != 0 || Map.Ground[b] == Ground.Stone) q.Enqueue(b);
                }
        }
        return n;
    }

    // ── 먹을 것 · 주울 것 ──

    public HarvestDef? HarvestAt(int tile) => (uint)tile < HexFrame.TileCount ? _harvest[tile] : null;

    public int StockAt(int tile, long now)
    {
        var h = HarvestAt(tile);
        if (h is null) return 0;
        if (!Stocks.TryGetValue(tile, out var s)) return h.Stock;
        if (h.RegrowTicks <= 0) return s.Stock;
        long grown = (now - s.Stamp) / h.RegrowTicks;
        return (int)Math.Min(h.Stock, s.Stock + grown);
    }

    /// <summary>묶음에서 가장 먼저 다시 열리는 시각(모두 비어 있을 때).</summary>
    private long NextReady(List<int> tiles, long now)
    {
        long best = long.MaxValue;
        foreach (int t in tiles)
        {
            var h = _harvest[t]!;
            if (!Stocks.TryGetValue(t, out var st) || h.RegrowTicks <= 0) continue;
            long grown = (now - st.Stamp) / h.RegrowTicks;
            best = Math.Min(best, st.Stamp + (grown + 1) * h.RegrowTicks);
        }
        return best;
    }

    /// <summary>몫을 가져간다(최대 want). 가져간 몫 수.</summary>
    public int Take(int tile, int want, long now)
    {
        var h = HarvestAt(tile);
        if (h is null) return 0;
        int have = StockAt(tile, now);
        int got = Math.Min(have, want);
        if (got <= 0) return 0;
        long stamp = now;
        if (Stocks.TryGetValue(tile, out var s) && h.RegrowTicks > 0 && s.Stock + (now - s.Stamp) / h.RegrowTicks < h.Stock)
            stamp = s.Stamp + (now - s.Stamp) / h.RegrowTicks * h.RegrowTicks; // 자라던 몫의 시계를 이어 간다
        Stocks[tile] = new TileStock { Stock = have - got, Stamp = stamp };
        return got;
    }

    /// <summary>오브젝트가 다 없어졌나(다시 나지 않는 돌을 다 주움).</summary>
    public bool IsGone(int tile) { var h = HarvestAt(tile); return h is not null && h.RegrowTicks <= 0 && Stocks.TryGetValue(tile, out var s) && s.Stock <= 0; }

    /// <summary>가장 가까운(직선 거리) 조건에 맞는 타일. 같은 이어진 땅만, maxDist 타일 안. 없으면 −1.</summary>
    private int Nearest(List<int>[] buckets, int px, int py, int maxDist, int mode, long now = 0, int skip = -1)
    {
        int reg = Region[py * HexFrame.Size + px];
        if (reg < 0) return -1;
        int bx = Math.Min(Buckets - 1, px / Bucket), by = Math.Min(Buckets - 1, py / Bucket);
        long best = long.MaxValue; int bestTile = -1;
        long max2 = (long)maxDist * maxDist;
        for (int r = 0; r < Buckets; r++)
        {
            // 이 고리 안쪽 가장 가까운 거리가 지금 최선보다 멀면 끝
            long ringMin = (long)Math.Max(0, (r - 1) * Bucket);
            if (ringMin * ringMin > best || ringMin * ringMin > max2) break;
            for (int yy = by - r; yy <= by + r; yy++)
                for (int xx = bx - r; xx <= bx + r; xx++)
                {
                    if (Math.Max(Math.Abs(xx - bx), Math.Abs(yy - by)) != r || (uint)xx >= Buckets || (uint)yy >= Buckets) continue;
                    int bi = yy * Buckets + xx;
                    long[]? ready = mode == 0 ? (ReferenceEquals(buckets, _food) ? _foodReady : _carryReady) : null;
                    if (ready is not null && now < ready[bi]) continue;
                    bool any = false;
                    foreach (int t in buckets[bi])
                    {
                        if (ready is not null && !any && (t == skip || StockAt(t, now) > 0)) any = true;
                        if (Region[t] != reg) continue;
                        long dx = t % HexFrame.Size - px, dy = t / HexFrame.Size - py, d2 = dx * dx + dy * dy;
                        if (d2 > max2 || d2 > best || (d2 == best && t > bestTile)) continue;
                        if (!Ok(t, mode, now, skip)) continue;
                        best = d2; bestTile = t;
                    }
                    if (ready is not null && !any) ready[bi] = NextReady(buckets[bi], now);
                }
        }
        return bestTile;
    }

    private bool Ok(int t, int mode, long now, int skip) => mode switch
    {
        0 => t != skip && StockAt(t, now) > (Claims is not null && Claims.TryGetValue(t, out int c) ? c : 0),
        1 => true,
        2 => Shelter[t] == 3,
        3 => Shelter[t] == 2,
        _ => false,
    };

    /// <summary>지금 이 타일로 따러 가고 있는 사람 수(찾기 직전에 채운다 — 다른 사람이 가는 곳은 피한다).</summary>
    public Dictionary<int, int>? Claims;

    public int NearestFood(int px, int py, long now, int maxDist, int skipTile = -1) => Nearest(_food, px, py, maxDist, 0, now, skipTile);
    public int NearestCarry(int px, int py, long now, int maxDist) { var c = Claims; Claims = null; try { return Nearest(_carry, px, py, maxDist, 0, now); } finally { Claims = c; } }
    public int NearestDrink(int px, int py, int maxDist) => Nearest(_drink, px, py, maxDist, 1);
    /// <summary>가장 가까운 그늘(동굴이면 우선: 동굴까지 거리는 반으로 친다).</summary>
    public int NearestShelter(int px, int py, int maxDist, bool caveOnly)
    {
        int cave = Nearest(_shelter, px, py, maxDist, 2);
        if (caveOnly) return cave;
        int rock = Nearest(_shelter, px, py, maxDist, 3);
        if (cave < 0) return rock;
        if (rock < 0) return cave;
        long dc = D2(cave, px, py), dr = D2(rock, px, py);
        return dc <= dr * 4 ? cave : rock;
    }

    public static long D2(int t, int px, int py) { long dx = t % HexFrame.Size - px, dy = t / HexFrame.Size - py; return dx * dx + dy * dy; }

    /// <summary>먹을 것의 하루 몫 어림(배부름, 1000 = 1) — 측정 · 지방 정보용.</summary>
    public long DailyFoodSupply()
    {
        long sum = 0;
        foreach (var b in _food)
            foreach (int t in b)
            {
                var h = HarvestAt(t)!;
                sum += h.Nutrition * GameTime.TicksPerDay / Math.Max(1, h.RegrowTicks);
            }
        return sum;
    }
}
