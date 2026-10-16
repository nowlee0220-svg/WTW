using Wtw.Sim.Data;
using Wtw.Sim.Planet;
using Wtw.Sim.Time;

namespace Wtw.Sim.People;

/// <summary>인물 카드에 보일 것(시뮬 스레드가 만들어 화면에 건넨다 — 읽기 전용 사본).</summary>
public sealed class PersonCard
{
    public int Id, Cell, Age, Mood, BreakThreshold;
    public string Name = "", SexName = "", StageName = "", Activity = "", Why = "", MoodWord = "", Place = "";
    public bool Alive, Female, Asleep;
    public LifeStage Stage;
    public Appearance Look;
    public List<(string Name, int Value, string Note)> Abilities = new();
    public List<(string Name, int Level, int Progress, int Passion)> Skills = new();
    public List<(string Name, string Effect)> Traits = new();
    public List<(string Name, int Value, string Note)> Needs = new();
    public List<string> Body = new();
    public List<(string Name, int Mood, string Note)> Thoughts = new();
    public List<(int Id, string Name, string Kind, int Opinion, int Grudge, int Favor, bool Alive)> Relations = new();
    public List<string> Story = new();
    public string Carry = "";
}

/// <summary>사람 목록 한 줄.</summary>
public readonly record struct PersonRow(int Id, string Name, int Age, string Activity, int Mood, bool Alive, int Cell, bool Female, LifeStage Stage);

/// <summary>문장은 틀 + 조각으로 만든다(AI 문장 금지, 기획서 원칙 8).</summary>
public static class PeopleText
{
    public static string CauseName(DeathCause c) => c switch
    {
        DeathCause.Starvation => "굶주림", DeathCause.Thirst => "목마름", DeathCause.Cold => "추위", DeathCause.Heat => "더위",
        DeathCause.OldAge => "늙음", DeathCause.Illness => "병", _ => "알 수 없음",
    };

    public static string StageName(LifeStage s, bool female) => s switch
    {
        LifeStage.Baby => "아기", LifeStage.Child => "아이", LifeStage.Elder => female ? "할머니" : "할아버지", _ => female ? "여자" : "남자",
    };

    public static string MoodWord(int m) => m >= 75 ? "아주 좋음" : m >= 55 ? "좋음" : m >= 40 ? "보통" : m >= 25 ? "나쁨" : "아주 나쁨";

    public static string Date(long tick) { var d = GameTime.ToDate(Math.Max(0, tick)); return $"{d.Year}년 {d.Day}일"; }
}

public sealed partial class PeopleSim
{
    private string NameOf(int id) => Get(id)?.Name ?? "누군가";

    private string ItemName(int item) => item >= 0 && item < ObjectCatalog.Default.Items.Count ? ObjectCatalog.Default.Items[item].Name : "무언가";

    /// <summary>지금 하는 일(짧게).</summary>
    public string ActivityText(Person p)
    {
        var life = Life(p.Cell);
        string food = p.Target >= 0 && p.Target < Wtw.Sim.Provinces.HexFrame.TileCount && life.HarvestAt(p.Target) is { } h ? h.ItemName : "먹을 것";
        bool w = p.Walking;
        return p.Act switch
        {
            Act.Dead => $"죽음 — {PeopleText.CauseName(p.Cause)}",
            Act.Carried => p.Carrier >= 0 ? $"{NameOf(p.Carrier)}의 품에 안겨 있음" : "혼자 남겨진 아기",
            Act.Eat => w ? $"{Hangul.Eul(food)} 따러 가는 중" : $"{Hangul.Eul(food)} 따 먹는 중",
            Act.Drink => w ? "물 마시러 가는 중" : "물 마시는 중",
            Act.Sleep => w ? "잘 곳으로 가는 중" : $"자는 중 ({ProvinceLife.ShelterName(p.SleepShelter)}{(p.Huddle ? " · 곁에 사람" : "")})",
            Act.Collapse => "지쳐 쓰러져 잠",
            Act.Rest => "앉아 쉬는 중",
            Act.Talk when p.Why == Why.TalkedTo || !w => p.TargetB == 2 ? $"{NameOf(p.Target)}에게 화풀이를 듣는 중" : $"{Hangul.Gwa(NameOf(p.Target))} 이야기하는 중",
            Act.Talk => $"{NameOf(p.Target)}에게 이야기하러 가는 중",
            Act.Warm => w ? $"{ProvinceLife.ShelterName(life.Shelter[p.Target < 0 ? 0 : p.Target])}(으)로 가는 중" : (p.Why == Why.Hot ? "그늘에서 더위를 피하는 중" : $"{ProvinceLife.ShelterName(life.Shelter[p.Tile(Now)])}에서 몸을 녹이는 중"),
            Act.Wander => w ? (p.Target >= 0 && life.Shelter[p.Target] == 3 ? "동굴로 가 보는 중" : "둘러보는 중") : "주위를 살피는 중",
            Act.Gather => w ? $"{Hangul.Eul(life.HarvestAt(p.Target)?.ItemName ?? "돌")} 주우러 가는 중" : $"{Hangul.Eul(life.HarvestAt(p.Target)?.ItemName ?? "돌")} 줍는 중",
            Act.Follow => w ? $"{Hangul.Eul(NameOf(p.Target))} 따라가는 중" : $"{NameOf(p.Target)} 곁에서 노는 중",
            Act.BreakWander => "울며 헤매는 중",
            Act.BreakArgue => $"{NameOf(p.Target)}에게 화풀이하는 중",
            Act.BreakLeave => p.Asleep ? "혼자 떠나 잠" : "혼자 떠나 있음",
            Act.EatCorpse => "시신을 먹는 중",
            _ => "둘러보는 중",
        };
    }

