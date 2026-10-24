using Wtw.Sim.Core;
using Wtw.Sim.Provinces;
using Wtw.Sim.Time;

namespace Wtw.Sim.People;

public sealed partial class PeopleSim
{
    // ───────── 뿌리기 (플레이어의 권능) ─────────

    /// <summary>그 지방 타일에 사람을 뿌릴 수 있나(걸을 수 있는 땅).</summary>
    public bool CanSpawn(int cell, int x, int y)
    {
        if (cell < 0 || cell >= _w.Planet.CellCount || !_w.Planet.IsLand(cell)) return false;
        if ((uint)x >= S || (uint)y >= S) return false;
        return Life(cell).Walk[y * S + x];
    }

    /// <summary>한 명(나이 15~40). 만든 사람(못 뿌리면 null).</summary>
    public Person? SpawnOne(int cell, int x, int y)
    {
        if (!CanSpawn(cell, x, y)) return null;
        var rng = Rng(cell);
        var p = Make(cell, y * S + x, rng.NextInt(15, 41), rng.NextInt(2) == 0 ? Sex.Male : Sex.Female, null, null, rng, lang: NewLanguage());
        Notify(WorldEventKind.Spawn, p, -1, $"{Hangul.I(DisplayName(p))} 뿌려졌습니다 (아직 이름이 없다)");
        return p;
    }

    /// <summary>가족: 짝(남 · 여) + 아이 0~2. 만든 사람들.</summary>
    public List<Person> SpawnFamily(int cell, int x, int y)
    {
        var list = new List<Person>();
        if (!CanSpawn(cell, x, y)) return list;
        var rng = Rng(cell);
        int tile = y * S + x;
        int ageM = rng.NextInt(18, 36);
        int ageF = Math.Clamp(ageM + rng.NextInt(-5, 4), 17, 40);
        var lang = NewLanguage(); // 함께 살던 가족은 한 말을 쓴다
        var man = Make(cell, tile, ageM, Sex.Male, null, null, rng, lang: lang);
        var woman = Make(cell, Beside(man, tile), ageF, Sex.Female, null, null, rng, lang: lang);
        man.Partner = woman.Id; woman.Partner = man.Id;
        var rm = man.GetRelation(woman.Id); rm.Opinion = 50 + rng.NextInt(25); man.SetRelation(rm);
        var rw = woman.GetRelation(man.Id); rw.Opinion = 50 + rng.NextInt(25); woman.SetRelation(rw);
        AddBio(man, BioKind.Paired, woman.Id); AddBio(woman, BioKind.Paired, man.Id);
        list.Add(man); list.Add(woman);
        int kids = rng.NextInt(3);
        for (int k = 0; k < kids; k++)
        {
            int maxAge = Math.Min(12, ageF - 16);
            if (maxAge < 0) break;
            int age = rng.NextInt(maxAge + 1);
            var c = Make(cell, Beside(woman, tile, 2), age, rng.NextInt(2) == 0 ? Sex.Male : Sex.Female, woman, man, rng);
            list.Add(c);
            foreach (var par in new[] { man, woman })
            {
                var r1 = par.GetRelation(c.Id); r1.Opinion = 60 + rng.NextInt(30); par.SetRelation(r1);
                var r2 = c.GetRelation(par.Id); r2.Opinion = 60 + rng.NextInt(30); c.SetRelation(r2);
            }
        }
        // 함께 지내던 가족은 서로 부르던 이름이 있다(그 말로, 생김새에서)
        GiveName(man, woman, LookConcepts(man, rng), alternative: () => LookConcepts(man, rng));
        GiveName(woman, man, LookConcepts(woman, rng), alternative: () => LookConcepts(woman, rng));
        foreach (var c in list.Skip(2)) if (c.Name.Length == 0) NameNewborn(c, woman, rng);
        Notify(WorldEventKind.Spawn, man, woman.Id, $"{Hangul.Gwa(DisplayName(man))} {DisplayName(woman)}의 가족 {list.Count}명이 뿌려졌습니다");
        return list;
    }

