namespace Wtw.Sim.Planet;

/// <summary>
/// 가장 최근에 만든 행성 몇 벌. 행성은 읽기 전용이고 시드 + 설정으로 똑같이 다시 만들어지므로 나눠 써도 결과가 같다.
/// (저장 → 불러오기, 같은 시드로 새로 시작할 때 다시 만들지 않아도 된다.)
/// </summary>
public static class PlanetCache
{
    private const int Capacity = 3;
    private static readonly object Lock = new();
    private static readonly List<Planet> Recent = new();

    public static Planet Get(ulong seed, PlanetSettings settings, Action<double, string>? progress)
    {
        lock (Lock)
        {
            foreach (var p in Recent)
                if (p.Seed == seed && p.Settings == settings) return p;
        }
        var made = PlanetGenerator.Generate(seed, settings, progress);
        lock (Lock)
        {
            Recent.Insert(0, made);
            if (Recent.Count > Capacity) Recent.RemoveAt(Recent.Count - 1);
        }
        return made;
    }
}