    /// <summary>왜 그 일을 하나.</summary>
    public string WhyText(Person p)
    {
        var life = Life(p.Cell);
        int arg = p.WhyArg;
        return p.Why switch
        {
            Why.Spawned => "막 이 땅에 뿌려졌다 — 아직 아무것도 모른다",
            Why.Hungry => $"배가 고파서 (배부름 {arg}%)",
            Why.VeryHungry => $"몹시 배가 고파서 — 급함 (배부름 {arg}%)",
            Why.Starving => "굶주리고 있어서 — 급함",
            Why.Thirsty => $"목이 말라서 (물 {arg}%)",
            Why.VeryThirsty => $"몹시 목이 말라서 — 급함 (물 {arg}%)",
            Why.Night => $"밤이라서 (이 지방 시각 {life.LocalHour(Now):00}시 — 해는 6시에 뜬다)",
            Why.Exhausted => "너무 지쳐서",
            Why.Cold => $"추워서 (느끼는 기온 {arg / 10.0:0.0}°C — 그늘 · 동굴은 밤에 덜 춥다)",
            Why.VeryCold => "얼어붙을 듯 추워서 — 급함 (몸이 식고 있다)",
            Why.Hot => "더워서 — 그늘을 찾는다",
            Why.Lonely or Why.WantsTalk => $"어울리고 싶어서 (어울림 {p.Social / 1000}%)",
            Why.Friend => $"친구라서 (호감 {p.GetRelation(p.Target).Opinion})",
            Why.Partner => p.Act == Act.Sleep ? "짝 곁에서 자려고" : "짝이라서",
            Why.Bored => $"쉬고 싶어서 (쉼 {arg}%)",
            Why.Curious => "궁금해서 (호기심)",
            Why.Nothing => "딱히 급한 일이 없어서",
            Why.FollowParent => "아이라서 어른 곁을 따라다닌다",
            Why.Carried => "아기라서 혼자 다니지 못한다",
            Why.TalkedTo => $"{Hangul.I(NameOf(arg))} 말을 걸어와 하던 일을 멈췄다",
            Why.Break => $"기분이 너무 나빠 무너졌다 (기분 {arg} — 무너지는 기준 {BreakThreshold(p)})",
            Why.FamilyNear => "가족 · 친구 곁에서 자려고",
            Why.NoFood => "가까운 곳에 먹을 것이 없어 찾아다닌다",
            Why.NoWater => "마실 물(민물)이 가까이 없어 찾아다닌다 — 바닷물은 못 마신다",
            _ => "",
        };
    }

    public List<PersonRow> Rows(int cell)
    {
        var list = new List<PersonRow>();
        if (!Provinces.TryGetValue(cell, out var life)) return list;
        foreach (var p in life.People)
            list.Add(new PersonRow(p.Id, p.Name, p.AgeYears(p.Dead ? p.DeathTick : Now), ActivityText(p), p.Dead ? 0 : Mood(p), !p.Dead, p.Cell, p.Female, p.Stage(p.Dead ? p.DeathTick : Now)));
        return list;
    }

    public PersonCard? Card(int id)
    {
        var p = Get(id);
        if (p is null) return null;
        long at = p.Dead ? p.DeathTick : Now;
        // 보기만 해도 결과가 바뀌면 안 된다(결정론): 지금까지의 욕구를 잠깐 셈해 보여 주고 되돌린다
        var saved = Read(p); long savedAt = p.LastUpdate; bool savedCold = p.SleptCold;
        if (!p.Dead) Integrate(p, Now);
        try { return BuildCard(p, at); }
        finally { Write(p, saved); p.LastUpdate = savedAt; p.SleptCold = savedCold; }
    }

