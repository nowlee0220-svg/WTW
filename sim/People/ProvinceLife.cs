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

/// <summary>
/// (S1) 땅 위의 불(타일 → 불, 타일 번호 순). SortedDictionary 와 같지만 훑기용 배열을 바뀔 때만 다시 만든다
/// (기온 셈이 사람마다 시간마다 불을 훑는다 — 같은 순서라 결과는 같다).
/// </summary>
public sealed class FireTable : IEnumerable<KeyValuePair<int, Fire>>
{
    private readonly SortedDictionary<int, Fire> _d = new();
    private KeyValuePair<int, Fire>[]? _arr;
    public int Count => _d.Count;
    public Fire this[int tile] { get => _d[tile]; set { _d[tile] = value; _arr = null; } }
    public bool TryGetValue(int tile, out Fire f) => _d.TryGetValue(tile, out f);
    public bool ContainsKey(int tile) => _d.ContainsKey(tile);
    public bool Remove(int tile) { _arr = null; return _d.Remove(tile); }
    /// <summary>타일 번호 순 배열(바뀌기 전까지 같은 것).</summary>
    public KeyValuePair<int, Fire>[] Items => _arr ??= _d.ToArray();
    public IEnumerable<Fire> Values => _d.Values;
    public IEnumerable<int> Keys => _d.Keys;
    public IEnumerator<KeyValuePair<int, Fire>> GetEnumerator() => ((IEnumerable<KeyValuePair<int, Fire>>)Items).GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// (E5b 고침) 지방의 짐승 무리(번호 순). SortedDictionary 를 그대로 돌면 돌 때마다 메모리를 잡아서(사냥 판단 · 기분 셈이 사람마다 자주 돈다),
/// 바뀌기 전까지 같은 배열을 돈다 — 같은 순서라 결과는 같다.
/// </summary>
public sealed class HerdTable
{
    private readonly SortedDictionary<int, Herd> _d = new();
    private KeyValuePair<int, Herd>[]? _arr;
    private Herd[]? _vals;
    public int Count => _d.Count;
    public Herd this[int id] { get => _d[id]; set { _d[id] = value; _arr = null; _vals = null; } }
    public bool TryGetValue(int id, out Herd h) => _d.TryGetValue(id, out h!);
    public bool ContainsKey(int id) => _d.ContainsKey(id);
    public bool Remove(int id) { _arr = null; _vals = null; return _d.Remove(id); }
    /// <summary>번호 순 배열(바뀌기 전까지 같은 것).</summary>
    public KeyValuePair<int, Herd>[] Items => _arr ??= _d.ToArray();
    public Herd[] Values => _vals ??= _d.Values.ToArray();
    public Enumerator GetEnumerator() => new(Items);

    public struct Enumerator
    {
        private readonly KeyValuePair<int, Herd>[] _a; private int _i;
        internal Enumerator(KeyValuePair<int, Herd>[] a) { _a = a; _i = -1; }
        public KeyValuePair<int, Herd> Current => _a[_i];
        public bool MoveNext() => ++_i < _a.Length;
    }
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
    public readonly FireTable Fires = new();
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
    /// <summary>(S1) 같은 것을 오브젝트 종류별로도(재료 · 주울 것 찾기가 맞는 종류의 타일만 보게 — 결과는 같다).</summary>
    private readonly List<int>[]?[] _matByObj = new List<int>[]?[ObjectCatalog.Default.Objects.Count], _carryByObj = new List<int>[]?[ObjectCatalog.Default.Objects.Count];
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
    /// <summary>(S1) 이 지방 사람들의 예약표(지방마다 따로 — 지방마다 CPU 를 따로 쓸 수 있게). 저장한다.</summary>
    public readonly Scheduling.Scheduler Sched = new();
    /// <summary>(S1) 지방 계산 중에 생긴, 맞추는 시각에 세계에 적용할 일(차례대로). 저장한다.</summary>
    public readonly List<PendingOp> Ops = new();
    /// <summary>(S1) 경계에 닿아 넘겨주기를 기다리는 사람(번호). 저장한다.</summary>
    public readonly List<int> CrossQueue = new();
    /// <summary>(S1) 이 지방의 ("people", 칸) 난수 스트림(RngStreams 의 같은 것 — 처음 쓸 때 채운다).</summary>
    internal Core.Pcg32? PeopleRng, AnimalRng;

