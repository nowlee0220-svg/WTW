using Wtw.Sim.Provinces;

namespace Wtw.Sim.People;

/// <summary>
/// 지방 하나의 길 찾기(150×150). 8방향 A*(모서리를 자르지 않음) + 얕은 물 · 습지는 비싸게 → 꺾임점만 남기도록 줄인다(보이는 곳까지 곧게).
/// 이어진 땅 번호(Region)가 다르면 찾지 않고 바로 포기한다. 버퍼는 지방마다 한 벌(시뮬 스레드 전용), 지우지 않고 세대 번호로 쓴다.
/// 결과는 지도 + 출발 · 도착으로만 정해진다(결정론: 같은 비용이면 번호가 작은 칸이 먼저).
/// </summary>
public sealed class PathFinder
{
    public const int MaxExpand = 9000;
    private const int S = HexFrame.Size;
    private readonly ProvinceLife _life;
    private readonly int[] _g = new int[HexFrame.TileCount];
    private readonly int[] _parent = new int[HexFrame.TileCount];
    private readonly int[] _seen = new int[HexFrame.TileCount];
    private readonly int[] _closed = new int[HexFrame.TileCount];
    private readonly byte[] _cost = new byte[HexFrame.TileCount];
    private int _gen;
    private long[] _heap = new long[1024];
    private int _heapCount;
    private readonly List<int> _tiles = new();

    public long Searches { get; private set; }
    public long Expanded { get; private set; }

    public PathFinder(ProvinceLife life)
    {
        _life = life;
        var g = life.Map.Ground;
        for (int i = 0; i < HexFrame.TileCount; i++)
            _cost[i] = !life.Walk[i] ? (byte)0 : g[i] is Ground.ShallowWater ? (byte)3 : g[i] is Ground.Marsh or Ground.Mud ? (byte)2 : (byte)1;
    }

    public bool Reachable(int from, int to) => _life.Walk[from] && _life.Walk[to] && _life.Region[from] == _life.Region[to];

    /// <summary>(E4) 타일이 걸을 수 있게 바뀌었다(캐낸 바위).</summary>
    public void SetWalkable(int tile, byte cost) => _cost[tile] = cost;

    /// <summary>
    /// (sx, sy) 밀리타일 위치에서 타일 goal 까지. 성공하면 꺾임점(밀리타일, 처음 = 지금 위치)을 points 에 채운다.
    /// </summary>
    public bool Find(int sx, int sy, int goal, List<(int X, int Y)> points)
    {
        points.Clear();
        int start = (sy / 1000) * S + sx / 1000;
        if ((uint)start >= HexFrame.TileCount || !Reachable(start, goal)) return false;
        Searches++;
        _tiles.Clear();
        if (start == goal) { points.Add((sx, sy)); points.Add(Center(goal)); return true; }
        // 곧게 보이면 찾을 것도 없다(탁 트인 땅에서 대부분)
        if (_cost[start] == 1 && Clear(sx, sy, Center(goal))) { points.Add((sx, sy)); points.Add(Center(goal)); return true; }
        if (++_gen == int.MaxValue) { Array.Clear(_seen); Array.Clear(_closed); _gen = 1; }
        int gx = goal % S, gy = goal / S;
        _heapCount = 0;
        _g[start] = 0; _parent[start] = -1; _seen[start] = _gen;
        Push(H(start % S, start / S, gx, gy), 0, start);
        int expanded = 0;
        bool found = false;
        while (_heapCount > 0)
        {
            int cur = Pop();
            if (_closed[cur] == _gen) continue;
            _closed[cur] = _gen;
            if (cur == goal) { found = true; break; }
            if (++expanded > MaxExpand) break;
            int cx = cur % S, cy = cur / S;
            for (int k = 0; k < 8; k++)
            {
                int dx = DX[k], dy = DY[k];
                int nx = cx + dx, ny = cy + dy;
                if ((uint)nx >= S || (uint)ny >= S) continue;
                int n = ny * S + nx;
                if (_cost[n] == 0 || _closed[n] == _gen) continue;
                if (dx != 0 && dy != 0 && (_cost[cy * S + nx] == 0 || _cost[ny * S + cx] == 0)) continue; // 모서리 자르기 금지
                int ng = _g[cur] + (dx != 0 && dy != 0 ? 14 : 10) * _cost[n];
                if (_seen[n] == _gen && ng >= _g[n]) continue;
                _seen[n] = _gen; _g[n] = ng; _parent[n] = cur;
                Push(ng + H(nx, ny, gx, gy), ng, n);
            }
        }
        Expanded += expanded;
        if (!found) return false;
        for (int t = goal; t >= 0; t = _parent[t]) _tiles.Add(t);
        _tiles.Reverse();
        Smooth(sx, sy, points);
        return true;
    }

