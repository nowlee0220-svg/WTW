using System.Text.Json;
using Wtw.Sim.Planet;
using Wtw.Sim.Provinces;

namespace Wtw.Sim.Data;

public sealed record ActionDef(string Id, string Name);

public sealed record ItemDef(string Id, string Name);

/// <summary>다루는 법 하나 → 나오는 아이템들.</summary>
public sealed record ObjectUse(ActionDef Action, IReadOnlyList<ItemDef> Yields);

/// <summary>지방 지도의 오브젝트 종류 하나(data/objects.json).</summary>
public sealed class ObjectDef
{
    public int Index { get; init; }
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Kind { get; init; } = "";
    public string Art { get; init; } = "";
    /// <summary>묶인 행성 자원(ResourceKinds 번호), 없으면 −1.</summary>
    public int Resource { get; init; } = -1;
    public double Density { get; init; }
    /// <summary>묶인 행성 자원이 없음이어도 이만큼(0~1)은 난다.</summary>
    public double ResourceFloor { get; init; }
    /// <summary>[Biome 값] 가중치.</summary>
    public double[] BiomeWeight { get; init; } = Array.Empty<double>();
    /// <summary>[Ground 값] 가중치.</summary>
    public double[] GroundWeight { get; init; } = Array.Empty<double>();
    public int ClusterCount { get; init; }
    public int ClusterSize { get; init; }
    public IReadOnlyList<ObjectUse> Uses { get; init; } = Array.Empty<ObjectUse>();

    public bool IsCluster => ClusterCount > 0;
    public bool IsTree => Kind == "tree";

    /// <summary>"따기 → 사과 · 베기 → 나무" 같은 한 줄.</summary>
    public string UsesText => string.Join(" · ", Uses.Select(u => $"{u.Action.Name} → {string.Join(", ", u.Yields.Select(y => y.Name))}"));
}

/// <summary>오브젝트 · 아이템 · 다루는 법 묶음. 기본값은 시뮬 안에 넣어 둔 data/*.json (게임·측정 도구·테스트가 같은 것을 본다).</summary>
public sealed class ObjectCatalog
{
    public const string ObjectsFile = "objects.json";
    public const string ItemsFile = "items.json";
    public static readonly string[] Kinds = { "tree", "bush", "plant", "rock", "ground" };

    public IReadOnlyList<ObjectDef> Objects { get; init; } = Array.Empty<ObjectDef>();
    public IReadOnlyList<ItemDef> Items { get; init; } = Array.Empty<ItemDef>();
    public IReadOnlyList<ActionDef> Actions { get; init; } = Array.Empty<ActionDef>();

    public ObjectDef? Find(string id) => Objects.FirstOrDefault(o => o.Id == id);
    public int IndexOf(string id) => Find(id)?.Index ?? -1;

    private static readonly Lazy<ObjectCatalog> DefaultCatalog = new(() => Load(EmbeddedFiles(ObjectsFile, ItemsFile)), LazyThreadSafetyMode.ExecutionAndPublication);
    /// <summary>시뮬에 넣어 둔 data/objects.json · items.json.</summary>
    public static ObjectCatalog Default => DefaultCatalog.Value;

    internal static Dictionary<string, string> EmbeddedFiles(params string[] names)
    {
        var asm = typeof(ObjectCatalog).Assembly;
        var files = new Dictionary<string, string>();
        foreach (var name in names)
        {
            using var s = asm.GetManifestResourceStream("data/" + name) ?? throw new InvalidOperationException($"시뮬 안에 data/{name} 가 없습니다(빌드 설정).");
            using var r = new StreamReader(s);
            files[name] = r.ReadToEnd();
        }
        return files;
    }

    public static ObjectCatalog LoadDirectory(string dir)
    {
        var files = new Dictionary<string, string>();
        foreach (var name in new[] { ObjectsFile, ItemsFile })
        {
            var path = Path.Combine(dir, name);
            files[name] = File.Exists(path) ? File.ReadAllText(path) : "";
        }
        return Load(files);
    }

