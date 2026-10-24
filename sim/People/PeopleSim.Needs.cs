using Wtw.Sim.Time;

namespace Wtw.Sim.People;

public sealed partial class PeopleSim
{
    private struct NeedState
    {
        public int Food, Water, Rest, Joy, Social, Mal, Dehy, Hypo, Heat, Health, Body;
        public bool Cold;
        /// <summary>(E6b 측정) 이번 셈에서 몸에서 빠진 · 몸에 쌓은 배부름(1000 = 1).</summary>
        public long Drawn, Refilled;
    }

    /// <summary>시간당 변화. Refill = 배부름에서 몸에 쌓는 몫(E6b — Food 에 이미 빠져 있다) · BodyScale = 몸 1% 가 배부름 몇인가.</summary>
    private struct Rates { public int Food, Water, Rest, Joy, Social, Refill, BodyScale; }

    /// <summary>
    /// (T1) 젖은 어머니가 먹은 것에서 나온다: 젖 먹이는 어머니가 하루에 더 쓰는 배부름(1000 = 1) — 먹는 양(96)의 23%(현실 어림 하루 500kcal ÷ 2,200kcal).
    /// 전에는 아기가 어머니의 배부름을 하나도 쓰지 않았다(젖이 공짜). 하루 경계에 한 번 뺀다(카드를 보느라 아기를 셈한 때와 상관없이 같게).
    /// </summary>
    public const int NursingFood = 21_600;

    private static NeedState Read(Person p) => new()
    {
        Food = p.Food, Water = p.Water, Rest = p.Rest, Joy = p.Joy, Social = p.Social,
        Mal = p.Malnutrition, Dehy = p.Dehydration, Hypo = p.Hypothermia, Heat = p.Heatstroke, Health = p.Health, Body = p.Body,
    };

    private static void Write(Person p, in NeedState n)
    {
        p.Food = n.Food; p.Water = n.Water; p.Rest = n.Rest; p.Joy = n.Joy; p.Social = n.Social;
        p.Malnutrition = n.Mal; p.Dehydration = n.Dehy; p.Hypothermia = n.Hypo; p.Heatstroke = n.Heat; p.Health = n.Health; p.Body = n.Body;
    }

    /// <summary>(E6b) 몸에 쌓기 · 병의 열: 욕구 셈에 더한다(깨어 있든 자든).</summary>
    private void BodyRates(Person p, ref Rates r)
    {
        r.BodyScale = BodyScale(p.Stage(Now));
        if (p.IllUntil > Now) r.Food -= IllFood;
        if (p.Food > 50_000 && p.Body < BodyNormal) { r.Refill = RefillFood; r.Food -= RefillFood; }
    }

    /// <summary>지금 하는 일에 따른 시간당 욕구 변화.</summary>
    private Rates RatesOf(Person p)
    {
        var r = new Rates();
        if (p.Asleep) { r.Food = -FoodAsleep; r.Water = -WaterAsleep; r.Rest = RestAsleep; BodyRates(p, ref r); return r; }
        r.Food = -FoodAwake; r.Water = -WaterAwake;
        r.Rest = -RestAwake;
        if (p.Stage(Now) == LifeStage.Elder || p.IllUntil > Now) r.Rest -= 1_500;
        r.Joy = p.HasTrait(T_industrious) ? -JoyAwake * 3 / 4 : p.HasTrait(T_lazy) ? -JoyAwake * 5 / 4 : -JoyAwake;
        r.Social = -SocialAwake;
        if (!p.Walking)
        {
            switch (p.Act)
            {
                case Act.Rest or Act.Warm or Act.Follow:
                    r.Joy = p.HasTrait(T_lazy) ? 16_000 : 12_000; r.Rest = -1_500; break;
                case Act.Talk when p.Target >= 0:
                    r.Social = 40_000; r.Joy = 5_000; break;
                case Act.Teach or Act.Learn:
                    r.Social = 25_000; r.Joy = 3_000; break;
                case Act.BreakLeave or Act.BreakWander:
                    r.Joy = 4_000; break;
            }
        }
        BodyRates(p, ref r);
        return r;
    }

    private static int Clamp(long v) => (int)Math.Clamp(v, 0, Person.Full);

