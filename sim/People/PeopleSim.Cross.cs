using System.Collections.Concurrent;
using Wtw.Sim.Planet;
using Wtw.Sim.Provinces;
using Wtw.Sim.Time;

namespace Wtw.Sim.People;

/// <summary>
/// (E3b) 지방 하나의 변 하나: 그 너머 이웃 지방과 어디로 넘어갈 수 있나 · 경계 너머 보이는 물가.
/// 지도(행성 + 칸)로만 정해지는 것이라 저장하지 않고, 처음 필요할 때 만든다(이웃 지방 지도도 그때 만든다).
/// </summary>
public sealed class Border
{
    public int Cell, Slot, Nb, NbSlot;
    public ProvinceLayout.Placed Place;
    /// <summary>넘어가는 자리: 이 지방 가장자리의 걸을 수 있는 타일(번호 순)과, 넘어가면 서는 이웃 지방의 타일(같은 순서).</summary>
    public int[] Gates = Array.Empty<int>(), Entries = Array.Empty<int>();
    /// <summary>경계 너머(이웃 지방 쪽 16 타일 안)의 물 마실 자리: 이 지방 좌표(밀리타일, 육각형 밖일 수 있음)와 이웃 지방의 타일.</summary>
    public int[] SightX = Array.Empty<int>(), SightY = Array.Empty<int>(), SightTile = Array.Empty<int>();
    /// <summary>넘어간 뒤 이웃 지방 안쪽으로 향하는 방향(0~7).</summary>
    public int InDir8;

    // (S1) 보이는 물가 찾기를 빠르게: 물 마실 자리를 16타일 묶음으로 나눈 색인(번호 순). 결과는 모두 훑을 때와 같다.
    private const int Cell16 = 16_000;
    private int _bx0, _by0, _bw, _bh;
    private int[] _start = Array.Empty<int>(), _items = Array.Empty<int>();

    internal void IndexSight()
    {
        if (SightX.Length == 0) return;
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        for (int k = 0; k < SightX.Length; k++)
        {
            minX = Math.Min(minX, Fl(SightX[k])); maxX = Math.Max(maxX, Fl(SightX[k]));
            minY = Math.Min(minY, Fl(SightY[k])); maxY = Math.Max(maxY, Fl(SightY[k]));
        }
        _bx0 = minX; _by0 = minY; _bw = maxX - minX + 1; _bh = maxY - minY + 1;
        var count = new int[_bw * _bh + 1];
        for (int k = 0; k < SightX.Length; k++) count[(Fl(SightY[k]) - _by0) * _bw + Fl(SightX[k]) - _bx0 + 1]++;
        for (int i = 1; i < count.Length; i++) count[i] += count[i - 1];
        _start = (int[])count.Clone();
        _items = new int[SightX.Length];
        for (int k = 0; k < SightX.Length; k++) _items[count[(Fl(SightY[k]) - _by0) * _bw + Fl(SightX[k]) - _bx0]++] = k;
    }

    private static int Fl(int v) => (int)Math.Floor(v / (double)Cell16);

    /// <summary>(px, py) 에서 거리² 가 bd 보다 작은 가장 가까운 물 마실 자리(같으면 번호가 작은 것). 없으면 −1.</summary>
    internal int NearestSight(long px, long py, ref long bd)
    {
        if (_items.Length == 0) return -1;
        long r = (long)Math.Sqrt(bd) + 1;
        int x0 = Math.Max(_bx0, (int)Math.Floor((px - r) / (double)Cell16)), x1 = Math.Min(_bx0 + _bw - 1, (int)Math.Floor((px + r) / (double)Cell16));
        int y0 = Math.Max(_by0, (int)Math.Floor((py - r) / (double)Cell16)), y1 = Math.Min(_by0 + _bh - 1, (int)Math.Floor((py + r) / (double)Cell16));
        int best = -1;
        for (int by = y0; by <= y1; by++)
            for (int bx = x0; bx <= x1; bx++)
            {
                int b = (by - _by0) * _bw + bx - _bx0;
                for (int i = _start[b]; i < _start[b + 1]; i++)
                {
                    int k = _items[i];
                    long dx = SightX[k] - px, dy = SightY[k] - py, d = dx * dx + dy * dy;
                    if (d < bd || (d == bd && best >= 0 && k < best)) { bd = d; best = k; }
                }
            }
        return best;
    }
}

