using Wtw.Sim.Core;
using Wtw.Sim.Data;
using Wtw.Sim.Provinces;
using Wtw.Sim.Time;

namespace Wtw.Sim.People;

/// <summary>
/// (E6) 건물(기획서 10절). "알아냈다"와 "지었다"는 다르다:
///   1) 짓는 법은 E4 처럼 바람(머물 곳 · 모아 둘 곳) × 재료의 성질 × 해 보기로 알아낸다(crafting.json 의 세우기 · 땅 파기 규칙 — 정답표 아님).
///      작업대는 그 일(돌 깨기 · 가죽 일 · 토기)을 자주 하던 사람이 들고 있던 것을 받침으로 놓아 보다가 알아낸다(받침 놓기 규칙).
///   2) 아는 사람이 자리를 고르고(빈 땅 · 모아 둔 자리 · 불 가까이, 덤불은 걷는다) → 재료를 날라 오고(속성이 맞는 것이면 무엇이든, 여럿이 함께) → 쌓는다(기술 "건설").
///   3) 다 지으면 품질(짓는 사람들의 솜씨 · 운)이 정해지고, 품질이 낮으면 빨리 낡고 덜 막는다. 낡으면 고치고, 다 낡으면 무너진다.
///   4) 낡음 · 버려짐은 예약 방식(무너지는 때만 본다) — 사람이 없는 지방은 멈췄다가 깨어나면 흐른 날만큼 낡는다.
///   5) 죽은 사람은 가족 · 가까운 사람이 묻는다(흙무덤) · 돌을 쌓는다(돌무더기) · 그대로 둔다 — 무리마다 처음 한 대로 굳어 가기 쉽다(버릇).
/// 난수는 ("people", 지방) 스트림에서만. 건물은 지방마다(지방 계산 중에는 그 지방의 건물만 만진다).
/// </summary>
public sealed partial class PeopleSim
{
    public BuildingData Builds { get; } = BuildingData.Default;
    /// <summary>규칙 번호 → 그 규칙으로 알아내는 건물(없으면 null) · 건물 → 짓는 법 규칙 · 규칙 → 그 일을 돕는 작업대(건물 번호, −1 = 없음).</summary>
    private BuildingDef?[] _ruleBuild = Array.Empty<BuildingDef?>();
    private CraftRule?[] _buildRule = Array.Empty<CraftRule?>();
    private int[] _ruleBench = Array.Empty<int>();
    private int W_shelter = -1, W_storage = -1, A_raise = -1, A_set = -1;
    private int B_windbreak, B_hut, B_pit, B_rack, B_firePit, B_grave, B_cairn, B_knap, B_hide, B_kiln;
    private int H_sleptHome = -1, H_laidToRest = -1, H_remembered = -1, H_leftBody = -1, H_builtHome = -1, H_fineThing = -1;

    /// <summary>짓다 만 자리는 이만큼 아무도 손대지 않으면 버려진다.</summary>
    public const long SiteAbandonDays = 20;
    /// <summary>상태(0~1000)가 이 아래면 "낡음" — 덜 막고, 아는 사람이 고친다.</summary>
    public const int WornCond = 400;
    /// <summary>지방마다 한꺼번에 짓는 자리(무덤 빼고).</summary>
    public const int MaxSites = 2;

    private void InitBuild()
    {
        W_shelter = -1; W_storage = -1;
        foreach (var w in Craft.Wishes) { if (w.Id == "shelter") W_shelter = w.Index; if (w.Id == "storage") W_storage = w.Index; }
        A_raise = Craft.ActionIndex("raise"); A_set = Craft.ActionIndex("set");
        int B(string id) { int i = Builds.Index(id); if (i < 0) throw new InvalidOperationException($"buildings.json 에 '{id}' 가 없습니다."); return i; }
        B_windbreak = B("windbreak"); B_hut = B("hut"); B_pit = B("storage_pit"); B_rack = B("rack"); B_firePit = B("fire_pit"); B_grave = B("grave"); B_cairn = B("cairn");
        B_knap = B("knap_spot"); B_hide = B("hide_frame"); B_kiln = B("kiln");
        _ruleBuild = new BuildingDef?[Craft.Rules.Count];
        _buildRule = new CraftRule?[Builds.Buildings.Count];
        _ruleBench = new int[Craft.Rules.Count];
        foreach (var d in Builds.Buildings)
            if (d.Item >= 0)
                foreach (var r in Craft.Rules)
                    if (r.Result == d.Item) { _ruleBuild[r.Index] = d; _buildRule[d.Index] ??= r; }
        int crafting = PeopleData.Crafting;
        foreach (var r in Craft.Rules)
        {
            _ruleBench[r.Index] = -1;
            if (_ruleBuild[r.Index] is not null || r.Result < 0) continue;
            // 돌 깨는 자리: 돌 · 뼈를 치고 깨고 갈아 날 · 끝을 내는 일 / 가죽 일 자리: 가죽을 긁고 말리고 꿰매는 일(불 없이) / 가마: 진흙 그릇을 빚고 말리고 굽는 일
            bool stone = (r.Action == A_strike || r.Action == A_break || r.Action == A_grind) && r.Skill == crafting && (Prop(r.Result, Craft.P_sharp) >= 1 || Prop(r.Result, Craft.P_pierces) >= 2);
            bool hide = r.Input.ContainsKey(Craft.P_covers) && r.Heat == 0;
            bool pottery = r.Input.ContainsKey(Craft.P_moldable) || r.Input.ContainsKey(Craft.P_fires_hard);
            _ruleBench[r.Index] = stone ? B_knap : hide ? B_hide : pottery ? B_kiln : -1;
        }
        int H(string id) => Data.ThoughtIndex(id);
        H_sleptHome = H("slept_home"); H_laidToRest = H("laid_to_rest"); H_remembered = H("remembered"); H_leftBody = H("left_body"); H_builtHome = H("built_home"); H_fineThing = H("fine_thing");
    }

    public BuildingDef BDef(Building b) => Builds.Buildings[b.Kind];
    public CraftRule? RuleOfBuilding(int kind) => (uint)kind < (uint)_buildRule.Length ? _buildRule[kind] : null;
    public int BenchOfRule(int rule) => (uint)rule < (uint)_ruleBench.Length ? _ruleBench[rule] : -1;
    private bool IsBuildingItem(int item) => (uint)item < (uint)Craft.Catalog.Items.Count && Craft.Catalog.Items[item].Kind == "building";
    private bool KnowsBuilding(Person p, int kind) => RuleOfBuilding(kind) is { } r && p.Knows(r.Index);

    // ───────── 낡음(예약 방식) ─────────

    /// <summary>품질에 따른 따뜻함 · 지키는 정도의 몫(%): 나쁨 60 · 보통 100 · 좋음 125 · 훌륭함 150.</summary>
    public static int QualityPct(int q) => q switch { <= 0 => 60, 1 => 100, 2 => 125, _ => 150 };
    public static string QualityName(int q) => q switch { <= 0 => "나쁨", 1 => "보통", 2 => "좋음", _ => "훌륭함" };

    /// <summary>다 낡아 무너지기까지 걸리는 틱(새것 1000 에서 0 까지): 보통 품질의 날 × (나쁨 0.5 · 좋음 1.5 · 훌륭함 2.2).</summary>
    private long LifeTicks(BuildingDef d, int q) => d.DecayDays <= 0 ? 0 : d.DecayDays * GameTime.TicksPerDay * (q <= 0 ? 50 : q == 1 ? 100 : q == 2 ? 150 : 220) / 100;

