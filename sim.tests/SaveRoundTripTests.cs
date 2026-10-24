using Wtw.Sim.Data;
using Wtw.Sim.People;
using Wtw.Sim.Persistence;
using Wtw.Sim.Time;
using Wtw.Sim.World;
using Xunit;
using Xunit.Abstractions;

namespace Wtw.Sim.Tests;

/// <summary>
/// (F1) 저장 → 불러오기 → 계속 = 저장 없이 계속. E6 의 상태(건물 · 짓다 만 자리 · 무덤 · 불 버릇 · 사연 있는 물건 · 낡은 건물)가 다 생긴 뒤에.
/// 다르면 갈래별 지문(SaveGame.DiffSections)으로 어느 갈래(지방 몇의 건물 · 사람 몇 · 난수 …)가 다른지 낸다 —
/// 새 상태를 더하고 저장을 잊으면(또는 시각에 기대는 기억을 저장하지 않으면) 여기서 걸린다.
/// </summary>
[Trait("Category", "Fast")]
public class SaveRoundTripTests
{
    private readonly ITestOutputHelper _out;
    public SaveRoundTripTests(ITestOutputHelper o) { _out = o; }

    private static GameWorld E6World(long days)
    {
        var w = BuildTests.BuildFamilies(2, 3, 3);
        // 무덤이 생기게 — 어른 하나가 죽는다(가족이 묻는다)
        var ps = w.People;
        var dead = ps.All.First(q => !q.Dead && q.Stage(w.Now) >= LifeStage.Adult && q.Partner >= 0);
        ps.Kill(dead, DeathCause.OldAge);
        w.AdvanceTo(days * GameTime.TicksPerDay + 777);
        return w;
    }

    private string Report(GameWorld w)
    {
        var ps = w.People;
        var lives = ps.Provinces.Values.ToList();
        int done = lives.Sum(l => l.Buildings.Values.Count(b => b.Done)), sites = lives.Sum(l => l.Buildings.Values.Count(b => !b.Done));
        int graves = lives.Sum(l => l.Buildings.Values.Count(b => BuildingData.Default.Buildings[b.Kind].Grave > 0 && b.Done));
        int worn = lives.Sum(l => l.Buildings.Values.Count(b => b.Done && BuildingData.Default.Buildings[b.Kind].DecayDays > 0 && ps.Condition(b, w.Now) < PeopleSim.WornCond));
        int habit = ps.All.Count(q => !q.Dead && q.FireHabit > 0);
        string s = $"건물 {done} · 짓는 중 {sites} · 무덤 {graves} · 낡음 {worn} · 불 버릇 {habit}명 · 사연 있는 물건 {ps.Named.Count} · 사람 {ps.AliveCount}";
        _out.WriteLine(s);
        return s;
    }

