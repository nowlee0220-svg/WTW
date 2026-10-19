using Wtw.Sim.Time;

namespace Wtw.Sim.People;

public sealed partial class PeopleSim
{
    private struct NeedState
    {
        public int Food, Water, Rest, Joy, Social, Mal, Dehy, Hypo, Heat, Health;
        public bool Cold;
    }

    private struct Rates { public int Food, Water, Rest, Joy, Social; }

    private static NeedState Read(Person p) => new()
    {
        Food = p.Food, Water = p.Water, Rest = p.Rest, Joy = p.Joy, Social = p.Social,
        Mal = p.Malnutrition, Dehy = p.Dehydration, Hypo = p.Hypothermia, Heat = p.Heatstroke, Health = p.Health,
    };

    private static void Write(Person p, in NeedState n)
    {
        p.Food = n.Food; p.Water = n.Water; p.Rest = n.Rest; p.Joy = n.Joy; p.Social = n.Social;
        p.Malnutrition = n.Mal; p.Dehydration = n.Dehy; p.Hypothermia = n.Hypo; p.Heatstroke = n.Heat; p.Health = n.Health;
    }

    /// <summary>지금 하는 일에 따른 시간당 욕구 변화.</summary>
    private Rates RatesOf(Person p)
    {
        var r = new Rates();
        if (p.Asleep) { r.Food = -FoodAsleep; r.Water = -WaterAsleep; r.Rest = RestAsleep; return r; }
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
        return r;
    }

    private static int Clamp(long v) => (int)Math.Clamp(v, 0, Person.Full);

    /// <summary>dt 틱 동안(한 시간 이하) 욕구 · 몸을 바꾼다. felt = 느끼는 기온(0.1°C).</summary>
    private void Step(ref NeedState n, in Rates r, int felt, long dt, bool tough)
    {
        const long H = GameTime.TicksPerHour;
        n.Food = Clamp(n.Food + r.Food * dt / H);
        n.Water = Clamp(n.Water + r.Water * dt / H);
        n.Rest = Clamp(n.Rest + r.Rest * dt / H);
        n.Joy = Clamp(n.Joy + r.Joy * dt / H);
        n.Social = Clamp(n.Social + r.Social * dt / H);
        n.Mal = Clamp(n.Mal + (n.Food <= 0 ? 520 : -800) * dt / H);
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
        if (p.Carrier >= 0 && Get(p.Carrier) is { Dead: false } c) return life.FeltTemp10(t, c.Tile(t), true);
        return life.FeltTemp10(t, p.Tile(t), p.Asleep && p.Huddle);
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
        while (t < until)
        {
            long dt = Math.Min(until - t, GameTime.TicksPerHour);
            int felt = life.FeltTemp10(t, p.Tile(t), p.Asleep && p.Huddle);
            Step(ref n, r, felt, dt, tough);
            if (p.IllUntil > t) n.Health = Clamp(n.Health - (long)p.IllDamage * dt / GameTime.TicksPerDay);
            t += dt;
        }
        if (n.Cold && p.Asleep) p.SleptCold = true;
        Write(p, n);
        p.LastUpdate = until;
    }

    /// <summary>안긴 아기: 안은 사람이 굶지 않으면 젖을 먹고, 안은 사람의 품에서 따뜻하다.</summary>
    private void IntegrateBaby(Person p, long until)
    {
        var c = Get(p.Carrier);
        var n = Read(p);
        long t = p.LastUpdate;
        var life = Life(p.Cell);
        while (t < until)
        {
            long dt = Math.Min(until - t, GameTime.TicksPerHour);
            // 젖: 안은 사람이 잠깐 배고픈 정도로는 끊기지 않고, 그 사람이 영양실조에 빠지면 끊긴다(젖이 마름 → 아기는 굶는다)
            bool fed = c is { Dead: false } && c.Malnutrition < 20_000;
            var r = new Rates { Food = fed ? 50_000 : -FoodAwake, Water = fed ? 50_000 : 0, /* 젖이 마르면 굶는다(물은 따로 못 마심 → 굶주림으로 셈) */ Rest = 5_000, Joy = 5_000, Social = 5_000 };
            int felt = c is { Dead: false } ? life.FeltTemp10(t, c.Tile(t), true) : life.FeltTemp10(t, p.Tile(t), false);
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
        bool watchF = n.Food >= fT, watchW = n.Water >= wT, watchR = !p.Asleep && n.Rest >= UrgentRest, watchC = n.Hypo < UrgentCold, watchH = n.Heat < UrgentCold;
        long t = from;
        while (t < until)
        {
            long dt = Math.Min(until - t, GameTime.TicksPerHour);
            int felt = life.FeltTemp10(t, tile, p.Asleep && p.Huddle);
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
            : p.Health <= 0 ? DeathCause.Illness : DeathCause.None;
        if (c == DeathCause.None) return false;
        Kill(p, c);
        return true;
    }
}
