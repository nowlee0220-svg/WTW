namespace Wtw.Sim.Time;

/// <summary>
/// 림월드식 틱 시계. 1시간 = 2,500틱, 하루 = 24시간 = 60,000틱, 1년 = 40일. 0틱 = 1년 1일 0시 0분.
/// </summary>
public static class GameTime
{
    public const long TicksPerHour = 2_500;
    public const int HoursPerDay = 24;
    public const long TicksPerDay = TicksPerHour * HoursPerDay; // 60,000
    public const int DaysPerYear = 40;
    public const long TicksPerYear = TicksPerDay * DaysPerYear; // 2,400,000
    public const int MinutesPerHour = 60;

    public static GameDate ToDate(long tick)
    {
        if (tick < 0) throw new ArgumentOutOfRangeException(nameof(tick));
        long year = tick / TicksPerYear;
        long inYear = tick % TicksPerYear;
        long day = inYear / TicksPerDay;
        long inDay = inYear % TicksPerDay;
        long hour = inDay / TicksPerHour;
        long inHour = inDay % TicksPerHour;
        long minute = inHour * MinutesPerHour / TicksPerHour;
        return new GameDate((int)year + 1, (int)day + 1, (int)hour, (int)minute);
    }

    /// <summary>그 분이 시작되는 첫 틱. ToDate(ToTicks(d)) == d.</summary>
    public static long ToTicks(GameDate d)
    {
        if (d.Year < 1 || d.Day < 1 || d.Day > DaysPerYear || d.Hour < 0 || d.Hour >= HoursPerDay || d.Minute < 0 || d.Minute >= MinutesPerHour)
            throw new ArgumentOutOfRangeException(nameof(d), d.ToString());
        long minuteTicks = (d.Minute * TicksPerHour + MinutesPerHour - 1) / MinutesPerHour; // 올림
        return (d.Year - 1) * TicksPerYear + (d.Day - 1) * TicksPerDay + d.Hour * TicksPerHour + minuteTicks;
    }

    public static long DayIndex(long tick) => tick / TicksPerDay;
    public static double Days(long ticks) => ticks / (double)TicksPerDay;

    /// <summary>화면 표시: "1년 3일 07시".</summary>
    public static string Format(long tick) => ToDate(tick).ToString();
    public static string FormatWithMinutes(long tick) => ToDate(tick).ToLongString();
}

public readonly record struct GameDate(int Year, int Day, int Hour, int Minute)
{
    public override string ToString() => $"{Year}년 {Day}일 {Hour:00}시";
    public string ToLongString() => $"{Year}년 {Day}일 {Hour:00}시 {Minute:00}분";
}
