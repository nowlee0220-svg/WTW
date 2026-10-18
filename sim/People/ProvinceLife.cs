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

/// <summary>(E4) 땅 위의 불 하나: 모닥불(Kind 1) · 돌 화덕(Kind 2). Stamp 시각부터 Fuel 틱 동안 탄다. Charcoal 이면 숯을 넣은 가둔 불(세기 3).</summary>
public struct Fire
{
    public byte Kind;
    public long Stamp;
    public long Fuel;
    public bool Charcoal;
    public int Lighter;
    public long Remaining(long now) => Stamp + Fuel - now;
    public bool Lit(long now) => Remaining(now) > 0;
    /// <summary>불 세기: 0 꺼짐 · 1 맨불 · 2 가둔 불 · 3 숯 + 가둔 불.</summary>
    public int Heat(long now) => !Lit(now) ? 0 : Kind == 2 ? (Charcoal ? 3 : 2) : 1;
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
    /// <summary>(E4) 땅 위의 불(타일 → 불) · 땅에 놓인 물건(타일 → 아이템 · 개수, 아이템 번호 순) · 캐낸 바위 타일(걸을 수 있는 자갈이 됨). 모두 저장한다.</summary>
    public readonly SortedDictionary<int, Fire> Fires = new();
    public readonly SortedDictionary<int, List<(int Item, int Count)>> Items = new();
    public readonly SortedSet<int> Dug = new();
    /// <summary>이 지방에 있는 사람(살아 있는 · 죽은 모두, 번호 순).</summary>
    public readonly List<Person> People = new();

