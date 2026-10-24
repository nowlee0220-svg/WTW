using Wtw.Sim.Persistence;
using Wtw.Sim.Time;
using Xunit;
using Xunit.Abstractions;

namespace Wtw.Sim.Tests;

/// <summary>
/// (F1) 느린 테스트: 시험용 장면(건물 · 무덤 · 밭 · 사연 있는 물건 · 짐승이 있는 세계)을 반년 남짓(24번 · 9일마다) 돌리며 여러 때에
/// 저장 → 불러오기 → 닷새 더 = 저장 없이 닷새 더. 다르면 어느 갈래인지 낸다.
/// </summary>
[Trait("Category", "Slow")]
public class SaveSlowTests
{
    private readonly ITestOutputHelper _out;
    public SaveSlowTests(ITestOutputHelper o) { _out = o; }

    [Theory]
    [InlineData("village-50y.wtw")]
    [InlineData("hut-village.wtw")]
    [InlineData("river-farm.wtw")]
    [InlineData("tundra-edge.wtw")]
    public void SceneSaveLoadAtManyPoints(string scene)
    {
        var w = SaveGame.LoadFile(SpeedTests.ScenePath(scene));
        w.Threads = 4;
        long start = w.Now;
        for (int k = 1; k <= 24; k++)
        {
            w.AdvanceTo(start + k * 9 * GameTime.TicksPerDay + k * 1777); // 서로 다른 때(하루 안의 시각도 다르게)
            var loaded = SaveGame.FromBytes(SaveGame.ToBytes(w));
            loaded.Threads = 1;
            long end = w.Now + 5 * GameTime.TicksPerDay;
            w.AdvanceTo(end); loaded.AdvanceTo(end);
            var diff = SaveGame.DiffSections(w, loaded);
            _out.WriteLine($"{scene} {k}: {w.Now} · 사람 {w.People.AliveCount} · 다른 갈래 {diff.Count}");
            Assert.True(diff.Count == 0, $"{scene} {k}번째 저장 뒤 닷새: {string.Join(", ", diff.Take(20))}");
        }
    }
}
