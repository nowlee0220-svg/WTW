using Wtw.Sim.Data;
using Wtw.Sim.Planet;
using Wtw.Sim.Time;

namespace Wtw.Sim.People;

/// <summary>인물 카드에 보일 것(시뮬 스레드가 만들어 화면에 건넨다 — 읽기 전용 사본).</summary>
public sealed class PersonCard
{
    public int Id, Cell, Age, Mood, BreakThreshold;
    public bool Unnamed;
    public string NameMeaning = "", Speech = "";
    public List<(string Word, string Meaning)> Vocabulary = new();
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
    public List<(int Id, string Name, string Kind, int Opinion, int Grudge, int Favor, bool Alive, int Understand)> Relations = new();
    public List<string> Story = new();
    public string Carry = "";
    /// <summary>(E4) 별명(뜻 · 까닭), 바람, 아는 법(무엇을 · 언제 · 왜 · 어떻게 · 누구에게서), 단서, 들고 있는 도구 · 그릇의 물.</summary>
    public string Nickname = "", NicknameMeaning = "", NicknameReason = "";
    public List<(string Name, int Strength)> Wishes = new();
    public List<string> Knowledge = new();
    public List<string> Clues = new();
    public List<(string Word, string Meaning, string Origin)> NewWords = new();
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
    private string NameOf(int id) => DisplayName(Get(id));

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
            Act.Eat when w => p.How switch
            {
                1 => $"기억해 둔 {food} 자리로 가는 중",
                2 => $"{Hangul.Eul(NameOf(p.HowArg))} 따라 {Hangul.Eul(food)} 따러 가는 중",
                3 => "먹을 것을 찾아 풀 · 나무가 짙은 쪽으로 가는 중",
                _ => $"{Hangul.Eul(food)} 따러 가는 중",
            },
            Act.Eat => $"{Hangul.Eul(food)} 따 먹는 중",
            Act.Drink when w => p.How switch
            {
                1 => "기억해 둔 물가로 가는 중",
                2 => $"{Hangul.Eul(NameOf(p.HowArg))} 따라 물가로 가는 중",
                3 => "물을 찾아 낮은 곳으로 가는 중",
                _ => "보이는 물가로 가는 중",
            },
            Act.Drink => "물 마시는 중",
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
            Act.Cross => CrossText(p),
            // (E4)
            Act.Experiment => w ? $"{PlanText(p)} 해 보러 불가로 가는 중" : $"{PlanText(p)} 해 보는 중",
            Act.Craft => w ? $"{Hangul.Eul(PlanResult(p))} 만들러 불가로 가는 중" : $"{Hangul.Eul(PlanResult(p))} 만드는 중 ({PlanText(p)})",
            Act.Material => w ? $"{Hangul.Eul(ItemName(p.PlanItem))} 얻으러 가는 중" : $"{Hangul.Eul(ItemName(p.PlanItem))} 얻는 중",
            Act.TakeFire => w ? (p.Target < 0 ? "불을 피울 자리로 가는 중" : "불씨를 얻으러 불이 있는 곳으로 가는 중") : "불씨를 얻는 중",
            Act.TendFire => w ? "불에 땔감을 넣으러 가는 중" : "불에 땔감을 넣는 중",
            Act.Mine => w ? "바위를 캐러 가는 중" : "바위를 캐는 중 (돌 도구로)",
            Act.Cook => w ? $"{Hangul.Eul(ItemName(p.PlanItem))} 익히러 불가로 가는 중" : $"{Hangul.Eul(ItemName(p.PlanItem))} 불에 익혀 먹는 중",
            Act.EatCarried => $"들고 있던 {Hangul.Eul(ItemName(p.PlanItem))} 먹는 중",
            Act.DrinkCarried => "그릇에 담아 온 물을 마시는 중",
            _ => "둘러보는 중",
        };
    }

    private string PlanText(Person p)
    {
        if (p.PlanAction < 0 || p.PlanInput < 0) return "이것저것";
        string s = Hangul.Eul(ItemName(p.PlanInput));
        if (p.PlanWith >= 0) s = $"{Hangul.Gwa(ItemName(p.PlanInput))} {Hangul.Eul(ItemName(p.PlanWith))}";
        if (p.PlanTool >= 0) s += $" {Hangul.Ro(ItemName(p.PlanTool))}";
        return $"{s} {Craft.ActionName(p.PlanAction)}";
    }

    private string PlanResult(Person p) => p.PlanRule >= 0 ? Craft.ItemName(Craft.Rules[p.PlanRule].Result) : "무언가";

    /// <summary>(E3b) 경계를 넘으러 가는 사람: 어디로 · 무엇 때문에(실제로 고른 까닭 그대로).</summary>
    private string CrossText(Person p)
    {
        var b = BorderOf(p.Cell, p.TargetB);
        string land = b is null ? "이웃 땅" : LandNameFor(b.Nb, p.LangId)?.Word is { } wd ? $"이웃 땅 '{wd}'" : "이웃 땅";
        if (!p.Walking) return $"경계를 넘는 중 — {Hangul.Ro(land)}";
        bool water = p.Why is Why.Thirsty or Why.VeryThirsty, food = p.Why is Why.Hungry or Why.VeryHungry or Why.Starving;
        if (water || food)
            return p.How switch
            {
                0 => water ? $"경계 너머 보이는 물가로 — {Hangul.Ro(land)} 가는 중" : $"{Hangul.Ro(land)} 먹을 것을 따러 가는 중",
                1 => water ? $"기억해 둔 물가가 있는 {Hangul.Ro(land)} 가는 중" : $"기억해 둔 열매 자리가 있는 {Hangul.Ro(land)} 가는 중",
                2 => $"{Hangul.Eul(NameOf(p.HowArg))} 따라 {Hangul.Ro(land)} 가는 중",
                _ => water ? $"물을 찾아 낮은 쪽, {Hangul.Ro(land)} 가는 중" : $"먹을 것을 찾아 푸른 쪽, {Hangul.Ro(land)} 가는 중",
            };
        return p.Why switch
        {
            Why.Scarce => $"먹을 것이 모자라 새 땅으로 옮겨 가는 중 — {Hangul.Ro(land)}",
            Why.SeekMate => $"짝을 찾아 {Hangul.Ro(land)} 가는 중",
            Why.Explore => $"{Hangul.Eul(land)} 둘러보러 가는 중",
            Why.GoBack => $"살던 땅으로 돌아가는 중 — {land}",
            Why.FollowFamily => $"가족을 따라 {Hangul.Ro(land)} 옮겨 가는 중",
            Why.Break => $"혼자 떠나 {Hangul.Ro(land)} 가는 중",
            _ => $"{Hangul.Ro(land)} 가는 중",
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
            Why.NoFood => "가 보니 다 따 가고 없어 다른 곳을 찾는다",
            Why.NoWater => "마실 물(민물)이 가까이 없어 찾아다닌다 — 바닷물은 못 마신다",
            Why.Scarce => "며칠째 둘레에도 기억에도 딸 것이 없어서 — 이 땅의 먹을 것이 모자라다(사람이 많으면 빨리 줄어든다)",
            Why.SeekMate => "짝이 될 만한 사람이 이 땅에 없어서",
            Why.Explore => "궁금해서 — 경계 너머 땅이 보고 싶다(호기심)",
            Why.GoBack => "이 땅에 혼자 남아 쓸쓸해서 — 사람이 있던 땅으로",
            Why.FollowFamily => p.WhyArg > 0 && Get(p.WhyArg) is { } lead ? $"{Hangul.I(DisplayName(lead))} 떠나서 가족이 함께 간다" : "가족이 이웃 땅에 있어서",
            // (E4)
            Why.WantWarmth => $"추워서 — 따뜻한 것이 있었으면 (바람 {WishStrength(p, "warmth")}): 잘 타는 것 · 단단한 돌을 부딪쳐 본다",
            Why.WantSharp => $"날카로운 것이 있었으면 — 나무를 베지 못해서 (바람 {WishStrength(p, "sharpness")}): 잘 쪼개지고 날이 서는 돌을 쳐 본다",
            Why.WantHardTool => $"단단한 도구가 있었으면 — 딱딱한 것을 못 깨서 (바람 {WishStrength(p, "hard_tool")})",
            Why.WantContainer => $"담을 그릇이 있었으면 — 물가가 멀어서 (바람 {WishStrength(p, "container")}): 휘는 것을 엮고 진흙을 빚어 본다",
            Why.WantCooking => $"익혀 먹었으면 — 날것에 배탈이 나서 (바람 {WishStrength(p, "cooking")})",
            Why.WantCarry => $"더 많이 나를 수 있었으면 (바람 {WishStrength(p, "carry")})",
            Why.WantShiny => $"반짝이는 돌이 궁금해서 (바람 {WishStrength(p, "shiny")}): 세찬 불에 넣어 본다",
            Why.KnowHow => $"{Hangul.I(WishName(arg))} 아쉬운데 만드는 법을 알아서",
            Why.Clue => "전에 거의 될 듯했던 단서를 따라서 — " + (p.Clues.Count > 0 ? ClueText(p.Clues[^1]) : ""),
            Why.Fuel => "불이 꺼져 가서 — 땔감을 넣어 지킨다",
            Why.TakeFire => "불이 보여서 — 불씨를 얻어 오면 추위를 피할 수 있다",
            Why.FireNear => p.Act == Act.Sleep ? "불가에서 자려고 — 밤에 덜 춥고 어둠이 덜 무섭다" : "불씨를 들고 있어서 — 자는 곳 가까이에 피운다",
            Why.Material => "만들 재료가 모자라서",
            Why.MineOre => "반짝이는 광맥이 보여서 — 돌 도구로 캔다",
            Why.MineStone => "돌이 더 있었으면 — 돌 도구로 바위를 캔다",
            Why.CookFood => "익히면 더 배가 차고 배탈이 안 나서",
            _ => "",
        } + HowText(p);
    }

    public string ClueText(Clue c) => $"{Craft.Rules[c.Rule].Name}: {(Craft.Rules[c.Rule].Partial.TryGetValue(c.Key, out var pd) ? pd.Text + " — " + pd.Hint : Craft.GenericHints.GetValueOrDefault(c.Key, c.Key))} ({PeopleText.Date(c.Tick)}, {ItemName(c.Input)}으로)";

    public string KnowledgeText(Knowledge k)
    {
        var r = Craft.Rules[k.Rule];
        string how = k.With >= 0 ? $"{Hangul.Gwa(ItemName(k.Input))} {Hangul.Eul(ItemName(k.With))} {Craft.ActionName(r.Action)}" : $"{Hangul.Eul(ItemName(k.Input))} {Craft.ActionName(r.Action)}";
        if (r.Heat > 0) how += $" ({Craft.HeatName(r.Heat)}에서)";
        string from = k.LearnedFrom < 0 ? $"{Hangul.I(WishName(k.Wish))} 아쉬워 스스로 알아냄" : $"{Hangul.I(NameOf(k.LearnedFrom))} 하는 것을 곁에서 봄";
        return $"{Craft.ItemName(r.Result)} — {how} · {PeopleText.Date(k.Tick)} · {from}";
    }

    /// <summary>물 · 먹을 것을 어떻게 찾아가는지 덧붙임(실제로 하는 일과 같게).</summary>
    private string HowText(Person p)
    {
        if (p.Act == Act.Cross && p.Walking && p.Why is Why.Thirsty or Why.VeryThirsty or Why.Hungry or Why.VeryHungry or Why.Starving)
        {
            bool wt = p.Why is Why.Thirsty or Why.VeryThirsty;
            return p.How switch
            {
                0 => wt ? " — 이 땅엔 보이는 물가가 없고 경계 너머에 보인다" : "",
                1 => wt ? " — 이 땅엔 아는 물가가 없어 전에 본 이웃 땅의 물가로" : " — 이 땅엔 아는 열매 자리가 없어 전에 본 이웃 땅으로",
                2 => " — 아는 곳이 없어 남을 따라 경계를 넘는다",
                _ => wt ? $" — 이 땅에서 {p.SearchMiss}번 찾아다녀도 물이 없어 이웃 땅으로(물은 낮은 곳에 고인다)" : $" — 이 땅에서 {p.SearchMiss}번 찾아다녀도 딸 것이 없어 이웃 땅으로",
            };
        }
        if (p.Act is not (Act.Eat or Act.Drink) || !p.Walking) return "";
        bool water = p.Act == Act.Drink;
        return p.How switch
        {
            1 => water ? " — 아는 물가(전에 본 곳)로 간다" : " — 전에 열매를 본 자리로 간다",
            2 => water ? $" — 아는 물가가 없어 {Hangul.Eul(NameOf(p.HowArg))} 따라간다" : $" — 아는 곳이 없어 {Hangul.Eul(NameOf(p.HowArg))} 따라간다",
            3 => water ? " — 아는 물가가 없어 찾아다닌다(물은 낮은 곳에 고인다)" : " — 아는 열매 자리가 없어 초록이 짙은 쪽을 찾아다닌다",
            _ => water ? " — 물가가 보인다" : "",
        };
    }

    public List<PersonRow> Rows(int cell)
    {
        var list = new List<PersonRow>();
        if (!Provinces.TryGetValue(cell, out var life)) return list;
        foreach (var p in life.People)
            list.Add(new PersonRow(p.Id, DisplayName(p), p.AgeYears(p.Dead ? p.DeathTick : Now), ActivityText(p), p.Dead ? 0 : Mood(p), !p.Dead, p.Cell, p.Female, p.Stage(p.Dead ? p.DeathTick : Now)));
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
            Id = p.Id, Cell = p.Cell, Name = DisplayName(p), NameMeaning = NameMeaning(p), Unnamed = p.Name.Length == 0, Female = p.Female, SexName = p.Female ? "여자" : "남자", Age = p.AgeYears(at), Stage = p.Stage(at),
            Alive = !p.Dead, Asleep = p.Asleep, Look = p.Look, Activity = ActivityText(p), Why = WhyText(p),
            Mood = p.Dead ? 0 : Mood(p), BreakThreshold = BreakThreshold(p),
            Place = $"{LandLabelOr(p.Cell, "이름 없는 땅")} · {PlanetNames.Of(_w.Planet.Biome[p.Cell])} 지방",
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
            rels.Add((id, DisplayName(q), kind, r.Opinion, r.Grudge, r.Favor, !q.Dead, order));
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
            c.Relations.Add((r.Id, r.Name, r.Kind, r.Opinion, r.Grudge, r.Favor, r.Alive, Get(r.Id) is { } rq ? Understanding(p, rq) : 0));
        // 말: 무리의 말 + 이 사람의 말씨. 아는 낱말(그 말로, 뜻과 함께)
        if (Languages.TryGetValue(p.LangId, out var lang))
        {
            int sp = Speakers(p.LangId);
            int acc = p.Accent.Sum(a => Math.Abs(a));
            c.Speech = $"{LanguageLabel(p.LangId)} — 지금 이 말을 쓰는 사람 {sp}명 · 아는 낱말 {lang.Known.Count}개 · 말씨 차이 {acc}";
            if (lang.Parent >= 0) c.Speech += $" · 갈라져 나온 말(말 {lang.Parent})과 알아듣기 {LanguageUnderstanding(lang.Id, lang.Parent)}%";
            foreach (int k in lang.Known) c.Vocabulary.Add((WordOf(p, k), Lang.Concepts[k].Meaning));
        }
        c.Carry = p.Carry.Count == 0 ? "없음" : string.Join(", ", p.Carry.Select(x => $"{ItemName(x.Item)} {x.Count}")) + (p.PotWater > 0 ? $" · 그릇의 물 {p.PotWater}모금" : "") + $" (무게 {CarryWeight(p)}/{CarryCapOf(p)})";
        // (E4) 별명 · 바람 · 아는 법 · 단서 · 새 낱말
        if (p.Nickname.Length > 0) { c.Nickname = p.Nickname; c.NicknameMeaning = string.Join(" ", p.NicknameConcepts.Where(x => (uint)x < Lang.Concepts.Count).Select(x => Lang.Concepts[x].Meaning)); c.NicknameReason = p.NicknameReason; }
        for (int w = 0; w < Craft.Wishes.Count; w++) if (p.Wish[w] > 0) c.Wishes.Add((Craft.Wishes[w].Name, p.Wish[w]));
        foreach (var k in p.Known) c.Knowledge.Add(KnowledgeText(k));
        foreach (var cl in p.Clues.AsEnumerable().Reverse()) c.Clues.Add(ClueText(cl));
        if (Languages.TryGetValue(p.LangId, out var lang2))
            foreach (int k in lang2.Known) if (!Lang.Concepts[k].Base) c.NewWords.Add((WordOf(p, k), Lang.Concepts[k].Meaning, WordOrigin(lang2, k)));
        foreach (var b in p.Bio) c.Story.Add($"{PeopleText.Date(b.Tick)} — {BioLine(p, b)}");
        return c;
    }

    private string BioLine(Person p, BioEntry b) => b.Kind switch
    {
        BioKind.Born => b.Other >= 0 && b.Param >= 0 ? $"{Hangul.Gwa(NameOf(b.Other))} {NameOf(b.Param)} 사이에서 태어났다" : b.Other >= 0 ? $"{NameOf(b.Other)}에게서 태어났다" : "태어났다",
        BioKind.Spawned => $"{PlanetNames.Of(_w.Planet.Biome[p.Cell])} 지방에 뿌려졌다 (나이 {b.Param}). 아무것도 모른 채.",
        BioKind.Paired => $"{Hangul.Gwa(NameOf(b.Other))} 짝이 되었다",
        BioKind.ChildBorn => $"아이 {Hangul.I(NameOf(b.Other))} 태어났다",
        BioKind.Died => $"{Hangul.Ro(PeopleText.CauseName((DeathCause)b.Param))} 죽었다 (나이 {p.AgeYears(b.Tick)})",
        BioKind.FamilyDied => $"가족 {Hangul.Eul(NameOf(b.Other))} 잃었다",
        BioKind.PartnerDied => $"짝 {Hangul.Eul(NameOf(b.Other))} 잃었다",
        BioKind.Break => b.Param switch { 1 => "무너져 화풀이를 했다", 2 => "무너져 혼자 떠났다", _ => "무너져 울며 헤맸다" },
        BioKind.CaveFound => "동굴을 찾아냈다",
        BioKind.Passion => $"{Data.Skills[b.Param].Name}에 열정이 생겼다",
        BioKind.Adult => "어른이 되었다",
        BioKind.Ill => "병에 걸렸다",
        BioKind.Argument => $"{Hangul.Gwa(NameOf(b.Other))} 거듭 다투어 원한이 깊어졌다",
        BioKind.Named => b.Param >= 0 ? $"{NameOf(b.Other)}의 말로 {Hangul.J("'" + p.Name + "'", "이라는", "라는")} 이름을 얻었다 — {NameOf(b.Param)}의 이름을 물려받음" : $"{Hangul.I(NameOf(b.Other))} 자기 말로 {Hangul.J("'" + p.Name + "'", "이라고", "라고")} 부르기 시작했다 (뜻: {string.Join(" ", p.NameConcepts.Where(x => (uint)x < Lang.Concepts.Count).Select(x => Lang.Concepts[x].Meaning))})",
        BioKind.LearnedSpeech => $"{NameOf(b.Other)}의 말을 배워 쓰기 시작했다",
        BioKind.FoundWater => "물가를 찾아냈다",
        BioKind.Moved => MovedLine(p, b),
        // (E4)
        BioKind.Discovered => $"{Hangul.I(WishName(b.Param))} 아쉬워 이것저것 해 보다 {Craft.ItemName(Craft.Rules[b.Other].Result)} 만드는 법을 알아냈다 ({Craft.Rules[b.Other].Name})",
        BioKind.LearnedWatching => $"{Hangul.I(NameOf(b.Other))} {Craft.ItemName(Craft.Rules[b.Param].Result)} 만드는 것을 곁에서 보고 따라 할 수 있게 되었다",
        BioKind.PartialSuccess => $"{Hangul.Eul(ItemName(b.Param))} 다뤄 보다 거의 될 듯했다 — {Craft.Rules[b.Other].Name}",
        BioKind.MadeThing => $"{Hangul.Eul(ItemName(b.Param))} 만들었다",
        BioKind.LitFire => "불을 피웠다",
        BioKind.FireDied => "비에 불이 꺼졌다",
        BioKind.Mined => b.Other >= 0 ? $"돌 도구로 바위를 캐 {Hangul.Eul(RockCatalog.Default.Ores[b.Other].Name)} 드러냈다" : "돌 도구로 바위를 캤다",
        BioKind.Nicknamed => $"{Hangul.Rago($"'{p.Nickname}'")} 불리게 되었다 (뜻: {string.Join(" ", p.NicknameConcepts.Where(x => (uint)x < Lang.Concepts.Count).Select(x => Lang.Concepts[x].Meaning))} — {p.NicknameReason})",
        BioKind.NewWord => (uint)b.Param < Lang.Concepts.Count ? $"무리의 말에 {Hangul.Eul($"'{Lang.Concepts[b.Param].Meaning}'")} 뜻하는 낱말 {Hangul.I($"'{WordOf(p, b.Param)}'")} 생겼다" : "무리의 말에 새 낱말이 생겼다",
        BioKind.BorrowedWord => (uint)b.Param < Lang.Concepts.Count ? $"{NameOf(b.Other)}의 말에서 {Hangul.Eul($"'{Lang.Concepts[b.Param].Meaning}'")} 뜻하는 낱말 {Hangul.Eul($"'{WordOf(p, b.Param)}'")} 빌려 왔다" : "다른 말에서 낱말을 빌려 왔다",
        BioKind.SawFire => b.Other >= 0 ? $"{NameOf(b.Other)}의 불에서 불씨를 얻었다" : "자연의 불을 보고 불씨를 얻었다",
        BioKind.BadStomach => $"날 {Hangul.Eul(ItemName(b.Param))} 먹고 배탈이 났다",
        BioKind.NamedLand => LandNames.TryGetValue(b.Param, out var ln) && ln.Find(n => n.Namer == p.Id) is { } mine
            ? $"머물던 땅을 {Hangul.Rago($"'{mine.Word}'")} 부르기 시작했다 (뜻: {LandMeaning(mine)} — {mine.Reason})" : "머물던 땅에 이름을 붙였다",
        _ => "",
    };

    /// <summary>(E3b) "물을 찾아 이웃 땅 '타레'로 옮겨 갔다" — Other = 간 곳, Param = 까닭(Why) | 떠난 곳 &lt;&lt; 8.</summary>
    private string MovedLine(Person p, BioEntry b)
    {
        var why = (Why)(b.Param & 0xFF);
        string phrase = why switch
        {
            Why.Scarce => "먹을 것이 모자라",
            Why.SeekMate => "짝을 찾아",
            Why.Explore => "궁금해서",
            Why.GoBack => "혼자 남아 쓸쓸해서",
            Why.FollowFamily => "가족을 따라",
            Why.Break => "무너져 혼자",
            Why.Thirsty or Why.VeryThirsty => "물을 찾아",
            Why.Hungry or Why.VeryHungry or Why.Starving => "먹을 것을 찾아",
            _ => "",
        };
        string land = LandNameFor(b.Other, p.LangId)?.Word is { } w ? $"이웃 땅 '{w}'" : "이웃 땅";
        return (phrase.Length > 0 ? phrase + " " : "") + $"{Hangul.Ro(land)} 옮겨 갔다";
    }
}