    /// <summary>사람 한 명을 만든다. mother/father 가 있으면 닮게(몸 · 능력 · 특성 조금).</summary>
    internal Person Make(int cell, int tile, int ageYears, Sex sex, Person? mother, Person? father, Pcg32 rng, bool newborn = false, Language? lang = null)
    {
        var life = Life(cell);
        var p = new Person
        {
            Id = _w.Entities.Allocate(), Sex = sex, Cell = cell,
            BirthTick = newborn ? Now : Now - ageYears * GameTime.TicksPerYear - rng.NextInt((int)GameTime.TicksPerYear),
            LastUpdate = Now, LastTalkTick = Now,
            BedShift = rng.NextInt(-2_500, 3_750), WakeShift = rng.NextInt(-1_250, 1_250),
        };
        // 말: 부모(엄마)의 말과 말씨를 이어받는다. 부모가 없으면 받은 말(뿌린 사람은 새 말).
        if (mother is not null && mother.LangId >= 0)
        {
            p.LangId = mother.LangId;
            for (int i = 0; i < p.Accent.Length; i++)
                p.Accent[i] = father is not null && father.LangId == mother.LangId ? (sbyte)((mother.Accent[i] + father.Accent[i]) / 2) : mother.Accent[i];
        }
        else p.LangId = (lang ?? NewLanguage()).Id;
        var dad = father ?? mother;
        p.Look = mother is not null ? Inherit(mother.Look, dad!.Look, sex, rng) : RandomLook(sex, rng);
        for (int a = 0; a < PeopleData.AbilityCount; a++)
        {
            int roll = 2 + rng.NextInt(4) + rng.NextInt(4) + rng.NextInt(3);
            p.BaseAbility[a] = mother is not null
                ? Math.Clamp((mother.BaseAbility[a] + dad!.BaseAbility[a] + roll + rng.NextInt(-1, 2)) / 3, 1, 10)
                : Math.Clamp(roll, 1, 10);
        }
        PickTraits(p, mother, father, rng);
        if (!newborn)
        {
            p.Food = 60_000 + rng.NextInt(40_001); p.Water = 60_000 + rng.NextInt(40_001); p.Rest = 60_000 + rng.NextInt(40_001);
            p.Joy = 50_000 + rng.NextInt(50_001); p.Social = 50_000 + rng.NextInt(50_001);
        }
        if (mother is not null) { p.Mother = mother.Id; mother.Children.Add(p.Id); }
        if (father is not null) { p.Father = father.Id; father.Children.Add(p.Id); }
        int tx = tile % S, ty = tile / S;
        p.NewMotion().SetStill(Now, tx * 1000 + 500, ty * 1000 + 500);
        _all.Add(p); _byId.Add(p.Id, p); life.People.Add(p);
        AliveCount++;
        if (newborn) AddBio(p, BioKind.Born, mother?.Id ?? -1, father?.Id ?? -1);
        else AddBio(p, BioKind.Spawned, -1, ageYears);
        if (p.Stage(Now) == LifeStage.Baby && mother is not null)
        {
            p.Carrier = mother.Id;
            SetWhy(p, Act.Carried, Why.Carried, mother.Id);
        }
        else
        {
            SetWhy(p, Act.Idle, newborn ? Why.None : Why.Spawned);
            Schedule(p, Now + 1 + rng.NextInt(600));
        }
        return p;
    }

    private static Appearance RandomLook(Sex sex, Pcg32 rng) => new(
        (byte)rng.NextInt(256), (byte)rng.NextInt(256), (byte)rng.NextInt(8), (byte)rng.NextInt(256), (byte)rng.NextInt(256),
        (byte)rng.NextInt(4), (byte)rng.NextInt(4), sex == Sex.Male ? (byte)rng.NextInt(4) : (byte)0);

    private static byte Mix(byte a, byte b, Pcg32 rng) => (byte)Math.Clamp((a + b) / 2 + rng.NextInt(-28, 29), 0, 255);
    private static byte Pick(byte a, byte b, int range, Pcg32 rng) => rng.NextInt(100) < 15 ? (byte)rng.NextInt(range) : rng.NextInt(2) == 0 ? a : b;