    /// <summary>곧게 걸어갈 수 있을 때만 길을 채운다(둘러보기처럼 아무 데나 가도 되는 걸음 — 찾기 비용 없음).</summary>
    public bool TryStraight(int sx, int sy, int goal, List<(int X, int Y)> points)
    {
        points.Clear();
        int start = (sy / 1000) * S + sx / 1000;
        if ((uint)start >= HexFrame.TileCount || !Reachable(start, goal) || _cost[start] != 1) return false;
        if (!Clear(sx, sy, Center(goal))) return false;
        points.Add((sx, sy)); points.Add(Center(goal));
        return true;
    }

    private static readonly int[] DX = { 1, -1, 0, 0, 1, 1, -1, -1 };
    private static readonly int[] DY = { 0, 0, 1, -1, 1, -1, 1, -1 };

    private static int H(int x, int y, int gx, int gy)
    {
        int dx = Math.Abs(x - gx), dy = Math.Abs(y - gy);
        return (10 * Math.Max(dx, dy) + 4 * Math.Min(dx, dy)) * 6 / 5; // 조금 부풀린 거리(조금 덜 곧은 길 대신 훨씬 덜 헤맨다)
    }

    public static (int X, int Y) Center(int tile) => (tile % S * 1000 + 500, tile / S * 1000 + 500);

    /// <summary>꺾임점만 남긴다: 지금 점에서 보이는(곧게 걸을 수 있는) 가장 먼 길 타일까지 바로 간다.</summary>
    private void Smooth(int sx, int sy, List<(int X, int Y)> points)
    {
        points.Add((sx, sy));
        var (ax, ay) = (sx, sy);
        int i = 0, n = _tiles.Count;
        while (i < n - 1)
        {
            // 보이는 가장 먼 타일: 1, 2, 4, 8 … 칸씩 늘려 보다가 막히면 그 사이를 반으로 나눠 찾는다
            int j = i + 1, step = 1, bad = n;
            while (true)
            {
                int k = Math.Min(n - 1, j + step);
                if (k == j) break;
                if (Clear(ax, ay, Center(_tiles[k]))) { j = k; step *= 2; }
                else { bad = k; break; }
            }
            while (bad - j > 1)
            {
                int mid = (j + bad) / 2;
                if (Clear(ax, ay, Center(_tiles[mid]))) j = mid; else bad = mid;
            }
            var c = Center(_tiles[j]);
            points.Add(c);
            (ax, ay) = c;
            i = j;
        }
    }

    /// <summary>a → b 를 곧게 걸을 수 있나: 사이를 1/4 타일마다 보아 모두 비싸지 않은 땅(비용 1)이고 대각선 모서리도 막히지 않았나.</summary>
    private bool Clear(int ax, int ay, (int X, int Y) b)
    {
        long dx = b.X - ax, dy = b.Y - ay;
        long len = Math.Max(Math.Abs(dx), Math.Abs(dy));
        int steps = (int)(len / 250) + 1;
        int px = ax / 1000, py = ay / 1000;
        for (int s = 1; s <= steps; s++)
        {
            int x = (int)((ax + dx * s / steps) / 1000), y = (int)((ay + dy * s / steps) / 1000);
            if ((uint)x >= S || (uint)y >= S) return false;
            if (_cost[y * S + x] != 1) return false;
            if (x != px && y != py && (_cost[py * S + x] != 1 || _cost[y * S + px] != 1)) return false;
            px = x; py = y;
        }
        return true;
    }

    /// <summary>열쇠 = (f, 더 멀리 온 것 먼저, 번호) — 같은 f 면 g 가 큰 쪽을 먼저 펼쳐 탁 트인 땅에서 덜 헤맨다.</summary>
    private void Push(int f, int g, int node)
    {
        if (_heapCount == _heap.Length) Array.Resize(ref _heap, _heap.Length * 2);
        long key = ((long)f << 42) | ((long)(0x3FFFFF - Math.Min(g, 0x3FFFFF)) << 20) | (uint)node;
        int i = _heapCount++;
        _heap[i] = key;
        while (i > 0)
        {
            int p = (i - 1) >> 1;
            if (_heap[p] <= key) break;
            _heap[i] = _heap[p]; i = p;
        }
        _heap[i] = key;
    }

    private int Pop()
    {
        long top = _heap[0];
        long last = _heap[--_heapCount];
        int i = 0;
        while (true)
        {
            int l = 2 * i + 1;
            if (l >= _heapCount) break;
            int r = l + 1;
            int m = r < _heapCount && _heap[r] < _heap[l] ? r : l;
            if (_heap[m] >= last) break;
            _heap[i] = _heap[m]; i = m;
        }
        if (_heapCount > 0) _heap[i] = last;
        return (int)(top & 0xFFFFF);
    }
}
