using Wtw.Sim.Data;
using Xunit;

namespace Wtw.Sim.Tests;

[Trait("Category", "Fast")]
public class DataTests
{
    private const string Props = """
    { "properties": [ { "id": "burns", "name": "탄다" }, { "id": "hardness", "name": "단단함" } ] }
    """;

    private static ContentSet Load(string props, string mats) =>
        ContentLoader.Load(new Dictionary<string, string> { [ContentLoader.PropertiesFile] = props, [ContentLoader.MaterialsFile] = mats });

    private static IReadOnlyList<string> Problems(string props, string mats) =>
        Assert.Throws<DataValidationException>(() => Load(props, mats)).Problems;

    [Fact]
    public void RepositoryDataIsValid()
    {
        var dir = FindDataDir();
        var set = ContentLoader.LoadDirectory(dir);
        Assert.True(set.Materials.Count >= 3);
        Assert.Contains(set.Materials, m => m.Id == "flint" && m.Name == "부싯돌" && m.Props["sharpens"] == 5);
    }

    [Fact]
    public void GoodDataLoads()
    {
        var set = Load(Props, """{ "materials": [ { "id": "wood", "name": "나무", "props": { "burns": 4 }, "becomes": [ { "by": "태우기", "into": "ash" } ] }, { "id": "ash", "name": "재", "props": {} } ] }""");
        Assert.Equal(2, set.Materials.Count);
        Assert.Equal(("태우기", "ash"), set.Materials[0].Becomes[0]);
    }

    [Fact]
    public void DuplicateId()
    {
        var p = Problems(Props, """{ "materials": [ { "id": "a", "name": "가", "props": {} }, { "id": "b", "name": "나", "props": {} }, { "id": "a", "name": "다", "props": {} } ] }""");
        Assert.Contains(p, s => s.Contains("materials.json 의 3번째 항목(a)") && s.Contains("1번째 항목과 겹칩니다"));
    }

    [Fact]
    public void MissingRequiredField()
    {
        var p = Problems(Props, """{ "materials": [ { "id": "a", "props": {} }, { "name": "이름만", "props": {} } ] }""");
        Assert.Contains(p, s => s.Contains("1번째 항목(a)") && s.Contains("'name'"));
        Assert.Contains(p, s => s.Contains("2번째 항목") && s.Contains("'id'"));
    }

    [Fact]
    public void UnknownReferences()
    {
        var p = Problems(Props, """{ "materials": [ { "id": "a", "name": "가", "props": { "shiny": 2 }, "becomes": [ { "by": "굽기", "into": "nothing" } ] } ] }""");
        Assert.Contains(p, s => s.Contains("'shiny'") && s.Contains("없는 속성"));
        Assert.Contains(p, s => s.Contains("'nothing'") && s.Contains("없습니다"));
    }

    [Fact]
    public void ValueOutOfRangeAndBadJson()
    {
        var p = Problems(Props, """{ "materials": [ { "id": "a", "name": "가", "props": { "burns": 9 } } ] }""");
        Assert.Contains(p, s => s.Contains("9") && s.Contains("0~5"));
        var q = Problems(Props, "{ \"materials\": [ { \"id\": \"a\", ");
        Assert.Contains(q, s => s.Contains("materials.json") && s.Contains("문법"));
        var r = Problems("", """{ "materials": [] }""");
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