/// <summary>
/// (E3b) 지방 넘기. 사람은 경계 타일까지 걸어가 "넘겨주기 시각"(CrossInterval 마다)에 이웃 지방으로 옮겨진다.
/// 넘겨주기는 그 시각의 다른 예약을 모두 처리한 뒤 사람 번호 순으로 한꺼번에 — 지방끼리 주고받는 일이 정해진 시각에만 생겨서
/// E8 에서 지방마다 CPU 를 따로 돌려도 같은 결과를 낼 수 있는 구조다. 걸음은 넘겨주기 시각에 딱 닿도록 조금 늘린다(서서 기다리지 않게).
/// 사람이 모두 떠난 지방은 하루 경계에서 잠든다(무거운 것을 버리고 바뀐 것만 남김). 다시 사람이 오면 깨어나고,
/// 딴 열매는 "딴 시각"으로부터 흐른 시간만큼 한 번에 다시 열린다(몫 계산이 원래 그 방식).
/// </summary>
public sealed partial class PeopleSim
{
    /// <summary>넘겨주기 간격: 6분(게임 시간). 하루 경계는 걷는 사람을 너무 오래 세워 두므로 더 짧게.</summary>
    public const long CrossInterval = GameTime.TicksPerHour / 10;

    /// <summary>변 기억(조회 전용 — 순회하지 않음). (S1) 지방 계산 중 여러 CPU 가 함께 읽는다 — 같은 변은 한 번만 만들고(만든 것은 누가 만들어도 같다) 다른 변은 기다리지 않는다.</summary>
    private readonly ConcurrentDictionary<int, Lazy<Border?>> _borders = new();

    /// <summary>잠든 지방(사람이 모두 떠남): 바뀐 것만.</summary>
    public readonly SortedDictionary<int, DormantProvince> Dormant = new();

    /// <summary>화면용: 최근에 경계를 넘은 일(저장하지 않음 — 결과에 영향 없음).</summary>
    public readonly List<Crossing> Crossings = new();
    public const int CrossingsKept = 400;

    /// <summary>(S1) 넘겨주기를 기다리는 사람(모든 지방, 번호 순)과 그 시각(없으면 long.MaxValue).</summary>
    internal (int[] Ids, long At) CrossQueueForSave()
    {
        var ids = new List<int>();
        foreach (var (_, life) in Provinces) ids.AddRange(life.CrossQueue);
        ids.Sort();
        return (ids.ToArray(), ids.Count > 0 ? NextCrossTick(_w.Now) : long.MaxValue);
    }

    /// <summary>불러오기: 넘겨주기 대기를 그 사람의 지방에 둔다(시각은 늘 다음 맞추는 시각이다).</summary>
    internal void LoadCrossQueue(int[] ids, long at)
    {
        foreach (int id in ids) if (_byId.TryGetValue(id, out var p)) Life(p.Cell).CrossQueue.Add(id);
    }

    public static long NextCrossTick(long t) => (t + CrossInterval - 1) / CrossInterval * CrossInterval;

    // ───────── 변 ─────────

