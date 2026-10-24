using Wtw.Sim.Time;

namespace Wtw.Sim.People;

/// <summary>
/// (E6b) 몸에 쌓인 것 — 야윔. 배부름(Food)은 끼니(몇 시간), 몸에 쌓인 것(Body)은 몇 주 · 몇 달.
///   - 배부름이 바닥난 채 몸이 쓰는 몫은 몸에 쌓인 것에서 빠진다(그동안 배부름은 0 에 머문다).
///   - 배부름이 넉넉하고(50% 넘게) 몸이 보통보다 야위었으면 먹은 것의 일부가 몸에 다시 쌓인다(그만큼 배가 빨리 꺼진다 — 더 먹는다).
///   - 0 이 되면 굶어 죽는다.
/// 어림(현실): 보통 몸(BMI 22 · 60%)에서 다 굶으면 어른은 50~60일, 아이는 그 절반쯤, 젖먹이는 며칠 만에 죽는다(몸에 쌓인 것이 먹는 양에 비해 적다).
/// 몸에 쌓인 것 1% 는 BMI 약 0.16: 60% ≈ BMI 22, 38% ≈ 18.5(야윔이 시작됨), 28% ≈ 17, 22% ≈ 16(몹시 야윔), 0% ≈ 12.5(죽음).
/// </summary>
public sealed partial class PeopleSim
{
    public const int BodyFull = 100_000_000, BodyNormal = 60_000_000;
    /// <summary>야윔 단계의 문턱: 조금 야윔(BMI 18.5) · 야윔(BMI 17) · 몹시 야윔(BMI 16).</summary>
    public const int BodyThin1 = 38_000_000, BodyThin2 = 28_000_000, BodyThin3 = 22_000_000;
    /// <summary>배부름이 넉넉할 때 몸에 다시 쌓는 몫(배부름, 시간당) — 하루 36(먹는 양의 37%)을 더 먹으면 어른은 하루 0.4% 씩 찬다(30% → 60% 에 두어 달).</summary>
    public const int RefillFood = 1_500;
    /// <summary>병든 사람은 열이 나 더 쓴다(시간당 배부름 — 깨어 있을 때 쓰는 양의 20%).</summary>
    public const int IllFood = 1_000;

    internal int H_thin = -1, H_lostBaby = -1;

    private void InitBody()
    {
        H_thin = Data.ThoughtIndex("always_hungry"); H_lostBaby = Data.ThoughtIndex("lost_baby");
        if (H_thin < 0 || H_lostBaby < 0) throw new InvalidOperationException("people.json 에 생각 'always_hungry' · 'lost_baby' 가 없습니다.");
    }

    /// <summary>
    /// 몸에 쌓인 것 1% 가 배부름 몇(1000 = 1)에 해당하나 — 나이 단계마다. 어른 88(먹는 양 96 의 0.92일치 → 60% 를 다 쓰는 데 약 55일),
    /// 아이는 몸이 작아 0.45배(약 25일), 늙은이는 살이 적어 0.7배, 젖먹이는 0.1배(며칠).
    /// </summary>
    public static int BodyScale(LifeStage st) => st switch { LifeStage.Baby => 8_800, LifeStage.Child => 40_000, LifeStage.Elder => 62_000, _ => 88_000 };

    /// <summary>야윔 단계: 0 보통 · 1 조금 야윔 · 2 야윔 · 3 몹시 야윔.</summary>
    public static int ThinStage(int body) => body >= BodyThin1 ? 0 : body >= BodyThin2 ? 1 : body >= BodyThin3 ? 2 : 3;
    public static int ThinStage(Person p) => ThinStage(p.Body);
    public static string ThinName(int stage) => stage switch { 0 => "보통", 1 => "조금 야윔", 2 => "야윔", _ => "몹시 야윔" };

    /// <summary>영양실조(0~100,000): 몹시 야윈 문턱(22%) 아래에서 0 까지 — 100% 가 되면 죽는다.</summary>
    public static int MalOf(int body) => body >= BodyThin3 ? 0 : (int)((long)(BodyThin3 - Math.Max(0, body)) * Person.Full / BodyThin3);

    /// <summary>
    /// 아이를 가질 수 있는 정도(1000 분의): 몸이 45% 이상이면 그대로, 28%(BMI 17) 에서 0 — 몸무게가 보통보다 10~15% 줄면 배란이 줄거나 멎는다(Frisch 의 어림).
    /// </summary>
    public static int Fertility(int body) => body >= 45_000_000 ? 1000 : body <= BodyThin2 ? 0 : (int)((long)(body - BodyThin2) * 1000 / (45_000_000 - BodyThin2));

    /// <summary>
    /// 젖(100 분의): 젖은 어머니가 꽤 야위어도 거의 그대로 나온다(현실: 젖의 양은 어머니가 몹시 굶주릴 때에야 준다) — 몹시 야윔(22%, BMI 16) 아래면 절반,
    /// 11%(BMI 14) 아래면 마른다. 절반일 때 아기는 먹는 양의 4분의 3 을 얻는다(조금씩 야윈다).
    /// </summary>
    public static int MilkOf(Person mother) => mother.Body >= BodyThin3 ? 100 : mother.Body >= 11_000_000 ? 50 : 0;

    /// <summary>배 속의 아기를 잃을 하루 확률(10,000 분의): 야윔 40 · 몹시 야윔 150 (임신 30일 동안 약 11% · 36%).</summary>
    public static int MiscarryChance(int body) => ThinStage(body) switch { 2 => 40, 3 => 150, _ => 0 };

    /// <summary>야위면 덜 따뜻하다(살이 덜 감싼다, 0.1°C): 조금 1°C · 야윔 2°C · 몹시 4°C.</summary>
    public static int ThinChill(Person p) => ThinStage(p.Body) switch { 1 => 10, 2 => 20, 3 => 40, _ => 0 };

    /// <summary>하루 경계: 배 속의 아기를 잃는가(몹시 야위었을 때).</summary>
    private bool BodyMiscarry(Person p, Core.Pcg32 rng)
    {
        if (p.Conceived < 0) return false;
        int ch = MiscarryChance(p.Body);
        if (ch <= 0 || rng.NextInt(10_000) >= ch) return false;
        p.Conceived = -1;
        if (InProvince) L.PregnantDelta--; else _pregnant--;
        St.Miscarriages++;
        AddThought(p, H_lostBaby);
        if (Get(p.ChildFather) is { Dead: false } f && f.Cell == p.Cell) AddThought(f, H_lostBaby);
        AddBio(p, BioKind.LostBaby, p.ChildFather);
        return true;
    }
}