    private PersonCard BuildCard(Person p, long at)
    {
        var c = new PersonCard
        {
            Id = p.Id, Cell = p.Cell, Name = p.Name, Female = p.Female, SexName = p.Female ? "여자" : "남자", Age = p.AgeYears(at), Stage = p.Stage(at),
            Alive = !p.Dead, Asleep = p.Asleep, Look = p.Look, Activity = ActivityText(p), Why = WhyText(p),
            Mood = p.Dead ? 0 : Mood(p), BreakThreshold = BreakThreshold(p),
            Place = $"{PlanetNames.Of(_w.Planet.Biome[p.Cell])} 지방",
        };
        c.StageName = PeopleText.StageName(c.Stage, p.Female);
        c.MoodWord = PeopleText.MoodWord(c.Mood);
        for (int a = 0; a < PeopleData.AbilityCount; a++)
        {
            var notes = new List<string>();
            foreach (int t in p.Traits) { int v = Data.Traits[t].Abilities[a]; if (v != 0) notes.Add($"{Data.Traits[t].Name} {v:+0;−0}"); }
            if (c.Stage == LifeStage.Child) notes.Add("아이라서 −");
            if (c.Stage == LifeStage.Elder && a is PeopleData.Str or PeopleData.Dex) notes.Add("늙어서 −2");
            if (p.IllUntil > Now) notes.Add("아파서 −1");
            c.Abilities.Add((Data.Abilities[a].Name, Ability(p, a), $"타고남 {p.BaseAbility[a]}" + (notes.Count > 0 ? " · " + string.Join(" · ", notes) : "")));
        }
        for (int s = 0; s < PeopleData.SkillCount; s++)
        {
            int lv = p.SkillLevel(s);
            int prog = lv >= 10 ? 100 : (int)((p.SkillXp[s] - Person.XpFor(lv)) * 100L / Math.Max(1, Person.XpFor(lv + 1) - Person.XpFor(lv)));
            c.Skills.Add((Data.Skills[s].Name, lv, prog, _w.Config.Passion ? p.Passion[s] : 0));
        }
        foreach (int t in p.Traits) c.Traits.Add((Data.Traits[t].Name, Data.Traits[t].Effect));
        int felt = p.Dead ? 0 : FeltNow(p);
        c.Needs.Add(("배부름", p.Food / 1000, p.Food < UrgentFood ? "몹시 배고픔" : p.Food < 35_000 ? "배고픔" : ""));
        c.Needs.Add(("물", p.Water / 1000, p.Water < UrgentWater ? "몹시 목마름" : ""));
        c.Needs.Add(("잠", p.Rest / 1000, p.Asleep ? "자는 중" : p.Rest < 25_000 ? "피곤함" : ""));
        c.Needs.Add(("쉼", p.Joy / 1000, ""));
        c.Needs.Add(("어울림", p.Social / 1000, p.Social < 20_000 ? "외로움" : ""));
        if (!p.Dead) c.Needs.Add(("따뜻함", Math.Clamp((felt - 0) / 3, 0, 100), $"느끼는 기온 {felt / 10.0:0.0}°C (바깥 {Life(p.Cell).OutsideTemp10(Now) / 10.0:0.0}°C · {ProvinceLife.ShelterName(Life(p.Cell).Shelter[p.Tile(Now)])}{(p.Asleep && p.Huddle ? " · 곁에 사람" : "")})"));
        c.Body.Add($"건강 {p.Health / 1000}%" + (p.IllUntil > Now ? $" — 병 (하루에 −{p.IllDamage / 1000}%, {PeopleText.Date(p.IllUntil)}까지)" : ""));
        if (p.Malnutrition > 0) c.Body.Add($"영양실조 {p.Malnutrition / 1000}% (100% 가 되면 죽는다)");
        if (p.Dehydration > 0) c.Body.Add($"탈수 {p.Dehydration / 1000}%");
        if (p.Hypothermia > 0) c.Body.Add($"몸이 식음(저체온) {p.Hypothermia / 1000}%");
        if (p.Heatstroke > 0) c.Body.Add($"더위 먹음 {p.Heatstroke / 1000}%");
        if (p.Conceived >= 0) c.Body.Add($"아이를 가짐 (아빠 {NameOf(p.ChildFather)}, {PeopleText.Date(p.Conceived + PregnancyTicks)}쯤 태어남)");
        c.Body.Add($"걷는 빠르기: 1배속에서 초당 {60.0 / TicksPerTile(p):0.0}타일");
        if (p.Dead) c.Body.Add($"{PeopleText.Date(p.DeathTick)}에 죽음 — {PeopleText.CauseName(p.Cause)}{(p.Eaten ? " (시신이 먹혔다)" : "")}");
        if (!p.Dead)
        {
            _tmpThoughts.Clear();
            StateThoughts(p, _tmpThoughts);
            c.Thoughts.Add(("기본", MoodBase(p), p.HasTrait(T_optimist) ? "낙천적 +8" : p.HasTrait(T_pessimist) ? "비관적 −8" : ""));
            foreach (int t in _tmpThoughts) c.Thoughts.Add((Data.Thoughts[t].Name, Data.Thoughts[t].Mood, "지금 상태"));
            foreach (var (def, count) in ActiveThoughtCounts(p))
            {
                long until = 0; int other = -1;
                foreach (var t in p.Thoughts) if (t.Def == def && t.Until > until) { until = t.Until; other = t.Other; }
                string note = $"{(until - Now + GameTime.TicksPerHour - 1) / GameTime.TicksPerHour}시간 남음" + (other >= 0 ? $" · {NameOf(other)}" : "") + (count > 1 ? $" · ×{count}" : "");
                c.Thoughts.Add((Data.Thoughts[def].Name, Data.Thoughts[def].Mood * count, note));
            }
        }
        // 관계: 가족 먼저, 그다음 호감 · 원한이 큰 순
        var rels = new List<(int Id, string Name, string Kind, int Opinion, int Grudge, int Favor, bool Alive, int Order)>();
        void AddRel(int id, string kind, int order)
        {
            if (id < 0 || rels.Any(r => r.Id == id) || Get(id) is not { } q) return;
            var r = p.GetRelation(id);
            rels.Add((id, q.Name, kind, r.Opinion, r.Grudge, r.Favor, !q.Dead, order));
        }
        if (p.Partner >= 0) AddRel(p.Partner, "짝", 0);
        AddRel(p.Mother, "어머니", 1); AddRel(p.Father, "아버지", 1);
        foreach (int ch in p.Children) AddRel(ch, Get(ch)?.Female == true ? "딸" : "아들", 2);
        foreach (var q in Life(p.Cell).People)
            if (q.Id != p.Id && ((p.Mother >= 0 && q.Mother == p.Mother) || (p.Father >= 0 && q.Father == p.Father))) AddRel(q.Id, q.Female ? "자매" : "형제", 3);
        foreach (var r in p.Relations)
        {
            if (r.Opinion >= 30) AddRel(r.Other, "친구", 4);
            else if (r.Grudge >= 20 || r.Opinion <= -20) AddRel(r.Other, "원한", 5);
            else if (r.Talks > 0) AddRel(r.Other, "아는 사람", 6);
        }
        foreach (var r in rels.OrderBy(r => r.Order).ThenByDescending(r => Math.Abs(r.Opinion) + r.Grudge).ThenBy(r => r.Id).Take(30))
            c.Relations.Add((r.Id, r.Name, r.Kind, r.Opinion, r.Grudge, r.Favor, r.Alive));
        c.Carry = p.Carry.Count == 0 ? "없음" : string.Join(", ", p.Carry.Select(x => $"{ItemName(x.Item)} {x.Count}"));
        foreach (var b in p.Bio) c.Story.Add($"{PeopleText.Date(b.Tick)} — {BioLine(p, b)}");
        return c;
    }

