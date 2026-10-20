using Wtw.Sim.People;
using Wtw.Sim.Time;
using Wtw.Sim.World;

/// <summary>(E5b 고침 · 측정) 할 일 × 까닭이 정해진 횟수(많은 순). dotnet run … -- people acts [--years 20] [--people 300] [--per 15]</summary>
public static class ActCountBench
{
    public static void Run(int people, int years, int per, ulong seed)
    {
        var counts = new Dictionary<(Act, Why), long>();
        PeopleSim.ActHook = (sim, p) => { lock (counts) counts[(p.Act, p.Why)] = counts.GetValueOrDefault((p.Act, p.Why)) + 1; };
        PeopleBench.Simulate(seed, people, years, true, cap: 300, perProvince: per);
        PeopleSim.ActHook = null;
        long tot = counts.Values.Sum();
        foreach (var ((a, why), n) in counts.OrderByDescending(kv => kv.Value).Take(40)) Console.WriteLine($"{n,10:N0} {n * 100.0 / tot,5:0.0}% {a}/{why}");
    }
}
