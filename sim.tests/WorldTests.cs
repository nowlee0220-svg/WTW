using Wtw.Sim.Movers;
using Wtw.Sim.Persistence;
using Wtw.Sim.Time;
using Wtw.Sim.World;
using Xunit;

namespace Wtw.Sim.Tests;

[Trait("Category", "Fast")]
public class WorldTests
{
    private static readonly WorldConfig Small = new() { DotCount = 60 };

    [Fact]
    public void SameSeedSameWorld()
    {
        var a = GameWorld.Create(5, Small);
        var b = GameWorld.Create(5, Small);
        a.AdvanceTo(GameTime.TicksPerDay * 3 + 777);
        b.AdvanceTo(GameTime.TicksPerDay * 3 + 777);
        Assert.Equal(SaveGame.ToBytes(a), SaveGame.ToBytes(b));
        var c = GameWorld.Create(6, Small);
        c.AdvanceTo(GameTime.TicksPerDay * 3 + 777);
        Assert.NotEqual(SaveGame.Fingerprint(a), SaveGame.Fingerprint(c));
    }

    [Fact]
    public void ChunkingDoesNotChangeResult()
    {
        var a = GameWorld.Create(11, Small);
        a.AdvanceTo(GameTime.TicksPerDay * 2);
        var b = GameWorld.Create(11, Small);
        for (long t = 0; t <= GameTime.TicksPerDay * 2; t += 997) b.AdvanceTo(t);
        b.AdvanceTo(GameTime.TicksPerDay * 2);
        Assert.Equal(SaveGame.ToBytes(a), SaveGame.ToBytes(b));
    }

