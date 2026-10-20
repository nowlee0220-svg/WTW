using Wtw.Sim.Core;
using Wtw.Sim.Data;
using Wtw.Sim.Provinces;
using Wtw.Sim.Time;

namespace Wtw.Sim.People;

/// <summary>(E5c) 밭일 한 가지(Act.Farm 의 TargetB).</summary>
public enum FarmJob : byte { None = 0, Harvest, Sow, Weed, Guard, Store, FetchSeed, GatherSeed }

/// <summary>
/// (E5c) 농사(기획서 8 · 9 · 20절). 이 행성엔 계절이 없다 — 자라는 빠르기는 그 땅의 기온 · 비 · 흙으로 정한다.
///   1) 씨(속성 sprouts)를 먹거나 익히거나 내려놓은 자리 둘레에 가끔 씨가 흘러 싹이 난다(실제로 그 타일에서 자란다).
///   2) 싹을 본 사람은 낮은 확률로 "씨를 묻으면 난다"를 알아낸다 — 보는 횟수 · 실험 기질 · 머문 날에 따라. 무엇을 알아내는지는 그 씨의 속성으로 판정한다(정답표 아님).
///   3) 심는 법을 알면 머무는 곳 곁에 씨를 묻고(그냥 묻은 자리 — 잡풀이 많다), 땅 파는 도구로 일구는 법(밭)을 알면 일군 밭에 심는다.
///   4) 밭은 예약 방식: 다음 단계로 바뀌는 시각에만 본다(묻음 → 싹 → 자람 → 익음 → 오래 두면 떨어짐 → 빈 밭 → 잡풀이 덮음).
///   5) 풀 먹는 짐승이 지키는 사람 · 불이 없는 밭을 뜯어 먹는다. 사람은 곁에서 지키고 쫓는다.
///   6) 거둔 것은 무리가 정해 둔 자리(모아 둔 자리)에 쌓아 나눠 먹는다. 굶주리면 남겨 둔 씨까지 먹어 버리기도 한다(앞을 내다보는 사람은 덜).
///   7) 밭이 있는 무리는 멀리 떠나는 판단이 약하다.
/// 난수는 ("people", 지방) 스트림(사람 일 · 하루 경계)과 ("animals", 지방) 스트림(짐승)에서만.
/// </summary>
public sealed partial class PeopleSim
{
    private int P_sprouts, P_digs, A_bury, A_dig, W_farming;
    private int I_grain, I_beans, I_tuber, I_sownPatch, I_field;
    private CraftRule? R_sow, R_field;
    private int T_ambitiousF;

    public const long PlotEmptyDays = 15, RipeKeepDays = 10, SproutDays = 4;
    public const int WildPlotCap = 40;

    private void InitFarm()
    {
        P_sprouts = Craft.PropIndex("sprouts"); P_digs = Craft.PropIndex("digs");
        A_bury = Craft.ActionIndex("bury"); A_dig = Craft.ActionIndex("dig");
        W_farming = -1; foreach (var w in Craft.Wishes) if (w.Id == "farming") W_farming = w.Index;
        I_grain = Craft.ItemIndex("grain"); I_beans = Craft.ItemIndex("beans"); I_tuber = Craft.ItemIndex("tuber");
        I_sownPatch = Craft.ItemIndex("sown_patch"); I_field = Craft.ItemIndex("field");
        R_sow = Craft.Rule("sow"); R_field = Craft.Rule("field");
        T_ambitiousF = Data.TraitIndex("ambitious");
    }

    /// <summary>씨(심으면 나는 것)인가.</summary>
    public bool IsSeed(int item) => P_sprouts >= 0 && Prop(item, P_sprouts) >= 2 && Craft.Catalog.Items[item].Kind != "place";
    public bool KnowsFarming(Person p) => (R_sow is not null && p.Knows(R_sow.Index)) || (R_field is not null && p.Knows(R_field.Index));
    public bool KnowsField(Person p) => R_field is not null && p.Knows(R_field.Index);

    // ───────── 자라기 좋은 정도 ─────────

    /// <summary>
    /// 타일마다 자라기 좋은 정도(0~100) = 흙 × 기온 × 물기. 흙: 풀 · 흙 100, 진흙 70, 모래 35, 늪 30, 자갈 25, 바위 · 눈 · 얼음 0(나무 밑 · 동굴도 0).
    /// 기온(연평균): 3°C 아래 0 · 10°C 50 · 18~28°C 100 · 34°C 60. 물기(연 강수): 150mm 아래 0 · 500mm 80 · 500~1,800mm 100 · 많으면 조금 덜; 민물 곁(2타일)은 75 이상.
    /// 지도로만 정해진다(저장 안 함).
    /// </summary>
    public byte[] GrowMap(ProvinceLife life)
    {
        if (life.Grow is not null) return life.Grow;
        var g = new byte[HexFrame.TileCount];
        int m = life.Mean10;
        int tf = m < 30 ? 0 : m < 100 ? (m - 30) * 50 / 70 : m < 180 ? 50 + (m - 100) * 50 / 80 : m <= 280 ? 100 : m <= 340 ? 100 - (m - 280) * 40 / 60 : 60;
        int pr = _w.Planet.PrecipMm[life.Cell];
        int mf = pr < 150 ? 0 : pr < 500 ? 20 + (pr - 150) * 60 / 350 : pr <= 1800 ? 100 : pr <= 3000 ? 100 - (pr - 1800) * 30 / 1200 : 70;
        var ground = life.Map.Ground;
        for (int i = 0; i < HexFrame.TileCount; i++)
        {
            if (!life.Walk[i] || life.Shelter[i] == 3) continue;
            int sf = ground[i] switch { Ground.Grass or Ground.Soil => 100, Ground.Mud => 70, Ground.Sand => 35, Ground.Marsh => 30, Ground.Gravel => 25, _ => 0 };
            if (sf == 0) continue;
            int o = life.Map.ObjectAt(i);
            if (o >= 0 && ObjectCatalog.Default.Objects[o].IsTree) continue;
            int moist = mf;
            if (moist < 75)
            {
                int x = i % S, y = i / S;
                for (int dy = -2; dy <= 2 && moist < 75; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if ((uint)nx < S && (uint)ny < S && life.Drink[ny * S + nx]) { moist = 75; break; }
                    }
            }
            g[i] = (byte)(sf * tf / 100 * moist / 100);
        }
        return life.Grow = g;
    }

    public int GrowAt(ProvinceLife life, int tile) => (uint)tile < HexFrame.TileCount ? GrowMap(life)[tile] : 0;