    /// <summary>지금 상태(0~1000). 짓는 중 · 낡지 않는 것은 1000.</summary>
    public int Condition(Building b, long now)
    {
        var d = BDef(b);
        long lt = LifeTicks(d, b.Quality);
        if (!b.Done || lt <= 0) return 1000;
        return (int)Math.Clamp(b.Cond0 - (now - b.Stamp) * 1000 / lt, 0, 1000);
    }

    /// <summary>다음에 볼 때: 짓는 중이면 버려지는 때, 다 지었으면 "낡음"에 닿는 때 · 무너지는 때.</summary>
    private void SetNextDue(ProvinceLife life, Building b, long now)
    {
        if (!b.Done) { life.SetBuildDue(b, now + SiteAbandonDays * GameTime.TicksPerDay); return; }
        var d = BDef(b);
        long lt = LifeTicks(d, b.Quality);
        if (lt <= 0) { life.SetBuildDue(b, 0); return; }
        int c = Condition(b, now);
        int target = c > WornCond ? WornCond : 0;
        long due = b.Stamp + (long)(b.Cond0 - target) * lt / 1000 + 1;
        life.SetBuildDue(b, Math.Max(now + 1, due));
    }

    /// <summary>바뀔 때(Due)가 된 건물을 (시각, 번호) 차례로 — 짓다 만 자리는 버려지고, 다 지은 것은 낡거나 무너진다. 잠들었다 깨어난 지방도 같은 길로.</summary>
    internal void ProcessBuildings(ProvinceLife life, long until)
    {
        for (int guard = 0; guard < 10_000 && life.BuildDue.Count > 0; guard++)
        {
            var (due, id) = life.BuildDue.Min;
            if (due > until) break;
            life.BuildDue.Remove((due, id));
            if (!life.Buildings.TryGetValue(id, out var b)) continue;
            b.Due = 0;
            var d = BDef(b);
            if (!b.Done)
            {
                St.SitesAbandoned++;
                life.RemoveBuilding(b.Id, d.W, d.H);
                continue;
            }
            if (Condition(b, due) <= 0) { Collapse(life, b, due); continue; }
            SetNextDue(life, b, due);
            life.BuildVersion++;
            RecomputeWarm(life);
        }
    }

    private void Collapse(ProvinceLife life, Building b, long at)
    {
        var d = BDef(b);
        St.BuildingsCollapsed++;
        if (b.Kind == B_firePit && life.Fires.TryGetValue(b.Tile, out var f) && f.Kind == 2) { f.Kind = 1; f.Charcoal = false; life.Fires[b.Tile] = f; } // 둘러쌓은 돌이 무너져 맨불 자리만 남았다
        life.RemoveBuilding(b.Id, d.W, d.H);
        RecomputeWarm(life);
        if (FirstAlive(life.Cell) is { } who)
            Notify(WorldEventKind.Collapsed, who, b.Kind, $"{LandLabelOr(life.Cell, "한 땅")}의 {Hangul.I(d.Name)} 낡아 무너졌습니다 (지은 지 {(at - Math.Max(0, b.Finished)) / GameTime.TicksPerYear}년)");
    }

    /// <summary>타일마다 건물이 주는 따뜻함(움집 안 · 바람막이 곁). 건물이 바뀔 때만 다시 셈한다.</summary>
    private void RecomputeWarm(ProvinceLife life)
    {
        short[]? warm = null;
        foreach (var (_, b) in life.Buildings)
        {
            if (!b.Done) continue;
            var d = BDef(b);
            if (d.WarmIn <= 0 && d.WarmNear <= 0) continue;
            warm ??= life.Warm is { } old ? Clear(old) : new short[HexFrame.TileCount];
            int pct = QualityPct(b.Quality) * (Condition(b, Now) < WornCond ? 70 : 100) / 100;
            int bx = b.Tile % S, by = b.Tile / S;
            for (int y = by - 1; y <= by + d.H; y++)
                for (int x = bx - 1; x <= bx + d.W; x++)
                {
                    if ((uint)x >= S || (uint)y >= S) continue;
                    bool inside = x >= bx && x < bx + d.W && y >= by && y < by + d.H;
                    int v = (inside ? d.WarmIn : d.WarmNear) * pct / 100;
                    int t = y * S + x;
                    if (v > warm[t]) warm[t] = (short)v;
                }
        }
        life.Warm = warm;
        static short[] Clear(short[] a) { Array.Clear(a); return a; }
    }

    // ───────── 하루 경계 · 깨어남 ─────────

    private void BuildDaily(ProvinceLife life)
    {
        ProcessBuildings(life, Now);
        // 작업대를 쓰던 사람이 일을 마쳤으면 놓는다
        foreach (var (_, b) in life.Buildings) if (b.User >= 0 && b.UserUntil <= Now) b.User = -1;
    }

    private void WakeBuild(ProvinceLife life, DormantProvince d)
    {
        life.RestoreCleared(d.Cleared);
        foreach (var b in d.Buildings) life.AddBuilding(b, Builds.Buildings[b.Kind].W, Builds.Buildings[b.Kind].H);
        life.NextBuildingId = Math.Max(life.NextBuildingId, d.NextBuildingId);
        if (d.SleptAt >= 0) ProcessBuildings(life, Now); // 잠든 동안 흐른 날만큼 낡는다
        RecomputeWarm(life);
    }

    // ───────── 자리 고르기 ─────────

    /// <summary>무리가 머무는 곳의 가운데: 모아 둔 자리 → 가까운 불(꺼진 자리도) → 지금 자리.</summary>
    private int CampCenter(ProvinceLife life, int here)
    {
        if (life.Stash >= 0 && life.Region[life.Stash] == life.Region[here]) return life.Stash;
        int ft = life.NearestFire(here % S, here / S, Now, 30, litOnly: false);
        return ft >= 0 ? ft : here;
    }

    /// <summary>
    /// 건물 자리(왼쪽 위 타일): center 둘레를 안에서부터. 차지할 타일이 모두 걸을 수 있는 같은 땅 · 물 · 밭 · 불 · 물건 · 다른 건물 · 동굴이 아니고,
    /// 나무 · 돌 · 먹을 것이 나는 오브젝트가 없을 것(덤불 · 풀은 걷는다). 머무는 건물(바람막이 · 움집)은 다른 건물과 한 타일 띄운다. 없으면 −1.
    /// </summary>
    private int FindSite(ProvinceLife life, BuildingDef d, int center, int minR, int maxR)
    {
        int cx = center % S, cy = center / S, reg = life.Region[center];
        for (int r = minR; r <= maxR; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    int x = cx + dx, y = cy + dy;
                    if ((uint)x >= S || (uint)y >= S || x + d.W > S || y + d.H > S) continue;
                    if (SiteOk(life, d, y * S + x, reg)) return y * S + x;
                }
        return -1;
    }

    private bool SiteOk(ProvinceLife life, BuildingDef d, int anchor, int reg)
    {
        int ax = anchor % S, ay = anchor / S;
        for (int y = ay; y < ay + d.H; y++)
            for (int x = ax; x < ax + d.W; x++)
            {
                int t = y * S + x;
                if (!life.Walk[t] || life.Region[t] != reg || HexFrame.BorderDistance(t) < 4 || GroundInfo.IsWater(life.Map.Ground[t]) || life.Shelter[t] == 3) return false;
                if (life.PlotAt[t] is not null || life.Fires.ContainsKey(t) || t == life.Stash || life.HasItems(t) || life.BuildingAt[t] != 0 || !life.Clearable(t)) return false;
                if (life.IsVolcanoFire(t)) return false;
            }
        if (d.IsShelter || d.IsBench)
            for (int y = ay - 1; y <= ay + d.H; y++)
                for (int x = ax - 1; x <= ax + d.W; x++)
                    if ((uint)x < S && (uint)y < S && life.BuildingAt[y * S + x] != 0) return false;
        return true;
    }

