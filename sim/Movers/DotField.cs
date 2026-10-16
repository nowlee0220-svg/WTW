using Wtw.Sim.Core;
using Wtw.Sim.Scheduling;
using Wtw.Sim.Time;

namespace Wtw.Sim.Movers;

/// <summary>점들의 걷기 규칙. 난수는 ("dots", 지방) 스트림에서만 뽑는다.</summary>
public sealed class DotField
{
    public const string StreamName = "dots";
    public const int TileUnits = 1000; // 1타일 = 1000 밀리타일

    private readonly List<Dot> _dots = new();          // 만든 순서 (순회용)
    private readonly Dictionary<int, Dot> _byId = new(); // 조회 전용

    public IReadOnlyList<Dot> Dots => _dots;
    public int MapUnits { get; }

    public DotField(int mapTiles) { MapUnits = mapTiles * TileUnits; }

    public Dot Get(int id) => _byId[id];

    public void AddLoaded(Dot d)
    {
        _dots.Add(d);
        _byId.Add(d.Id, d);
    }

    /// <summary>새 점: 임의 지점에서 쉬다가 한 시간 안에 첫 출발.</summary>
    public Dot Spawn(int id, long now, Pcg32 rng, Scheduler sched)
    {
        var d = new Dot
        {
            Id = id,
            Province = 0,
            TicksPerTile = rng.NextInt(12, 25), // 1배속에서 초당 2.5~5타일 (림월드 사람 걷기와 비슷)
            ColorIndex = rng.NextInt(8),
        };
        int x = rng.NextInt(MapUnits), y = rng.NextInt(MapUnits);
        d.NewLeg().Set(now, now, new[] { x }, new[] { y });
        d.Walking = false;
        d.PendingHandle = sched.Schedule(now + 1 + rng.NextInt((int)GameTime.TicksPerHour), id, World.EventKinds.DotDepart);
        AddLoaded(d);
        return d;
    }

    public void OnDepart(Dot d, long now, Pcg32 rng, Scheduler sched)
    {
        d.IntPositionAt(now, out int sx, out int sy);
        int tx = rng.NextInt(MapUnits), ty = rng.NextInt(MapUnits);
        StartWalk(d, now, sx, sy, tx, ty, rng, sched);
    }

    public void OnArrive(Dot d, long now, Pcg32 rng, Scheduler sched)
    {
        d.Walking = false;
        // 쉼: 1분 ~ 1시간
        long rest = GameTime.TicksPerHour / 60 + rng.NextInt((int)GameTime.TicksPerHour);
        var cur = d.Current;
        d.NewLeg().Set(now, now, new[] { cur.EndX }, new[] { cur.EndY });
        d.PendingHandle = sched.Schedule(now + rest, d.Id, World.EventKinds.DotDepart);
    }

    /// <summary>하던 일을 끊고 (x, y) 근처로 간다. 예약을 취소하고 새로 건다.</summary>
    public void Interrupt(Dot d, long now, int x, int y, Pcg32 rng, Scheduler sched)
    {
        d.IntPositionAt(now, out int sx, out int sy);
        if (d.PendingHandle != 0) sched.Cancel(d.PendingHandle);
        int spread = 6 * TileUnits;
        int tx = Math.Clamp(x + rng.NextInt(-spread, spread + 1), 0, MapUnits - 1);
        int ty = Math.Clamp(y + rng.NextInt(-spread, spread + 1), 0, MapUnits - 1);
        StartWalk(d, now, sx, sy, tx, ty, rng, sched);
    }

    private void StartWalk(Dot d, long now, int sx, int sy, int tx, int ty, Pcg32 rng, Scheduler sched)
    {
        int bends = rng.NextInt(3); // 꺾임 0~2개
        Span<int> xs = stackalloc int[Leg.MaxPoints];
        Span<int> ys = stackalloc int[Leg.MaxPoints];
        xs[0] = sx; ys[0] = sy;
        int jitter = 12 * TileUnits;
        for (int k = 1; k <= bends; k++)
        {
            int mx = sx + (int)((long)(tx - sx) * k / (bends + 1));
            int my = sy + (int)((long)(ty - sy) * k / (bends + 1));
            xs[k] = Math.Clamp(mx + rng.NextInt(-jitter, jitter + 1), 0, MapUnits - 1);
            ys[k] = Math.Clamp(my + rng.NextInt(-jitter, jitter + 1), 0, MapUnits - 1);
        }
        xs[bends + 1] = tx; ys[bends + 1] = ty;
        int n = bends + 2;

        var leg = d.NewLeg();
        leg.Set(now, now, xs[..n], ys[..n]);
        long duration = Math.Max(1, (leg.Length * d.TicksPerTile + TileUnits - 1) / TileUnits);
        leg.Arrive = now + duration;
        d.Walking = true;
        d.PendingHandle = sched.Schedule(leg.Arrive, d.Id, World.EventKinds.DotArrive);
    }
}