    /// <summary>부모를 닮는다: 피부 · 머리색 · 체형 · 키는 부모 사이 + 조금, 머리 모양 · 얼굴 · 눈은 둘 중 하나(가끔 새로).</summary>
    public static Appearance Inherit(Appearance m, Appearance f, Sex sex, Pcg32 rng) => new(
        Mix(m.Skin, f.Skin, rng), Mix(m.Hair, f.Hair, rng), Pick(m.HairStyle, f.HairStyle, 8, rng), Mix(m.Build, f.Build, rng), Mix(m.Height, f.Height, rng),
        Pick(m.Face, f.Face, 4, rng), Pick(m.Eyes, f.Eyes, 4, rng), sex == Sex.Male ? (byte)rng.NextInt(4) : (byte)0);

    private void PickTraits(Person p, Person? mother, Person? father, Pcg32 rng)
    {
        int want = rng.NextInt(100) switch { < 40 => 1, < 80 => 2, _ => 3 };
        bool Ok(int t) => !p.Traits.Contains(t) && p.Traits.All(o => Data.Traits[o].Id != Data.Traits[t].Opposite && Data.Traits[t].Id != Data.Traits[o].Opposite);
        foreach (var par in new[] { mother, father })
            if (par is not null)
                foreach (int t in par.Traits)
                    if (p.Traits.Count < want && rng.NextInt(100) < 25 && Ok(t)) p.Traits.Add(t);
        for (int tries = 0; p.Traits.Count < want && tries < 50; tries++)
        {
            int t = rng.NextInt(Data.Traits.Count);
            if (Ok(t)) p.Traits.Add(t);
        }
        p.Traits.Sort();
    }

    // ───────── 하루 경계 ─────────