    /// <summary>dt 틱 동안(한 시간 이하) 욕구 · 몸을 바꾼다. felt = 느끼는 기온(0.1°C).</summary>
    private void Step(ref NeedState n, in Rates r, int felt, long dt, bool tough)
    {
        const long H = GameTime.TicksPerHour;
        // (E6b) 배부름이 바닥나면 모자란 몫을 몸에 쌓인 것에서 쓴다 · 넉넉하면 몸에 다시 쌓는다
        long food = n.Food + r.Food * dt / H;
        if (food < 0) { n.Body = (int)Math.Max(0, n.Body + food * 1_000_000 / r.BodyScale); n.Drawn -= food; }
        if (r.Refill > 0) { long add = r.Refill * dt / H; n.Body = (int)Math.Min(BodyFull, n.Body + add * 1_000_000 / r.BodyScale); n.Refilled += add; }
        n.Food = Clamp(food);
        n.Water = Clamp(n.Water + r.Water * dt / H);
        n.Rest = Clamp(n.Rest + r.Rest * dt / H);
        n.Joy = Clamp(n.Joy + r.Joy * dt / H);
        n.Social = Clamp(n.Social + r.Social * dt / H);
        n.Mal = MalOf(n.Body);
        n.Dehy = Clamp(n.Dehy + (n.Water <= 0 ? 1_400 : -3_000) * dt / H);
        if (felt < 60) { n.Hypo = Clamp(n.Hypo + (60 - felt) * 100L * dt / H); n.Cold = true; }
        else n.Hypo = Clamp(n.Hypo - (felt >= 100 ? 12_000 : 3_000) * dt / H);
        n.Heat = Clamp(n.Heat + (felt > 400 ? (felt - 400) * 150L : -12_000L) * dt / H);
        // 병이 아니면 건강이 천천히 돌아온다
        n.Health = Clamp(n.Health + (tough ? 8_000 : 4_000) * dt / GameTime.TicksPerDay);
    }

    public int FeltNow(Person p) => FeltAt(p, Now);

    private int FeltAt(Person p, long t)
    {
        var life = Life(p.Cell);
        if (p.Carrier >= 0 && Here(p.Carrier, p) is { Dead: false } c) return life.FeltTemp10(t, c.Tile(t), true, ClothWarm(c) / 2) - ThinChill(p);
        return life.FeltTemp10(t, p.Tile(t), p.Asleep && p.Huddle, ClothWarm(p)) - ThinChill(p);
    }

    /// <summary>LastUpdate 부터 until 까지 욕구를 몰아서 계산한다(한 시간씩).</summary>
    private void Integrate(Person p, long until)
    {
        if (until <= p.LastUpdate) return;
        if (p.Carrier >= 0) { IntegrateBaby(p, until); return; }
        var n = Read(p);
        var r = RatesOf(p);
        bool tough = p.HasTrait(T_tough);
        long t = p.LastUpdate;
        var life = Life(p.Cell);
        int cloth = ClothWarm(p), chill = ThinChill(p);
        while (t < until)
        {
            long dt = Math.Min(until - t, GameTime.TicksPerHour);
            int felt = life.FeltTemp10(t, p.Tile(t), p.Asleep && p.Huddle, cloth) - chill;
            Step(ref n, r, felt, dt, tough);
            if (p.IllUntil > t) n.Health = Clamp(n.Health - (long)p.IllDamage * dt / GameTime.TicksPerDay);
            t += dt;
        }
        if (n.Cold && p.Asleep) p.SleptCold = true;
        St.BodyDrawn += n.Drawn; St.BodyRefilled += n.Refilled;
        Write(p, n);
        p.LastUpdate = until;
    }