    /// <summary>새 자리를 정한다: 덤불을 걷고 건물(짓는 중)을 놓는다.</summary>
    private Building NewSite(Person p, ProvinceLife life, BuildingDef d, int anchor, int person = -1)
    {
        var b = new Building { Id = life.NextBuildingId++, Kind = d.Index, Tile = anchor, Stage = BuildStage.Site, Have = new int[d.Parts.Length], Founder = p.Id, Started = Now, Person = person, Lang = p.LangId };
        if (d.IsShelter && d.Index == B_hut) b.Owner = p.Id;
        for (int y = 0; y < d.H; y++) for (int x = 0; x < d.W; x++) if (life.ClearObject(anchor + y * S + x)) St.ClearedBrush++;
        life.AddBuilding(b, d.W, d.H);
        SetNextDue(life, b, Now);
        St.SitesStarted++;
        return b;
    }

    // ───────── 건물 일 고르기 ─────────

    /// <summary>지방의 건물 모습(건물이 바뀔 때만 다시 셈한다 — 시각 · 사람에 따라 바뀌는 것은 넣지 않는다).</summary>
    private sealed class BuildView
    {
        public int Version = -1;
        public readonly List<Building> Sites = new(), Worn = new();
        public readonly int[] Done = new int[16], Started = new int[16];
        /// <summary>묻히지 않은 시신(번호 순).</summary>
        public readonly List<Person> Bodies = new();
        public int NonGraveSites;
    }

    private BuildView BViewOf(ProvinceLife life)
    {
        var v = life.BuildCache as BuildView ?? (BuildView)(life.BuildCache = new BuildView());
        if (v.Version == life.BuildVersion) return v;
        v.Version = life.BuildVersion;
        v.Sites.Clear(); v.Worn.Clear(); v.Bodies.Clear(); Array.Clear(v.Done); Array.Clear(v.Started); v.NonGraveSites = 0;
        foreach (var (_, b) in life.Buildings)
        {
            if (b.Kind < v.Done.Length) { v.Started[b.Kind]++; if (b.Done) v.Done[b.Kind]++; }
            if (!b.Done) { v.Sites.Add(b); if (BDef(b).Grave == 0) v.NonGraveSites++; }
            else if (BDef(b).DecayDays > 0 && Condition(b, Now) < WornCond) v.Worn.Add(b);
        }
        foreach (var q in life.People) if (q.Dead && q.Burial == 0 && !q.Eaten) v.Bodies.Add(q);
        return v;
    }

    /// <summary>재료 하나가 그 몫(part)에 맞나 — 사연 있는 물건 · 불씨는 쓰지 않는다.</summary>
    private bool PartOk(BuildPart part, int item)
    {
        if (item == I_ember) return false;
        foreach (var (pr, mn) in part.Min) if (Prop(item, pr) < mn) return false;
        foreach (var (pr, mx) in part.Max) if (Prop(item, pr) > mx) return false;
        return true;
    }

    /// <summary>자리에 아직 모자란 재료(첫 번째 몫 번호, 없으면 −1).</summary>
    private static int MissingPart(Building b, BuildingDef d)
    {
        for (int i = 0; i < d.Parts.Length; i++) if (b.Have[i] < d.Parts[i].Count) return i;
        return -1;
    }

    /// <summary>p 가 든 것 가운데 자리의 모자란 재료에 맞는 것이 있나.</summary>
    private bool CarriesFor(Person p, Building b, BuildingDef d)
    {
        for (int i = 0; i < d.Parts.Length; i++)
        {
            if (b.Have[i] >= d.Parts[i].Count) continue;
            foreach (var (it, _) in p.Carry) if (PartOk(d.Parts[i], it) && !IsNamedHeld(p, it)) return true;
        }
        return false;
    }

    /// <summary>
    /// 건물 일 점수(열 살 넘은 사람, 낮에): 가까운 이 묻기 26 · 든 재료 날라다 놓기 22 · 쌓기 20 · 재료 구하러 15 · 고치기 14 · 새 자리 정하기 16.
    /// 가장 높은 일 하나를 고른다(이길 수 없는 일은 셈하지 않는다).
    /// </summary>
    private int BuildScore(Person p, ProvinceLife life, out BuildJob job, out int target, int bar)
    {
        job = BuildJob.None; target = -1;
        if (p.AgeYears(Now) < 10 || life.IsNight(Now)) return int.MinValue;
        // (E6 고침) 몸이 식어 가는 사람은 짓지 않는다 — 몸부터 데운다(툰드라에서 짓기 · 재료 찾기에 나섰다가 얼어 죽었다)
        if (p.Hypothermia > 0) return int.MinValue;
        int skill = p.SkillLevel(PeopleData.Building);
        if (bar >= 30 + skill) return int.MinValue;
        var v = BViewOf(life);
        bool look = Now - p.LastBuildLook >= GameTime.TicksPerDay;
        if (v.Sites.Count == 0 && v.Worn.Count == 0 && v.Bodies.Count == 0 && !look) return int.MinValue;
        int here = p.Tile(Now);
        int best = bar;
        // 1) 묻지 않은 가까운 이(죽은 지 닷새 안 — 그 뒤로는 짐승이 흩는다)
        if (v.Bodies.Count > 0 && p.AgeYears(Now) >= 14 && 26 > best)
            foreach (var q in v.Bodies)
            {
                if (Now - q.DeathTick > 5 * GameTime.TicksPerDay || !CaresFor(p, q)) continue;
                if (GraveSiteOf(life, q.Id) is not null) continue; // 이미 묻는 중(아래 쌓기 · 나르기로 돕는다)
                if (!life.Paths.Reachable(here, q.Tile(Now))) continue;
                best = 26; job = BuildJob.Bury; target = q.Id; break;
            }
        // 2) 짓는 중인 자리
        foreach (var b in v.Sites)
        {
            var d = BDef(b);
            if (life.Region[b.Tile] != life.Region[here] || Cheb(b.Tile, here) > 60) continue;
            bool grave = d.Grave > 0;
            if (grave && !(Get(b.Person) is { } dead && CaresFor(p, dead))) continue;
            bool knows = grave || KnowsBuilding(p, b.Kind);
            int miss = MissingPart(b, d);
            if (miss >= 0)
            {
                if (22 + skill > best && CarriesFor(p, b, d)) { best = 22 + skill; job = BuildJob.Deliver; target = b.Id; }
                else if (15 + skill > best && (knows || Cheb(b.Tile, here) <= 30) && !BuildMissed(p, b.Kind, miss) && CarryWeight(p) + 3 <= CarryCapOf(p)) { best = 15 + skill; job = BuildJob.Fetch; target = b.Id; }
            }
            else if (knows && 20 + skill * 2 > best) { best = 20 + skill * 2; job = BuildJob.Work; target = b.Id; }
        }
        // 3) 낡은 것 고치기(짓는 법을 아는 사람)
        if (14 + skill > best)
            foreach (var b in v.Worn)
                if (KnowsBuilding(p, b.Kind) && life.Region[b.Tile] == life.Region[here] && Cheb(b.Tile, here) <= 40) { best = 14 + skill; job = BuildJob.Repair; target = b.Id; break; }
        // 4) 새로 지을까(하루에 한 번 둘러본다)
        if (look && 16 + skill > best && v.NonGraveSites < MaxSites && p.AgeYears(Now) >= 14)
        {
            p.LastBuildLook = Now;
            int kind = WhatToBuild(p, life, v);
            if (kind >= 0) { best = 16 + skill; job = BuildJob.Fetch; target = -1 - kind; }
        }
        return job == BuildJob.None ? int.MinValue : best;
    }