    /// <summary>하루에 한 번(세계 하루 경계): 나이 · 병 · 늙음 · 아기 · 출산 · 원한이 풀림 · 솜씨가 무뎌짐.</summary>
    internal void OnDay()
    {
        _forgotToday.Clear();
        for (int i = 0; i < _all.Count; i++)
        {
            var p = _all[i];
            if (p.Dead) continue;
            var rng = Rng(p.Cell);
            Integrate(p, Now);
            if (CheckDeath(p)) continue;
            // (T1) 안긴 아기가 젖을 먹었으면 어머니(안은 사람)가 그 몫을 더 쓴다
            // (E6b) 젖이 줄면 그만큼만 — 배부름이 모자라면 어머니 몸에 쌓인 것에서 나온다
            if (p.Carrier >= 0 && Get(p.Carrier) is { Dead: false } nurse && nurse.Cell == p.Cell)
            {
                Integrate(nurse, Now);
                int milk = MilkOf(nurse);
                if (milk > 0)
                {
                    int need = NursingFood * milk / 100, fromFood = Math.Min(need, nurse.Food);
                    nurse.Food -= fromFood;
                    if (need > fromFood) { nurse.Body = Math.Max(0, nurse.Body - (int)((long)(need - fromFood) * 1_000_000 / BodyScale(nurse.Stage(Now)))); St.BodyDrawn += need - fromFood; }
                    nurse.Malnutrition = MalOf(nurse.Body);
                    CheckDeath(nurse);
                }
            }
            long age = p.AgeTicks(Now);
            // 아기가 자라 혼자 걷는다
            if (p.Carrier >= 0 && age >= 3 * GameTime.TicksPerYear)
            {
                var c = Get(p.Carrier);
                int x = p.Current.EndX, y = p.Current.EndY;
                if (c is not null) c.Position(Now, out x, out y);
                p.NewMotion().SetStill(Now, x, y);
                p.Carrier = -1;
                SetWhy(p, Act.Idle, Why.None);
                Schedule(p, Now + 1 + rng.NextInt(600));
            }
            if (age >= 14 * GameTime.TicksPerYear && age < 14 * GameTime.TicksPerYear + GameTime.TicksPerDay) AddBio(p, BioKind.Adult);
            // 병
            if (p.IllUntil > 0 && p.IllUntil <= Now) p.IllUntil = 0;
            else if (p.IllUntil == 0)
            {
                int ch = 12; // 10,000 분의 (하루) — 1년에 약 5%
                if (p.HasTrait(T_frail)) ch *= 2;
                if (p.HasTrait(T_tough)) ch /= 2;
                // (E6b) 야위면 병에 약하다(조금 1.5배 · 야윔 2배 · 몹시 3배)
                int thin = ThinStage(p);
                if (thin == 1) ch = ch * 3 / 2; else if (thin == 2) ch *= 2; else if (thin == 3) ch *= 3;
                if (p.Hypothermia > 30_000) ch *= 2;
                var st = p.Stage(Now);
                if (st <= LifeStage.Child) ch = ch * 3 / 2; else if (st == LifeStage.Elder) ch *= 2;
                if (rng.NextInt(10_000) < ch)
                {
                    p.IllUntil = Now + (3 + rng.NextInt(8)) * GameTime.TicksPerDay;
                    p.IllDamage = 3_000 + rng.NextInt(6_000) + (st == LifeStage.Elder ? 4_000 : 0) + (p.HasTrait(T_frail) ? 3_000 : 0) - (p.HasTrait(T_tough) ? 2_000 : 0);
                    St.Illnesses++;
                    AddBio(p, BioKind.Ill);
                }
            }
            // 늙음
            int years = p.AgeYears(Now);
            int oldCh = years < 45 ? 0 : years < 60 ? 5 : years < 70 ? 20 : years < 80 ? 70 : 200; // 10,000 분의
            if (oldCh > 0 && rng.NextInt(10_000) < oldCh) { Kill(p, DeathCause.OldAge); continue; }
            // 출산
            if (p.Conceived >= 0 && Now - p.Conceived >= PregnancyTicks) GiveBirth(p, rng);
            else BodyMiscarry(p, rng); // (E6b) 몹시 야위면 배 속의 아기를 잃기도 한다
            St.ThinDays[ThinStage(p)]++; // (E6b 측정)
            // 원한은 천천히 풀린다 · 솜씨는 안 쓰면 아주 천천히 무뎌진다
            for (int k = 0; k < p.Relations.Count; k++)
            {
                var r = p.Relations[k];
                if (r.Grudge <= 0) continue;
                int dec = p.HasTrait(T_kind) ? 3 : p.HasTrait(T_abrasive) ? ((Now / GameTime.TicksPerDay) % 2 == 0 ? 1 : 0) : 1;
                r.Grudge = Math.Max(0, r.Grudge - dec);
                p.Relations[k] = r;
            }
            int g = PeopleData.Gathering;
            if (Now - p.LastGather > 3 * GameTime.TicksPerDay && p.SkillXp[g] > 0) p.SkillXp[g] = Math.Max(0, p.SkillXp[g] - p.SkillLevel(g) * 3);
            PurgeThoughts(p);
            BodyDaily(p, rng);
            CraftDaily(p);
            FireHabitDaily(p); // (E6)
            MoveIntent(p, rng);
            // 안아 줄 사람이 없는 아기
            if (p.Carrier >= 0 && Get(p.Carrier) is not { Dead: false }) ReassignCarrier(p);
        }
        if (Now / GameTime.TicksPerDay % GameTime.DaysPerYear == 0) LanguageYear();
        NameLands();
        foreach (var (cell, life) in Provinces.ToList()) { FiresDaily(cell, life); AnimalsDaily(life); RotGround(life, Rng(cell)); FarmDaily(cell, life); BuildDaily(life); }
        NamedDaily(); // (E6) 사연 있는 물건이 아직 있나
        CheckLostDiscoveries();
        GroupKnowledgeDaily();
        SleepEmptyProvinces();
    }

    // ───────── 아기 ─────────

