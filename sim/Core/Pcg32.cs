namespace Wtw.Sim.Core;

/// <summary>
/// PCG32 (XSH-RR, 64비트 상태). 빠르고 품질이 좋으며 상태(state, inc)만 저장하면 그대로 이어진다.
/// </summary>
public sealed class Pcg32
{
    private const ulong Multiplier = 6364136223846793005UL;

    public ulong State { get; private set; }
    public ulong Increment { get; private set; }

    public Pcg32(ulong seed, ulong sequence)
    {
        Increment = (sequence << 1) | 1UL;
        State = 0;
        NextUInt();
        State += seed;
        NextUInt();
    }

    private Pcg32() { }

    /// <summary>저장해 둔 상태로 되살린다.</summary>
    public static Pcg32 FromState(ulong state, ulong increment)
    {
        if ((increment & 1UL) == 0) throw new ArgumentException("increment 는 홀수여야 한다", nameof(increment));
        return new Pcg32 { State = state, Increment = increment };
    }

    public uint NextUInt()
    {
        ulong old = State;
        State = unchecked(old * Multiplier + Increment);
        uint xorShifted = (uint)(((old >> 18) ^ old) >> 27);
        int rot = (int)(old >> 59);
        return (xorShifted >> rot) | (xorShifted << ((-rot) & 31));
    }

    /// <summary>[0, bound) 의 고른 정수 (치우침 없음, Lemire 방식).</summary>
    public int NextInt(int bound)
    {
        if (bound <= 0) throw new ArgumentOutOfRangeException(nameof(bound));
        uint b = (uint)bound;
        ulong m = (ulong)NextUInt() * b;
        uint low = (uint)m;
        if (low < b)
        {
            uint threshold = (uint)(-(int)b) % b;
            while (low < threshold)
            {
                m = (ulong)NextUInt() * b;
                low = (uint)m;
            }
        }
        return (int)(m >> 32);
    }

    /// <summary>[min, maxExclusive) 의 고른 정수.</summary>
    public int NextInt(int min, int maxExclusive) => min + NextInt(maxExclusive - min);

    /// <summary>[0, 1) 의 실수 (53비트).</summary>
    public double NextDouble()
    {
        ulong bits = ((ulong)NextUInt() << 21) ^ (NextUInt() >> 11);
        return (bits & ((1UL << 53) - 1)) * (1.0 / (1UL << 53));
    }

    public bool Chance(int percent) => NextInt(100) < percent;
}