    /// <summary>가까운 이(가족 · 좋아하던 사람)인가.</summary>
    private bool CaresFor(Person p, Person dead) => IsFamily(p, dead) || p.GetRelation(dead.Id).Opinion >= 30;

    private Building? GraveSiteOf(ProvinceLife life, int person)
    {
        foreach (var (_, b) in life.Buildings) if (b.Person == person) return b;
        return null;
    }

    private bool BuildMissed(Person p, int kind, int part) => p.BuildMissKey == kind * 16 + part + 1 && Now - p.BuildMissTick < 12 * GameTime.TicksPerHour;

    /// <summary>
    /// 무엇을 새로 지을까(아는 법 가운데 — 없으면 −1):
    ///   머물 곳: 우리 집안이 잘 움집이 없다(밤이 춥거나 머물 곳이 아쉬우면) — 움집을 알면 움집, 아니면 바람막이(지방에 둘까지).
    ///   모아 둘 곳: 모아 둔 자리가 있는데 구덩이 · 시렁이 없다.
    ///   작업대: 그 일(돌 · 가죽 · 토기)을 아는데 이 땅에 그 작업대가 없다.
    /// </summary>
    private int WhatToBuild(Person p, ProvinceLife life, BuildView v)
    {
        bool cold = life.Mean10 - life.Range10 / 2 < 120;
        bool wantShelter = cold || (W_shelter >= 0 && p.Wish[W_shelter] >= 20);
        if (wantShelter && KnowsBuilding(p, B_hut) && HomeHut(p, life) is null && v.Started[B_hut] < Math.Max(1, FamiliesIn(life))) return B_hut;
        if (wantShelter && KnowsBuilding(p, B_windbreak) && v.Started[B_windbreak] < 2 && HomeHut(p, life) is null) return B_windbreak;
        if (life.Stash >= 0 && v.Started[B_pit] + v.Started[B_rack] == 0)
        {
            if (KnowsBuilding(p, B_pit)) return B_pit;
            if (KnowsBuilding(p, B_rack)) return B_rack;
        }
        foreach (int k in new[] { B_knap, B_hide, B_kiln })
            if (v.Started[k] == 0 && KnowsBuilding(p, k) && UsesBench(p, k)) return k;
        return -1;
    }

    /// <summary>그 작업대가 돕는 일을 아는가.</summary>
    private bool UsesBench(Person p, int bench) { foreach (var k in p.Known) if (BenchOfRule(k.Rule) == bench) return true; return false; }

    /// <summary>지방의 집안 수(짝이 있는 어른 둘을 하나로, 혼자 사는 어른도 하나).</summary>
    private int FamiliesIn(ProvinceLife life)
    {
        int n = 0;
        foreach (var q in life.People)
        {
            if (q.Dead || q.Stage(Now) < LifeStage.Adult) continue;
            if (q.Partner >= 0 && q.Partner < q.Id && Here(q.Partner, q) is { Dead: false }) continue;
            if (Here(q.Mother, q) is { Dead: false } || Here(q.Father, q) is { Dead: false }) { if (q.Partner < 0) continue; }
            n++;
        }
        return n;
    }

    /// <summary>p 의 집안(자기 · 짝 · 부모)이 주인인 다 지은 움집(같은 땅). 없으면 null.</summary>
    private Building? HomeHut(Person p, ProvinceLife life)
    {
        if (life.Buildings.Count == 0) return null;
        Building? best = null;
        foreach (var (_, b) in life.Buildings)
        {
            if (b.Kind != B_hut || b.Owner < 0) continue;
            if (b.Owner == p.Id || b.Owner == p.Partner || (p.Stage(Now) <= LifeStage.Child || p.Partner < 0) && (b.Owner == p.Mother || b.Owner == p.Father)
                || Get(b.Owner) is { } o && o.Partner == p.Id)
                if (best is null || b.Done && !best.Done) best = b;
        }
        return best;
    }

    private bool TryBuild(Person p, BuildJob job, int target)
    {
        var life = Life(p.Cell);
        int here = p.Tile(Now);
        Building? b;
        if (job == BuildJob.Bury)
        {
            var dead = Here(target, p);
            if (dead is null || !dead.Dead) return false;
            return StartBurial(p, life, dead);
        }
        if (target < 0)
        {
            // 새 자리
            var d = Builds.Buildings[-1 - target];
            int center = d.IsStorage && life.Stash >= 0 ? life.Stash : CampCenter(life, here);
            int anchor = FindSite(life, d, center, d.IsStorage ? 1 : d.IsBench ? 2 : 3, d.IsStorage ? 6 : 16);
            if (anchor < 0) return false;
            b = NewSite(p, life, d, anchor);
            AddBio(p, BioKind.Built, d.Index, -1);
        }
        else if (!life.Buildings.TryGetValue(target, out b)) return false;
        var def = BDef(b);
        switch (job)
        {
            case BuildJob.Fetch:
            {
                int miss = MissingPart(b, def);
                if (miss < 0) return false;
                if (CarriesFor(p, b, def)) return GoBuild(p, life, b, BuildJob.Deliver);
                var part = def.Parts[miss];
                p.PlanWish = -1;
                if (StartMaterialFor(p, it => PartOk(part, it) && !IsNamedHeld(p, it), part.Min.Keys[0], Why.BuildFetch)) { p.WhyArg = b.Id; return true; }
                p.BuildMissKey = b.Kind * 16 + miss + 1; p.BuildMissTick = Now;
                return false;
            }
            default:
                return GoBuild(p, life, b, job);
        }
    }

    private bool GoBuild(Person p, ProvinceLife life, Building b, BuildJob job)
    {
        int here = p.Tile(Now);
        var d = BDef(b);
        int stand = BesideRect(p, life, b.Tile, d.W, d.H, here);
        if (stand < 0) return false;
        var why = job switch { BuildJob.Deliver => Why.BuildDeliver, BuildJob.Work => d.Grave > 0 ? Why.Bury : Why.BuildWork, BuildJob.Repair => Why.BuildRepair, _ => Why.BuildWork };
        if (stand != here && !WalkTo(p, stand)) return false;
        SetWhy(p, Act.Build, why, b.Kind); p.Target = b.Id; p.TargetB = (int)job;
        if (stand == here) { StandStill(p); ArriveBuild(p); }
        return true;
    }

    /// <summary>건물 둘레(또는 그 안)의 설 자리 — here 에서 가장 가까운 것(같은 땅). 없으면 −1.</summary>
    private int BesideRect(Person p, ProvinceLife life, int anchor, int w, int h, int here)
    {
        int ax = anchor % S, ay = anchor / S, reg = life.Region[here], best = -1; long bd = long.MaxValue;
        for (int y = ay - 1; y <= ay + h; y++)
            for (int x = ax - 1; x <= ax + w; x++)
            {
                if ((uint)x >= S || (uint)y >= S) continue;
                int t = y * S + x;
                if (!life.Walk[t] || life.Region[t] != reg) continue;
                long dd = ProvinceLife.D2(t, here % S, here / S) * 8 + (int)(StableHash.Mix((ulong)(p.Id * 131 + t)) % 8);
                if (dd < bd) { bd = dd; best = t; }
            }
        return best;
    }

    /// <summary>자리에 닿았다: 내려놓기 10분 · 쌓기 한 시간 · 고치기 한 시간 · 묻기 한 시간(흙무덤은 땅 파는 것이 없으면 더).</summary>
    private void ArriveBuild(Person p)
    {
        var life = Life(p.Cell);
        if (!life.Buildings.TryGetValue(p.Target, out var b)) { Decide(p); return; }
        var job = (BuildJob)p.TargetB;
        long min = job switch { BuildJob.Deliver => 10, BuildJob.Work or BuildJob.Repair => 60, _ => 30 };
        if (job == BuildJob.Work && BDef(b).Grave == 1 && BestTool(p, P_digs, 1) < 0) min = 90; // 손으로 판다
        DoStill(p, Math.Max(300, min * GameTime.TicksPerHour / 60 * WorkPercent(p) / 100));
    }