    /// <summary>안긴 아기: 안은 사람이 굶지 않으면 젖을 먹고, 안은 사람의 품에서 따뜻하다.</summary>
    private void IntegrateBaby(Person p, long until)
    {
        var c = Here(p.Carrier, p);
        var n = Read(p);
        long t = p.LastUpdate;
        var life = Life(p.Cell);
        // 젖: 안은 사람이 잠깐 배고픈 정도로는 끊기지 않는다. (E6b) 안은 사람이 야위면 젖이 줄고(절반 — 아기가 야윈다), 몹시 야위면 마른다(아기는 굶는다)
        int milk = c is { Dead: false } ? MilkOf(c) : 0;
        int scale = BodyScale(LifeStage.Baby), chill = ThinChill(p);
        while (t < until)
        {
            long dt = Math.Min(until - t, GameTime.TicksPerHour);
            var r = new Rates { Food = milk >= 100 ? 50_000 : milk > 0 ? -FoodAwake / 4 : -FoodAwake, Water = milk > 0 ? 50_000 : 0, /* 젖이 마르면 굶는다(물은 따로 못 마심 → 굶주림으로 셈) */ Rest = 5_000, Joy = 5_000, Social = 5_000, BodyScale = scale };
            if (milk >= 100 && n.Body < BodyNormal) r.Refill = RefillFood;
            int felt = (c is { Dead: false } ? life.FeltTemp10(t, c.Tile(t), true, ClothWarm(c) / 2) : life.FeltTemp10(t, p.Tile(t), false)) - chill;
            Step(ref n, r, felt, dt, p.HasTrait(T_tough));
            if (p.IllUntil > t) n.Health = Clamp(n.Health - (long)p.IllDamage * dt / GameTime.TicksPerDay);
            t += dt;
        }
        Write(p, n);
        p.LastUpdate = until;
    }

    /// <summary>
    /// 지금 일을 계속하면 급한 일(심한 배고픔 · 목마름 · 지침 · 추위)이 언제 닥치나. until 까지 없으면 until.
    /// 이미 그 선 아래에 있는 욕구는 보지 않는다(그 판단은 이미 했다 → 같은 자리에서 맴돌지 않게).
    /// </summary>
    private long PredictUrgent(Person p, long from, long until, int tile)
    {
        if (p.Carrier >= 0 || until <= from) return until;
        var n = Read(p);
        var r = RatesOf(p);
        bool tough = p.HasTrait(T_tough);
        var life = Life(p.Cell);
        int fT = p.Asleep ? 8_000 : UrgentFood, wT = p.Asleep ? 8_000 : UrgentWater;
        int cloth = ClothWarm(p), chill = ThinChill(p);
        bool watchF = n.Food >= fT, watchW = n.Water >= wT, watchR = !p.Asleep && n.Rest >= UrgentRest, watchC = n.Hypo < UrgentCold, watchH = n.Heat < UrgentCold;
        long t = from;
        while (t < until)
        {
            long dt = Math.Min(until - t, GameTime.TicksPerHour);
            int felt = life.FeltTemp10(t, tile, p.Asleep && p.Huddle, cloth) - chill;
            var before = n;
            Step(ref n, r, felt, dt, tough);
            long hit = long.MaxValue;
            if (watchF && n.Food < fT && r.Food < 0) hit = Math.Min(hit, t + (before.Food - fT) * GameTime.TicksPerHour / -r.Food + 1);
            if (watchW && n.Water < wT && r.Water < 0) hit = Math.Min(hit, t + (before.Water - wT) * GameTime.TicksPerHour / -r.Water + 1);
            if (watchR && n.Rest < UrgentRest && r.Rest < 0) hit = Math.Min(hit, t + (before.Rest - UrgentRest) * GameTime.TicksPerHour / -r.Rest + 1);
            if (watchC && n.Hypo >= UrgentCold && n.Hypo > before.Hypo) hit = Math.Min(hit, t + Math.Max(1, (UrgentCold - before.Hypo) * dt / Math.Max(1, n.Hypo - before.Hypo)));
            if (watchH && n.Heat >= UrgentCold && n.Heat > before.Heat) hit = Math.Min(hit, t + Math.Max(1, (UrgentCold - before.Heat) * dt / Math.Max(1, n.Heat - before.Heat)));
            if (hit != long.MaxValue) return Math.Clamp(hit, from + 1, until);
            t += dt;
        }
        return until;
    }

    /// <summary>죽을 만큼 나빠졌나 → 죽음.</summary>
    private bool CheckDeath(Person p)
    {
        if (p.Dead) return true;
        DeathCause c = p.Malnutrition >= Person.Full ? DeathCause.Starvation
            : p.Dehydration >= Person.Full ? DeathCause.Thirst
            : p.Hypothermia >= Person.Full ? DeathCause.Cold
            : p.Heatstroke >= Person.Full ? DeathCause.Heat
            : p.Health <= 0 ? (p.Injury > 0 && p.InjuryCause != DeathCause.None ? p.InjuryCause : DeathCause.Illness) : DeathCause.None;
        if (c == DeathCause.None) return false;
        Kill(p, c);
        return true;
    }
}
