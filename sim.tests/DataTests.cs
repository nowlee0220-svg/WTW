using Wtw.Sim.Data;
using Xunit;

namespace Wtw.Sim.Tests;

[Trait("Category", "Fast")]
public class DataTests
{
    private const string Props = """
    { "properties": [ { "id": "burns", "name": "탄다" }, { "id": "hardness", "name": "단단함" } ] }
    """;

    private static ContentSet Load(string props, string items) =>
        ContentLoader.Load(new Dictionary<string, string> { [ContentLoader.PropertiesFile] = props, [ContentLoader.ItemsFile] = items });

    private static IReadOnlyList<string> Problems(string props, string items) =>
        Assert.Throws<DataValidationException>(() => Load(props, items)).Problems;

    [Fact]
    public void RepositoryDataIsValid()
    {
        var dir = FindDataDir();
        var set = ContentLoader.LoadDirectory(dir);
        Assert.True(set.Materials.Count >= 60);
        Assert.Contains(set.Materials, m => m.Id == "flint" && m.Name == "부싯돌" && m.Props["sharpens"] == 5);
        Assert.True(set.Properties.Count >= 22);
        // 다루는 규칙도 읽힌다(아이템 · 속성 · 행동 참조가 모두 맞음)
        var craft = CraftData.Default;
        Assert.True(craft.Rules.Count >= 25);
        Assert.Contains(craft.Rules, r => r.Id == "copper" && r.Heat == 3);
    }

    [Fact]
    public void GoodDataLoads()
    {
        var set = Load(Props, """{ "items": [ { "id": "wood", "name": "나무", "props": { "burns": 4 } }, { "id": "ash", "name": "재" } ] }""");
        Assert.Equal(2, set.Materials.Count);
        Assert.Equal(4, set.Materials[0].Props["burns"]);
        Assert.Empty(set.Materials[1].Props);
    }

    [Fact]
    public void DuplicateId()
    {
        var p = Problems(Props, """{ "items": [ { "id": "a", "name": "가" }, { "id": "b", "name": "나" }, { "id": "a", "name": "다" } ] }""");
        Assert.Contains(p, s => s.Contains("items.json 의 3번째 항목(a)") && s.Contains("1번째 항목과 겹칩니다"));
    }

    [Fact]
    public void MissingRequiredField()
    {
        var p = Problems(Props, """{ "items": [ { "id": "a", "props": {} }, { "name": "이름만", "props": {} } ] }""");
        Assert.Contains(p, s => s.Contains("1번째 항목(a)") && s.Contains("'name'"));
        Assert.Contains(p, s => s.Contains("2번째 항목") && s.Contains("'id'"));
    }

    [Fact]
    public void UnknownReferences()
    {
        var p = Problems(Props, """{ "items": [ { "id": "a", "name": "가", "props": { "shiny": 2 } } ] }""");
        Assert.Contains(p, s => s.Contains("'shiny'") && s.Contains("없는 속성"));
    }

    [Fact]
    public void ValueOutOfRangeAndBadJson()
    {
        var p = Problems(Props, """{ "items": [ { "id": "a", "name": "가", "props": { "burns": 9 } } ] }""");
        Assert.Contains(p, s => s.Contains("9") && s.Contains("0~5"));
        var q = Problems(Props, "{ \"items\": [ { \"id\": \"a\", ");
        Assert.Contains(q, s => s.Contains("items.json") && s.Contains("문법"));
        var r = Problems("", """{ "items": [] }""");
        Assert.Contains(r, s => s.Contains("properties.json") && s.Contains("없거나"));
    }

    internal static string FindDataDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Wtw.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "data");
    }
}