    private void TryConceive(Person woman, Person man)
    {
        if (woman.Conceived >= 0 || man.Dead || man.Cell != woman.Cell) return;
        int wa = woman.AgeYears(Now), ma = man.AgeYears(Now);
        if (wa < 16 || wa > 45 || ma < 16 || woman.IllUntil > Now) return;
        if (CapUsed(woman) >= _w.Config.PeopleCap) return; // 상한(안전장치)
        int ch = 50; // 1,000 분의 (한 밤)
        foreach (int cid in woman.Children)
            if (Get(cid) is { } c && c.Cell == woman.Cell && c.AgeTicks(Now) < GameTime.TicksPerYear * 3 / 2 && !c.Dead) { ch /= 3; break; } // (S1) 젖먹이는 늘 같은 지방에 안겨 있다
        int roll = Rng(woman.Cell).NextInt(1_000);
        if (roll >= ch) return;
        // (E6b) 야위면 아이가 덜 생긴다(배란이 줄거나 멎는다)
        if (roll >= ch * Fertility(woman.Body) / 1000) { St.ConceiveHungry++; return; }
        woman.Conceived = Now;
        if (InProvince) L.PregnantDelta++; else _pregnant++;
        woman.ChildFather = man.Id;
        St.Conceptions++;
    }

    /// <summary>아이 밴 산 사람 수(S1: 셈을 따로 둔다 — 날마다 모든 사람을 훑지 않게).</summary>
    private int Pregnant => _pregnant;
    private int _pregnant;

    /// <summary>
    /// (S1) 상한 셈: 산 사람 + 아이 밴 사람. 지방 계산 중에는 다른 지방을 볼 수 없으므로 지난 맞추는 시각의 값(CapBase)에
    /// 그 뒤 이 지방에서 생긴 아이만 더한다(CPU 수와 상관없이 같다 — 여러 지방에서 같은 6분 안에 생긴 아이는 상한을 조금 넘을 수 있다).
    /// </summary>
    private int CapUsed(Person woman)
    {
        if (!InProvince) return AliveCount + Pregnant;
        long since = (Now - 1) / CrossInterval * CrossInterval;
        int n = CapBase;
        foreach (var q in Life(woman.Cell).People) if (!q.Dead && q.Conceived > since) n++;
        return n;
    }

    private void GiveBirth(Person mother, Pcg32 rng)
    {
        var father = Get(mother.ChildFather);
        mother.Conceived = -1;
        _pregnant--;
        mother.Position(Now, out int x, out int y);
        var baby = Make(mother.Cell, (y / 1000) * S + x / 1000, 0, rng.NextInt(2) == 0 ? Sex.Male : Sex.Female, mother, father, rng, newborn: true);
        // 부모가 그 말로 뜻이 있는 이름을 짓는다(이름이 있는 쪽이 — 둘 다 없으면 엄마)
        var namer = mother.Name.Length == 0 && father is { Dead: false, Name.Length: > 0 } && father.LangId == mother.LangId ? father : mother;
        NameNewborn(baby, namer, rng);
        foreach (var par in new[] { mother, father })
        {
            if (par is null || par.Dead) continue;
            AddThought(par, H_childBorn, baby.Id);
            AddBio(par, BioKind.ChildBorn, baby.Id);
            var r1 = par.GetRelation(baby.Id); r1.Opinion = 70; par.SetRelation(r1);
        }
        St.Births++;
        Notify(WorldEventKind.Birth, baby, mother.Id, $"{Hangul.I(DisplayName(mother))} 아이 {Hangul.Eul(baby.Name)} 낳았습니다 — 이름의 뜻: {NameMeaning(baby)}");
    }

    private void ReassignCarrier(Person baby)
    {
        Person? best = null;
        int bestScore = int.MinValue;
        foreach (var q in Life(baby.Cell).People)
        {
            if (q.Dead || q.Id == baby.Id || q.Stage(Now) < LifeStage.Adult || q.Carrier >= 0) continue;
            int sc = q.Id == baby.Mother ? 1000 : q.Id == baby.Father ? 900 : IsFamily(q, baby) ? 500 : 0;
            if (Get(baby.Mother) is { } m) sc += q.GetRelation(m.Id).Opinion;
            if (Get(baby.Father) is { } f) sc += q.GetRelation(f.Id).Opinion;
            if (q.Female) sc += 20;
            if (sc > bestScore) { bestScore = sc; best = q; }
        }
        Integrate(baby, Now);
        var old = Get(baby.Carrier);
        int bx = baby.Current.EndX, by = baby.Current.EndY;
        if (old is not null) old.Position(Now, out bx, out by);
        baby.NewMotion().SetStill(Now, bx, by);
        if (best is null) { baby.Carrier = -1; SetWhy(baby, Act.Carried, Why.Carried, -1); return; }
        baby.Carrier = best.Id;
        SetWhy(baby, Act.Carried, Why.Carried, best.Id);
    }

