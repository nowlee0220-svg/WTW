namespace Wtw.Sim.Core;

/// <summary>
/// 난수 스트림 묶음. 스트림 하나 = (이름 × 지방 번호). 시드는 (세계 시드, 이름, 지방) 의 안정 해시에서만 뽑으므로
/// 새 스트림을 만들어도 기존 스트림의 값은 바뀌지 않는다.
/// </summary>
public sealed class RngStreams
{
    public const int GlobalProvince = -1;

    private readonly ulong _worldSeed;
    private readonly Dictionary<(string Name, int Province), Pcg32> _streams = new();

    public RngStreams(ulong worldSeed) { _worldSeed = worldSeed; }

    public ulong WorldSeed => _worldSeed;
    public int Count => _streams.Count;

    public Pcg32 Get(string name, int province = GlobalProvince)
    {
        if (!_streams.TryGetValue((name, province), out var rng))
        {
            rng = Create(_worldSeed, name, province);
            _streams.Add((name, province), rng);
        }
        return rng;
    }

    public static Pcg32 Create(ulong worldSeed, string name, int province)
    {
        ulong key = StableHash.Combine(StableHash.Of(worldSeed), StableHash.Of(name), StableHash.Of(unchecked((ulong)(long)province)));
        return new Pcg32(key, StableHash.Mix(key ^ 0xD1B54A32D192ED03UL));
    }

    /// <summary>저장용: (이름, 지방) 순으로 정렬한 목록. Dictionary 순회 순서에 기대지 않는다.</summary>
    public IReadOnlyList<(string Name, int Province, ulong State, ulong Increment)> Export()
    {
        var list = new List<(string, int, ulong, ulong)>(_streams.Count);
        foreach (var kv in _streams) list.Add((kv.Key.Name, kv.Key.Province, kv.Value.State, kv.Value.Increment));
        list.Sort((a, b) =>
        {
            int c = string.CompareOrdinal(a.Item1, b.Item1);
            return c != 0 ? c : a.Item2.CompareTo(b.Item2);
        });
        return list;
    }

    public void Import(string name, int province, ulong state, ulong increment)
    {
        _streams[(name, province)] = Pcg32.FromState(state, increment);
    }
}