    [Fact]
    public void EntityIdsAreSequential()
    {
        var w = GameWorld.Create(1, new WorldConfig { DotCount = 5 });
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, w.Dots.Dots.Select(d => d.Id).ToArray());
        w.AdvanceTo(100);
        w.Apply(new Intervention(100, Intervention.Kinds.AddDots, new long[] { 2 }));
        Assert.Equal(new[] { 6, 7 }, w.Dots.Dots.Skip(5).Select(d => d.Id).ToArray());
    }

    [Fact]
    public void SaveLoadContinueEqualsUninterrupted()
    {
        long t1 = GameTime.TicksPerDay + 12_345, t2 = GameTime.TicksPerDay * 4 + 5;

        var straight = GameWorld.Create(77, Small);
        straight.AdvanceTo(t1);
        straight.Apply(new Intervention(t1, Intervention.Kinds.Gather, new long[] { 40_000, 90_000 }));
        var bytes = SaveGame.ToBytes(straight);
        straight.AdvanceTo(t2);

        var loaded = SaveGame.FromBytes(bytes);
        Assert.Equal(bytes, SaveGame.ToBytes(loaded)); // 읽고 바로 쓰면 같은 바이트
        loaded.AdvanceTo(t2);

        Assert.Equal(SaveGame.ToBytes(straight), SaveGame.ToBytes(loaded));
    }

    [Fact]
    public void SaveFileRoundTrip()
    {
        var w = GameWorld.Create(3, Small);
        w.AdvanceTo(30_000);
        var path = Path.Combine(Path.GetTempPath(), $"wtw-test-{Guid.NewGuid():N}.wtw");
        try
        {
            SaveGame.SaveFile(w, path);
            var l = SaveGame.LoadFile(path);
            Assert.Equal(SaveGame.ToBytes(w), SaveGame.ToBytes(l));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LoadRejectsWrongFileAndVersion()
    {
        Assert.Throws<InvalidDataException>(() => SaveGame.FromBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
        var bytes = SaveGame.ToBytes(GameWorld.Create(1, Small));
        bytes[4] = 99; // 버전
        var e = Assert.Throws<InvalidDataException>(() => SaveGame.FromBytes(bytes));
        Assert.Contains("버전", e.Message);
    }

    [Fact]
    public void ReplayFromStartRecordEqualsOriginal()
    {
        var w = GameWorld.Create(2024, Small);
        w.AdvanceTo(20_000);
        w.Apply(new Intervention(20_000, Intervention.Kinds.AddDots, new long[] { 15 }));
        w.Apply(new Intervention(20_000, Intervention.Kinds.Gather, new long[] { 10_000, 10_000 }));
        w.AdvanceTo(GameTime.TicksPerDay + 3);
        w.Apply(new Intervention(GameTime.TicksPerDay + 3, Intervention.Kinds.Gather, new long[] { 140_000, 75_000 }));
        w.AdvanceTo(GameTime.TicksPerDay * 3);

        var record = w.ToStartRecord();
        Assert.Equal(3, record.Interventions.Count);
        var replayed = GameWorld.Replay(record, GameTime.TicksPerDay * 3);
        Assert.Equal(SaveGame.ToBytes(w), SaveGame.ToBytes(replayed));
    }

    [Fact]
    public void DayBoundaryHookRunsOncePerDayBeforeThatTicksEvents()
    {
        var w = GameWorld.Create(8, Small);
        var days = new List<long>();
        w.DayBoundary += (world, day) =>
        {
            days.Add(day);
            Assert.Equal(day * GameTime.TicksPerDay, world.Now);
            Assert.True(!world.Scheduler.TryPeek(out var e) || e.Tick >= world.Now);
        };
        w.AdvanceTo(GameTime.TicksPerDay * 5);
        Assert.Equal(new long[] { 1, 2, 3, 4, 5 }, days);
        Assert.Equal(5, w.DaysElapsed);
    }

    [Fact]
    public void DotsArriveExactlyAtArriveTickOnTheirPath()
    {
        var w = GameWorld.Create(31, Small);
        // 점마다 도착 예약 시각을 기억해 두고, 그 시각까지만 돌렸을 때 정확히 끝점에 있어야 한다.
        int checkedArrivals = 0;
        for (int step = 0; step < 40; step++)
        {
            Assert.True(w.Scheduler.TryPeek(out _));
            var walking = w.Dots.Dots.Where(d => d.Walking).ToList();
            if (walking.Count == 0) { w.AdvanceTo(w.Now + 500); continue; }
            var d = walking.OrderBy(x => x.Current.Arrive).ThenBy(x => x.Id).First();
            var leg = d.Current;
            long arrive = leg.Arrive;
            int ex = leg.EndX, ey = leg.EndY;
            w.AdvanceTo(arrive - 1);
            Assert.True(d.Walking);
            w.AdvanceTo(arrive);
            Assert.False(d.Walking);
            d.IntPositionAt(arrive, out int x, out int y);
            Assert.Equal((ex, ey), (x, y));
            checkedArrivals++;
        }
        Assert.True(checkedArrivals > 20);
    }

    [Fact]
    public void InterpolatedPositionLiesOnPath()
    {
        var leg = new Leg();
        leg.Set(1000, 1000, new[] { 0, 10_000, 10_000, 30_000 }, new[] { 0, 0, 20_000, 20_000 });
        leg.Arrive = 1000 + 5000;
        Assert.Equal(50_000, leg.Length);
        for (double t = 900; t <= 6100; t += 13.37)
        {
            leg.PositionAt(t, out double x, out double y);
            Assert.True(DistanceToPolyline(leg, x, y) < 1e-6, $"t={t} ({x},{y})");
            leg.IntPositionAt((long)t, out int ix, out int iy);
            Assert.True(DistanceToPolyline(leg, ix, iy) <= 1.5, $"int t={t}");
        }
        leg.PositionAt(6000, out double ax, out double ay);
        Assert.Equal((30_000.0, 20_000.0), (ax, ay));
        leg.PositionAt(1000, out double sx, out double sy);
        Assert.Equal((0.0, 0.0), (sx, sy));
        leg.PositionAt(3500, out double mx, out double my); // 절반 = 길이 25,000 → 둘째 마디 중간
        Assert.Equal(10_000, mx, 6);
        Assert.Equal(15_000, my, 6);
    }

    [Fact]
    public void WorldDotsInterpolateOnTheirLegs()
    {
        var w = GameWorld.Create(12, Small);
        w.AdvanceTo(GameTime.TicksPerHour * 3);
        foreach (var d in w.Dots.Dots)
        {
            var leg = d.Current;
            for (int k = 0; k <= 10; k++)
            {
                double t = leg.Depart + (leg.Arrive - leg.Depart) * k / 10.0;
                leg.PositionAt(t, out double x, out double y);
                Assert.True(DistanceToPolyline(leg, x, y) < 1e-6);
                Assert.InRange(x, 0, w.Dots.MapUnits);
                Assert.InRange(y, 0, w.Dots.MapUnits);
            }
        }
    }

    private static double DistanceToPolyline(Leg leg, double px, double py)
    {
        if (leg.PointCount == 1) return Math.Sqrt((px - leg.X[0]) * (px - leg.X[0]) + (py - leg.Y[0]) * (py - leg.Y[0]));
        double best = double.MaxValue;
        for (int i = 1; i < leg.PointCount; i++)
        {
            double ax = leg.X[i - 1], ay = leg.Y[i - 1], bx = leg.X[i], by = leg.Y[i];
            double dx = bx - ax, dy = by - ay, len2 = dx * dx + dy * dy;
            double t = len2 == 0 ? 0 : Math.Clamp(((px - ax) * dx + (py - ay) * dy) / len2, 0, 1);
            double cx = ax + t * dx - px, cy = ay + t * dy - py;
            best = Math.Min(best, Math.Sqrt(cx * cx + cy * cy));
        }
        return best;
    }
}
