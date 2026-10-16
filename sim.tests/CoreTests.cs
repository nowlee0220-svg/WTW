using Wtw.Sim.Core;
using Xunit;

namespace Wtw.Sim.Tests;

[Trait("Category", "Fast")]
public class CoreTests
{
    [Fact]
    public void SameSeedGivesSameSequence()
    {
        var a = RngStreams.Create(42, "dots", 0);
        var b = RngStreams.Create(42, "dots", 0);
        for (int i = 0; i < 1000; i++) Assert.Equal(a.NextUInt(), b.NextUInt());
    }

    [Fact]
    public void DifferentSeedsOrStreamsDiffer()
    {
        var a = RngStreams.Create(42, "dots", 0);
        var b = RngStreams.Create(43, "dots", 0);
        var c = RngStreams.Create(42, "dots", 1);
        var d = RngStreams.Create(42, "food", 0);
        var sa = Enumerable.Range(0, 8).Select(_ => a.NextUInt()).ToArray();
        Assert.NotEqual(sa, Enumerable.Range(0, 8).Select(_ => b.NextUInt()).ToArray());
        Assert.NotEqual(sa, Enumerable.Range(0, 8).Select(_ => c.NextUInt()).ToArray());
        Assert.NotEqual(sa, Enumerable.Range(0, 8).Select(_ => d.NextUInt()).ToArray());
    }

    [Fact]
    public void StreamsAreIndependentAndNewStreamDoesNotDisturbOld()
    {
        // 기준: 스트림 A 만 쓴 값
        var only = new RngStreams(7);
        var expected = Enumerable.Range(0, 100).Select(_ => only.Get("a", 3).NextUInt()).ToArray();

        // 같은 시드에서 A 사이사이에 B·C(새 스트림)를 끼워 써도 A 의 값은 같다
        var mixed = new RngStreams(7);
        var got = new uint[100];
        for (int i = 0; i < 100; i++)
        {
            if (i == 10) mixed.Get("b", 3);
            if (i % 3 == 0) mixed.Get("c", i).NextUInt();
            got[i] = mixed.Get("a", 3).NextUInt();
            mixed.Get("b", 3).NextUInt();
        }
        Assert.Equal(expected, got);
    }

    [Fact]
    public void RngStateRoundTrips()
    {
        var a = new Pcg32(123, 456);
        for (int i = 0; i < 37; i++) a.NextUInt();
        var b = Pcg32.FromState(a.State, a.Increment);
        for (int i = 0; i < 100; i++) Assert.Equal(a.NextUInt(), b.NextUInt());
    }

    [Fact]
    public void Pcg32MatchesReferenceOutput()
    {
        // PCG 공식 예제(pcg32-demo, 시드 42, 수열 54)의 첫 6개 값
        var r = new Pcg32(42, 54);
        uint[] expected = { 0xa15c02b7, 0x7b47f409, 0xba1d3330, 0x83d2f293, 0xbfa4784b, 0xcbed606e };
        foreach (var e in expected) Assert.Equal(e, r.NextUInt());
    }

    [Fact]
    public void NextIntStaysInRangeAndIsRoughlyUniform()
    {
        var r = new Pcg32(1, 1);
        var counts = new int[10];
        for (int i = 0; i < 100_000; i++) counts[r.NextInt(10)]++;
        foreach (var c in counts) Assert.InRange(c, 9_500, 10_500);
        for (int i = 0; i < 1000; i++) Assert.InRange(r.NextInt(-5, 5), -5, 4);
        for (int i = 0; i < 1000; i++) Assert.InRange(r.NextDouble(), 0.0, 0.9999999999);
    }

    [Fact]
    public void StableHashMatchesKnownValues()
    {
        // 값을 박아 둔다: 플랫폼·실행마다 같아야 한다. 바뀌면 저장 파일과 모든 시드의 역사가 바뀐다.
        Assert.Equal(KnownHashes.Empty, StableHash.Of(""));
        Assert.Equal(KnownHashes.Dots, StableHash.Of("dots"));
        Assert.Equal(KnownHashes.Korean, StableHash.Of("사람"));
        Assert.Equal(KnownHashes.Number42, StableHash.Of(42UL));
        Assert.Equal(KnownHashes.Combine12, StableHash.Combine(1, 2));
        Assert.NotEqual(StableHash.Combine(1, 2), StableHash.Combine(2, 1));
    }
}

internal static class KnownHashes
{
    public const ulong Empty = 17665956581633026203UL;
    public const ulong Dots = 5509087359601942158UL;
    public const ulong Korean = 2397663884488612987UL;
    public const ulong Number42 = 13679457532755275413UL;
    public const ulong Combine12 = 9316788204962561755UL;
}