    /// <summary>(E5b) 짐승 무리(번호 순) · 다음 무리 번호 · 민물의 물고기 몫 · 종마다 살 수 있는 마릿수 · 물고기 몫의 한도. 무리 · 번호 · 물고기는 저장한다.</summary>
    public readonly HerdTable Herds = new();
    public int NextHerdId = 1;
    public int FishStock;
    public int[] AnimalCap = Array.Empty<int>();
    public int FishCap;

    /// <summary>
    /// (E5c) 밭 · 심은 자리(타일 → 밭, 타일 번호 순) · 다음 단계로 바뀌는 시각 순 목록 · 모아 둔 자리(−1 = 없음) · 처음 밭이 생긴 때 ·
    /// 올해 거둔 양 · 거둘 수 있었던 양(흉년을 가리는 데) · 그 해. 모두 저장한다. 자라기 좋은 정도(Grow)는 지도로 다시 만든다.
    /// </summary>
    public readonly SortedDictionary<int, Plot> Plots = new();
    public readonly SortedSet<(long Due, int Tile)> PlotDue = new();
    public int Stash = -1;
    public long FarmSince = -1;
    public int YearHarvest, YearPotential, HarvestYear;
    /// <summary>(E5c) 이 무리가 처음 거둔 때(−1 = 아직) · 씨앗까지 먹어 버렸다고 알린 해(−1 = 없음). 저장한다.</summary>
    public long FirstHarvest = -1;
    public int SeedsEatenYear = -1;
    public byte[]? Grow;
    /// <summary>(E5c) 밭일 찾기의 기억(PeopleSim.FarmView — 저장 안 함).</summary>
    public object? FarmCache;
    /// <summary>(E5c) 밭이 바뀔 때마다 오른다(밭일 찾기의 기억을 버리려고 — 저장 안 함).</summary>
    public int FarmVersion;

    public void SetPlotDue(Plot p, long due)
    {
        if (p.Due > 0) PlotDue.Remove((p.Due, p.Tile));
        p.Due = due;
        if (due > 0) PlotDue.Add((due, p.Tile));
        FarmVersion++;
    }

    public void AddPlot(Plot p) { Plots[p.Tile] = p; PlotAt[p.Tile] = p; _plotArr = null; if (p.Due > 0) PlotDue.Add((p.Due, p.Tile)); FarmVersion++; }

    public void RemovePlot(int tile)
    {
        if (!Plots.Remove(tile, out var p)) return;
        PlotAt[tile] = null; _plotArr = null;
        if (p.Due > 0) PlotDue.Remove((p.Due, p.Tile));
        FarmVersion++;
    }

    /// <summary>(E5c) 타일 → 밭(없으면 null) — Plots 와 같은 것을 빨리 찾으려고. 밭들(타일 순 배열, 바뀌기 전까지 같은 것 — 돌 때 메모리를 잡지 않게).</summary>
    public readonly Plot?[] PlotAt = new Plot?[HexFrame.TileCount];
    private Plot[]? _plotArr;
    public Plot[] PlotList => _plotArr ??= Plots.Values.ToArray();

    // ── (E6) 건물 · 걷은 덤불 ──

    /// <summary>(E6) 건물(번호 → 건물, 번호 순) · 다음 번호 · 바뀌는 때 순 목록(무너짐 · 낡음 · 버려짐). 모두 저장한다.</summary>
    public readonly SortedDictionary<int, Building> Buildings = new();
    public int NextBuildingId = 1;
    public readonly SortedSet<(long Due, int Id)> BuildDue = new();
    /// <summary>(E6) 타일 → 그 타일을 차지한 건물 번호(0 = 없음) · 타일마다 건물이 주는 따뜻함(0.1°C, 밤 기준). 건물로 다시 만든다(저장 안 함).</summary>
    public readonly int[] BuildingAt = new int[HexFrame.TileCount];
    public short[]? Warm;
    /// <summary>(E6) 건물이 바뀔 때마다 오른다(건물 일 찾기의 기억 · 화면 — 저장 안 함).</summary>
    public int BuildVersion;
    public object? BuildCache;
    /// <summary>(E6) 밭 · 건물 자리를 고르며 걷어 낸 덤불 · 풀 타일(그 오브젝트가 없어졌다). 저장한다.</summary>
    public readonly SortedSet<int> Cleared = new();
    /// <summary>(E6) 이 땅에 놓인 사연 있는 물건(번호 순 — 세계 목록으로 다시 만든다, 저장 안 함).</summary>
    public readonly SortedSet<int> NamedGround = new();

