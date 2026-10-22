using System.Text.Json;

namespace Wtw.Sim.Data;

/// <summary>(E6) 건물에 들어가는 재료 한 가지: 하는 일(쉬운 말) · 속성 조건(최소 · 최대) · 개수.</summary>
public sealed class BuildPart
{
    public string Role = "";
    public readonly Conds Min = new(), Max = new();
    public int Count;
}

/// <summary>(E6) 건물 한 종류(data/buildings.json).</summary>
public sealed class BuildingDef
{
    public int Index;
    public string Id = "", Name = "";
    /// <summary>짓는 법 규칙의 결과 아이템(−1 = 규칙 없이 — 무덤).</summary>
    public int Item = -1;
    public int W = 1, H = 1, WorkHours, WarmIn, WarmNear, Hold, Sleeps, DecayDays, Grave;
    public bool Rain;
    /// <summary>채워 주는 바람 아이디("" 없음) · 작업대의 일("" 아님).</summary>
    public string Serves = "", Bench = "";
    public BuildPart[] Parts = Array.Empty<BuildPart>();
    public bool IsBench => Bench.Length > 0;
    public bool IsShelter => Serves == "shelter";
    public bool IsStorage => Serves == "storage";
    public int PartTotal { get { int n = 0; foreach (var p in Parts) n += p.Count; return n; } }
}

/// <summary>
/// (E6) data/buildings.json — 건물 종류 · 크기 · 재료(속성으로) · 일하는 시간 · 하는 일(따뜻함 · 모아 둠 · 작업대 · 무덤) · 낡는 빠르기.
/// </summary>
public sealed class BuildingData
{
    public const string File = "buildings.json";
    public IReadOnlyList<BuildingDef> Buildings { get; private init; } = Array.Empty<BuildingDef>();

    public int Index(string id) { foreach (var b in Buildings) if (b.Id == id) return b.Index; return -1; }
    /// <summary>규칙의 결과 아이템 → 건물(없으면 null).</summary>
    public BuildingDef? ByItem(int item) { foreach (var b in Buildings) if (b.Item >= 0 && b.Item == item) return b; return null; }

    private static readonly Lazy<BuildingData> DefaultData = new(() =>
        Load(ObjectCatalog.EmbeddedFiles(File)[File], CraftData.Default), LazyThreadSafetyMode.ExecutionAndPublication);
    public static BuildingData Default => DefaultData.Value;

    public static BuildingData Load(string text, CraftData craft)
    {
        var problems = new List<string>();
        using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        var list = new List<BuildingDef>();
        int Int(JsonElement e, string k, int d = 0) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : d;
        string Str(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        if (!root.TryGetProperty("buildings", out var arr)) problems.Add($"{File}: buildings 목록이 없습니다.");
        else
            foreach (var e in arr.EnumerateArray())
            {
                var b = new BuildingDef { Index = list.Count, Id = Str(e, "id"), Name = Str(e, "name") };
                string where = $"{File} 의 {b.Id}";
                if (b.Id.Length == 0 || b.Name.Length == 0) problems.Add($"{where}: id · name 이 있어야 합니다.");
                if (list.Any(x => x.Id == b.Id)) problems.Add($"{where}: 아이디가 겹칩니다.");
                string item = Str(e, "item");
                if (item.Length > 0) { b.Item = craft.ItemIndex(item); if (b.Item < 0) problems.Add($"{where}: item '{item}' 이(가) items.json 에 없습니다."); }
                b.W = Int(e, "w", 1); b.H = Int(e, "h", 1);
                if (b.W is < 1 or > 3 || b.H is < 1 or > 3) problems.Add($"{where}: 크기(w · h)는 1~3 타일이어야 합니다.");
                b.WorkHours = Int(e, "work_hours"); b.WarmIn = Int(e, "warm_in"); b.WarmNear = Int(e, "warm_near"); b.Hold = Int(e, "hold"); b.Sleeps = Int(e, "sleeps");
                b.DecayDays = Int(e, "decay_days"); b.Grave = Int(e, "grave");
                b.Rain = e.TryGetProperty("rain", out var rn) && rn.ValueKind == JsonValueKind.True;
                b.Serves = Str(e, "serves"); b.Bench = Str(e, "bench");
                if (b.Serves.Length > 0 && b.Serves != "warmth" && !craft.Wishes.Any(w => w.Id == b.Serves)) problems.Add($"{where}: serves '{b.Serves}' 은(는) crafting.json 의 바람이 아닙니다.");
                if (b.Bench is not ("" or "stone" or "hide" or "pottery")) problems.Add($"{where}: bench 는 stone · hide · pottery 중 하나여야 합니다.");
                var parts = new List<BuildPart>();
                if (e.TryGetProperty("parts", out var pa))
                    foreach (var pe in pa.EnumerateArray())
                    {
                        var part = new BuildPart { Role = Str(pe, "role"), Count = Int(pe, "count", 1) };
                        void Read(string key, Conds into)
                        {
                            if (!pe.TryGetProperty(key, out var o)) return;
                            foreach (var p in o.EnumerateObject())
                            {
                                int pi = craft.PropIndex(p.Name);
                                if (pi < 0) problems.Add($"{where}: 재료 '{part.Role}' 의 속성 '{p.Name}' 을(를) 모릅니다."); else into[pi] = p.Value.GetInt32();
                            }
                        }
                        Read("props", part.Min); Read("max", part.Max);
                        if (part.Min.Count == 0) problems.Add($"{where}: 재료 '{part.Role}' 에 속성 조건(props)이 없습니다.");
                        if (part.Count is < 1 or > 20) problems.Add($"{where}: 재료 '{part.Role}' 의 count 는 1~20 이어야 합니다.");
                        parts.Add(part);
                    }
                b.Parts = parts.ToArray();
                list.Add(b);
            }
        if (problems.Count > 0) throw new DataValidationException(problems);
        return new BuildingData { Buildings = list };
    }
}