    /// <summary>파일 이름 → 내용. 틀린 곳은 모두 모아 DataValidationException 으로.</summary>
    public static ObjectCatalog Load(IReadOnlyDictionary<string, string> files)
    {
        var problems = new List<string>();
        string Get(string n) => files.TryGetValue(n, out var s) ? s : "";

        var items = new List<ItemDef>();
        var itemById = new Dictionary<string, ItemDef>();
        using (var doc = Parse(ItemsFile, Get(ItemsFile), problems))
            if (doc is not null && GetArray(doc.RootElement, ItemsFile, "items", problems) is { } arr)
            {
                int i = 0;
                foreach (var e in arr.EnumerateArray())
                {
                    string where = Where(ItemsFile, i++, e);
                    string? id = ReqString(e, "id", where, problems), name = ReqString(e, "name", where, problems);
                    if (id is null || name is null) continue;
                    if (itemById.ContainsKey(id)) { problems.Add($"{where}: 아이디 '{id}' 가 겹칩니다."); continue; }
                    var it = new ItemDef(id, name);
                    itemById[id] = it; items.Add(it);
                }
            }

        var actions = new List<ActionDef>();
        var actionById = new Dictionary<string, ActionDef>();
        var objects = new List<ObjectDef>();
        using (var doc = Parse(ObjectsFile, Get(ObjectsFile), problems))
        {
            if (doc is not null && GetArray(doc.RootElement, ObjectsFile, "actions", problems) is { } acts)
            {
                int i = 0;
                foreach (var e in acts.EnumerateArray())
                {
                    string where = Where(ObjectsFile + " 의 actions", i++, e);
                    string? id = ReqString(e, "id", where, problems), name = ReqString(e, "name", where, problems);
                    if (id is null || name is null) continue;
                    if (actionById.ContainsKey(id)) { problems.Add($"{where}: 다루는 법 아이디 '{id}' 가 겹칩니다."); continue; }
                    var a = new ActionDef(id, name);
                    actionById[id] = a; actions.Add(a);
                }
            }
            if (doc is not null && GetArray(doc.RootElement, ObjectsFile, "objects", problems) is { } objs)
            {
                var seen = new Dictionary<string, int>();
                int i = 0;
                foreach (var e in objs.EnumerateArray())
                {
                    int index = i++;
                    string where = Where(ObjectsFile, index, e);
                    var def = ReadObject(e, where, objects.Count, itemById, actionById, problems);
                    if (def is null) continue;
                    if (seen.TryGetValue(def.Id, out int first)) { problems.Add($"{where}: 아이디 '{def.Id}' 가 {first + 1}번째 항목과 겹칩니다."); continue; }
                    seen[def.Id] = index;
                    objects.Add(def);
                }
            }
        }
        if (problems.Count > 0) throw new DataValidationException(problems);
        return new ObjectCatalog { Objects = objects, Items = items, Actions = actions };
    }

    private static ObjectDef? ReadObject(JsonElement e, string where, int index, Dictionary<string, ItemDef> items, Dictionary<string, ActionDef> actions, List<string> problems)
    {
        int before = problems.Count;
        string? id = ReqString(e, "id", where, problems);
        string? name = ReqString(e, "name", where, problems);
        string? kind = ReqString(e, "kind", where, problems);
        string? art = ReqString(e, "art", where, problems);
        if (kind is not null && !Kinds.Contains(kind)) problems.Add($"{where}: kind '{kind}' 은(는) 모르는 분류입니다({string.Join(" · ", Kinds)} 중 하나).");

        int resource = -1;
        if (e.TryGetProperty("resource", out var re))
        {
            string? r = re.ValueKind == JsonValueKind.String ? re.GetString() : null;
            resource = r is null ? -1 : System.Array.IndexOf(ResourceKinds.Ids, r);
            if (resource < 0) problems.Add($"{where}: resource '{r}' 은(는) resources.json 에 없는 행성 자원입니다.");
        }

        double floor = 0;
        if (e.TryGetProperty("resource_floor", out var rf))
        {
            if (rf.ValueKind != JsonValueKind.Number || rf.GetDouble() < 0 || rf.GetDouble() > 1) problems.Add($"{where}: resource_floor 는 0~1 이어야 합니다.");
            else floor = rf.GetDouble();
            if (resource < 0) problems.Add($"{where}: resource_floor 는 행성 자원(resource)에 묶인 오브젝트에만 씁니다.");
        }

        double density = 0;
        if (!e.TryGetProperty("density", out var de) || de.ValueKind != JsonValueKind.Number) problems.Add($"{where}: 꼭 있어야 하는 칸 'density'(날 확률)가 없습니다.");
        else
        {
            density = de.GetDouble();
            if (density < 0 || density > 1) problems.Add($"{where}: density {density} 는 0~1 이어야 합니다.");
        }

        var biomes = Weights(e, "biomes", BiomeIds.Ids, "생물군계", where, problems);
        var grounds = Weights(e, "grounds", GroundInfo.Ids, "땅 종류", where, problems);

        int cCount = 0, cSize = 0;
        if (e.TryGetProperty("cluster", out var ce))
        {
            if (ce.ValueKind != JsonValueKind.Object || !ce.TryGetProperty("count", out var cc) || !cc.TryGetInt32(out cCount) ||
                !ce.TryGetProperty("size", out var cs) || !cs.TryGetInt32(out cSize) || cCount < 1 || cSize < 1)
                problems.Add($"{where}: cluster 는 {{ \"count\": 1 이상, \"size\": 1 이상 }} 이어야 합니다.");
            if (resource < 0) problems.Add($"{where}: cluster(뭉쳐 나기)는 행성 자원(resource)에 묶여 있어야 합니다.");
        }

        var uses = new List<ObjectUse>();
        if (!e.TryGetProperty("uses", out var ue) || ue.ValueKind != JsonValueKind.Array || ue.GetArrayLength() == 0)
            problems.Add($"{where}: 꼭 있어야 하는 칸 'uses'(다루는 법 → 자원)가 없거나 비었습니다.");
        else
        {
            foreach (var u in ue.EnumerateArray())
            {
                string? a = u.TryGetProperty("action", out var ae) && ae.ValueKind == JsonValueKind.String ? ae.GetString() : null;
                if (a is null || !actions.TryGetValue(a, out var act)) { problems.Add($"{where}: 다루는 법 '{a}' 이(가) actions 목록에 없습니다."); continue; }
                var ys = new List<ItemDef>();
                if (!u.TryGetProperty("yields", out var ye) || ye.ValueKind != JsonValueKind.Array || ye.GetArrayLength() == 0)
                { problems.Add($"{where}: '{a}' 의 yields(나오는 자원)가 없습니다."); continue; }
                foreach (var y in ye.EnumerateArray())
                {
                    string? yid = y.ValueKind == JsonValueKind.String ? y.GetString() : null;
                    if (yid is null || !items.TryGetValue(yid, out var item)) problems.Add($"{where}: '{a}' 로 나오는 자원 '{yid}' 이(가) {ItemsFile} 에 없습니다.");
                    else ys.Add(item);
                }
                uses.Add(new ObjectUse(act, ys));
            }
        }
        if (problems.Count > before || id is null || name is null || kind is null || art is null) return null;
        return new ObjectDef
        {
            Index = index, Id = id, Name = name, Kind = kind, Art = art, Resource = resource, Density = density, ResourceFloor = floor,
            BiomeWeight = biomes, GroundWeight = grounds, ClusterCount = cCount, ClusterSize = cSize, Uses = uses,
        };
    }