    public Building? BuildingOn(int tile) => (uint)tile < HexFrame.TileCount && BuildingAt[tile] != 0 && Buildings.TryGetValue(BuildingAt[tile], out var b) ? b : null;

    public void SetBuildDue(Building b, long due)
    {
        if (b.Due > 0) BuildDue.Remove((b.Due, b.Id));
        b.Due = due;
        if (due > 0) BuildDue.Add((due, b.Id));
    }

    /// <summary>(E6) 건물을 더한다(차지한 타일을 적는다).</summary>
    public void AddBuilding(Building b, int w, int h)
    {
        Buildings[b.Id] = b;
        if (b.Id >= NextBuildingId) NextBuildingId = b.Id + 1;
        for (int dy = 0; dy < h; dy++) for (int dx = 0; dx < w; dx++) { int t = b.Tile + dy * HexFrame.Size + dx; if ((uint)t < HexFrame.TileCount) BuildingAt[t] = b.Id; }
        if (b.Due > 0) BuildDue.Add((b.Due, b.Id));
        BuildVersion++;
    }

    public void RemoveBuilding(int id, int w, int h)
    {
        if (!Buildings.Remove(id, out var b)) return;
        if (b.Due > 0) BuildDue.Remove((b.Due, b.Id));
        for (int dy = 0; dy < h; dy++) for (int dx = 0; dx < w; dx++) { int t = b.Tile + dy * HexFrame.Size + dx; if ((uint)t < HexFrame.TileCount && BuildingAt[t] == id) BuildingAt[t] = 0; }
        BuildVersion++;
    }

    /// <summary>(E6) 걷어 낸 것을 빼고 그 타일의 오브젝트(없으면 −1).</summary>
    public int ObjectAt(int tile) => Cleared.Count > 0 && Cleared.Contains(tile) ? -1 : Map.ObjectAt(tile);

    /// <summary>(E6) 걷을 수 있는 오브젝트인가: 나무 · 돌 · 진흙 바닥이 아니고 먹을 것 · 주울 것이 나지 않는 덤불 · 풀(가시덤불 · 마른 덤불 · 풀 · 갈대 …).</summary>
    public bool Clearable(int tile)
    {
        int o = ObjectAt(tile);
        if (o < 0) return true;
        var od = ObjectCatalog.Default.Objects[o];
        return od.Kind is "bush" or "plant" && _harvest[tile] is null;
    }

    /// <summary>(E6) 덤불 · 풀을 걷는다(그 자리의 재료 · 다루는 법이 없어진다). 걷었으면 true.</summary>
    public bool ClearObject(int tile)
    {
        int o = ObjectAt(tile);
        if (o < 0 || !Clearable(tile)) return false;
        Cleared.Add(tile);
        int b = Bk(tile % HexFrame.Size, tile / HexFrame.Size);
        _materials[b].Remove(tile);
        _matByObj[o]?[b].Remove(tile);
        return true;
    }

    /// <summary>(E6) 걷은 자리를 되살린다(불러오기 · 깨어남).</summary>
    public void RestoreCleared(IEnumerable<int> tiles) { foreach (int t in tiles) ClearObject(t); }

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
            if (o >= 0 && data.Harvest[o] is { } h) { _harvest[i] = h; (h.Food ? _food : _carry)[b].Add(i); if (h.Food) FoodTiles++; else (_carryByObj[o] ??= NewBuckets())[b].Add(i); }
            if (o >= 0 && ObjectCatalog.Default.Objects[o].Uses.Count > 0) { _materials[b].Add(i); (_matByObj[o] ??= NewBuckets())[b].Add(i); }
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
        RegionHasDrink = new bool[regions]; RegionHasFood = new bool[regions]; RegionTiles = new int[regions];
        for (int i = 0; i < HexFrame.TileCount; i++)
        {
            if (Region[i] < 0) continue;
            RegionTiles[Region[i]]++;
            if (Drink[i]) RegionHasDrink[Region[i]] = true;
            if (_harvest[i] is { Food: true }) RegionHasFood[Region[i]] = true;
        }
    }

