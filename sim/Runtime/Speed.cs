using Wtw.Sim.Time;

namespace Wtw.Sim.Runtime;

/// <summary>
/// 속도 단계(기획서 6-2). 화면에는 1배속~5배속. 값 = 실제 1초에 흐르는 틱.
/// 1~3배속 = 림월드 1·3·6배(초당 60·180·360틱), 4배속 = 하루 3초, 5배속 = 하루 0.4초.
/// </summary>
public static class Speed
{
    public const int Max = 5;

    private static readonly double[] TicksPerSecond =
    {
        0,
        60,
        180,
        360,
        GameTime.TicksPerDay / 3.0,
        GameTime.TicksPerDay / 0.4,
    };

    public static double Tps(int level) => TicksPerSecond[Math.Clamp(level, 0, Max)];
    public static string Label(int level) => level <= 0 ? "일시정지" : $"{level}배속";
}