    /// <summary>{ "아이디": 가중치, "*": 나머지 } → 배열. 모르는 아이디는 문제로.</summary>
    private static double[] Weights(JsonElement e, string key, string[] ids, string what, string where, List<string> problems)
    {
        var w = new double[ids.Length];
        if (!e.TryGetProperty(key, out var o) || o.ValueKind != JsonValueKind.Object)
        {
            problems.Add($"{where}: 꼭 있어야 하는 칸 '{key}'({what} → 가중치)가 없습니다.");
            return w;
        }
        if (o.TryGetProperty("*", out var star) && star.ValueKind == JsonValueKind.Number)
            for (int k = 1; k < ids.Length; k++) w[k] = star.GetDouble();
        foreach (var p in o.EnumerateObject())
        {
            if (p.Name == "*") continue;
            int k = System.Array.IndexOf(ids, p.Name);
            if (k <= 0) { problems.Add($"{where}: {key} 의 '{p.Name}' 은(는) 모르는 {what}입니다."); continue; }
            if (p.Value.ValueKind != JsonValueKind.Number || p.Value.GetDouble() < 0) { problems.Add($"{where}: {key} 의 '{p.Name}' 값은 0 이상의 수여야 합니다."); continue; }
            w[k] = p.Value.GetDouble();
        }
        return w;
    }

    private static JsonDocument? Parse(string file, string text, List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(text)) { problems.Add($"{file} 파일이 없거나 비어 있습니다."); return null; }
        try { return JsonDocument.Parse(text, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }); }
        catch (JsonException ex) { problems.Add($"{file}: JSON 문법이 틀렸습니다({(ex.LineNumber ?? 0) + 1}번째 줄 근처). {ex.Message}"); return null; }
    }

    private static JsonElement? GetArray(JsonElement root, string file, string key, List<string> problems)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            problems.Add($"{file}: 맨 바깥에 \"{key}\": [ ... ] 목록이 있어야 합니다.");
            return null;
        }
        return arr;
    }

    private static string Where(string file, int index, JsonElement e)
    {
        string? id = e.ValueKind == JsonValueKind.Object && e.TryGetProperty("id", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        return id is null ? $"{file} 의 {index + 1}번째 항목" : $"{file} 의 {index + 1}번째 항목({id})";
    }

    private static string? ReqString(JsonElement item, string key, string where, List<string> problems)
    {
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(v.GetString()))
        {
            problems.Add($"{where}: 꼭 있어야 하는 칸 '{key}' 이(가) 없거나 비었습니다.");
            return null;
        }
        return v.GetString();
    }
}