    /// <summary>씨를 묻고 익을 때까지 날 수(자라기 좋은 정도 g — 15 아래면 자라지 않는다): 100 이면 16일, 50 이면 32일, 60일까지.</summary>
    public static int GrowDays(int g) => g < 15 ? 0 : Math.Clamp(1600 / g, 16, 60);

    /// <summary>그 해 그 땅의 비 · 날씨(보이지 않는 작은 계절 — 기획서 5-2): 70~115%, 열두 해에 한 번꼴로 가뭄 35%. 시드 · 칸 · 해로만 정해진다.</summary>
    public int YearLuck(int cell, long year)
    {
        ulong h = StableHash.Combine(_w.Seed, (ulong)cell, (ulong)year) % 1000;
        return h < 80 ? 35 : 70 + (int)(h % 46);
    }

    /// <summary>잡풀(0~100): 마지막으로 김을 맨 때부터 하루 2씩. 그냥 묻은 자리 · 저절로 난 싹은 50 에서 시작.</summary>
    public int WeedsOf(Plot p, long now) => (int)Math.Min(100, (p.Tilled ? 0 : 50) + Math.Max(0, now - p.LastTend) / GameTime.TicksPerDay * 2);

    /// <summary>한 칸에서 거둘 수 있는 양(잡풀 · 짐승 · 날씨를 빼기 전): 10 × 자라기 좋은 정도, 그냥 묻은 자리는 절반.</summary>
    public int PlotPotential(ProvinceLife life, Plot p) => 10 * GrowAt(life, p.Tile) * (p.Tilled ? 100 : 50) / 10_000;

    // ───────── 하루 경계: 단계가 바뀌는 밭만 ─────────

    /// <summary>하루 경계(지방마다, 한 줄기): 바뀔 때가 된 밭만 다음 단계로 · 모아 둔 것이 조금씩 없어짐 · 해가 바뀌면 흉년을 본다.</summary>
    private void FarmDaily(int cell, ProvinceLife life)
    {
        ProcessPlots(life, Now);
        long year = Now / GameTime.TicksPerYear;
        if (life.HarvestYear != year)
        {
            if (life.YearPotential >= 20 && life.YearHarvest * 2 < life.YearPotential)
            {
                St.BadHarvests++;
                if (FirstAlive(cell) is { } who)
                    Notify(WorldEventKind.BadHarvest, who, -1, $"흉년: {LandLabelOr(cell, "한 땅")}의 무리가 지난해 거둘 만한 것의 {life.YearHarvest * 100 / Math.Max(1, life.YearPotential)}%만 거두었습니다" + (YearLuck(cell, year - 1) < 50 ? " — 가뭄이 들었다" : ""));
            }
            life.HarvestYear = (int)year; life.YearHarvest = 0; life.YearPotential = 0;
        }
        StashDaily(life, Rng(cell));
    }

    /// <summary>바뀔 때(Due)가 된 밭을 차례(시각, 타일)대로 다음 단계로 — 잠들었다 깨어난 지방도 같은 길로 흐른 날만큼 맞춰진다.</summary>
    internal void ProcessPlots(ProvinceLife life, long until)
    {
        for (int guard = 0; guard < 100_000 && life.PlotDue.Count > 0; guard++)
        {
            var (due, tile) = life.PlotDue.Min;
            if (due > until) break;
            life.PlotDue.Remove((due, tile));
            if (!life.Plots.TryGetValue(tile, out var p)) continue;
            p.Due = 0;
            AdvancePlot(life, p, due);
        }
    }

    private void AdvancePlot(ProvinceLife life, Plot p, long at)
    {
        const long D = GameTime.TicksPerDay;
        int g = GrowAt(life, p.Tile), days = GrowDays(g);
        switch (p.Stage)
        {
            case PlotStage.Sown:
                if (days == 0) { St.SeedsFailed++; Empty(p); return; } // 싹이 트지 않았다(너무 춥거나 · 마르거나 · 흙이 없다)
                p.Stage = PlotStage.Sprout; p.Stamp = at;
                life.SetPlotDue(p, at + Math.Max(1, (days - SproutDays) / 3) * D);
                return;
            case PlotStage.Sprout:
                p.Stage = PlotStage.Growing; p.Stamp = at;
                life.SetPlotDue(p, at + Math.Max(1, days - SproutDays - Math.Max(1, (days - SproutDays) / 3)) * D);
                return;
            case PlotStage.Growing:
                p.Stage = PlotStage.Ripe; p.Stamp = at;
                life.SetPlotDue(p, at + RipeKeepDays * D);
                return;
            case PlotStage.Ripe:
                if (!p.Wild) St.FieldsFallen++; // 다 익고 오래 두어 떨어져 버려졌다
                Empty(p);
                return;
            case PlotStage.Empty:
                St.FieldsWeededOver++; // 일군 빈 밭을 오래 두어 잡풀이 다시 덮었다
                life.RemovePlot(p.Tile);
                return;
        }
        void Empty(Plot q)
        {
            if (!q.Tilled) { life.RemovePlot(q.Tile); return; }
            q.Stage = PlotStage.Empty; q.Crop = -1; q.Damage = 0; q.Stamp = at;
            life.SetPlotDue(q, at + PlotEmptyDays * D);
        }
    }

    /// <summary>잠든 지방이 깨어남: 밭을 되살리고, 흐른 날만큼 단계를 맞춘다(사람이 없던 동안 익어 떨어지거나 잡풀이 덮었다).</summary>
    private void WakeFarm(ProvinceLife life, DormantProvince d)
    {
        foreach (var p in d.Plots) life.AddPlot(p);
        life.Stash = d.Stash; life.FarmSince = d.FarmSince; life.FirstHarvest = d.FirstHarvest; life.SeedsEatenYear = d.SeedsEatenYear;
        life.YearHarvest = d.YearHarvest; life.YearPotential = d.YearPotential; life.HarvestYear = d.HarvestYear;
        if (d.SleptAt >= 0) ProcessPlots(life, Now); // 저장할 때 깨어 있던 지방은 하루 경계에 그대로 이어서
    }

    // ───────── 흘린 씨 · 싹을 보고 알아냄 ─────────

