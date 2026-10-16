using System.Text.Json;
using Wtw.Sim.Planet;
using Wtw.Sim.Provinces;

namespace Wtw.Sim.Data;

/// <summary>바위 종류 하나(data/rocks.json). 바위는 타일이다 — 캐면 Yields.</summary>
public sealed class RockDef
{
    public int Index { get; init; }
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public (byte R, byte G, byte B) Color { get; init; }
    public IReadOnlyList<ItemDef> Yields { get; init; } = Array.Empty<ItemDef>();
    /// <summary>[Terrain 값] 나기 쉬운 정도(물은 0).</summary>
    public double[] TerrainWeight { get; init; } = Array.Empty<double>();
    /// <summary>[Biome 값] 곱하는 값.</summary>
    public double[] BiomeMult { get; init; } = Array.Empty<double>();
}

/// <summary>광맥 하나: 바위 덩어리 안에 박혀 있다.</summary>
public sealed class OreDef
{
    public int Index { get; init; }
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public int Resource { get; init; }
    public (byte R, byte G, byte B) Color { get; init; }
    public IReadOnlyList<ItemDef> Yields { get; init; } = Array.Empty<ItemDef>();
    public int Count { get; init; }
    public int Size { get; init; }
}

public sealed class RockCatalog
{
    public const string RocksFile = "rocks.json";
    public static readonly string[] TerrainIds = { "plain", "hill", "plateau", "mountain", "high_mountain" };
    private static readonly Terrain[] TerrainOf = { Terrain.Plain, Terrain.Hill, Terrain.Plateau, Terrain.Mountain, Terrain.HighMountain };

    public IReadOnlyList<RockDef> Rocks { get; init; } = Array.Empty<RockDef>();
    public IReadOnlyList<OreDef> Ores { get; init; } = Array.Empty<OreDef>();

    private static readonly Lazy<RockCatalog> DefaultCatalog = new(() => Load(ObjectCatalog.EmbeddedFiles(RocksFile, ObjectCatalog.ItemsFile)), LazyThreadSafetyMode.ExecutionAndPublication);
    public static RockCatalog Default => DefaultCatalog.Value;

    public static RockCatalog LoadDirectory(string dir)
    {
        var files = new Dictionary<string, string>();
        foreach (var name in new[] { RocksFile, ObjectCatalog.ItemsFile })
        {
            var path = Path.Combine(dir, name);
            files[name] = File.Exists(path) ? File.ReadAllText(path) : "";
        }
        return Load(files);
    }

