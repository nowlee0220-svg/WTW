using System.Text.Json;

namespace Wtw.Sim.Data;

public sealed record PropertyDef(int Index, string Id, string Name, string Group, int Max);

/// <summary>재료 하나(items.json): 아이디 · 이름 · 속성 세기.</summary>
public sealed record MaterialDef(string Id, string Name, IReadOnlyDictionary<string, int> Props);

public sealed class ContentSet
{
    public IReadOnlyList<PropertyDef> Properties { get; init; } = Array.Empty<PropertyDef>();
    public IReadOnlyList<MaterialDef> Materials { get; init; } = Array.Empty<MaterialDef>();
}

public sealed class DataValidationException : Exception
{
    public IReadOnlyList<string> Problems { get; }
    public DataValidationException(IReadOnlyList<string> problems)
        : base("데이터에 문제가 있습니다:\n- " + string.Join("\n- ", problems)) { Problems = problems; }
}

/// <summary>
/// data/*.json 읽기와 검사. 틀리면 "어느 파일 몇 번째 항목이 왜" 틀렸는지 쉬운 문장으로 모아서 알려 준다.
/// 여기서 보는 파일: properties.json(재료 속성 목록) · items.json(아이템의 속성 세기 props). 다루는 규칙은 CraftData.
/// </summary>
public static class ContentLoader
{
    public const string PropertiesFile = "properties.json";
    public const string ItemsFile = "items.json";

    public static ContentSet LoadDirectory(string dir)
    {
        var files = new Dictionary<string, string>();
        foreach (var name in new[] { PropertiesFile, ItemsFile })
        {
            var path = Path.Combine(dir, name);
            files[name] = File.Exists(path) ? File.ReadAllText(path) : "";
        }
        return Load(files);
    }

    /// <summary>파일 이름 → 내용. 내용이 비어 있으면 "파일이 없다" 로 본다.</summary>
    public static ContentSet Load(IReadOnlyDictionary<string, string> files)
    {
        var problems = new List<string>();
        var props = ReadProperties(Get(files, PropertiesFile), problems);
        var known = new HashSet<string>(props.Select(p => p.Id));
        var mats = ReadItems(Get(files, ItemsFile), known, props, problems);
        if (problems.Count > 0) throw new DataValidationException(problems);
        return new ContentSet { Properties = props, Materials = mats };
    }

    private static string Get(IReadOnlyDictionary<string, string> files, string name) => files.TryGetValue(name, out var s) ? s : "";

    private static JsonElement? Parse(string file, string text, string rootKey, List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(text)) { problems.Add($"{file} 파일이 없거나 비어 있습니다."); return null; }
        try
        {
            using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty(rootKey, out var arr) || arr.ValueKind != JsonValueKind.Array)
            {
                problems.Add($"{file}: 맨 바깥에 \"{rootKey}\": [ ... ] 목록이 있어야 합니다.");
                return null;
            }
            return arr.Clone();
        }
        catch (JsonException e)
        {
            problems.Add($"{file}: JSON 문법이 틀렸습니다({(e.LineNumber ?? 0) + 1}번째 줄 근처). {e.Message}");
            return null;
        }
    }

    private static string Where(string file, int index, string? id) => id is null ? $"{file} 의 {index + 1}번째 항목" : $"{file} 의 {index + 1}번째 항목({id})";

    private static string? ReqString(JsonElement item, string key, string where, List<string> problems)
    {
        if (!item.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(v.GetString()))
        {
            problems.Add($"{where}: 꼭 있어야 하는 칸 '{key}' 이(가) 없거나 비었습니다.");
            return null;
        }
        return v.GetString();
    }

    private static List<PropertyDef> ReadProperties(string text, List<string> problems)
    {
        var list = new List<PropertyDef>();
        if (Parse(PropertiesFile, text, "properties", problems) is not { } arr) return list;
        var seen = new Dictionary<string, int>();
        int i = 0;
        foreach (var item in arr.EnumerateArray())
        {
            string? id = item.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String ? idEl.GetString() : null;
            string where = Where(PropertiesFile, i, id);
            id = ReqString(item, "id", where, problems);
            string? name = ReqString(item, "name", where, problems);
            string group = item.TryGetProperty("group", out var g) && g.ValueKind == JsonValueKind.String ? g.GetString()! : "";
            int max = item.TryGetProperty("max", out var m) && m.ValueKind == JsonValueKind.Number ? m.GetInt32() : 5;
            if (id is not null)
            {
                if (seen.TryGetValue(id, out int first)) problems.Add($"{where}: 아이디 '{id}' 가 {first + 1}번째 항목과 겹칩니다.");
                else seen[id] = i;
            }
            if (id is not null && name is not null) list.Add(new PropertyDef(list.Count, id, name, group, max));
            i++;
        }
        return list;
    }

    private static List<MaterialDef> ReadItems(string text, HashSet<string> knownProps, List<PropertyDef> propDefs, List<string> problems)
    {
        var list = new List<MaterialDef>();
        if (Parse(ItemsFile, text, "items", problems) is not { } arr) return list;
        var maxById = propDefs.GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First().Max);
        var seen = new Dictionary<string, int>();
        int i = 0;
        foreach (var item in arr.EnumerateArray())
        {
            string? rawId = item.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String ? idEl.GetString() : null;
            string where = Where(ItemsFile, i, rawId);
            string? id = ReqString(item, "id", where, problems);
            string? name = ReqString(item, "name", where, problems);
            var props = new SortedDictionary<string, int>(StringComparer.Ordinal);
            if (item.TryGetProperty("props", out var pe))
            {
                if (pe.ValueKind != JsonValueKind.Object) problems.Add($"{where}: 'props'(속성 세기 목록)는 {{ \"속성\": 수 }} 이어야 합니다.");
                else
                    foreach (var p in pe.EnumerateObject())
                    {
                        if (!knownProps.Contains(p.Name)) { problems.Add($"{where}: 속성 '{p.Name}' 은(는) {PropertiesFile} 에 없는 속성입니다."); continue; }
                        if (p.Value.ValueKind != JsonValueKind.Number || !p.Value.TryGetInt32(out int v)) { problems.Add($"{where}: 속성 '{p.Name}' 의 값은 정수여야 합니다."); continue; }
                        int max = maxById[p.Name];
                        if (v < 0 || v > max) { problems.Add($"{where}: 속성 '{p.Name}' 의 값 {v} 이(가) 0~{max} 범위를 벗어납니다."); continue; }
                        props[p.Name] = v;
                    }
            }
            if (id is not null)
            {
                if (seen.TryGetValue(id, out int first)) problems.Add($"{where}: 아이디 '{id}' 가 {first + 1}번째 항목과 겹칩니다.");
                else seen[id] = i;
            }
            if (id is not null && name is not null) list.Add(new MaterialDef(id, name, props));
            i++;
        }
        return list;
    }
}