    /// <summary>씨를 먹거나 익히거나 내려놓았다: 드물게(스무 번에 한 번 — 흘린 씨는 대개 새 · 쥐가 먹는다) 둘레 2타일에 씨가 흘러 싹이 난다(그 땅에서 자랄 수 있으면).</summary>
    private void SpillSeed(Person p, int item, Pcg32 rng)
    {
        if (!IsSeed(item) || rng.NextInt(100) >= 5) return;
        var life = Life(p.Cell);
        int wild = 0;
        foreach (var (_, q) in life.Plots) if (q.Wild) wild++;
        if (wild >= WildPlotCap) return;
        int here = p.Tile(Now);
        for (int tries = 0; tries < 6; tries++)
        {
            int x = here % S + rng.NextInt(-2, 3), y = here / S + rng.NextInt(-2, 3);
            if ((uint)x >= S || (uint)y >= S) continue;
            int t = y * S + x;
            if (!FreeForPlot(life, t)) continue;
            var plot = new Plot { Tile = t, Stage = PlotStage.Sown, Wild = true, Crop = item, Stamp = Now, LastTend = Now };
            plot.Due = Now + SproutDays * GameTime.TicksPerDay;
            life.AddPlot(plot);
            St.WildSprouts++;
            return;
        }
    }

    private bool FreeForPlot(ProvinceLife life, int t) =>
        (uint)t < HexFrame.TileCount && life.Walk[t] && !GroundInfo.IsWater(life.Map.Ground[t]) && GrowAt(life, t) > 0 && !life.Plots.ContainsKey(t)
        && !life.Fires.ContainsKey(t) && t != life.Stash && !life.HasItems(t) && HexFrame.BorderDistance(t) >= 3;

    /// <summary>
    /// 판단할 때(하루에 한 번까지 — 둘러보기): 심는 법을 모르는 사람이 둘레 8타일의 싹 · 자라는 것을 보면, 낮은 확률로 "씨를 묻으면 난다"를 알아낸다.
    /// 확률(천분의): 4 + 호기심 8 + 영리함 2씩 · 본 횟수(10번까지 1씩) · 이 땅에 열흘 넘게 머묾 4 − 고집 3. 알아내는 것은 그 씨의 속성으로 판정한다.
    /// </summary>
    private void SeeSprouts(Person p, ProvinceLife life, int here)
    {
        if (life.Plots.Count == 0 || R_sow is null || A_bury < 0) return;
        if (Now - p.LastSproutLook < GameTime.TicksPerDay || p.Stage(Now) < LifeStage.Child || p.Knows(R_sow.Index)) return;
        p.LastSproutLook = Now;
        int st = NearestOf(ViewOf(life).Crops, here, 8);
        if (st < 0) return;
        var seen = life.Plots[st];
        St.SproutSights++;
        if (p.SproutSeen == 0) AddBio(p, BioKind.SawSprouts, seen.Crop, seen.Tile);
        p.SproutSeen++;
        var rng = Rng(p.Cell);
        int chance = 4 + (p.HasTrait(T_curious) ? 8 : 0) + (Ability(p, PeopleData.Int) - 5) * 2 + Math.Min(p.SproutSeen, 10)
            + (Now - p.ArrivedTick >= 10 * GameTime.TicksPerDay ? 4 : 0) - (p.HasTrait(T_stubborn) ? 3 : 0);
        if (rng.NextInt(1000) >= chance) return;
        // "저기 떨어진 낟알에서 싹이 났다 — 묻으면 나는구나": 그 씨를 묻어 두는 일로 판정한다
        var outcome = Craft.Judge(A_bury, PropsOf(seen.Crop), null, null, 0, 0, 0, 0);
        if (!outcome.Success || outcome.Rule is null) return;
        St.SproutLearned++;
        Learn(p, outcome.Rule, W_farming, seen.Crop, -1, -1);
        AddWish(p, W_farming, 30);
    }

    // ───────── 밭일 고르기 ─────────

    /// <summary>지방의 밭 모습(밭이 바뀌거나 한 시간이 지나면 다시 셈한다 — 결과에 영향 없는 기억).</summary>
    private sealed class FarmView
    {
        public int Version = -1; public long Hour = -1;
        public readonly List<int> Ripe = new(), Weedy = new(), EmptyTilled = new(), Crops = new();
        public int Tilled, Plots, Wild, People;
        /// <summary>새로 심을 땅(가운데 · 판) · 노려지는 밭(한 시간마다).</summary>
        public int NewTile = -2, NewCenter = -1, NewVersion = -1, Threat = -2;
    }
    private FarmView ViewOf(ProvinceLife life)
    {
        var v = life.FarmCache as FarmView ?? (FarmView)(life.FarmCache = new FarmView());
        long hour = Now / GameTime.TicksPerHour;
        if (v.Version == life.FarmVersion && v.Hour == hour) return v;
        v.Version = life.FarmVersion; v.Hour = hour;
        v.Ripe.Clear(); v.Weedy.Clear(); v.EmptyTilled.Clear(); v.Crops.Clear(); v.Tilled = 0; v.Plots = 0; v.Wild = 0; v.Threat = -2; v.People = 0;
        foreach (var q in life.People) if (!q.Dead && q.Stage(Now) != LifeStage.Baby) v.People++;
        foreach (var (t, q) in life.Plots)
        {
            v.Plots++;
            if (q.Wild) v.Wild++;
            if (q.Tilled) v.Tilled++;
            if (q.Stage == PlotStage.Ripe && q.Crop >= 0) v.Ripe.Add(t);
            if (q.Stage == PlotStage.Empty) v.EmptyTilled.Add(t);
            if (q.Stage is PlotStage.Sprout or PlotStage.Growing or PlotStage.Ripe && q.Crop >= 0) v.Crops.Add(t);
            if (q.Stage is PlotStage.Sprout or PlotStage.Growing && q.Tilled && WeedsOf(q, Now) >= 50) v.Weedy.Add(t); // 김은 일군 밭만(그냥 묻은 자리는 매도 금세 덮인다)
        }
        return v;
    }

    /// <summary>모아 둔 자리에 놓인 씨의 수.</summary>
    private int StashSeeds(ProvinceLife life)
    {
        if (life.Stash < 0 || !life.Items.TryGetValue(life.Stash, out var list)) return 0;
        int n = 0; foreach (var (it, c) in list) if (IsSeed(it)) n += c;
        return n;
    }

    private int CarriedSeeds(Person p) { int n = 0; foreach (var (it, c) in p.Carry) if (IsSeed(it)) n += c; return n; }

    /// <summary>모아 둘 만한 먹을 것(오래가는 것 — 씨 · 볶은 낟알 · 말린 고기 …)을 든 수.</summary>
    private int StorableCarried(Person p) { int n = 0, seeds = 0; foreach (var (it, c) in p.Carry) if (Storable(it)) { if (IsSeed(it)) seeds += c; else n += c; } return n + Math.Max(0, seeds - SeedKeep); }
    /// <summary>심으려고 들고 다니는 씨(모아 둔 자리에 두지 않는다).</summary>
    public const int SeedKeep = 6;
    private const long SeedMissKey = -77;
    private bool Storable(int it) => Prop(it, Craft.P_edible) >= 1 && Prop(it, Craft.P_rots) <= 2 && Prop(it, Craft.P_poison) <= 1 && Craft.Catalog.Items[it].Kind != "place";

