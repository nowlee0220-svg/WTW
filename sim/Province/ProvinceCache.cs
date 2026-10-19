using System.Runtime.CompilerServices;
using Wtw.Sim.Planet;

namespace Wtw.Sim.Provinces;

/// <summary>
/// 만든 지방 지도를 기억해 둔다(행성마다 가장 최근 Capacity 개). 지도는 읽기 전용이고 행성 + 칸으로 똑같이 다시 만들어지므로
/// 나눠 써도 결과가 같다. 오래 안 본 지도는 잊는다 → 여러 지방을 드나들어도 메모리가 계속 늘지 않는다.
/// 여러 스레드에서 불러도 된다(같은 지도를 동시에 두 번 만들 수는 있지만 결과는 같다).
/// (E3b) 행성마다 따로 기억한다 — 여러 세계를 함께 돌리는 측정에서 서로의 지도를 지우지 않게.
/// </summary>
public static class ProvinceCache
{
    public const int Capacity = 512;
    private static readonly object Lock = new();
    private static readonly ConditionalWeakTable<Planet.Planet, LinkedList<ProvinceMap>> ByPlanet = new();
    private static Planet.Planet? _last;

    /// <summary>가장 최근에 쓴 행성의 지도 수.</summary>
    public static int Count { get { lock (Lock) return _last is not null && ByPlanet.TryGetValue(_last, out var l) ? l.Count : 0; } }

    /// <summary>그 행성의 지도 수(다른 스레드가 다른 행성을 써도 흔들리지 않는다).</summary>
    public static int CountFor(Planet.Planet p) { lock (Lock) return ByPlanet.TryGetValue(p, out var l) ? l.Count : 0; }

    /// <summary>그 행성의 지도만 잊는다.</summary>
    public static void Forget(Planet.Planet p) { lock (Lock) { ByPlanet.Remove(p); if (ReferenceEquals(_last, p)) _last = null; } }

    public static ProvinceMap Get(Planet.Planet p, int cell)
    {
        LinkedList<ProvinceMap> recent;
        lock (Lock)
        {
            _last = p;
            recent = ByPlanet.GetValue(p, _ => new LinkedList<ProvinceMap>());
            for (var n = recent.First; n is not null; n = n.Next)
                if (n.Value.Cell == cell)
                {
                    recent.Remove(n); recent.AddFirst(n);
                    return n.Value;
                }
        }
        var made = ProvinceGenerator.Generate(p, cell);
        lock (Lock)
        {
            for (var n = recent.First; n is not null; n = n.Next) if (n.Value.Cell == cell) return n.Value;
            recent.AddFirst(made);
            while (recent.Count > Capacity) recent.RemoveLast();
        }
        return made;
    }

    public static void Clear() { lock (Lock) { ByPlanet.Clear(); _last = null; } }
}
