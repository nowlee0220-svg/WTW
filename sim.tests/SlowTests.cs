using Wtw.Sim.Persistence;
using Wtw.Sim.Time;
using Wtw.Sim.World;
using Xunit;

namespace Wtw.Sim.Tests;

/// <summary>느린 테스트: 단계 끝에 한 번 (tools/test.sh --slow).</summary>
[Trait("Category", "Slow")]
public class SlowTests
{
    [Fact]
    public void LongRunSaveLoadAtManyPointsStaysIdentical()
    {
        var cfg = new WorldConfig { DotCount = 500 };
        var straight = GameWorld.Create(20261016, cfg);
        var hopping = GameWorld.Create(20261016, cfg);
        for (int day = 1; day <= 120; day++)
        {
            long t = day * GameTime.TicksPerDay + day * 37;
            straight.AdvanceTo(t);
            hopping.AdvanceTo(t);
            if (day % 10 == 0) hopping = SaveGame.FromBytes(SaveGame.ToBytes(hopping));
            if (day % 30 == 0)
            {
                var iv = new Intervention(t, Intervention.Kinds.Gather, new long[] { day * 1000, 75_000 });
                straight.Apply(iv);
                hopping.Apply(iv);
            }
        }
        Assert.Equal(SaveGame.ToBytes(straight), SaveGame.ToBytes(hopping));
        var replay = GameWorld.Replay(straight.ToStartRecord(), straight.Now);
        Assert.Equal(SaveGame.ToBytes(straight), SaveGame.ToBytes(replay));
    }
}