    /// <summary>
    /// 밭일 점수(심는 법을 아는 열 살 넘은 사람, 낮에): 익은 것 거두기 30 · 밭을 노리는 짐승 쫓기 24 · 심기 18 · 거둔 것 모아 두기 20 · 씨 가지러 15 · 김매기 12.
    /// 가장 급한 일 하나를 고른다.
    /// </summary>
    private int FarmScore(Person p, ProvinceLife life, out FarmJob job, out int tile)
    {
        job = FarmJob.None; tile = -1;
        var bj = FarmJob.None; int btile = -1;
        int r = FarmScoreInner(p, life, ref bj, ref btile);
        job = bj; tile = btile;
        return r;
    }

    private int FarmScoreInner(Person p, ProvinceLife life, ref FarmJob jobOut, ref int tileOut)
    {
        if (p.Known.Count == 0 || life.IsNight(Now) || p.AgeYears(Now) < 10 || !KnowsFarming(p)) return int.MinValue;
        int here = p.Tile(Now);
        int skill = p.SkillLevel(PeopleData.Farming);
        var v = ViewOf(life);
        int best = int.MinValue; FarmJob job = FarmJob.None; int tile = -1;
        void Take(FarmJob j, int t, int s) { if (s > best) { best = s; job = j; tile = t; } }
        bool room = CarryWeight(p) + 3 <= CarryCapOf(p);
        // 1) 익은 것
        if (room && v.Ripe.Count > 0 && NearestOf(v.Ripe, here, 60, life) is int rt && rt >= 0) Take(FarmJob.Harvest, rt, 30 + skill * 2);
        // 2) 밭을 노리는 짐승
        if (v.Crops.Count > 0)
        {
            if (v.Threat == -2) v.Threat = ThreatenedPlot(life, v);
            if (v.Threat >= 0 && Cheb(v.Threat, here) <= 50 && life.Region[v.Threat] == life.Region[here]) Take(FarmJob.Guard, v.Threat, 24);
        }
        // 3) 거둔 것 · 말린 고기를 모아 두기
        int store = StorableCarried(p);
        bool stashOk = life.Stash < 0 || life.Paths.Reachable(here, life.Stash);
        if (store >= 5 && stashOk && (life.Stash < 0 || Cheb(life.Stash, here) > 2)) Take(FarmJob.Store, life.Stash, 20);
        // 4) 심기: 씨가 있으면 빈 밭에, 없으면 새 땅에(무리 사람 수만큼 넓힌다)
        int seeds = CarriedSeeds(p);
        bool expand = v.Plots - v.Wild < FarmTarget(v.People);
        if (seeds > 0)
        {
            int st = v.EmptyTilled.Count > 0 ? NearestOf(v.EmptyTilled, here, 60, life) : -1;
            if (st < 0 && expand)
            {
                int center = life.Stash >= 0 ? life.Stash : here;
                if (v.NewVersion != life.FarmVersion || v.NewCenter != center) { v.NewVersion = life.FarmVersion; v.NewCenter = center; v.NewTile = NewPlotTile(life, here); }
                st = v.NewTile >= 0 && life.Region[v.NewTile] == life.Region[here] ? v.NewTile : -1;
            }
            if (st >= 0) Take(FarmJob.Sow, st, 18 + skill);
        }
        else if ((v.EmptyTilled.Count > 0 || expand && v.NewTile >= 0) && life.Stash >= 0 && stashOk && StashSeeds(life) > 0 && Cheb(life.Stash, here) <= 60 && room) Take(FarmJob.FetchSeed, life.Stash, 15);
        else if ((v.EmptyTilled.Count > 0 || expand) && room && !(p.MissKey == SeedMissKey && Now - p.MissTick < GameTime.TicksPerHour))
        {
            // 심을 씨가 하나도 없다 → 들의 낟알 · 콩 · 덩이뿌리를 모은다
            p.Position(Now, out int px, out int py);
            int gt2 = life.NearestCarryWhere(px / 1000, py / 1000, Now, 30, h => IsSeed(h.ItemIndex));
            if (gt2 >= 0) Take(FarmJob.GatherSeed, gt2, 14 + skill);
            else { p.MissKey = SeedMissKey; p.MissTick = Now; } // 둘레에 없다 — 한 시간은 다시 찾지 않는다(재료 못 찾음 기억과 같은 칸)
        }
        // 5) 김매기
        if (v.Weedy.Count > 0 && NearestOf(v.Weedy, here, 40, life) is int wt && wt >= 0) Take(FarmJob.Weed, wt, 10 + skill);
        jobOut = job; tileOut = tile;
        return best;
    }

    /// <summary>무리가 일구려는 밭 크기: 사람(아기 빼고) 하나에 네 칸, 120칸까지.</summary>
    public static int FarmTarget(int people) => Math.Min(120, people * 4);

    private int NearestOf(List<int> tiles, int here, int max, ProvinceLife? sameRegion = null)
    {
        int best = -1, bd = max + 1;
        int reg = sameRegion is null ? -1 : sameRegion.Region[here];
        foreach (int t in tiles) { int d = Cheb(t, here); if (d < bd && (reg < 0 || sameRegion!.Region[t] == reg)) { bd = d; best = t; } }
        return best;
    }

    /// <summary>풀 먹는 들짐승 무리가 15타일 안에 있고 곁(5타일)에 아무도 없는 자라는 밭(없으면 −1).</summary>
    private int ThreatenedPlot(ProvinceLife life, FarmView v)
    {
        if (life.Herds.Count == 0) return -1;
        foreach (var h in life.Herds.Values)
        {
            if (h.Tame || Fauna.Animals[h.Species].Predator) continue;
            int ht = h.Tile(Now);
            int pt = NearestOf(v.Crops, ht, 15);
            if (pt < 0) continue;
            if (NearestPerson(life, pt % S * 1000 + 500, pt / S * 1000 + 500, 5, out int n) is not null && n > 0) continue;
            return pt;
        }
        return -1;
    }