    /// <summary>
    /// 저장할 때: 다 지은 건물 하나가 막 "낡음"에 닿은 뒤 · 하루 경계 전(따뜻함 · 건물 모습이 아직 낡기 전의 것). F1 이 고친 자리 —
    /// 따뜻함을 불러온 시각의 낡음으로 다시 셈하거나 건물 모습을 다시 만들면 갈라졌다.
    /// </summary>
    [Fact]
    public void E6WorldSaveLoadContinues()
    {
        var a = E6World(100);
        var ps = a.People;
        long due = long.MaxValue;
        foreach (var (_, l) in ps.Provinces)
            foreach (var (d, id) in l.BuildDue)
                if (l.Buildings.TryGetValue(id, out var bd) && bd.Done && BuildingData.Default.Buildings[bd.Kind].WarmIn > 0 && ps.Condition(bd, a.Now) >= PeopleSim.WornCond && d < due) due = d;
        Assert.True(due < a.Now + 200 * GameTime.TicksPerDay, "낡음에 닿을 따뜻한 건물이 없다");
        a.AdvanceTo(due - 5 * GameTime.TicksPerDay);
        foreach (var q in ps.All) if (!q.Dead && q.Stage(a.Now) >= LifeStage.Adult) q.FireHabit = 60; // 불 버릇(불 지킴이 · 저녁 불) — 따뜻한 땅이라 저절로는 잘 안 생긴다
        // 낡음에 닿는 첫 시각까지(한 시간씩) — 그 뒤 하루 경계 전에 저장한다
        while (!ps.Provinces.Values.Any(l => l.Buildings.Values.Any(b => b.Done && BuildingData.Default.Buildings[b.Kind].DecayDays > 0 && ps.Condition(b, a.Now) < PeopleSim.WornCond)))
            a.AdvanceTo(a.Now + GameTime.TicksPerHour);
        string r = Report(a);
        Assert.True(ps.Named.Count > 0 && ps.All.Any(q => q.Burial == 1) && ps.All.Any(q => !q.Dead && q.FireHabit > 0), "E6 의 상태가 다 나오지 않았다: " + r);
        // 불러온 직후 같은 바이트
        var b = SaveGame.FromBytes(SaveGame.ToBytes(a));
        Assert.Empty(SaveGame.DiffSections(a, b));
        // 맞추는 시각 전(미룬 일이 불러온 것과 새것으로 섞여 있을 때)에도 같은 바이트
        a.AdvanceTo(a.Now + 5); b.AdvanceTo(b.Now + 5);
        Assert.Empty(SaveGame.DiffSections(a, b));
        // 하루씩 견줘 처음 갈라진 날 · 갈래를 낸다
        for (int day = 1; day <= 5; day++)
        {
            long t = a.Now + GameTime.TicksPerDay;
            a.AdvanceTo(t); b.AdvanceTo(t);
            var diff = SaveGame.DiffSections(a, b);
            Assert.True(diff.Count == 0, $"불러온 뒤 {day}일째 갈라짐: {string.Join(", ", diff.Take(20))}");
        }
    }

    /// <summary>
    /// 여러 지방에 미룬 일(알림 · 배움 …)이 남은 채 맞추는 시각 사이에 저장 → 불러오기 → 조금 더. 미룬 일을 모으는 차례대로 쓰면
    /// (불러온 것 먼저, 그다음 지방마다) 이어 간 세계와 저장 바이트가 달랐다 — F1 에서 적용할 차례로 쓴다.
    /// </summary>
    [Fact]
    public void SaveBetweenSyncsWithPendingOps()
    {
        var a = GameWorld.Create(20261016, new WorldConfig { DotCount = 0, PeopleCap = 300 });
        var homes = PeopleSamples.Ranked(a.Planet, (pl, c) => pl.Biome[c] is Planet.Biome.Temperate or Planet.Biome.Grassland or Planet.Biome.Savanna && pl.RiverSize[c] > 0 ? pl.Temp10[c] : int.MinValue).Take(8).ToList();
        foreach (int cell in homes)
            foreach (var (x, y) in PeopleSamples.SpawnSpots(a.People.Life(cell), 10))
                a.Apply(new Intervention(0, Intervention.Kinds.SpawnPerson, new long[] { cell, x, y }));
        a.AdvanceTo(2 * GameTime.TicksPerDay + 7);
        int guard = 0;
        while (a.People.PendingOpsForSave().Select(o => o.Cell).Distinct().Count() < 2 && guard++ < 5000) a.AdvanceTo(a.Now + 37);
        var ops = a.People.PendingOpsForSave();
        _out.WriteLine($"미룬 일 {ops.Count} (지방 {ops.Select(o => o.Cell).Distinct().Count()}곳) · {a.Now}");
        Assert.True(ops.Select(o => o.Cell).Distinct().Count() >= 2, "미룬 일이 남은 지방이 둘이 안 된다");
        var b = SaveGame.FromBytes(SaveGame.ToBytes(a));
        long from = a.Now;
        foreach (long dt in Enumerable.Range(1, 300).Select(k => (long)k).Append(GameTime.TicksPerDay))
        {
            long t = from + dt;
            a.AdvanceTo(t); b.AdvanceTo(t);
            var diff = SaveGame.DiffSections(a, b);
            Assert.True(diff.Count == 0, $"불러온 뒤 {dt}틱: {string.Join(", ", diff.Take(20))}");
        }
    }
}
