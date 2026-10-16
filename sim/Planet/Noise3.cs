using Wtw.Sim.Core;

namespace Wtw.Sim.Planet;

/// <summary>
/// 3차원 경사 잡음(개선 펄린). 순열표는 난수 스트림에서 섞는다.
/// 사칙연산과 Math.Floor 만 쓰므로 어느 기계에서나 같은 값이 나온다(삼각함수 없음).
/// </summary>
public sealed class Noise3
{
    private readonly byte[] _perm = new byte[512];

    public Noise3(Pcg32 rng)
    {
        var p = new byte[256];
        for (int i = 0; i < 256; i++) p[i] = (byte)i;
        for (int i = 255; i > 0; i--)
        {
            int j = rng.NextInt(i + 1);
            (p[i], p[j]) = (p[j], p[i]);
        }
        for (int i = 0; i < 512; i++) _perm[i] = p[i & 255];
    }

    /// <summary>대략 −1 ~ 1.</summary>
    public double Sample(double x, double y, double z)
    {
        double fx = Math.Floor(x), fy = Math.Floor(y), fz = Math.Floor(z);
        int X = (int)fx & 255, Y = (int)fy & 255, Z = (int)fz & 255;
        x -= fx; y -= fy; z -= fz;
        double u = Fade(x), v = Fade(y), w = Fade(z);
        var p = _perm;
        int A = p[X] + Y, AA = p[A] + Z, AB = p[A + 1] + Z;
        int B = p[X + 1] + Y, BA = p[B] + Z, BB = p[B + 1] + Z;
        return Lerp(w,
            Lerp(v, Lerp(u, Grad(p[AA], x, y, z), Grad(p[BA], x - 1, y, z)),
                    Lerp(u, Grad(p[AB], x, y - 1, z), Grad(p[BB], x - 1, y - 1, z))),
            Lerp(v, Lerp(u, Grad(p[AA + 1], x, y, z - 1), Grad(p[BA + 1], x - 1, y, z - 1)),
                    Lerp(u, Grad(p[AB + 1], x, y - 1, z - 1), Grad(p[BB + 1], x - 1, y - 1, z - 1))));
    }

    /// <summary>여러 겹(옥타브) 합. 결과를 진폭 합으로 나눠 대략 −1 ~ 1.</summary>
    public double Fbm(double x, double y, double z, int octaves, double lacunarity = 2.0, double gain = 0.5)
    {
        double sum = 0, amp = 1, norm = 0;
        for (int o = 0; o < octaves; o++)
        {
            sum += amp * Sample(x, y, z);
            norm += amp;
            amp *= gain;
            x *= lacunarity; y *= lacunarity; z *= lacunarity;
            // 겹마다 살짝 옮겨 격자 무늬가 겹치지 않게
            x += 17.31; y += 9.73; z += 3.17;
        }
        return sum / norm;
    }

    private static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);
    private static double Lerp(double t, double a, double b) => a + t * (b - a);

    private static double Grad(int hash, double x, double y, double z)
    {
        int h = hash & 15;
        double u = h < 8 ? x : y;
        double v = h < 4 ? y : h == 12 || h == 14 ? x : z;
        return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
    }
}
