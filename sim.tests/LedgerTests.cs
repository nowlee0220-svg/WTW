using System.Text.Json;
using System.Text.Json.Nodes;
using Wtw.Sim.Data;
using Wtw.Sim.People;
using Wtw.Sim.Provinces;
using Wtw.Sim.Time;
using Wtw.Sim.World;
using Xunit;

namespace Wtw.Sim.Tests;

/// <summary>(T1) 먹을 것 장부: 데이터에서 바로 셈한 값과 맞고, 데이터만 바뀌어도 맞게 바뀐다.</summary>
[Trait("Category", "Fast")]
public class LedgerTests
{
    private static (GameWorld W, ProvinceLife Life) Home()
    {
        var w = GameWorld.Create(20261016, new WorldConfig { DotCount = 0 });
        return (w, w.People.Life(PeopleSamples.Home(w.Planet)));
    }

    [Fact]
    public void NeedIsOneDayOfEating()
    {
        // 깨어 16시간 · 자는 8시간: 한 끼(≈33)를 세 번 남짓
        Assert.Equal(PeopleSim.FoodAwake * 16 + PeopleSim.FoodAsleep * 8, PeopleSim.DailyNeed);
        Assert.InRange(PeopleSim.DailyNeed, 3 * 25_000, Person.Full * 3 / 2);
    }

    [Fact]
    public void FruitMatchesRawDataAndOldSupply()
    {
        var (w, life) = Home();
        var led = w.People.Ledger(life);
        // people.json 을 직접 읽어 오브젝트마다 (배부름 ÷ 다시 열리는 날)을 타일 수만큼 더한다
        var foods = JsonNode.Parse(ObjectCatalog.EmbeddedFiles(PeopleData.File)[PeopleData.File], documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!["foods"]!.AsArray();
        var per = new Dictionary<string, double>();
        foreach (var f in foods) per[(string)f!["object"]!] = (double)f["nutrition"]! / (double)f["regrow_days"]!;
        double expect = 0;
        var cat = ObjectCatalog.Default;
        for (int i = 0; i < HexFrame.TileCount; i++)
        {
            if (!life.Walk[i]) continue;
            int o = life.Map.ObjectAt(i);
            if (o >= 0 && per.TryGetValue(cat.Objects[o].Id, out double v)) expect += v;
        }
        Assert.True(expect > 100, $"이 땅에는 딸 것이 많다 ({expect:0})");
        Assert.Equal(expect, led.Fruit, 0);
        Assert.Equal(life.DailyFoodSupply() / 1000.0, led.Fruit, 0); // E5c 부터 쓰던 셈과도 같다
        Assert.True(led.Game > 0 && led.FieldPerTile > 0 && led.FieldTiles > 0);
    }

    [Fact]
    public void LedgerFollowsData()
    {
        var (w, life) = Home();
        var text = ObjectCatalog.EmbeddedFiles(PeopleData.File)[PeopleData.File];
        var root = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!;
        foreach (var key in new[] { "foods", "carry" })
            foreach (var f in root[key]!.AsArray()) f!["regrow_days"] = (double)f["regrow_days"]! * 2;
        var slow = PeopleData.Load(root.ToJsonString(), ObjectCatalog.Default);
        var (f0, w0) = PeopleSim.PlantFood(life, PeopleData.Default, w.People.CarriedNutrition);
        var (f1, w1) = PeopleSim.PlantFood(life, slow, w.People.CarriedNutrition);
        Assert.Equal(f0 / 2, f1, 3);
        Assert.Equal(w0 / 2, w1, 3);
        // 주워 드는 것: 날로 먹을 때의 배부름(먹을 수 있음 × 5) — 낟알 하나는 5
        Assert.Equal(5_000, w.People.CarriedNutrition(CraftData.Default.ItemIndex("grain")));
    }

    [Fact]
    public void GameIsLogisticYield()
    {
        var (w, life) = Home();
        var led = w.People.Ledger(life);
        var fa = w.People.Fauna;
        int meat = CraftData.Default.ItemIndex("raw_meat");
        double expect = 0;
        for (int s = 0; s < fa.Animals.Count; s++)
        {
            var a = fa.Animals[s];
            if (a.Predator) continue;
            int m = a.Yields.Where(y => y.Item == meat).Sum(y => y.Count);
            expect += a.Growth1000 / 1000.0 * life.AnimalCap[s] / 4 * m * 20 / GameTime.DaysPerYear; // 날고기 하나 = 배부름 20
        }
        Assert.Equal(expect, led.Game, 3);
    }
}