    /// <summary>새로 심을 땅: 모아 둔 자리(없으면 지금 자리) 둘레 14타일을 안에서부터 — 이미 있는 밭에 붙여서, 자라기 좋은 곳(25 이상).</summary>
    private int NewPlotTile(ProvinceLife life, int here)
    {
        int c = life.Stash >= 0 ? life.Stash : here;
        int cx = c % S, cy = c / S, reg = life.Region[c];
        for (int r = 1; r <= 14; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    int x = cx + dx, y = cy + dy;
                    if ((uint)x >= S || (uint)y >= S) continue;
                    int t = y * S + x;
                    if (life.Region[t] != reg || GrowAt(life, t) < 25 || !FreeForPlot(life, t)) continue;
                    return t;
                }
        return -1;
    }

    /// <summary>모아 둘 자리를 정한다: 불이 15타일 안이면 그 곁, 아니면 지금 자리(걸을 수 있고 물건이 없는 곳).</summary>
    private int ChooseStash(ProvinceLife life, int here)
    {
        int ft = life.NearestFire(here % S, here / S, Now, 15, litOnly: false);
        int c = ft >= 0 ? ft : here;
        for (int r = 1; r <= 4; r++)
            for (int k = 0; k < 8; k++)
            {
                int x = c % S + DX8[k] * r, y = c / S + DY8[k] * r;
                if ((uint)x >= S || (uint)y >= S) continue;
                int t = y * S + x;
                if (life.Walk[t] && life.Region[t] == life.Region[here] && !life.Fires.ContainsKey(t) && !life.Plots.ContainsKey(t) && !GroundInfo.IsWater(life.Map.Ground[t])) return t;
            }
        return here;
    }

    private bool TryFarm(Person p, FarmJob job, int tile)
    {
        var life = Life(p.Cell);
        int here = p.Tile(Now);
        if (job == FarmJob.Store && life.Stash < 0) { life.Stash = ChooseStash(life, here); tile = life.Stash; }
        if (tile < 0) return false;
        if (job == FarmJob.GatherSeed)
        {
            if (!WalkTo(p, tile)) return false;
            SetWhy(p, Act.Gather, Why.FarmSeed); p.Target = tile;
            return true;
        }
        int goal = job == FarmJob.Guard ? Beside(p, tile, 2) : tile;
        var why = job switch { FarmJob.Harvest => Why.FarmRipe, FarmJob.Sow => KnowsField(p) ? Why.FarmTill : Why.FarmSow, FarmJob.Weed => Why.FarmWeed, FarmJob.Guard => Why.FarmGuard, FarmJob.Store => Why.FarmStore, _ => Why.FarmSeed };
        if (goal != here && !WalkTo(p, goal)) return false;
        SetWhy(p, Act.Farm, why); p.Target = tile; p.TargetB = (int)job;
        if (goal == here) ArriveFarm(p);
        return true;
    }

    /// <summary>밭에 닿았다: 일하는 시간(타일 수 × 일 — 거두기 25분 · 김매기 20분 · 심기: 일구기 땅 파는 힘에 따라 30~90분, 그냥 묻기 15분).</summary>
    private void ArriveFarm(Person p)
    {
        var life = Life(p.Cell);
        var job = (FarmJob)p.TargetB;
        long min = job switch
        {
            FarmJob.Harvest => 25L * Math.Max(1, Around(life, p.Target, q => q.Stage == PlotStage.Ripe && q.Crop >= 0).Count),
            FarmJob.Weed => 8L * Math.Max(1, Around(life, p.Target, q => q.Stage is PlotStage.Sprout or PlotStage.Growing, 9).Count),
            FarmJob.Sow => SowMinutes(p, life) * Math.Min(3, Math.Max(1, CarriedSeeds(p))),
            FarmJob.Guard => 120,
            _ => 10,
        };
        if (job == FarmJob.Guard) ChaseHerds(p, life);
        DoStill(p, Math.Max(300, min * GameTime.TicksPerHour / 60 * WorkPercent(p) / 100));
    }

    private long SowMinutes(Person p, ProvinceLife life)
    {
        bool tilled = life.Plots.TryGetValue(p.Target, out var q) && q.Tilled;
        if (tilled) return 15;
        if (!KnowsField(p)) return 15;
        int digs = BestTool(p, P_digs, 1) is int tl && tl >= 0 ? Prop(tl, P_digs) : 0;
        return digs >= 4 ? 30 : digs >= 2 ? 50 : 90;
    }