    private void FinishBuild(Person p, bool done)
    {
        if (!done) return;
        var life = Life(p.Cell);
        if (!life.Buildings.TryGetValue(p.Target, out var b)) return;
        var d = BDef(b);
        switch ((BuildJob)p.TargetB)
        {
            case BuildJob.Deliver:
            {
                int n = 0;
                for (int i = 0; i < d.Parts.Length; i++)
                    for (int k = p.Carry.Count - 1; k >= 0 && b.Have[i] < d.Parts[i].Count; k--)
                    {
                        var (it, c) = p.Carry[k];
                        if (!PartOk(d.Parts[i], it) || IsNamedHeld(p, it)) continue;
                        int give = Math.Min(c, d.Parts[i].Count - b.Have[i]);
                        p.RemoveCarry(it, give);
                        b.Have[i] += give; n += give;
                    }
                if (n > 0) { St.BuildDelivered += n; Touch(life, b, p); }
                break;
            }
            case BuildJob.Work:
            {
                if (MissingPart(b, d) >= 0) break;
                int skill = p.SkillLevel(PeopleData.Building);
                int add = 60 * (100 + skill * 8) / 100;
                b.Work += add; St.BuildWorkMin += add;
                GainSkill(p, PeopleData.Building, 10);
                Touch(life, b, p);
                if (RuleOfBuilding(b.Kind) is { } rule) { if (p.Knows(rule.Index)) UseKnowledge(p, rule.Index); TeachWatchers(p, rule, -1, -1, -1); }
                if (b.Work >= d.WorkHours * 60) Complete(p, life, b);
                break;
            }
            case BuildJob.Repair:
            {
                if (!b.Done) break;
                b.Cond0 = 1000; b.Stamp = Now; b.Repaired = Now;
                SetNextDue(life, b, Now);
                life.BuildVersion++;
                RecomputeWarm(life);
                St.Repairs++;
                GainSkill(p, PeopleData.Building, 6);
                AddBio(p, BioKind.Repaired, b.Kind, b.Id);
                break;
            }
        }
    }

    /// <summary>자리에 손을 댔다: 함께 지은 사람 · 버려지는 때를 뒤로.</summary>
    private void Touch(ProvinceLife life, Building b, Person p)
    {
        if (!b.Builders.Contains(p.Id) && b.Builders.Count < 8) b.Builders.Add(p.Id);
        if (!b.Done) SetNextDue(life, b, Now);
        life.BuildVersion++;
    }

    /// <summary>
    /// 다 지었다: 품질(함께 지은 사람들의 건설 솜씨 · 손재주 · 운) — 나쁨 · 보통 · 좋음 · 훌륭함. 머무는 곳은 따뜻함, 모아 둘 곳은 모아 둔 자리를 옮긴다.
    /// </summary>
    private void Complete(Person p, ProvinceLife life, Building b)
    {
        var d = BDef(b);
        var rng = Rng(p.Cell);
        int sum = 0, n = 0;
        foreach (int id in b.Builders) if (Here(id, p) is { } q) { sum += q.SkillLevel(PeopleData.Building) * 8 + (Ability(q, PeopleData.Dex) - 5) * 3 + (q.HasTrait(T_handy) ? 8 : 0) - (q.HasTrait(T_clumsy) ? 8 : 0); n++; }
        int score = (n > 0 ? sum / n : 0) + rng.NextInt(50) + Math.Min(10, (b.Builders.Count - 1) * 3);
        b.Quality = d.Grave > 0 ? 1 : score < 20 ? 0 : score < 52 ? 1 : score < 75 ? 2 : 3;
        b.Stage = BuildStage.Done; b.Finished = Now; b.Stamp = Now; b.Cond0 = 1000;
        SetNextDue(life, b, Now);
        life.BuildVersion++;
        St.BuildingsDone++;
        RecomputeWarm(life);
        if (d.Grave > 0) { FinishGrave(p, life, b); return; }
        if (d.IsStorage) MoveStash(life, b.Tile);
        foreach (int id in b.Builders)
            if (Here(id, p) is { } q)
            {
                AddBio(q, BioKind.Built, d.Index, b.Quality);
                AddThought(q, H_builtHome);
                if (d.IsShelter && W_shelter >= 0) q.Wish[W_shelter] = Math.Max(0, q.Wish[W_shelter] - 50);
                if (d.IsStorage && W_storage >= 0) q.Wish[W_storage] = Math.Max(0, q.Wish[W_storage] - 50);
                PassionEvent(q, PeopleData.Building, 15);
            }
        if (InProvince) Defer(new PendingOp { Kind = PendingOp.Kinds.Build, A = 1, B = p.Id, C = d.Index, D = b.Quality });
        else BuiltWorld(p, d.Index, b.Quality);
    }

    /// <summary>(S1) 다 지은 일의 세계 쪽(맞추는 시각에): 그 말의 무리에서 처음 지은 것이면 ★★ 알림.</summary>
    private void BuiltWorld(Person p, int kind, int quality)
    {
        var d = Builds.Buildings[kind];
        long key = (long)p.LangId * 1000 + 900 + kind;
        if (!GroupFirst.ContainsKey(key))
        {
            GroupFirst[key] = Now;
            Notify(WorldEventKind.BuiltFirst, p, kind, $"이 무리에서 처음: {Hangul.I(DisplayName(p))} {LandLabelOr(p.Cell, "한 땅")}에 {Hangul.Eul(d.Name)} 지었습니다(품질 {QualityName(quality)})");
        }
        else Notify(WorldEventKind.Built, p, kind, $"{Hangul.I(DisplayName(p))} {Hangul.Eul(d.Name)} 다 지었습니다(품질 {QualityName(quality)})");
    }

    /// <summary>모아 둔 자리를 구덩이 · 시렁으로 옮긴다(놓인 것을 모두).</summary>
    private void MoveStash(ProvinceLife life, int tile)
    {
        if (life.Stash == tile) return;
        if (life.Stash >= 0 && life.Items.TryGetValue(life.Stash, out var list))
        {
            foreach (var (it, c) in list.ToArray()) life.Drop(tile, it, c);
            life.RemoveItems(life.Stash);
        }
        life.Stash = tile;
        life.FarmVersion++;
    }

    /// <summary>모아 둔 자리를 지키는 건물(구덩이 · 시렁)의 지키는 정도(바구니 3 · 토기 4 와 같은 눈금, 품질 반영). 없으면 0.</summary>
    private int StashBuildingHold(ProvinceLife life)
    {
        if (life.Stash < 0 || life.BuildingOn(life.Stash) is not { Done: true } b) return 0;
        var d = BDef(b);
        if (!d.IsStorage) return 0;
        int h = d.Hold + (b.Quality >= 2 ? 1 : b.Quality <= 0 ? -1 : 0);
        if (Condition(b, Now) < WornCond) h--;
        return Math.Max(0, h);
    }