    public static RockCatalog Load(IReadOnlyDictionary<string, string> files)
    {
        var problems = new List<string>();
        string Get(string n) => files.TryGetValue(n, out var s) ? s : "";
        // 아이템 목록(이름 찾기용) — 검사는 ObjectCatalog 가 한다
        var items = new Dictionary<string, ItemDef>();
        try
        {
            using var idoc = JsonDocument.Parse(Get(ObjectCatalog.ItemsFile).Length > 0 ? Get(ObjectCatalog.ItemsFile) : "{\"items\":[]}", new JsonDocumentOptions { AllowTrailingCommas = true });
            if (idoc.RootElement.TryGetProperty("items", out var arr))
                foreach (var e in arr.EnumerateArray())
                    if (e.TryGetProperty("id", out var id) && e.TryGetProperty("name", out var nm) && id.GetString() is string sid)
                        items[sid] = new ItemDef(sid, nm.GetString() ?? sid);
        }
        catch (JsonException ex) { problems.Add($"{ObjectCatalog.ItemsFile}: JSON 문법이 틀렸습니다. {ex.Message}"); }

        var rocks = new List<RockDef>();
        var ores = new List<OreDef>();
        string text = Get(RocksFile);
        if (string.IsNullOrWhiteSpace(text)) problems.Add($"{RocksFile} 파일이 없거나 비어 있습니다.");
        else
        {
            try
            {
                using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                var root = doc.RootElement;
                var seen = new HashSet<string>();
                if (!root.TryGetProperty("rocks", out var ra) || ra.ValueKind != JsonValueKind.Array) problems.Add($"{RocksFile}: 맨 바깥에 \"rocks\": [ ... ] 목록이 있어야 합니다.");
                else
                {
                    int i = 0;
                    foreach (var e in ra.EnumerateArray())
                    {
                        string where = Where("rocks", i++, e);
                        string? id = Str(e, "id", where, problems), name = Str(e, "name", where, problems);
                        var color = Color(e, where, problems);
                        var yields = Yields(e, where, items, problems);
                        var tw = new double[8];
                        if (!e.TryGetProperty("terrain", out var te) || te.ValueKind != JsonValueKind.Object) problems.Add($"{where}: 꼭 있어야 하는 칸 'terrain'(지형 → 나기 쉬운 정도)이 없습니다.");
                        else foreach (var pr in te.EnumerateObject())
                            {
                                int k = Array.IndexOf(TerrainIds, pr.Name);
                                if (k < 0) problems.Add($"{where}: terrain 의 '{pr.Name}' 은(는) 모르는 지형입니다({string.Join(" · ", TerrainIds)}).");
                                else if (pr.Value.ValueKind != JsonValueKind.Number || pr.Value.GetDouble() < 0) problems.Add($"{where}: terrain 의 '{pr.Name}' 값은 0 이상의 수여야 합니다.");
                                else tw[(int)TerrainOf[k]] = pr.Value.GetDouble();
                            }
                        var bm = new double[BiomeIds.Count];
                        Array.Fill(bm, 1.0);
                        if (e.TryGetProperty("biomes", out var be) && be.ValueKind == JsonValueKind.Object)
                            foreach (var pr in be.EnumerateObject())
                            {
                                int k = Array.IndexOf(BiomeIds.Ids, pr.Name);
                                if (k <= 0) problems.Add($"{where}: biomes 의 '{pr.Name}' 은(는) 모르는 생물군계입니다.");
                                else bm[k] = pr.Value.GetDouble();
                            }
                        if (id is null || name is null) continue;
                        if (!seen.Add(id)) { problems.Add($"{where}: 아이디 '{id}' 가 겹칩니다."); continue; }
                        rocks.Add(new RockDef { Index = rocks.Count, Id = id, Name = name, Color = color, Yields = yields, TerrainWeight = tw, BiomeMult = bm });
                    }
                }
                if (!root.TryGetProperty("ores", out var oa) || oa.ValueKind != JsonValueKind.Array) problems.Add($"{RocksFile}: 맨 바깥에 \"ores\": [ ... ] 목록이 있어야 합니다.");
                else
                {
                    int i = 0;
                    foreach (var e in oa.EnumerateArray())
                    {
                        string where = Where("ores", i++, e);
                        string? id = Str(e, "id", where, problems), name = Str(e, "name", where, problems);
                        var color = Color(e, where, problems);
                        var yields = Yields(e, where, items, problems);
                        string? res = e.TryGetProperty("resource", out var re) ? re.GetString() : null;
                        int r = res is null ? -1 : Array.IndexOf(ResourceKinds.Ids, res);
                        if (r < 0) problems.Add($"{where}: resource '{res}' 은(는) resources.json 에 없는 행성 자원입니다.");
                        int count = e.TryGetProperty("count", out var ce) && ce.TryGetInt32(out int cv) ? cv : 0;
                        int size = e.TryGetProperty("size", out var se) && se.TryGetInt32(out int sv) ? sv : 0;
                        if (count < 1 || size < 1) problems.Add($"{where}: count · size 는 1 이상이어야 합니다.");
                        if (id is null || name is null || r < 0) continue;
                        if (!seen.Add(id)) { problems.Add($"{where}: 아이디 '{id}' 가 겹칩니다."); continue; }
                        ores.Add(new OreDef { Index = ores.Count, Id = id, Name = name, Resource = r, Color = color, Yields = yields, Count = count, Size = size });
                    }
                }
            }
            catch (JsonException ex) { problems.Add($"{RocksFile}: JSON 문법이 틀렸습니다({(ex.LineNumber ?? 0) + 1}번째 줄 근처). {ex.Message}"); }
        }
        if (rocks.Count == 0 && problems.Count == 0) problems.Add($"{RocksFile}: 바위 종류가 하나도 없습니다.");
        if (problems.Count > 0) throw new DataValidationException(problems);
        return new RockCatalog { Rocks = rocks, Ores = ores };
    }

    private static string Where(string list, int index, JsonElement e)
    {
        string? id = e.ValueKind == JsonValueKind.Object && e.TryGetProperty("id", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        return id is null ? $"{RocksFile} 의 {list} {index + 1}번째 항목" : $"{RocksFile} 의 {list} {index + 1}번째 항목({id})";
    }

    private static string? Str(JsonElement e, string key, string where, List<string> problems)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(v.GetString()))
        { problems.Add($"{where}: 꼭 있어야 하는 칸 '{key}' 이(가) 없거나 비었습니다."); return null; }
        return v.GetString();
    }

    private static (byte, byte, byte) Color(JsonElement e, string where, List<string> problems)
    {
        if (e.TryGetProperty("color", out var c) && c.ValueKind == JsonValueKind.Array && c.GetArrayLength() == 3 && c.EnumerateArray().All(x => x.TryGetInt32(out int v) && v is >= 0 and <= 255))
        {
            var v = c.EnumerateArray().Select(x => (byte)x.GetInt32()).ToArray();
            return (v[0], v[1], v[2]);
        }
        problems.Add($"{where}: color 는 [빨강, 초록, 파랑] (0~255) 이어야 합니다.");
        return (128, 128, 128);
    }

    private static List<ItemDef> Yields(JsonElement e, string where, Dictionary<string, ItemDef> items, List<string> problems)
    {
        var list = new List<ItemDef>();
        if (!e.TryGetProperty("yields", out var y) || y.ValueKind != JsonValueKind.Array || y.GetArrayLength() == 0)
        { problems.Add($"{where}: 꼭 있어야 하는 칸 'yields'(캐면 나오는 것)가 없습니다."); return list; }
        foreach (var x in y.EnumerateArray())
        {
            string? id = x.GetString();
            if (id is null || !items.TryGetValue(id, out var it)) problems.Add($"{where}: 나오는 자원 '{id}' 이(가) {ObjectCatalog.ItemsFile} 에 없습니다.");
            else list.Add(it);
        }
        return list;
    }
}