    // ── 다시 만들 수 있는 것 ──
    public readonly bool[] Walk = new bool[HexFrame.TileCount];
    public readonly int[] Region = new int[HexFrame.TileCount];
    /// <summary>0 없음 · 1 나무 그늘 · 2 바위 그늘 · 3 동굴(위가 바위).</summary>
    public readonly byte[] Shelter = new byte[HexFrame.TileCount];
    public readonly bool[] Drink = new bool[HexFrame.TileCount];
    private readonly List<int>[] _food = NewBuckets(), _carry = NewBuckets(), _drink = NewBuckets(), _shelter = NewBuckets();
    /// <summary>(E4) 재료를 얻을 오브젝트(다루는 법이 있는 모든 오브젝트) · 바위 타일(드러난 것만 캔다 — 물을 때 확인).</summary>
    private readonly List<int>[] _materials = NewBuckets(), _stone = NewBuckets();
    /// <summary>
    /// 칸 묶음마다 "이 시각 전에는 딸 것이 하나도 없다"(다 따 간 묶음을 다시 뒤지지 않게). 따 가는 일은 몫을 줄이기만 하므로 늘 맞는 아래 경계다.
    /// 결과에는 영향이 없고(이미 빈 것이 확실한 묶음만 건너뛴다) 저장하지 않는다.
    /// </summary>
    private readonly long[] _foodReady = new long[Buckets * Buckets], _carryReady = new long[Buckets * Buckets];
    /// <summary>(E3b) 살아 있는 사람이 없어진 시각(−1 = 있음). 잠들기 전 기다림 — 결과에 영향 없음(저장 안 함).</summary>
    public long EmptySince = -1;
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
            if (HexFrame.Inside[i] && g[i] == Ground.Stone) _stone[Bk(i % HexFrame.Size, i / HexFrame.Size)].Add(i);
            if (!Walk[i]) continue;
            int x = i % HexFrame.Size, y = i / HexFrame.Size, b = Bk(x, y);
            int o = map.ObjectAt(i);
            if (o >= 0 && data.Harvest[o] is { } h) { _harvest[i] = h; (h.Food ? _food : _carry)[b].Add(i); if (h.Food) FoodTiles++; }
            if (o >= 0 && ObjectCatalog.Default.Objects[o].Uses.Count > 0) _materials[b].Add(i);
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
        for (int i = 0; i < HexFrame.TileCount; i++)
        {
            if (!Walk[i]) continue;
            int x = i % HexFrame.Size, y = i / HexFrame.Size;
            bool crater = false;
            for (int dy = -1; dy <= 1 && !crater; dy++)
                for (int dx = -1; dx <= 1 && !crater; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if ((uint)nx >= HexFrame.Size || (uint)ny >= HexFrame.Size) continue;
                    if (g[ny * HexFrame.Size + nx] == Ground.Crater) crater = true;
                }
            if (crater) _volcanoFire.Add(i);
        }
        Paths = new PathFinder(this);
        // 이어진 땅마다 마실 물 · 먹을 것이 있나(측정: "지방 안에 없어서 죽은 사람")
        int regions = 0; foreach (int r in Region) regions = Math.Max(regions, r + 1);
        RegionHasDrink = new bool[regions]; RegionHasFood = new bool[regions];
        for (int i = 0; i < HexFrame.TileCount; i++)
        {
            if (Region[i] < 0) continue;
            if (Drink[i]) RegionHasDrink[Region[i]] = true;
            if (_harvest[i] is { Food: true }) RegionHasFood[Region[i]] = true;
        }
    }

    public bool[] RegionHasDrink = Array.Empty<bool>(), RegionHasFood = Array.Empty<bool>();
    public bool HasDrinkFrom(int tile) => (uint)tile < HexFrame.TileCount && Region[tile] >= 0 && RegionHasDrink[Region[tile]];
    public bool HasFoodFrom(int tile) => (uint)tile < HexFrame.TileCount && Region[tile] >= 0 && RegionHasFood[Region[tile]];

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
        // (E4) 불은 추울 때 데워 준다 — 이미 따뜻하면 사람은 불에서 한 걸음 물러난다(불 때문에 26°C 를 넘지는 않는다)
        if (Fires.Count > 0 || _volcanoFire.Count > 0) { int fw = FireWarmth(tile, tick); if (fw > 0 && t < 260) t = Math.Min(t + fw, 260); }
        return t;
    }

    /// <summary>(E4) 화산의 불: 분화구와 맞닿은 걸을 수 있는 타일(늘 타는 맨불 — 불씨를 얻을 수 있다).</summary>
    private readonly List<int> _volcanoFire = new();
    public IReadOnlyList<int> VolcanoFireTiles => _volcanoFire;
    public bool IsVolcanoFire(int tile) => _volcanoFire.Contains(tile);

    /// <summary>(E4) 불이 주는 따뜻함(0.1°C): 바로 곁 +20°C, 두 칸 +12°C, 세 칸 +6°C, 네 칸 +2.5°C. 돌 화덕은 1.25배(불을 가둬 열이 덜 샌다).</summary>
    public int FireWarmth(int tile, long tick)
    {
        if ((uint)tile >= HexFrame.TileCount) return 0;
        int best = 0, tx = tile % HexFrame.Size, ty = tile / HexFrame.Size;
        foreach (var (ft, f) in Fires)
        {
            if (!f.Lit(tick)) continue;
            int d = Math.Max(Math.Abs(ft % HexFrame.Size - tx), Math.Abs(ft / HexFrame.Size - ty));
            int w = d <= 1 ? 200 : d == 2 ? 120 : d == 3 ? 60 : d == 4 ? 25 : 0;
            if (f.Kind == 2) w = w * 5 / 4;
            best = Math.Max(best, w);
        }
        if (_volcanoFire.Count > 0 && best < 200)
            foreach (int ft in _volcanoFire)
            {
                int d = Math.Max(Math.Abs(ft % HexFrame.Size - tx), Math.Abs(ft / HexFrame.Size - ty));
                if (d <= 1) return 200;
                if (d == 2) best = Math.Max(best, 120);
            }
        return best;
    }

    /// <summary>(E4) 불이 켜진 가장 가까운 불 타일(maxDist 안, 같은 이어진 땅 곁). 없으면 −1.</summary>
    public int NearestFire(int px, int py, long now, int maxDist, bool litOnly = true)
    {
        int here = py * HexFrame.Size + px;
        int reg = (uint)here < HexFrame.TileCount ? Region[here] : -1;
        int best = -1; long bd = (long)maxDist * maxDist;
        foreach (var (t, f) in Fires)
        {
            if (litOnly && !f.Lit(now)) continue;
            long d = D2(t, px, py);
            if (d > bd) continue;
            if (reg >= 0 && !BesideRegion(t, reg)) continue;
            bd = d; best = t;
        }
        foreach (int t in _volcanoFire)
        {
            long d = D2(t, px, py);
            if (d > bd || (reg >= 0 && Region[t] != reg)) continue;
            bd = d; best = t;
        }
        return best;
    }

    /// <summary>타일 t 자체 또는 둘레 8칸이 이어진 땅 reg 에 닿아 있나.</summary>
    public bool BesideRegion(int t, int reg)
    {
        int x = t % HexFrame.Size, y = t / HexFrame.Size;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if ((uint)nx >= HexFrame.Size || (uint)ny >= HexFrame.Size) continue;
                if (Region[ny * HexFrame.Size + nx] == reg) return true;
            }
        return false;
    }

    /// <summary>(E4) 불 세기(0~3) — 그 자리 둘레 1칸 안의 가장 센 불.</summary>
    public int HeatAt(int tile, long now)
    {
        int best = 0, tx = tile % HexFrame.Size, ty = tile / HexFrame.Size;
        foreach (var (ft, f) in Fires)
        {
            if (Math.Max(Math.Abs(ft % HexFrame.Size - tx), Math.Abs(ft / HexFrame.Size - ty)) > 1) continue;
            best = Math.Max(best, f.Heat(now));
        }
        if (best == 0) foreach (int ft in _volcanoFire) if (Math.Max(Math.Abs(ft % HexFrame.Size - tx), Math.Abs(ft / HexFrame.Size - ty)) <= 1) return 1;
        return best;
    }

    /// <summary>(E4) 불을 피운다(이미 불 자리면 땔감을 더한다). fuel = 타는 시간(틱).</summary>
    public void LightFire(int tile, byte kind, long fuel, long now, int lighter)
    {
        if (Fires.TryGetValue(tile, out var f))
        {
            long left = Math.Max(0, f.Remaining(now));
            f.Kind = Math.Max(f.Kind, kind); f.Stamp = now; f.Fuel = left + fuel; if (f.Lighter < 0) f.Lighter = lighter;
            Fires[tile] = f;
        }
        else Fires[tile] = new Fire { Kind = kind, Stamp = now, Fuel = fuel, Lighter = lighter };
    }

    /// <summary>(E4) 땅에 물건을 놓는다.</summary>
    public void Drop(int tile, int item, int count)
    {
        if (count <= 0) return;
        if (!Items.TryGetValue(tile, out var list)) Items[tile] = list = new List<(int, int)>();
        int k = list.FindIndex(c => c.Item == item);
        if (k >= 0) list[k] = (item, list[k].Count + count);
        else { list.Add((item, count)); list.Sort((a, b) => a.Item.CompareTo(b.Item)); }
    }

    /// <summary>(E4) 바위 타일을 캔다: 자갈 바닥이 되어 걸을 수 있다. 이어진 땅 번호를 이웃에서 받고(둘이 다르면 합친다), 길 찾기 비용도 바꾼다. 캤으면 true.</summary>
    public bool Mine(int tile)
    {
        if ((uint)tile >= HexFrame.TileCount || Map.Ground[tile] != Ground.Stone || Dug.Contains(tile)) return false;
        Dug.Add(tile);
        Walk[tile] = true;
        int x = tile % HexFrame.Size, y = tile / HexFrame.Size;
        int reg = -1;
        var merge = new List<int>();
        for (int k = 0; k < 4; k++)
        {
            int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = y + (k == 2 ? 1 : k == 3 ? -1 : 0);
            if ((uint)nx >= HexFrame.Size || (uint)ny >= HexFrame.Size) continue;
            int r = Region[ny * HexFrame.Size + nx];
            if (r < 0) continue;
            if (reg < 0) reg = r; else if (r != reg && !merge.Contains(r)) merge.Add(r);
        }
        if (reg < 0) { reg = RegionHasDrink.Length; Array.Resize(ref RegionHasDrink, reg + 1); Array.Resize(ref RegionHasFood, reg + 1); }
        Region[tile] = reg;
        if (merge.Count > 0)
            for (int i = 0; i < HexFrame.TileCount; i++)
                if (merge.Contains(Region[i])) { Region[i] = reg; }
        foreach (int m in merge) { RegionHasDrink[reg] |= RegionHasDrink[m]; RegionHasFood[reg] |= RegionHasFood[m]; }
        Shelter[tile] = 2;
        Paths.SetWalkable(tile, 1);
        // 캔 자리 둘레의 숨은 바위 속(광맥)이 드러난다
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if ((uint)nx >= HexFrame.Size || (uint)ny >= HexFrame.Size) continue;
                int n = ny * HexFrame.Size + nx;
                if (Map.IsHidden(n)) Revealed.Add(n);
            }
        _stone[Bk(x, y)].Remove(tile);
        return true;
    }

    /// <summary>캐낸 자리를 되살린다(불러오기).</summary>
    public void RestoreDug(IEnumerable<int> tiles) { foreach (int t in tiles) Mine(t); }

    /// <summary>(E4) 가장 가까운 드러난 바위 타일(캘 수 있는 — 걸을 수 있는 땅과 맞닿은). wantOre 면 드러난 광맥만.</summary>
    public int NearestStone(int px, int py, int maxDist, bool wantOre)
    {
        int here = py * HexFrame.Size + px;
        int reg = (uint)here < HexFrame.TileCount ? Region[here] : -1;
        if (reg < 0) return -1;
        int best = -1; long bd = (long)maxDist * maxDist;
        int bx = Math.Min(Buckets - 1, px / Bucket), by = Math.Min(Buckets - 1, py / Bucket);
        for (int r = 0; r < Buckets; r++)
        {
            long ringMin = (long)Math.Max(0, (r - 1) * Bucket);
            if (ringMin * ringMin > bd) break;
            for (int yy = by - r; yy <= by + r; yy++)
                for (int xx = bx - r; xx <= bx + r; xx++)
                {
                    if (Math.Max(Math.Abs(xx - bx), Math.Abs(yy - by)) != r || (uint)xx >= Buckets || (uint)yy >= Buckets) continue;
                    foreach (int t in _stone[yy * Buckets + xx])
                    {
                        if (Dug.Contains(t) || IsHidden(t)) continue;
                        if (wantOre && Map.OreAt(t) < 0) continue;
                        long d = D2(t, px, py);
                        if (d > bd || (d == bd && t > best)) continue;
                        if (!BesideRegion(t, reg)) continue;
                        bd = d; best = t;
                    }
                }
        }
        return best;
    }

    /// <summary>(E4) 가장 가까운 재료 오브젝트(ok 가 참인 오브젝트 종류), 같은 이어진 땅. 없으면 −1.</summary>
    private bool[] _okBuf = Array.Empty<bool>();

    public int NearestMaterial(int px, int py, int maxDist, Func<int, bool> ok)
    {
        int here = py * HexFrame.Size + px;
        int reg = (uint)here < HexFrame.TileCount ? Region[here] : -1;
        if (reg < 0) return -1;
        int best = -1; long bd = (long)maxDist * maxDist;
        var okByObject = _okBuf.Length == ObjectCatalog.Default.Objects.Count ? _okBuf : (_okBuf = new bool[ObjectCatalog.Default.Objects.Count]);
        for (int o = 0; o < okByObject.Length; o++) okByObject[o] = ok(o);
        int bx = Math.Min(Buckets - 1, px / Bucket), by = Math.Min(Buckets - 1, py / Bucket);
        for (int r = 0; r < Buckets; r++)
        {
            long ringMin = (long)Math.Max(0, (r - 1) * Bucket);
            if (ringMin * ringMin > bd) break;
            for (int yy = by - r; yy <= by + r; yy++)
                for (int xx = bx - r; xx <= bx + r; xx++)
                {
                    if (Math.Max(Math.Abs(xx - bx), Math.Abs(yy - by)) != r || (uint)xx >= Buckets || (uint)yy >= Buckets) continue;
                    foreach (int t in _materials[yy * Buckets + xx])
                    {
                        if (Region[t] != reg) continue;
                        long d = D2(t, px, py);
                        if (d > bd || (d == bd && t > best)) continue;
                        int o = Map.ObjectAt(t);
                        if (o < 0 || !okByObject[o]) continue;
                        bd = d; best = t;
                    }
                }
        }
        return best;
    }

    /// <summary>(E4) 걸을 수 있는 땅인가 — 캐낸 바위도 걸을 수 있다.</summary>
    public Ground GroundAt(int i) => Dug.Contains(i) ? Ground.Gravel : Map.Ground[i];

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
        long grown = Math.Max(0, (now - s.Stamp) / h.RegrowTicks);
        return (int)Math.Clamp(s.Stock + grown, 0, h.Stock);
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

    /// <summary>(E4) 조건에 맞는 주울 것(몫이 남은) 중 가장 가까운 것.</summary>
    public int NearestCarryWhere(int px, int py, long now, int maxDist, Func<HarvestDef, bool> ok)
    {
        int here = py * HexFrame.Size + px;
        int reg = (uint)here < HexFrame.TileCount ? Region[here] : -1;
        if (reg < 0) return -1;
        int best = -1; long bd = (long)maxDist * maxDist;
        int bx = Math.Min(Buckets - 1, px / Bucket), by = Math.Min(Buckets - 1, py / Bucket);
        for (int r = 0; r < Buckets; r++)
        {
            long ringMin = (long)Math.Max(0, (r - 1) * Bucket);
            if (ringMin * ringMin > bd) break;
            for (int yy = by - r; yy <= by + r; yy++)
                for (int xx = bx - r; xx <= bx + r; xx++)
                {
                    if (Math.Max(Math.Abs(xx - bx), Math.Abs(yy - by)) != r || (uint)xx >= Buckets || (uint)yy >= Buckets) continue;
                    foreach (int t in _carry[yy * Buckets + xx])
                    {
                        if (Region[t] != reg) continue;
                        long d = D2(t, px, py);
                        if (d > bd || (d == bd && t > best)) continue;
                        var h = _harvest[t]!;
                        if (!ok(h) || StockAt(t, now) <= 0) continue;
                        bd = d; best = t;
                    }
                }
        }
        return best;
    }
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