    private string BioLine(Person p, BioEntry b) => b.Kind switch
    {
        BioKind.Born => b.Other >= 0 && b.Param >= 0 ? $"{Hangul.Gwa(NameOf(b.Other))} {NameOf(b.Param)} 사이에서 태어났다" : b.Other >= 0 ? $"{NameOf(b.Other)}에게서 태어났다" : "태어났다",
        BioKind.Spawned => $"{PlanetNames.Of(_w.Planet.Biome[p.Cell])} 지방에 뿌려졌다 (나이 {b.Param}). 아무것도 모른 채.",
        BioKind.Paired => $"{Hangul.Gwa(NameOf(b.Other))} 짝이 되었다",
        BioKind.ChildBorn => $"아이 {Hangul.I(NameOf(b.Other))} 태어났다",
        BioKind.Died => $"{PeopleText.CauseName((DeathCause)b.Param)}(으)로 죽었다 (나이 {p.AgeYears(b.Tick)})",
        BioKind.FamilyDied => $"가족 {Hangul.Eul(NameOf(b.Other))} 잃었다",
        BioKind.PartnerDied => $"짝 {Hangul.Eul(NameOf(b.Other))} 잃었다",
        BioKind.Break => b.Param switch { 1 => "무너져 화풀이를 했다", 2 => "무너져 혼자 떠났다", _ => "무너져 울며 헤맸다" },
        BioKind.CaveFound => "동굴을 찾아냈다",
        BioKind.Passion => $"{Data.Skills[b.Param].Name}에 열정이 생겼다",
        BioKind.Adult => "어른이 되었다",
        BioKind.Ill => "병에 걸렸다",
        BioKind.Argument => $"{Hangul.Gwa(NameOf(b.Other))} 거듭 다투어 원한이 깊어졌다",
        _ => "",
    };
}