    // ───────── 죽음 ─────────

    internal void Kill(Person p, DeathCause cause)
    {
        if (p.Dead) return;
        Integrate(p, Now);
        DeathHook?.Invoke(this, p, cause);
        Unschedule(p);
        p.Position(Now, out int x, out int y);
        if (p.Carrier >= 0 && Get(p.Carrier) is { } c) c.Position(Now, out x, out y);
        p.NewMotion().SetStill(Now, x, y);
        p.Dead = true; p.DeathTick = Now; p.Cause = cause;
        if (p.Conceived >= 0) { if (InProvince) L.PregnantDelta--; else _pregnant--; }
        p.Walking = false; p.Asleep = false; p.Conceived = -1;
        SetWhy(p, Act.Dead, Why.None);
        p.Carrier = -1;
        if (InProvince) L.AliveDelta--; else AliveCount--;
        St.Deaths[(int)cause]++;
        int deathTile = (y / 1000) * S + x / 1000;
        // (E6) 사연 있는 물건은 곁의 가족이 물려받는다(없으면 그 자리에 남는다) · 시신이 생겼다(묻을 사람이 본다)
        if ((uint)deathTile < HexFrame.TileCount) PassOnNamed(p, Life(p.Cell), deathTile);
        Life(p.Cell).BuildVersion++;
        // (E4) 들고 있던 것은 그 자리에 남는다
        if (p.Carry.Count > 0 && (uint)deathTile < HexFrame.TileCount)
        {
            var lifeHere = Life(p.Cell);
            foreach (var (item, n) in p.Carry) if (item != I_ember) lifeHere.Drop(deathTile, item, n);
            p.Carry.Clear(); p.PotWater = 0; p.EmberTick = -1;
        }
        // (E5b) 입던 옷도 그 자리에 남는다(가족이 주워 입는다)
        if ((uint)deathTile < HexFrame.TileCount && (p.Worn >= 0 || p.WornFeet >= 0))
        {
            var lifeHere = Life(p.Cell);
            if (p.Worn >= 0) lifeHere.Drop(deathTile, p.Worn, 1);
            if (p.WornFeet >= 0) lifeHere.Drop(deathTile, p.WornFeet, 1);
            p.Worn = p.WornFeet = -1;
        }
        if (cause == DeathCause.Thirst && !Life(p.Cell).HasDrinkFrom(deathTile)) St.NoWaterDeaths++;
        if (cause == DeathCause.Starvation && !Life(p.Cell).HasFoodFrom(deathTile)) St.NoFoodDeaths++;
        AddBio(p, BioKind.Died, -1, (int)cause);
        foreach (var q in Life(p.Cell).People)
        {
            if (q.Dead || q.Id == p.Id) continue;
            if (q.Partner == p.Id) { q.Partner = -1; AddThought(q, H_partnerDied, p.Id); AddBio(q, BioKind.PartnerDied, p.Id); }
            else if (IsFamily(q, p)) { AddThought(q, H_familyDied, p.Id); AddBio(q, BioKind.FamilyDied, p.Id); }
            else if (q.GetRelation(p.Id).Opinion >= 40) AddThought(q, H_friendDied, p.Id);
        }
        foreach (var q in Life(p.Cell).People)
            if (!q.Dead && q.Carrier == p.Id) ReassignCarrier(q);
        Notify(WorldEventKind.Death, p, -1, $"{Hangul.I(DisplayName(p))} 죽었습니다 — {PeopleText.CauseName(cause)} (나이 {p.AgeYears(Now)})");
    }
}