    /// <summary>tile 과 그 둘레 1타일의 맞는 밭(타일 번호 순, 넷까지).</summary>
    private List<Plot> Around(ProvinceLife life, int tile, Func<Plot, bool> ok, int max = 4)
    {
        var list = new List<Plot>();
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int x = tile % S + dx, y = tile / S + dy;
                if ((uint)x >= S || (uint)y >= S) continue;
                if (life.Plots.TryGetValue(y * S + x, out var q) && ok(q)) list.Add(q);
            }
        list.Sort((a, b) => a.Tile.CompareTo(b.Tile));
        if (list.Count > max) list.RemoveRange(max, list.Count - max);
        return list;
    }

    private void FinishFarm(Person p, bool done)
    {
        if (!done) return;
        var life = Life(p.Cell);
        var rng = Rng(p.Cell);
        switch ((FarmJob)p.TargetB)
        {
            case FarmJob.Harvest: Harvest(p, life, rng); break;
            case FarmJob.Sow: Sow(p, life, rng); break;
            case FarmJob.Weed:
                foreach (var q in Around(life, p.Target, q => q.Stage is PlotStage.Sown or PlotStage.Sprout or PlotStage.Growing, 9)) { q.LastTend = Now; St.Weeded++; life.FarmVersion++; }
                GainSkill(p, PeopleData.Farming, 4);
                break;
            case FarmJob.Guard: ChaseHerds(p, life); St.Guarded++; break;
            case FarmJob.Store: StoreFood(p, life); break;
            case FarmJob.FetchSeed: FetchSeeds(p, life); break;
        }
    }

    private void Harvest(Person p, ProvinceLife life, Pcg32 rng)
    {
        bool sharp = HasSharpTool(p);
        int got = 0, tiles = 0, potential = 0;
        int luck = YearLuck(life.Cell, Now / GameTime.TicksPerYear);
        bool poor = false;
        foreach (var q in Around(life, p.Target, q => q.Stage == PlotStage.Ripe && q.Crop >= 0))
        {
            if (CarryWeight(p) + 2 > CarryCapOf(p)) break;
            int pot = PlotPotential(life, q);
            long y1000 = (long)pot * 1000 * (100 - WeedsOf(q, Now) / 2) / 100 * (100 - q.Damage) / 100 * luck / 100 * (sharp ? 120 : 100) / 100;
            int y = (int)(y1000 / 1000) + (rng.NextInt(1000) < y1000 % 1000 ? 1 : 0);
            if (!q.Wild) { life.YearHarvest += y; life.YearPotential += pot; }
            if (!q.Tilled) poor = true;
            int crop = q.Crop;
            p.AddCarry(crop, y);
            got += y; tiles++; potential += pot;
            St.HarvestTiles++; St.HarvestItems += y;
            if (q.Tilled) { q.Stage = PlotStage.Empty; q.Crop = -1; q.Damage = 0; q.Stamp = Now; life.SetPlotDue(q, Now + PlotEmptyDays * GameTime.TicksPerDay); }
            else life.RemovePlot(q.Tile);
        }
        if (tiles == 0) return;
        p.Harvested += got;
        GainSkill(p, PeopleData.Farming, 5 * tiles);
        AddBio(p, BioKind.Harvested, -1, got);
        if (got > 0 && life.FirstHarvest < 0)
        {
            life.FirstHarvest = Now;
            if (InProvince) Defer(new PendingOp { Kind = PendingOp.Kinds.Farm, A = 1, B = p.Id, C = got });
            else HarvestFirstWorld(p, got);
        }
        // 그냥 묻은 자리는 잡풀에 덮여 덜 거둔다 → 땅을 제대로 일구고 싶다(밭일 도구 · 일구는 법)
        if (poor) AddWish(p, W_farming, 20);
        if (got * 2 < potential) AddWish(p, W_farming, 10);
    }

    /// <summary>(S1) 무리에서 처음 거둠(세계 쪽 — 맞추는 시각에): 알림 하나.</summary>
    internal void HarvestFirstWorld(Person p, int got)
    {
        Notify(WorldEventKind.HarvestFirst, p, -1, $"이 무리에서 처음: {Hangul.I(DisplayName(p))} 심은 것을 거두었습니다({got}개) — {LandLabelOr(p.Cell, "한 땅")}");
    }

    private void Sow(Person p, ProvinceLife life, Pcg32 rng)
    {
        bool field = KnowsField(p);
        int tool = BestTool(p, P_digs, 1);
        bool canTill = field && tool >= 0 && Prop(tool, P_digs) >= 2;
        if (field && !canTill) AddWish(p, W_farming, 12); // 일구는 법은 아는데 땅을 팔 것이 없다
        if (life.Stash < 0) life.Stash = ChooseStash(life, p.Tile(Now));
        if (life.FarmSince < 0) life.FarmSince = Now;
        // 심을 타일: 겨눈 타일과 둘레의 빈 밭 · 빈 땅(셋까지)
        var tiles = new List<int> { p.Target };
        var view = ViewOf(life);
        int room = FarmTarget(view.People) - (life.Plots.Count - view.Wild);
        for (int k = 0; k < 8 && tiles.Count < 3; k++)
        {
            int x = p.Target % S + DX8[k], y = p.Target / S + DY8[k];
            if ((uint)x >= S || (uint)y >= S) continue;
            int t = y * S + x;
            if (life.Plots.TryGetValue(t, out var e) ? e.Stage == PlotStage.Empty : room-- > 0 && GrowAt(life, t) >= 25 && FreeForPlot(life, t)) tiles.Add(t);
        }
        int sown = 0;
        foreach (int t in tiles)
        {
            int seed = -1, bn = 0;
            foreach (var (it, c) in p.Carry) if (IsSeed(it) && c > bn) { bn = c; seed = it; }
            if (seed < 0) break;
            bool existing = life.Plots.TryGetValue(t, out var q);
            if (existing && q!.Stage != PlotStage.Empty) continue;
            if (!existing && !FreeForPlot(life, t)) continue;
            p.RemoveCarry(seed, 1);
            if (!existing) { q = new Plot { Tile = t, Tilled = canTill }; if (canTill) St.Tilled++; }
            q!.Stage = PlotStage.Sown; q.Crop = seed; q.Stamp = Now; q.LastTend = Now; q.Damage = 0; q.Planter = p.Id; q.Wild = false;
            if (existing) life.SetPlotDue(q, Now + SproutDays * GameTime.TicksPerDay);
            else { q.Due = Now + SproutDays * GameTime.TicksPerDay; life.AddPlot(q); }
            sown++; St.Sown++;
        }
        if (sown == 0) return;
        GainSkill(p, PeopleData.Farming, 4 * sown);
        if (canTill && tool >= 0) WearTool(p, tool, rng);
        if (p.FirstSow < 0) { p.FirstSow = Now; AddBio(p, canTill ? BioKind.FirstField : BioKind.Sowed, -1, sown); }
        if (R_sow is not null && p.Knows(R_sow.Index)) UseKnowledge(p, R_sow.Index);
        if (canTill && R_field is not null) UseKnowledge(p, R_field.Index);
        TeachFarmWatchers(p, life, canTill ? R_field : R_sow);
    }

    /// <summary>곁(6타일)에서 심는 것을 본 사람은 따라 배운다(만들기와 같은 길).</summary>
    private void TeachFarmWatchers(Person p, ProvinceLife life, CraftRule? rule)
    {
        if (rule is null || !p.Knows(rule.Index)) return;
        TeachWatchers(p, rule, W_farming, -1, -1);
    }

    /// <summary>둘레 8타일의 풀 먹는 들짐승을 쫓는다(다가가 소리친다).</summary>
    private void ChaseHerds(Person p, ProvinceLife life)
    {
        if (life.Herds.Count == 0) return;
        p.Position(Now, out int px, out int py);
        var rng = ARng(life);
        foreach (var h in life.Herds.Values)
        {
            var a = Fauna.Animals[h.Species];
            if (h.Tame || a.Predator) continue;
            h.Position(Now, out int hx, out int hy);
            if (Math.Max(Math.Abs(hx - px), Math.Abs(hy - py)) > 8_000) continue;
            HerdStop(h);
            if (HerdFlee(life, h, a, px, py, rng)) { St.ChasedOff++; h.TargetKind = 0; h.Target = -1; }
        }
    }

    private void StoreFood(Person p, ProvinceLife life)
    {
        int here = p.Tile(Now);
        if (life.Stash < 0) life.Stash = ChooseStash(life, here);
        if (Cheb(here, life.Stash) > 2) return;
        int keep = 2, seedKeep = SeedKeep;
        for (int k = p.Carry.Count - 1; k >= 0; k--)
        {
            var (it, c) = p.Carry[k];
            if (!Storable(it)) continue;
            int give;
            if (IsSeed(it)) { give = Math.Max(0, c - seedKeep); seedKeep = Math.Max(0, seedKeep - c); }
            else give = Math.Max(0, c - keep);
            if (give <= 0) { if (!IsSeed(it)) keep = Math.Max(0, keep - c); continue; }
            p.RemoveCarry(it, give);
            life.Drop(life.Stash, it, give);
            St.Stored += give;
        }
        // 담는 것(바구니 · 그릇)이 둘 넘으면 하나를 모아 둔 자리에 둔다(덜 잃는다)
        if (!StashHasContainer(life))
            foreach (var (it, c) in p.Carry)
                if (Prop(it, Craft.P_holds) >= 2 && (c >= 2 || ContainerCount(p) >= 2)) { p.RemoveCarry(it, 1); life.Drop(life.Stash, it, 1); break; }
    }

    private int ContainerCount(Person p) { int n = 0; foreach (var (it, c) in p.Carry) if (Prop(it, Craft.P_holds) >= 2) n += c; return n; }

    private int StashContainer(ProvinceLife life)
    {
        if (life.Stash < 0 || !life.Items.TryGetValue(life.Stash, out var list)) return 0;
        int best = 0; foreach (var (it, _) in list) best = Math.Max(best, Prop(it, Craft.P_holds));
        return best;
    }
    private bool StashHasContainer(ProvinceLife life) => StashContainer(life) >= 2;

    private void FetchSeeds(Person p, ProvinceLife life)
    {
        if (life.Stash < 0 || !life.Items.TryGetValue(life.Stash, out var list)) return;
        int want = 6;
        for (int k = 0; k < list.Count && want > 0; k++)
        {
            var (it, c) = list[k];
            if (!IsSeed(it) || c <= 0) continue;
            int t = Math.Min(c, want); want -= t;
            list[k] = (it, c - t);
            p.AddCarry(it, t);
            St.SeedFetch += t;
        }
        list.RemoveAll(c => c.Count <= 0);
        if (list.Count == 0) life.RemoveItems(life.Stash);
    }

    /// <summary>하루 경계: 모아 둔 자리의 먹을 것 · 씨는 쥐 · 벌레 · 짐승이 조금씩 먹는다 — 담는 것이 없으면 하루 1/12, 바구니 1/30, 토기 1/80.</summary>
    private void StashDaily(ProvinceLife life, Pcg32 rng)
    {
        if (life.Stash < 0 || !life.Items.TryGetValue(life.Stash, out var list)) return;
        int hold = StashContainer(life);
        int div = hold >= 4 ? 80 : hold >= 2 ? 30 : 12;
        for (int k = list.Count - 1; k >= 0; k--)
        {
            var (it, n) = list[k];
            if (Prop(it, Craft.P_edible) <= 0) continue;
            int r = Prop(it, Craft.P_rots);
            int d = r >= 3 ? Math.Max(2, div / (r * 2)) : div;
            int lose = Math.Min(n, (n + rng.NextInt(d)) / d);
            if (lose <= 0) continue;
            St.StashLost += lose;
            if (lose == n) list.RemoveAt(k); else list[k] = (it, n - lose);
        }
        if (list.Count == 0) life.RemoveItems(life.Stash);
    }

    // ───────── 모아 둔 자리에서 먹기 · 씨까지 먹어 버림 ─────────

    /// <summary>남겨 둬야 할 씨: 일군 밭(빈 것 포함) 하나에 하나, 넷 이상.</summary>
    private int SeedReserve(ProvinceLife life) { int n = 0; foreach (var (_, q) in life.Plots) if (q.Tilled) n++; return Math.Max(4, n); }

    /// <summary>
    /// 배고픈 사람이 모아 둔 자리에서 먹을 것을 가져간다(모아 둔 자리가 40타일 안). 씨는 남겨 둘 만큼 남기고 가져간다 —
    /// 아주 배고프면(15% 아래) 남겨 둔 씨까지 먹어 버리기도 한다: 기본 70%, 영리함 · 근면 · 야심(앞을 내다봄)은 덜, 게으름 · 예민은 더.
    /// </summary>
    private bool TryStashFood(Person p, ProvinceLife life, int here, Why why)
    {
        if (life.Stash < 0 || !life.Items.TryGetValue(life.Stash, out var list) || Cheb(here, life.Stash) > 40) return false;
        if (!life.Paths.Reachable(here, life.Stash)) return false;
        int best = -1, bs = int.MinValue;
        int seeds = 0; foreach (var (it, c) in list) if (IsSeed(it)) seeds += c;
        int spare = seeds - SeedReserve(life);
        bool roast = KnowsAnyRoast(p);
        bool seedsOnly = true;
        foreach (var (it, c) in list)
        {
            if (c <= 0 || Prop(it, Craft.P_edible) <= 0) continue;
            bool seed = IsSeed(it);
            if (seed && !roast && Prop(it, Craft.P_edible) < 2) continue;
            int s = Prop(it, Craft.P_edible) * 10 - (seed ? 15 : 0) - Prop(it, Craft.P_poison) * 10;
            if (seed && spare <= 0) continue;
            if (!seed) seedsOnly = false;
            if (s > bs) { bs = s; best = it; }
        }
        if (best < 0 && seeds > 0 && p.Food < UrgentFood)
        {
            // 남은 것은 씨뿐 — 다음 해를 생각하면 먹으면 안 된다
            var rng = Rng(p.Cell);
            int eat = 70 - (Ability(p, PeopleData.Int) - 5) * 8 - (p.HasTrait(T_industrious) ? 20 : 0) - (p.HasTrait(T_ambitiousF) ? 15 : 0) + (p.HasTrait(T_lazy) ? 15 : 0) + (p.HasTrait(T_nervous) ? 10 : 0);
            if (rng.NextInt(100) < eat)
            {
                foreach (var (it, c) in list) if (IsSeed(it) && c > 0) { best = it; break; }
                if (best >= 0)
                {
                    St.SeedEaten++;
                    AddBio(p, BioKind.AteSeeds, best, 0);
                    int yr = (int)(Now / GameTime.TicksPerYear);
                    if (life.SeedsEatenYear != yr) { life.SeedsEatenYear = yr; if (InProvince) Defer(new PendingOp { Kind = PendingOp.Kinds.Farm, A = 2, B = p.Id, C = best });
                    else SeedsEatenWorld(p, best); }
                }
            }
        }
        _ = seedsOnly;
        if (best < 0) return false;
        if (Cheb(here, life.Stash) <= 0) { SetWhy(p, Act.Material, why); p.Target = life.Stash; p.TargetB = -2; p.PlanItem = best; St.StashTaken++; ArriveMaterial(p); return true; }
        if (!WalkTo(p, life.Stash)) return false;
        SetWhy(p, Act.Material, why); p.Target = life.Stash; p.TargetB = -2; p.PlanItem = best;
        St.StashTaken++;
        return true;
    }

    internal void SeedsEatenWorld(Person p, int seed)
    {
        Notify(WorldEventKind.SeedsEaten, p, -1, $"{LandLabelOr(p.Cell, "한 땅")}의 무리가 굶주려 남겨 둔 씨앗({Craft.ItemName(seed)})까지 먹어 버렸습니다 — {Hangul.I(DisplayName(p))} 먼저 손을 댔다. 다음 밭이 걱정이다");
    }

    // ───────── 씨를 먹을 거리로 줍기 ─────────

    /// <summary>딸 열매가 없으면 둘레(25타일)의 낟알 · 콩 · 덩이뿌리를 줍는다(익히는 법을 알거나 아주 배고플 때).</summary>
    private bool TryGatherSeedFood(Person p, ProvinceLife life, int hx, int hy, Why why)
    {
        if (!KnowsAnyRoast(p) && p.Food > UrgentFood) return false;
        int t = life.NearestCarryWhere(hx, hy, Now, 25, h => IsSeed(h.ItemIndex) && Prop(h.ItemIndex, Craft.P_edible) > 0);
        if (t < 0 || !WalkTo(p, t)) return false;
        SetWhy(p, Act.Gather, Why.SeedFood); p.Target = t;
        St.SeedGathered++;
        return true;
    }

    // ───────── 짐승이 밭을 먹는다 ─────────

    /// <summary>
    /// (짐승 판단 중) 풀 먹는 들짐승: 18타일 안의 자라는 밭으로 간다 — 곁(5타일)에 사람이 있거나 불이 있으면 가지 않는다. 갔으면 true.
    /// 닿으면(TargetKind 3) 마릿수만큼 둘레 밭을 뜯어 먹는다(한 칸에 35씩, 100 이면 다 먹힘).
    /// </summary>
    internal bool HerdToField(ProvinceLife life, Herd h, AnimalDef a, Pcg32 rng)
    {
        if (a.Predator || h.Tame || life.Plots.Count == 0) return false;
        int here = h.Tile(Now);
        if (h.TargetKind == 3)
        {
            h.TargetKind = 0;
            int t0 = h.Target; h.Target = -1;
            if (Cheb(here, t0) <= 2 && life.Plots.ContainsKey(t0))
            {
                int bites = Math.Max(1, Math.Min(6, h.Count));
                foreach (var q in AroundCrops(life, t0, 2))
                {
                    if (bites-- <= 0) break;
                    q.Damage = Math.Min(100, q.Damage + 35);
                    St.FieldEaten++;
                    life.FarmVersion++;
                    if (q.Damage >= 100) { St.FieldsRuined++; if (q.Tilled) { q.Stage = PlotStage.Empty; q.Crop = -1; q.Damage = 0; q.Stamp = Now; life.SetPlotDue(q, Now + PlotEmptyDays * GameTime.TicksPerDay); } else life.RemovePlot(q.Tile); }
                    if (q.Planter >= 0 && Get(q.Planter) is { Dead: false } owner && owner.Cell == life.Cell && Cheb(owner.Tile(Now), t0) <= SightTiles)
                    {
                        AddBio(owner, BioKind.FieldEaten, h.Species, q.Tile);
                        AddWish(owner, W_farming, 8);
                    }
                }
                h.State = HerdState.Graze;
                ScheduleHerd(life, h, Now + 2 * GameTime.TicksPerHour + rng.NextInt((int)(2 * GameTime.TicksPerHour)));
                return true;
            }
        }
        if (rng.NextInt(100) >= 50) return false;
        int best = NearestOf(ViewOf(life).Crops, here, 18);
        if (best < 0) return false;
        int bx = best % S * 1000 + 500, by = best / S * 1000 + 500;
        if (NearestPerson(life, bx, by, 5, out int near) is not null && near > 0) return false;
        if (life.NearestFire(best % S, best / S, Now, 5) >= 0) return false;
        if (Cheb(here, best) > 1 && !HerdWalk(life, h, best, a.Walk)) return false;
        h.State = HerdState.Graze; h.TargetKind = 3; h.Target = best;
        ScheduleHerd(life, h, (Cheb(here, best) > 1 ? h.Motion.Arrive : Now) + 1);
        return true;
    }

    private List<Plot> AroundCrops(ProvinceLife life, int tile, int r)
    {
        var list = new List<Plot>();
        for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                int x = tile % S + dx, y = tile / S + dy;
                if ((uint)x >= S || (uint)y >= S) continue;
                if (life.Plots.TryGetValue(y * S + x, out var q) && q.Stage >= PlotStage.Sprout && q.Crop >= 0) list.Add(q);
            }
        list.Sort((a, b) => a.Tile.CompareTo(b.Tile));
        return list;
    }

    // ───────── 세계 쪽 · 측정 ─────────

    internal void ApplyFarmOp(PendingOp op)
    {
        var p = Get(op.B);
        if (p is null) return;
        if (op.A == 1) HarvestFirstWorld(p, op.C);
        else if (op.A == 2) SeedsEatenWorld(p, op.C);
    }

    /// <summary>측정 · 화면: 지방의 밭 셈(일군 밭 · 그냥 묻은 자리 · 저절로 난 싹 · 자라는 것 · 익은 것 · 모아 둔 먹을 것 · 씨).</summary>
    public (int Tilled, int Patches, int Wild, int Growing, int Ripe, int StashFood, int StashSeeds) FarmCounts(ProvinceLife life)
    {
        int ti = 0, pa = 0, wi = 0, gr = 0, ri = 0, sf = 0, ss = 0;
        foreach (var (_, q) in life.Plots)
        {
            if (q.Wild) wi++; else if (q.Tilled) ti++; else pa++;
            if (q.Stage is PlotStage.Sown or PlotStage.Sprout or PlotStage.Growing) gr++;
            if (q.Stage == PlotStage.Ripe) ri++;
        }
        if (life.Stash >= 0 && life.Items.TryGetValue(life.Stash, out var list))
            foreach (var (it, c) in list) { if (IsSeed(it)) ss += c; else if (Prop(it, Craft.P_edible) > 0) sf += c; }
        return (ti, pa, wi, gr, ri, sf, ss);
    }

    /// <summary>밭이 있는 무리인가(지방에 사람이 심은 밭 · 자리 셋 이상).</summary>
    private bool FarmTies(Person p, ProvinceLife life)
    {
        if (life.Plots.Count < 3 || !KnowsFarming(p)) return false;
        int n = 0; foreach (var (_, q) in life.Plots) if (!q.Wild && ++n >= 3) return true;
        return false;
    }
}
