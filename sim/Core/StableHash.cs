using System.Text;

namespace Wtw.Sim.Core;

/// <summary>
/// 플랫폼·실행마다 같은 값을 내는 64비트 해시. string.GetHashCode 는 실행마다 달라서 쓰지 않는다.
/// 문자열은 UTF-8 바이트에 FNV-1a 64, 숫자 섞기는 SplitMix64 마무리 함수.
/// </summary>
public static class StableHash
{
    private const ulong FnvOffset = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    public static ulong Of(string text)
    {
        ulong h = FnvOffset;
        foreach (byte b in Encoding.UTF8.GetBytes(text))
        {
            h ^= b;
            h *= FnvPrime;
        }
        return Mix(h);
    }

    public static ulong Of(ulong value) => Mix(value ^ 0x9E3779B97F4A7C15UL);

    /// <summary>두 값을 순서를 가려서 섞는다 (Combine(a,b) != Combine(b,a)).</summary>
    public static ulong Combine(ulong a, ulong b) => Mix(a * 0xBF58476D1CE4E5B9UL + Mix(b + 0x632BE59BD9B4E019UL));

    public static ulong Combine(ulong a, ulong b, ulong c) => Combine(Combine(a, b), c);

    /// <summary>SplitMix64 마무리(finalizer). 비트를 고루 퍼뜨린다.</summary>
    public static ulong Mix(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
