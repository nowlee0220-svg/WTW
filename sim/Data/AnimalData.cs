using System.Text.Json;
using Wtw.Sim.Planet;

namespace Wtw.Sim.Data;

/// <summary>(E5b) 짐승 한 종(data/animals.json).</summary>
public sealed class AnimalDef
{
    public int Index;
    public string Id = "", Name = "", Art = "";
    public int Size, Walk, Run, GroupMin, GroupMax, Fear, Fierce, Capacity, HuntDays, PreySize;
    /// <summary>한 해에 늘어나는 빠르기(×1000).</summary>
    public int Growth1000;
    public bool Predator, Flies, Tame;
    /// <summary>[Biome 값] 무게(×1000).</summary>
    public int[] Biome1000 = new int[16];
    /// <summary>잡아서 손질하면 나오는 것(아이템 번호, 개수) — 아이템 번호 순.</summary>
    public (int Item, int Count)[] Yields = Array.Empty<(int, int)>();
    public int MeatYield;
}

/// <summary>
/// (E5b) data/animals.json — 짐승 종류 · 사는 땅 · 크기 · 겁 · 사나움 · 나오는 것, 물고기. 숫자는 사람이 읽고 고칠 수 있게 여기에 모았다.
/// </summary>
public sealed class AnimalData
{
    public const string File = "animals.json";
    public IReadOnlyList<AnimalDef> Animals { get; private init; } = Array.Empty<AnimalDef>();
    public int FishItem { get; private init; } = -1;
    public int FishPerLevel { get; private init; }
    public int FishGrowth1000 { get; private init; }

    public int Index(string id) { foreach (var a in Animals) if (a.Id == id) return a.Index; return -1; }

    private static readonly Lazy<AnimalData> DefaultData = new(() =>
        Load(ObjectCatalog.EmbeddedFiles(File)[File], ObjectCatalog.Default), LazyThreadSafetyMode.ExecutionAndPublication);
    public static AnimalData Default => DefaultData.Value;

    public static AnimalData Load(string text, ObjectCatalog cat)
    {
        var problems = new List<string>();
        int II(string id) { for (int i = 0; i < cat.Items.Count; i++) if (cat.Items[i].Id == id) return i; return -1; }
        using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        var list = new List<AnimalDef>();
        int Int(JsonElement e, string k, int d = 0) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : d;
        bool Bool(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.True;
        if (!root.TryGetProperty("animals", out var arr)) problems.Add($"{File}: animals 목록이 없습니다.");
        else
            foreach (var e in arr.EnumerateArray())
            {
                var a = new AnimalDef { Index = list.Count, Id = e.GetProperty("id").GetString() ?? "", Name = e.GetProperty("name").GetString() ?? "" };
                string where = $"{File} 의 {a.Id}";
                if (list.Any(x => x.Id == a.Id)) problems.Add($"{where}: 아이디가 겹칩니다.");
                a.Art = e.TryGetProperty("art", out var art) ? art.GetString() ?? a.Id : a.Id;
                a.Size = Int(e, "size", 1); a.Walk = Int(e, "walk", 20); a.Run = Int(e, "run", 8); a.Fear = Int(e, "fear", 8); a.Fierce = Int(e, "fierce");
                a.Capacity = Int(e, "capacity"); a.HuntDays = Int(e, "hunt_days", 3); a.PreySize = Int(e, "prey_size", 2);
                a.Growth1000 = e.TryGetProperty("growth", out var g) ? (int)Math.Round(g.GetDouble() * 1000) : 300;
                a.Predator = Bool(e, "predator"); a.Flies = Bool(e, "flies"); a.Tame = Bool(e, "tame");
                if (a.Size is < 1 or > 3) problems.Add($"{where}: size 는 1~3 이어야 합니다.");
                if (a.Fierce is < 0 or > 5) problems.Add($"{where}: fierce 는 0~5 여야 합니다.");
                if (e.TryGetProperty("group", out var gr) && gr.GetArrayLength() == 2) { a.GroupMin = gr[0].GetInt32(); a.GroupMax = gr[1].GetInt32(); }
                else problems.Add($"{where}: group 은 [최소, 최대] 여야 합니다.");
                if (a.GroupMin < 1 || a.GroupMax < a.GroupMin) problems.Add($"{where}: group 이 잘못됐습니다.");
                if (e.TryGetProperty("biomes", out var bs))
                    foreach (var b in bs.EnumerateObject())
                    {
                        if (!Enum.TryParse<Biome>(b.Name, out var bio) || bio == Biome.None) { problems.Add($"{where}: 모르는 생물군계 '{b.Name}'."); continue; }
                        a.Biome1000[(int)bio] = (int)Math.Round(b.Value.GetDouble() * 1000);
                    }
                var ys = new List<(int, int)>();
                if (e.TryGetProperty("yields", out var yy))
                    foreach (var y in yy.EnumerateObject())
                    {
                        int it = II(y.Name);
                        if (it < 0) { problems.Add($"{where}: yields 의 '{y.Name}' 이(가) items.json 에 없습니다."); continue; }
                        ys.Add((it, y.Value.GetInt32()));
                    }
                ys.Sort((p, q) => p.Item1.CompareTo(q.Item1));
                a.Yields = ys.ToArray();
                int meat = II("raw_meat");
                foreach (var (it, n) in ys) if (it == meat) a.MeatYield = n;
                list.Add(a);
            }
        int fishItem = -1, perLevel = 0, fg = 0;
        if (root.TryGetProperty("fish", out var f))
        {
            fishItem = II(f.GetProperty("item").GetString() ?? "");
            if (fishItem < 0) problems.Add($"{File}: fish 의 item 이 items.json 에 없습니다.");
            perLevel = Int(f, "per_level", 30);
            fg = f.TryGetProperty("growth", out var fgv) ? (int)Math.Round(fgv.GetDouble() * 1000) : 1000;
        }
        if (problems.Count > 0) throw new DataValidationException(problems);
        return new AnimalData { Animals = list, FishItem = fishItem, FishPerLevel = perLevel, FishGrowth1000 = fg };
    }
}