    public bool[] RegionHasDrink = Array.Empty<bool>(), RegionHasFood = Array.Empty<bool>();
    /// <summary>(E5b 고침) 이어진 땅마다 타일 수 — 경계를 넘어 설 자리가 갇힌 작은 땅인지 가린다.</summary>
    public int[] RegionTiles = Array.Empty<int>();
    /// <summary>tile 이 사람이 살 만한 이어진 땅인가(마실 물이 있거나, 아주 넓어 찾아다닐 수 있다 — 물 없는 작은 땅은 갇히는 곳).</summary>
    public bool OpenLand(int tile) => (uint)tile < HexFrame.TileCount && Region[tile] >= 0 && (RegionHasDrink[Region[tile]] || RegionTiles[Region[tile]] >= 1000);
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
    public int FeltTemp10(long tick, int tile, bool huddle, int cloth = 0)
    {
        int t = OutsideTemp10(tick);
        int sh = (uint)tile < HexFrame.TileCount ? Shelter[tile] : 0;
        bool night = IsNight(tick);
        if (sh == 3) t = Mean10 + 20;
        else if (sh == 2) t += night ? 25 : (t > 280 ? -30 : 0);
        else if (sh == 1) t += night ? 15 : (t > 280 ? -40 : 0);
        // (E6) 바람막이 · 움집 안(곁): 밤에 다, 낮에는 절반(동굴은 이미 따뜻하다)
        if (Warm is not null && (uint)tile < HexFrame.TileCount && sh != 3) { int bw = Warm[tile]; if (bw > 0) t += night ? bw : bw / 2; }
        if (huddle) t += 30;
        // (E5b) 입은 옷: 따뜻함만큼 — 이미 따뜻하면 여미지 않는다(옷 때문에 22°C 를 넘지는 않는다)
        if (cloth > 0 && t < 220) t = Math.Min(t + cloth, 220);
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
        foreach (var (ft, f) in Fires.Items)
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
        foreach (var (t, f) in Fires.Items)
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
        foreach (var (ft, f) in Fires.Items)
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

    /// <summary>(S1) 물건이 놓인 타일인가(Items 의 열쇠와 같다 — 둘레만 빨리 보려고).</summary>
    private readonly bool[] _hasItems = new bool[HexFrame.TileCount];
    public bool HasItems(int tile) => _hasItems[tile];
    public void RemoveItems(int tile) { Items.Remove(tile); _hasItems[tile] = false; }

    /// <summary>(E4) 땅에 물건을 놓는다.</summary>
    /// <summary>
    /// (E5b 고침) 썩거나 먹힐 수 있는 것이 놓였을 수 있는 타일(번호 순) — 하루 경계에 땅 전체 대신 이것만 훑는다(같은 차례라 결과는 같다).
    /// Perish = 아이템마다 썩거나 먹힐 수 있나(사람 계산이 넣어 준다; 없으면 모두 그렇다고 본다).
    /// </summary>
    public readonly SortedSet<int> PerishTiles = new();
    public bool[]? Perish;

    public void Drop(int tile, int item, int count)
    {
        if (count <= 0) return;
        if (Perish is null || (uint)item >= (uint)Perish.Length || Perish[item]) PerishTiles.Add(tile);
        if (!Items.TryGetValue(tile, out var list)) { Items[tile] = list = new List<(int, int)>(); _hasItems[tile] = true; }
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
        if (reg < 0) { reg = RegionHasDrink.Length; Array.Resize(ref RegionHasDrink, reg + 1); Array.Resize(ref RegionHasFood, reg + 1); Array.Resize(ref RegionTiles, reg + 1); }
        Region[tile] = reg;
        RegionTiles[reg]++;
        if (merge.Count > 0)
            for (int i = 0; i < HexFrame.TileCount; i++)
                if (merge.Contains(Region[i])) { Region[i] = reg; }
        foreach (int m in merge) { RegionHasDrink[reg] |= RegionHasDrink[m]; RegionHasFood[reg] |= RegionHasFood[m]; RegionTiles[reg] += RegionTiles[m]; RegionTiles[m] = 0; }
        Shelter[tile] = 2;
        Paths.SetWalkable(tile, 1);
        _sightDrink = null;
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
                    if (RectD2(xx, yy, px, py) > bd) continue; // (S1) 이 묶음엔 더 가까운 것이 없다
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

    public int NearestMaterial(int px, int py, int maxDist, Func<int, bool> ok)
    {
        int here = py * HexFrame.Size + px;
        int reg = (uint)here < HexFrame.TileCount ? Region[here] : -1;
        if (reg < 0) return -1;
        int best = -1; long bd = (long)maxDist * maxDist;
        // (S1) 맞는 종류의 오브젝트 타일만 본다 — 가장 가까운(거리, 같으면 작은 번호) 것은 훑는 차례와 상관없다
        for (int o = 0; o < _matByObj.Length; o++)
            if (_matByObj[o] is { } buckets && ok(o)) NearestIn(buckets, px, py, reg, ref bd, ref best);
        return best;
    }

    /// <summary>(S1) 묶음들에서 (px, py) 에 가장 가까운 같은 이어진 땅의 타일(bd 보다 가깝거나 같으면 작은 번호)로 best 를 고친다.</summary>
    private void NearestIn(List<int>[] buckets, int px, int py, int reg, ref long bd, ref int best, Func<int, bool>? tileOk = null)
    {
        int bx = Math.Min(Buckets - 1, px / Bucket), by = Math.Min(Buckets - 1, py / Bucket);
        for (int r = 0; r < Buckets; r++)
        {
            long ringMin = (long)Math.Max(0, (r - 1) * Bucket);
            if (ringMin * ringMin > bd) break;
            for (int yy = by - r; yy <= by + r; yy++)
                for (int xx = bx - r; xx <= bx + r; xx++)
                {
                    if (Math.Max(Math.Abs(xx - bx), Math.Abs(yy - by)) != r || (uint)xx >= Buckets || (uint)yy >= Buckets) continue;
                    if (RectD2(xx, yy, px, py) > bd) continue;
                    foreach (int t in buckets[yy * Buckets + xx])
                    {
                        if (Region[t] != reg) continue;
                        long d = D2(t, px, py);
                        if (d > bd || (d == bd && t > best)) continue;
                        if (tileOk is not null && !tileOk(t)) continue;
                        bd = d; best = t;
                    }
                }
        }
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
                    // (S1) 묶음 네모 안의 가장 가까운 점도 maxDist · 지금 최선보다 멀면 볼 것이 없다(결과는 같다)
                    long rd = RectD2(xx, yy, px, py);
                    if (rd > max2 || rd > best) continue;
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

    /// <summary>(S1) 묶음 (bx, by) 의 네모 안에서 (px, py) 에 가장 가까운 타일까지 거리²(아래 경계).</summary>
    private static long RectD2(int bx, int by, int px, int py)
    {
        int x0 = bx * Bucket, x1 = bx == Buckets - 1 ? HexFrame.Size - 1 : x0 + Bucket - 1;
        int y0 = by * Bucket, y1 = by == Buckets - 1 ? HexFrame.Size - 1 : y0 + Bucket - 1;
        long dx = px < x0 ? x0 - px : px > x1 ? px - x1 : 0, dy = py < y0 ? y0 - py : py > y1 ? py - y1 : 0;
        return dx * dx + dy * dy;
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
        // (S1) 맞는 종류만(종류마다 한 번 묻는다), 몫이 남은 것
        for (int o = 0; o < _carryByObj.Length; o++)
            if (_carryByObj[o] is { } buckets && Data.Harvest[o] is { } h && ok(h)) NearestIn(buckets, px, py, reg, ref bd, ref best, t => StockAt(t, now) > 0);
        return best;
    }
    public int NearestDrink(int px, int py, int maxDist)
    {
        if (maxDist != PeopleSim.SightTiles) return Nearest(_drink, px, py, maxDist, 1);
        // (S1) 둘러보기(SightTiles)의 물가는 지도로만 정해진다 → 타일마다 한 번만 찾는다(바위를 캐 이어진 땅이 바뀌면 다시)
        int i = py * HexFrame.Size + px;
        _sightDrink ??= NewSightDrink();
        int v = _sightDrink[i];
        if (v == int.MinValue) _sightDrink[i] = v = Nearest(_drink, px, py, maxDist, 1);
        return v;
    }

    private int[]? _sightDrink;
    private static int[] NewSightDrink() { var a = new int[HexFrame.TileCount]; Array.Fill(a, int.MinValue); return a; }
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
