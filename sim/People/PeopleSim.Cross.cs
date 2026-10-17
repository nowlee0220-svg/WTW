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

    private readonly List<int> _crossQueue = new();
    private long _crossAt = long.MaxValue;
    private readonly Dictionary<int, Border?> _borders = new(); // 조회 전용(순회하지 않음)

    /// <summary>잠든 지방(사람이 모두 떠남): 바뀐 것만.</summary>
    public readonly SortedDictionary<int, DormantProvince> Dormant = new();

    /// <summary>화면용: 최근에 경계를 넘은 일(저장하지 않음 — 결과에 영향 없음).</summary>
    public readonly List<Crossing> Crossings = new();
    public const int CrossingsKept = 400;

    /// <summary>다음 넘겨주기 시각(없으면 long.MaxValue).</summary>
    public long CrossTick => _crossQueue.Count > 0 ? _crossAt : long.MaxValue;

    internal (int[] Ids, long At) CrossQueueForSave() => (_crossQueue.ToArray(), _crossQueue.Count > 0 ? _crossAt : long.MaxValue);
    internal void LoadCrossQueue(int[] ids, long at) { _crossQueue.Clear(); _crossQueue.AddRange(ids); _crossAt = ids.Length > 0 ? at : long.MaxValue; }

    public static long NextCrossTick(long t) => (t + CrossInterval - 1) / CrossInterval * CrossInterval;

    // ───────── 변 ─────────

    public Border? BorderOf(int cell, int slot)
    {
        int key = cell * 6 + slot;
        if (_borders.TryGetValue(key, out var b)) return b;
        b = BuildBorder(cell, slot);
        _borders[key] = b;
        return b;
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

    /// <summary>경계 타일에 닿았다: 넘겨주기를 기다린다(이 시각의 예약을 다 처리한 뒤).</summary>
    private void ArriveAtBorder(Person p)
    {
        _crossQueue.Add(p.Id);
        long t = NextCrossTick(Now);
        if (_crossQueue.Count == 1 || t < _crossAt) _crossAt = t;
        p.Walking = false;
    }

    /// <summary>넘겨주기(GameWorld 가 그 시각의 예약을 다 처리한 뒤 부른다).</summary>
    internal void DoCrossings()
    {
        var ids = _crossQueue.ToArray();
        _crossQueue.Clear();
        Stats.CrossBatches++;
        _crossAt = long.MaxValue;
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
        var to = Life(b.Nb);
        from = Life(p.Cell); // (to 를 깨우며 마지막 지방 기억이 바뀌었을 수 있다)
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
        p.SearchDir = b.InDir8;
        Stats.Crossings++;
        if (p.Why is Why.Scarce or Why.SeekMate or Why.Explore or Why.NoWater or Why.NoFood or Why.FollowFamily or Why.Break || !VisitedBefore(p, b.Nb))
            AddBio(p, BioKind.Moved, b.Nb, (int)p.Why | (from.Cell << 8));
        Crossings.Add(new Crossing(Now, p.Id, from.Cell, b.Nb, b.Slot, fromTile, entry, p.Why));
        if (Crossings.Count > CrossingsKept) Crossings.RemoveRange(0, Crossings.Count - CrossingsKept);
        _lastLife = null;
        Decide(p);
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
            Provinces.Remove(cell);
            Dormant[cell] = new DormantProvince(cell, life.Stocks.Select(kv => (kv.Key, kv.Value)).ToArray(), life.Revealed.ToArray(), life.People);
        }
        _lastLife = null;
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

/// <summary>잠든 지방: 딴 몫(타일 번호 순) · 걷힌 숨김 · 그곳의 (죽은) 사람.</summary>
public sealed record DormantProvince(int Cell, (int Tile, TileStock Stock)[] Stocks, int[] Revealed, List<Person> People);

/// <summary>경계를 넘은 일 하나(화면 · 지구본의 이동 표시).</summary>
public readonly record struct Crossing(long Tick, int Person, int From, int To, int Slot, int FromTile, int ToTile, Why Why);