    /// <summary>불 타일 곁(1타일)에 비를 막는 건물(바람막이 · 움집)이 있나 — 그 불은 비에 꺼지지 않는다.</summary>
    private bool RainCover(ProvinceLife life, int t)
    {
        if (life.Buildings.Count == 0) return false;
        int x = t % S, y = t / S;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if ((uint)nx >= S || (uint)ny >= S) continue;
                if (life.BuildingOn(ny * S + nx) is { Done: true } b && BDef(b).Rain && b.Kind != B_firePit) return true;
            }
        return false;
    }

    /// <summary>돌 화덕을 쌓았다: 그 불 자리가 무리의 불자리(건물 — 누가 · 언제 · 품질, 낡으면 돌이 무너져 맨불 자리로).</summary>
    private void FirePitAt(Person p, ProvinceLife life, int ft)
    {
        if (life.BuildingAt[ft] != 0) return;
        var d = Builds.Buildings[B_firePit];
        var rng = Rng(p.Cell);
        int score = p.SkillLevel(PeopleData.Building) * 8 + (Ability(p, PeopleData.Dex) - 5) * 3 + rng.NextInt(50);
        var b = new Building { Id = life.NextBuildingId++, Kind = B_firePit, Tile = ft, Stage = BuildStage.Done, Have = Array.Empty<int>(), Founder = p.Id, Started = Now, Finished = Now, Stamp = Now, Lang = p.LangId,
            Quality = score < 20 ? 0 : score < 52 ? 1 : score < 75 ? 2 : 3 };
        b.Builders.Add(p.Id);
        life.AddBuilding(b, d.W, d.H);
        SetNextDue(life, b, Now);
        St.BuildingsDone++;
        if (InProvince) Defer(new PendingOp { Kind = PendingOp.Kinds.Build, A = 1, B = p.Id, C = B_firePit, D = b.Quality });
        else BuiltWorld(p, B_firePit, b.Quality);
    }

    // ───────── 잠자리: 움집 · 바람막이 ─────────

    /// <summary>
    /// 잘 곳: 우리 집안의 움집(다 지은 것) 안 → 추운 밤이면 바람막이 곁(불 가까이면 더 좋다). 없으면 −1.
    /// 사람마다 다른 타일(번호로) — 여럿이 한 타일에 겹치지 않게.
    /// </summary>
    private int ShelterSpot(Person p, ProvinceLife life, int here, bool coldNight, out Why why)
    {
        why = Why.None;
        if (life.Buildings.Count == 0) return -1;
        if (HomeHut(p, life) is { Done: true } hut && life.Paths.Reachable(here, hut.Tile) && Cheb(hut.Tile, here) <= 80)
        {
            var d = BDef(hut);
            int k = (int)(StableHash.Mix((ulong)p.Id) % (ulong)(d.W * d.H));
            int t = hut.Tile + k / d.W * S + k % d.W;
            why = Why.Home;
            return t;
        }
        if (!coldNight) return -1;
        Building? wb = null; long bd = long.MaxValue;
        foreach (var (_, b) in life.Buildings)
        {
            if (!b.Done || b.Kind != B_windbreak || Cheb(b.Tile, here) > 40 || !life.Paths.Reachable(here, b.Tile)) continue;
            long dd = ProvinceLife.D2(b.Tile, here % S, here / S);
            if (dd < bd) { bd = dd; wb = b; }
        }
        if (wb is null) return -1;
        // 바람막이 바로 곁(가려진 쪽 — 아래 줄 · 그 안), 사람마다 다른 자리
        var wd = BDef(wb);
        int n = wd.W * wd.H * 2;
        ulong h0 = StableHash.Mix((ulong)p.Id);
        for (int k = 0; k < n; k++)
        {
            int j = (int)((h0 + (ulong)k) % (ulong)n);
            int x = wb.Tile % S + j % wd.W, y = wb.Tile / S + j / wd.W;
            if ((uint)x >= S || (uint)y >= S) continue;
            int t = y * S + x;
            if (life.Walk[t] && life.Region[t] == life.Region[here]) { why = Why.Windbreak; return t; }
        }
        return -1;
    }

    // ───────── 저녁 불 · 버릇 ─────────

    /// <summary>추운 땅에서 불 피우는 법을 아는 사람의 저녁 불 점수(버릇이 있을 때, 그 땅 16시 ~ 잠들 무렵, 저녁마다 한 번).</summary>
    private int EveningFireScore(Person p, ProvinceLife life)
    {
        if (p.FireHabit < 25 || R_campfire is null || p.Stage(Now) < LifeStage.Child || !p.Knows(R_campfire.Index)) return int.MinValue;
        if (life.Mean10 - life.Range10 / 2 >= 120) return int.MinValue;
        long tod = life.LocalTickOfDay(Now);
        if (tod < 16 * GameTime.TicksPerHour || tod >= 21 * GameTime.TicksPerHour || Now - p.LastEveningFire < 10 * GameTime.TicksPerHour) return int.MinValue;
        return 18 + p.FireHabit / 4;
    }

    /// <summary>
    /// 저녁 불: 머무는 곳(25타일 안)의 불이 밤새(다음 날 7시까지) 탈 만큼 땔감이 없으면 넣는다 · 밤에 넣을 땔감 둘을 미리 챙긴다 ·
    /// 불이 없으면 불씨를 가져오거나 피운다. 할 일이 없으면 false(이번 저녁은 챙겼다).
    /// </summary>
    private bool TryEveningFire(Person p, ProvinceLife life)
    {
        int here = p.Tile(Now);
        int ft = life.NearestFire(here % S, here / S, Now, 25);
        // 막 꺼진 불(네 시간 안 — 재 속에 불씨가 남았다)은 땔감을 얹으면 되살아난다
        if (ft < 0 && EmberFire(life, here, 25) is int ef && ef >= 0)
        {
            int fuel0 = BestCarried(p, Craft.P_burns, 3, I_ember);
            if (fuel0 >= 0) { St.EveningFires++; return StartTend(p, ef, fuel0, Why.EveningFire); }
            if (StartMaterialFor(p, Craft.P_burns, 3, Why.EveningFire)) return true;
        }
        long tod = life.LocalTickOfDay(Now);
        long untilMorning = (31 * GameTime.TicksPerHour - tod) % GameTime.TicksPerDay;
        int fuels = CountCarried(p, Craft.P_burns, 3, Craft.P_burns, 3) - p.CarryCount(I_ember);
        if (ft >= 0)
        {
            var f = life.Fires[ft];
            if (f.Remaining(Now) < untilMorning)
            {
                int fuel = BestCarried(p, Craft.P_burns, 3, I_ember);
                // 밤새 탈 만큼(땔감 셋 남짓) 모아서 넣는다
                if (fuel >= 0 && (fuels >= 3 || f.Remaining(Now) < 2 * GameTime.TicksPerHour)) { St.EveningFires++; return StartTend(p, ft, fuel, Why.EveningFire); }
                if (StartMaterialFor(p, Craft.P_burns, 3, Why.EveningFire)) return true;
                if (fuel >= 0) { St.EveningFires++; return StartTend(p, ft, fuel, Why.EveningFire); }
            }
            else if (fuels < 3 && StartMaterialFor(p, Craft.P_burns, 3, Why.EveningFire)) return true; // 밤에 다시 넣을 땔감
            p.LastEveningFire = Now;
            return false;
        }
        p.LastEveningFire = Now;
        // 불이 꺼졌다 · 없다: 불씨를 들고 있으면 TryFireWork 가 피운다 — 아니면 보이는 불에서 가져오거나, 아는 법으로 새로 피운다
        if (p.CarryCount(I_ember) > 0) return false;
        int far = life.NearestFire(here % S, here / S, Now, 60);
        if (far >= 0) { int spot = Beside(p, far); if (WalkTo(p, spot)) { SetWhy(p, Act.TakeFire, Why.EveningFire); p.Target = far; return true; } }
        St.EveningFires++;
        return TryLightKnownFire(p);
    }

    /// <summary>r 타일 안의 막 꺼진 불(꺼진 지 네 시간 안 — 불씨가 남았다). 없으면 −1.</summary>
    internal int EmberFire(ProvinceLife life, int here, int r)
    {
        int best = -1, bd = r + 1;
        foreach (var (t, f) in life.Fires.Items)
        {
            if (f.Lit(Now) || Now - (f.Stamp + f.Fuel) > EmberHours * GameTime.TicksPerHour) continue;
            int d = Cheb(t, here);
            if (d < bd && life.BesideRegion(t, life.Region[here])) { bd = d; best = t; }
        }
        return best;
    }

    /// <summary>꺼진 불의 재 속에 불씨가 남아 있는 시간.</summary>
    public const int EmberHours = 4;

    /// <summary>
    /// 깨어날 때: 불 피우는 법을 아는 사람이 추운 땅에서 — 불 없이 떨며 잤으면 버릇이 크게 늘고(25), 불 곁에서 따뜻하게 잤으면 조금(6).
    /// </summary>
    private void FireHabitOnWake(Person p, ProvinceLife life, bool fireNear)
    {
        if (R_campfire is null || !p.Knows(R_campfire.Index) || life.Mean10 - life.Range10 / 2 >= 120) return;
        // 새벽(가장 추울 때) 몸이 시렸으면 — 옷을 입었어도 — 불이 아쉬웠다
        bool chilly = !fireNear && FeltNow(p) < 110;
        int add = p.SleptCold ? (fireNear ? 10 : 25) : chilly ? 10 : fireNear ? 6 : 0;
        p.FireHabit = Math.Min(100, p.FireHabit + add);
    }

    /// <summary>하루 경계: 버릇은 천천히 잊는다 · 버릇이 굳은 부모 곁의 아이는 따라 배운다.</summary>
    private void FireHabitDaily(Person p)
    {
        if (p.FireHabit > 0) p.FireHabit = p.FireHabit * 49 / 50;
        if (p.Stage(Now) == LifeStage.Child && p.AgeYears(Now) >= 8 && p.FireHabit < 60)
            foreach (int id in new[] { p.Mother, p.Father })
                if (Here(id, p) is { Dead: false } par && par.FireHabit >= 50) { p.FireHabit = Math.Min(100, p.FireHabit + 4); break; }
    }

    /// <summary>저녁에 불을 챙기는 것을 곁(8타일)에서 본 사람은 버릇이 는다(불 피우는 법을 알면).</summary>
    private void FireHabitWatchers(Person p, ProvinceLife life)
    {
        if (R_campfire is null) return;
        int here = p.Tile(Now);
        foreach (var q in life.People)
            if (!q.Dead && q.Id != p.Id && !q.Asleep && q.FireHabit < 60 && q.Knows(R_campfire.Index) && Cheb(q.Tile(Now), here) <= 8) q.FireHabit = Math.Min(100, q.FireHabit + 8);
    }

    // ───────── 무덤 ─────────

    /// <summary>
    /// 무리(말)마다 묻는 버릇: [흙무덤, 돌무더기, 그대로 둠] 횟수. 처음 한 대로 굳어 가기 쉽다(고를 무게 = 1 + 횟수 × 3, 그대로 둠은 처음엔 반).
    /// 갈라져 나온 말은 갈라져 나온 말의 버릇을 이어받는다(아직 없으면).
    /// </summary>
    public readonly SortedDictionary<int, int[]> BurialWays = new();

    public int[] BurialWayOf(int lang)
    {
        for (int guard = 0, cur = lang; guard < 50 && cur >= 0; guard++)
        {
            if (BurialWays.TryGetValue(cur, out var c)) return c;
            cur = Languages.TryGetValue(cur, out var l) ? l.Parent : -1;
        }
        return new int[3];
    }

    /// <summary>묻기 시작: 무리의 버릇과 할 수 있는 것(땅 파는 것 · 둘레의 돌)으로 정한다 — 흙무덤 · 돌무더기면 시신 곁 빈 땅에 자리를, 그대로 두면 그만.</summary>
    private bool StartBurial(Person p, ProvinceLife life, Person dead)
    {
        var rng = Rng(p.Cell);
        var way = BurialWayOf(p.LangId);
        int wEarth = 2 + way[0] * 6 + (BestTool(p, P_digs, 1) >= 0 || KnowsField(p) ? 3 : 0);
        int wCairn = 2 + way[1] * 6;
        int wLeave = 1 + way[2] * 6;
        int r = rng.NextInt(wEarth + wCairn + wLeave);
        int choice = r < wEarth ? 0 : r < wEarth + wCairn ? 1 : 2;
        if (InProvince) Defer(new PendingOp { Kind = PendingOp.Kinds.Build, A = 2, B = p.Id, C = choice }); else BurialWayWorld(p.LangId, choice);
        if (choice == 2)
        {
            dead.Burial = 2;
            life.BuildVersion++;
            St.BodiesLeft++;
            AddThought(p, H_leftBody, dead.Id);
            AddBio(p, BioKind.LeftBody, dead.Id, 0);
            p.LastBuildLook = Now;
            return false;
        }
        var d = Builds.Buildings[choice == 0 ? B_grave : B_cairn];
        int bt = dead.Tile(Now);
        int anchor = SiteOk(life, d, bt, life.Region[bt] >= 0 ? life.Region[bt] : life.Region[p.Tile(Now)]) ? bt : FindSite(life, d, bt, 1, 4);
        if (anchor < 0) return false;
        var b = NewSite(p, life, d, anchor, dead.Id);
        return d.Parts.Length > 0 ? TryBuild(p, BuildJob.Fetch, b.Id) : GoBuild(p, life, b, BuildJob.Work);
    }

    internal void BurialWayWorld(int lang, int choice)
    {
        if (!BurialWays.TryGetValue(lang, out var c)) { c = (int[])BurialWayOf(lang).Clone(); BurialWays[lang] = c; }
        c[choice]++;
    }

    private void FinishGrave(Person p, ProvinceLife life, Building b)
    {
        var dead = Get(b.Person);
        bool earth = b.Kind == B_grave;
        if (earth) St.BuriedEarth++; else St.BuriedCairn++;
        if (dead is null) return;
        dead.Burial = 1; dead.GraveId = b.Id;
        AddBio(dead, BioKind.Buried, p.Id, b.Kind);
        foreach (int id in b.Builders)
            if (Here(id, p) is { } q) { AddThought(q, H_laidToRest, dead.Id); AddBio(q, BioKind.BuriedSomeone, dead.Id, b.Kind); }
        if (InProvince) Defer(new PendingOp { Kind = PendingOp.Kinds.Build, A = 3, B = p.Id, C = dead.Id, D = b.Kind });
        else BuriedWorld(p, dead, b.Kind);
    }

    private void BuriedWorld(Person p, Person dead, int kind)
    {
        long key = (long)p.LangId * 1000 + 950;
        string how = kind == B_grave ? "흙을 파고 묻었습니다" : "돌을 쌓아 덮었습니다";
        if (!GroupFirst.ContainsKey(key))
        {
            GroupFirst[key] = Now;
            Notify(WorldEventKind.GraveFirst, p, dead.Id, $"이 무리에서 처음: {Hangul.I(DisplayName(p))} 죽은 {Hangul.Eul(DisplayName(dead))} {how} — {LandLabelOr(p.Cell, "한 땅")}에 첫 무덤");
        }
        else Notify(WorldEventKind.Buried, p, dead.Id, $"{Hangul.I(DisplayName(p))} {Hangul.Eul(DisplayName(dead))} {how}");
    }

    /// <summary>하루에 한 번(판단할 때): 가까운 이의 무덤 곁(5타일)을 지나면 그 사람을 떠올린다.</summary>
    private void SeeGraves(Person p, ProvinceLife life, int here)
    {
        if (life.Buildings.Count == 0 || Now - p.LastGraveLook < GameTime.TicksPerDay || p.Stage(Now) < LifeStage.Child) return;
        p.LastGraveLook = Now;
        foreach (var (_, b) in life.Buildings)
        {
            if (!b.Done || b.Person < 0 || Cheb(b.Tile, here) > 5) continue;
            if (Get(b.Person) is { } dead && CaresFor(p, dead)) { AddThought(p, H_remembered, dead.Id); return; }
        }
    }

    // ───────── 작업대 ─────────

    /// <summary>tile 곁(1타일)에 그 종류의 다 지은 작업대가 있나(있으면 그것).</summary>
    private Building? BenchAt(ProvinceLife life, int bench, int tile)
    {
        if (bench < 0 || life.Buildings.Count == 0) return null;
        int x = tile % S, y = tile / S;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if ((uint)nx >= S || (uint)ny >= S) continue;
                if (life.BuildingOn(ny * S + nx) is { Done: true } b && b.Kind == bench) return b;
            }
        return null;
    }

    /// <summary>12타일 안의 비어 있는(쓰는 사람이 없는) 그 종류의 작업대. 없으면 null.
    /// (E6 고침) 처음엔 30타일 — 툰드라에서 멀리 있는 작업대까지 오가느라 불 · 무리 곁을 떠나 얼었다.</summary>
    private Building? FreeBench(ProvinceLife life, int bench, int here, Person p)
    {
        if (bench < 0 || life.Buildings.Count == 0) return null;
        Building? best = null; int bd = 13;
        foreach (var (_, b) in life.Buildings)
        {
            if (!b.Done || b.Kind != bench || (b.User >= 0 && b.User != p.Id && b.UserUntil > Now)) continue;
            int d = Cheb(b.Tile, here);
            if (d < bd && life.Paths.Reachable(here, b.Tile)) { bd = d; best = b; }
        }
        return best;
    }

    /// <summary>
    /// 그 일을 아는 법대로 마쳤다: 그 일을 돕는 작업대가 이 땅에 없고 짓는 법도 모르면, 가끔(4%, 호기심 +4) 들고 있던 것을 받침으로 놓아 본다 —
    /// 받침 놓기 규칙으로 판정(속성 — 정답표 아님). 되면 그 작업대 짓는 법을 알고, 모자라면 단서가 남는다.
    /// </summary>
    private void TryBenchIdea(Person p, ProvinceLife life, CraftRule done, Pcg32 rng)
    {
        int bench = BenchOfRule(done.Index);
        if (bench < 0 || A_set < 0 || RuleOfBuilding(bench) is not { } br || p.Knows(br.Index)) return;
        if (BViewOf(life).Started[bench] > 0) return;
        if (rng.NextInt(100) >= 4 + (p.HasTrait(T_curious) ? 4 : 0)) return;
        var d = Builds.Buildings[bench];
        // 받침으로 놓아 볼 것: 그 작업대의 첫 재료에 가장 가까운 것(들고 있는 것)
        int input = -1, bs = int.MinValue;
        foreach (var (it, _) in p.Carry)
        {
            if (it == I_ember || IsNamedHeld(p, it)) continue;
            int s = 0; foreach (var (pr, mn) in br.Input) s += Math.Min(Prop(it, pr), mn) * 10;
            if (s > bs) { bs = s; input = it; }
        }
        if (input < 0) return;
        int with = -1;
        if (br.HasWith) foreach (var (it, _) in p.Carry) if (it != input && WithOk(it, br)) { with = it; break; }
        int heat = life.HeatAt(p.Tile(Now), Now);
        var outcome = Craft.Judge(A_set, PropsOf(input), with >= 0 ? PropsOf(with) : null, null, heat, 0, rng.NextInt(100), (Ability(p, PeopleData.Int) - 5) * 3, br);
        St.Experiments++;
        if (outcome.Success)
        {
            St.BenchLearned++;
            Learn(p, br, -1, input, with, -1);
            p.LastBuildLook = long.MinValue / 2; // 곧 지어 볼까 둘러본다
            return;
        }
        if (outcome.IsPartial) { AddClue(p, new Clue { Rule = br.Index, Key = outcome.Partial!.Key, Tick = Now, Input = input }); St.Partials++; }
        _ = d;
    }

    // ───────── 세계 쪽 · 측정 · 화면 ─────────

    internal void ApplyBuildOp(PendingOp op)
    {
        var p = Get(op.B);
        if (op.A == 2) { BurialWayWorld(p?.LangId ?? -1, op.C); return; }
        if (p is null) return;
        if (op.A == 1) BuiltWorld(p, op.C, op.D);
        else if (op.A == 3 && Get(op.C) is { } dead) BuriedWorld(p, dead, op.D);
    }

    /// <summary>측정 · 화면: 지방의 건물 셈(다 지은 것 종류별 · 짓는 중 · 낡음).</summary>
    public (int[] Done, int Sites, int Worn) BuildCounts(ProvinceLife life)
    {
        var done = new int[Builds.Buildings.Count];
        int sites = 0, worn = 0;
        foreach (var (_, b) in life.Buildings)
        {
            if (!b.Done) { sites++; continue; }
            done[b.Kind]++;
            if (BDef(b).DecayDays > 0 && Condition(b, Now) < WornCond) worn++;
        }
        return (done, sites, worn);
    }

    /// <summary>화면: 지금 그 건물 안 · 곁에서 자는 사람(움집 · 바람막이) · 쓰는 사람(작업대).</summary>
    public List<int> BuildingUsers(ProvinceLife life, Building b)
    {
        var d = BDef(b);
        var list = new List<int>();
        if (d.IsBench) { if (b.User >= 0 && b.UserUntil > Now) list.Add(b.User); return list; }
        int x0 = b.Tile % S - (d.IsShelter && b.Kind == B_windbreak ? 1 : 0), y0 = b.Tile / S - (b.Kind == B_windbreak ? 1 : 0);
        int x1 = b.Tile % S + d.W - 1 + (b.Kind == B_windbreak ? 1 : 0), y1 = b.Tile / S + d.H - 1 + (b.Kind == B_windbreak ? 1 : 0);
        foreach (var q in life.People)
        {
            if (q.Dead || !q.Asleep) continue;
            int t = q.Tile(Now), x = t % S, y = t / S;
            if (x >= x0 && x <= x1 && y >= y0 && y <= y1) list.Add(q.Id);
        }
        return list;
    }

    // ───────── 테스트용 ─────────

    /// <summary>(테스트) p 곁 anchor 에 그 종류의 짓는 자리를 놓는다.</summary>
    internal Building TestSite(Person p, int kind, int anchor) => NewSite(p, Life(p.Cell), Builds.Buildings[kind], anchor);
    /// <summary>(테스트) 재료 · 일을 다 채우고 다 짓는다.</summary>
    internal void TestComplete(Person p, Building b)
    {
        var d = BDef(b);
        for (int i = 0; i < d.Parts.Length; i++) b.Have[i] = d.Parts[i].Count;
        b.Work = d.WorkHours * 60;
        if (!b.Builders.Contains(p.Id)) b.Builders.Add(p.Id);
        Complete(p, Life(p.Cell), b);
    }
    /// <summary>(테스트) 그 자리가 지금 쓸 수 있나 · 하루 경계의 모아 둔 것 줄기.</summary>
    internal bool TestSiteOk(ProvinceLife life, int kind, int anchor) => SiteOk(life, Builds.Buildings[kind], anchor, life.Region[anchor]);
    internal void TestStashDaily(ProvinceLife life) => StashDaily(life, Rng(life.Cell));
    internal int TestFindSite(ProvinceLife life, int kind, int center) => FindSite(life, Builds.Buildings[kind], center, 2, 20);
}