    public Border? BorderOf(int cell, int slot)
    {
        int key = cell * 6 + slot;
        if (_borders.TryGetValue(key, out var b)) return b.Value;
        return _borders.GetOrAdd(key, _ => new Lazy<Border?>(() => BuildBorder(cell, slot), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    private Border? BuildBorder(int cell, int slot)
    {
        var planet = _w.Planet;
        var a = _w.Province(cell);
        if (slot == a.BlockedSlot) return null;
        int nb = a.SlotCell[slot];
        if (nb < 0 || !planet.IsLand(nb)) return null; // 바다 · 호수 너머는 못 간다
        if (ProvinceLayout.Neighbor(planet, cell, slot) is not { } place) return null;
        var bmap = _w.Province(nb);
        int m = bmap.SlotOf(cell);
        if (m < 0 || m == bmap.BlockedSlot) return null;
        var border = new Border { Cell = cell, Slot = slot, Nb = nb, NbSlot = m, Place = place };
        // 이웃 지방 안쪽 방향
        {
            double ix = -HexFrame.NX[m], iy = -HexFrame.NY[m];
            double bd = double.NegativeInfinity;
            for (int d = 0; d < 8; d++)
            {
                double l = Math.Sqrt(DX8[d] * DX8[d] + DY8[d] * DY8[d]);
                double dot = (DX8[d] * ix + DY8[d] * iy) / l;
                if (dot > bd) { bd = dot; border.InDir8 = d; }
            }
        }
        bool BWalk(int t) => HexFrame.Inside[t] && GroundInfo.IsWalkable(bmap.Ground[t]);
        var gates = new List<int>(); var entries = new List<int>();
        for (int i = 0; i < HexFrame.TileCount; i++)
        {
            if (!HexFrame.Inside[i] || !GroundInfo.IsWalkable(a.Ground[i])) continue;
            double ed = HexFrame.EdgeDistance(i, slot);
            if (ed >= 1.5) continue;
            double px = i % S + 0.5 + HexFrame.NX[slot] * (ed + 0.8), py = i / S + 0.5 + HexFrame.NY[slot] * (ed + 0.8);
            var (bx, by) = place.ToLocal(px - HexFrame.Center, py - HexFrame.Center);
            int tx = (int)Math.Floor(bx), ty = (int)Math.Floor(by), entry = -1;
            for (int r = 0; r <= 2 && entry < 0; r++)
                for (int dy = -r; dy <= r && entry < 0; dy++)
                    for (int dx = -r; dx <= r && entry < 0; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                        int x = tx + dx, y = ty + dy;
                        if ((uint)x >= S || (uint)y >= S) continue;
                        int t = y * S + x;
                        if (BWalk(t) && HexFrame.EdgeDistance(t, m) < 3) entry = t;
                    }
            if (entry < 0) continue;
            gates.Add(i); entries.Add(entry);
        }
        border.Gates = gates.ToArray(); border.Entries = entries.ToArray();
        // 경계 너머 보이는 물 마실 자리(ProvinceLife.Drink 와 같은 규칙: 걸을 수 있고 둘레 8칸에 민물)
        var sx = new List<int>(); var sy = new List<int>(); var st = new List<int>();
        for (int t = 0; t < HexFrame.TileCount; t++)
        {
            if (!BWalk(t) || HexFrame.EdgeDistance(t, m) >= SightTiles + 2) continue;
            int x = t % S, y = t / S;
            bool fresh = false;
            for (int dy = -1; dy <= 1 && !fresh; dy++)
                for (int dx = -1; dx <= 1 && !fresh; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if ((uint)nx >= S || (uint)ny >= S) continue;
                    int j = ny * S + nx;
                    if (HexFrame.Inside[j] && GroundInfo.IsWater(bmap.Ground[j]) && (bmap.Flags[j] & TileFlags.Sea) == 0) fresh = true;
                }
            if (!fresh) continue;
            // 이웃 지도 좌표 → 이 지방 좌표: 이웃 배치(Place)는 이 지방이 가운데(0, 0)인 평면
            var (lx, ly) = place.ToLayout(x + 0.5, y + 0.5);
            sx.Add((int)Math.Round((lx + HexFrame.Center) * 1000)); sy.Add((int)Math.Round((ly + HexFrame.Center) * 1000)); st.Add(t);
        }
        border.SightX = sx.ToArray(); border.SightY = sy.ToArray(); border.SightTile = st.ToArray();
        border.IndexSight();
        return border;
    }

    /// <summary>here 에서 걸어갈 수 있는(같은 이어진 땅) 넘어갈 자리 중 (tx, ty)(밀리타일, 이 지방 좌표)에 가장 가까운 것의 번호(없으면 −1).</summary>
    private int NearestGate(Border b, ProvinceLife life, int here, long tx, long ty)
    {
        int best = -1; long bd = long.MaxValue;
        int reg = life.Region[here];
        if (reg < 0) return -1;
        for (int k = 0; k < b.Gates.Length; k++)
        {
            int g = b.Gates[k];
            if (life.Region[g] != reg) continue;
            long dx = g % S * 1000 + 500 - tx, dy = g / S * 1000 + 500 - ty, d = dx * dx + dy * dy;
            if (d < bd) { bd = d; best = k; }
        }
        return best;
    }

    /// <summary>here 에서 갈 수 있는 변들(이웃이 땅이고 걸어갈 수 있는 넘어갈 자리가 있음).</summary>
    private IEnumerable<Border> OpenBorders(ProvinceLife life, int here)
    {
        for (int s = 0; s < 6; s++)
        {
            var b = BorderOf(life.Cell, s);
            if (b is null || b.Gates.Length == 0) continue;
            int reg = life.Region[here];
            bool any = false;
            foreach (int g in b.Gates) if (life.Region[g] == reg) { any = true; break; }
            if (any) yield return b;
        }
    }

    /// <summary>
    /// 경계로 걸어가기 시작한다(넘겨주기 시각에 딱 닿게). gate = Gates 번호. 걸을 수 없으면 false.
    /// </summary>
    private bool StartCross(Person p, Border b, int gate, Why why, int arg = 0)
    {
        int g = b.Gates[gate];
        if (!WalkTo(p, g)) return false;
        var m = p.Current;
        long t = NextCrossTick(m.Arrive);
        m.Arrive = t;
        p.ActUntil = t;
        Schedule(p, t);
        SetWhy(p, Act.Cross, why, arg);
        p.Target = g; p.TargetB = b.Slot;
        return true;
    }

    /// <summary>경계 타일에 닿았다: 넘겨주기를 기다린다(다음 맞추는 시각에 — 그 시각의 예약을 다 처리한 뒤).</summary>
    private void ArriveAtBorder(Person p)
    {
        Life(p.Cell).CrossQueue.Add(p.Id);
        p.Walking = false;
    }

    /// <summary>넘겨주기(맞추는 시각에 GameWorld 가 — 모든 지방의 대기를 사람 번호 순으로).</summary>
    internal void DoCrossings()
    {
        List<int>? queue = null;
        foreach (var life in Lives()) { if (life.CrossQueue.Count == 0) continue; (queue ??= new()).AddRange(life.CrossQueue); life.CrossQueue.Clear(); }
        if (queue is null) return;
        var ids = queue.ToArray();
        St.CrossBatches++;
        Array.Sort(ids);
        foreach (int id in ids)
        {
            var p = _byId[id];
            if (p.Dead || p.Act != Act.Cross || p.Walking || p.PendingHandle != 0) continue; // 그 사이 다른 일을 시작했다
            var b = BorderOf(p.Cell, p.TargetB);
            int gi = b is null ? -1 : Array.BinarySearch(b.Gates, p.Target);
            if (b is null || gi < 0 || p.Tile(Now) != p.Target) { Decide(p); continue; }
            Transfer(p, b, b.Entries[gi]);
        }
    }

    private void Transfer(Person p, Border b, int entry)
    {
        var from = Life(p.Cell);
        int fromTile = p.Tile(Now);
        bool fresh = !Provinces.ContainsKey(b.Nb) && !Dormant.ContainsKey(b.Nb); // 아무도 들어가 본 적 없는 땅
        var to = Life(b.Nb);
        from = Life(p.Cell); // (to 를 깨우며 마지막 지방 기억이 바뀌었을 수 있다)
        entry = OpenEntry(to, entry);
        var babies = new List<Person>();
        foreach (var q in from.People) if (!q.Dead && q.Carrier == p.Id) babies.Add(q);
        foreach (var q in babies.Prepend(p))
        {
            Integrate(q, Now);
            RemoveSorted(from.People, q);
            InsertSorted(to.People, q);
            q.Cell = b.Nb;
            q.FromCell = from.Cell;
            q.ArrivedTick = Now;
            q.NewMotion().SetStill(Now, entry % S * 1000 + 500, entry / S * 1000 + 500);
        }
        MoveCompanions(p, from, to, entry);
        p.SearchDir = b.InDir8;
        St.Crossings++;
        if (p.Why is Why.Scarce or Why.SeekMate or Why.Explore or Why.NoWater or Why.NoFood or Why.FollowFamily or Why.Break || !VisitedBefore(p, b.Nb))
            AddBio(p, BioKind.Moved, b.Nb, (int)p.Why | (from.Cell << 8));
        if (fresh) Notify(WorldEventKind.Moved, p, from.Cell, $"{Hangul.I(DisplayName(p))} 아무도 살지 않던 새 땅으로 들어갔습니다");
        Crossings.Add(new Crossing(Now, p.Id, from.Cell, b.Nb, b.Slot, fromTile, entry, p.Why));
        if (Crossings.Count > CrossingsKept) Crossings.RemoveRange(0, Crossings.Count - CrossingsKept);
        ForgetLastLife();
        Decide(p);
    }

    /// <summary>
    /// (E5b 고침) 넘어가 설 자리가 바위 · 물에 갇힌 작은 땅이면(변은 지도의 땅만 보고 만들어 이어짐을 모른다) 가까운(10타일 안) 넓은 땅으로.
    /// 갇힌 땅에 서면 물도 출구도 없어 목말라 죽거나, 두 지방 사이를 끝없이 오갔다.
    /// </summary>
    private static int OpenEntry(ProvinceLife to, int entry)
    {
        if (to.OpenLand(entry)) return entry;
        int ex = entry % S, ey = entry / S;
        for (int r = 1; r <= 10; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    int x = ex + dx, y = ey + dy;
                    if ((uint)x >= S || (uint)y >= S) continue;
                    int t = y * S + x;
                    if (to.Walk[t] && to.OpenLand(t)) return t;
                }
        return entry;
    }

    private bool VisitedBefore(Person p, int cell)
    {
        foreach (var e in p.Bio) if (e.Kind == BioKind.Moved && e.Other == cell) return true;
        return false;
    }

    private static void RemoveSorted(List<Person> list, Person p)
    {
        int i = IndexSorted(list, p.Id);
        if (i >= 0) list.RemoveAt(i);
    }

    private static void InsertSorted(List<Person> list, Person p)
    {
        int i = IndexSorted(list, p.Id);
        if (i < 0) list.Insert(~i, p);
    }

    private static int IndexSorted(List<Person> list, int id)
    {
        int lo = 0, hi = list.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            int c = list[mid].Id.CompareTo(id);
            if (c == 0) return mid;
            if (c < 0) lo = mid + 1; else hi = mid - 1;
        }
        return ~lo;
    }

    // ───────── 잠들기 · 깨어나기 ─────────

    /// <summary>비고 나서 이만큼 지나면 잠든다(사람이 곧 되돌아오는 일이 많아 바로 재우면 깨우는 비용이 크다).</summary>
    public const long SleepAfter = 10 * GameTime.TicksPerDay;

    /// <summary>하루 경계: 살아 있는 사람이 없어진 지 SleepAfter 가 지난 지방을 잠재운다. 잠들었는지는 결과에 영향이 없다.</summary>
    private void SleepEmptyProvinces()
    {
        List<int>? empty = null;
        foreach (var (cell, life) in Provinces)
        {
            bool alive = false;
            foreach (var q in life.People) if (!q.Dead) { alive = true; break; }
            if (alive) { life.EmptySince = -1; continue; }
            if (life.EmptySince < 0) life.EmptySince = Now;
            if (Now - life.EmptySince >= SleepAfter) (empty ??= new List<int>()).Add(cell);
        }
        if (empty is null) return;
        foreach (int cell in empty)
        {
            var life = Provinces[cell];
            SleepAnimals(life);
            Provinces.Remove(cell);
            _livesDirty = true;
            Dormant[cell] = new DormantProvince(cell, life.Stocks.Select(kv => (kv.Key, kv.Value)).ToArray(), life.Revealed.ToArray(), life.People)
            {
                Fires = life.Fires.Select(kv => (kv.Key, kv.Value)).ToArray(),
                Ground = life.Items.SelectMany(kv => kv.Value.Select(c => (kv.Key, c.Item, c.Count))).ToArray(),
                Dug = life.Dug.ToArray(),
                // (S1) 예약 번호는 지방마다 이어진다(잠들었다 깨어나도 — 잠든 상태가 결과에 영향이 없게)
                SchedHandle = life.Sched.NextHandle, SchedOrder = life.Sched.NextOrder, Events = life.Sched.ExportSorted(),
                Herds = life.Herds.Values.ToList(), NextHerdId = life.NextHerdId, FishStock = life.FishStock, SleptAt = Now, AnimalsReady = true,
                Plots = life.Plots.Values.ToList(), Stash = life.Stash, FarmSince = life.FarmSince, YearHarvest = life.YearHarvest, YearPotential = life.YearPotential, HarvestYear = life.HarvestYear, FirstHarvest = life.FirstHarvest, SeedsEatenYear = life.SeedsEatenYear,
                Buildings = life.Buildings.Values.ToList(), NextBuildingId = life.NextBuildingId, Cleared = life.Cleared.ToArray(),
            };
        }
        ForgetLastLife();
    }

    /// <summary>한 번이라도 사람이 들어간 지방(깨어 있거나 잠든).</summary>
    public IEnumerable<int> OpenedProvinces()
    {
        var all = new SortedSet<int>(Provinces.Keys);
        foreach (var c in Dormant.Keys) all.Add(c);
        return all;
    }

    /// <summary>그 지방의 걷힌 숨김(깨어 있든 잠들었든).</summary>
    public IReadOnlyCollection<int> RevealedOf(int cell) =>
        Provinces.TryGetValue(cell, out var l) ? l.Revealed : Dormant.TryGetValue(cell, out var d) ? d.Revealed : Array.Empty<int>();

    /// <summary>그 지방에 있는 사람(깨어 있든 잠들었든, 번호 순).</summary>
    public IReadOnlyList<Person> PeopleIn(int cell) =>
        Provinces.TryGetValue(cell, out var l) ? l.People : Dormant.TryGetValue(cell, out var d) ? d.People : Array.Empty<Person>();
}

/// <summary>잠든 지방: 딴 몫(타일 번호 순) · 걷힌 숨김 · 그곳의 (죽은) 사람 · (E4) 불 · 땅에 놓인 물건 · 캐낸 바위 · (S1) 예약표의 다음 번호(와 불러온 예약).</summary>
public sealed record DormantProvince(int Cell, (int Tile, TileStock Stock)[] Stocks, int[] Revealed, List<Person> People)
{
    public (int Tile, Fire Fire)[] Fires { get; init; } = Array.Empty<(int, Fire)>();
    public (int Tile, int Item, int Count)[] Ground { get; init; } = Array.Empty<(int, int, int)>();
    public int[] Dug { get; init; } = Array.Empty<int>();
    public long SchedHandle { get; init; } = 1;
    public long SchedOrder { get; init; } = 1;
    public Scheduling.ScheduledEvent[] Events { get; init; } = Array.Empty<Scheduling.ScheduledEvent>();
    /// <summary>(E5b) 짐승 무리 · 다음 무리 번호 · 물고기 몫 · 잠든 시각(−1 = 저장할 때 깨어 있었다 — 예약이 그대로 있다) · 짐승을 이미 만들었나(버전 9 이하 파일은 아니다).</summary>
    public List<Herd> Herds { get; init; } = new();
    public int NextHerdId { get; init; } = 1;
    public int FishStock { get; init; }
    public long SleptAt { get; init; } = -1;
    public bool AnimalsReady { get; init; }
    /// <summary>(E5c) 밭 · 모아 둔 자리 · 처음 밭 · 올해 거둔 양(ProvinceLife 와 같은 뜻).</summary>
    public List<Plot> Plots { get; init; } = new();
    public int Stash { get; init; } = -1;
    public long FarmSince { get; init; } = -1;
    public int YearHarvest { get; init; }
    public int YearPotential { get; init; }
    public int HarvestYear { get; init; }
    public long FirstHarvest { get; init; } = -1;
    public int SeedsEatenYear { get; init; } = -1;
    /// <summary>(E6) 건물(번호 순) · 다음 건물 번호 · 걷은 덤불 타일.</summary>
    public List<Building> Buildings { get; init; } = new();
    public int NextBuildingId { get; init; } = 1;
    public int[] Cleared { get; init; } = Array.Empty<int>();
}

/// <summary>경계를 넘은 일 하나(화면 · 지구본의 이동 표시).</summary>
public readonly record struct Crossing(long Tick, int Person, int From, int To, int Slot, int FromTile, int ToTile, Why Why);
