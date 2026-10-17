namespace Wtw.Sim.World;

/// <summary>플레이어가 한 일 하나: 시각 + 종류 + 값.</summary>
public sealed record Intervention(long Tick, string Kind, long[] Values)
{
    public static class Kinds
    {
        /// <summary>점 추가. 값: [개수]</summary>
        public const string AddDots = "add_dots";
        /// <summary>모든 점이 하던 일을 끊고 한 곳으로 모임(예약 당김 시험). 값: [x 밀리타일, y 밀리타일]</summary>
        public const string Gather = "gather";
        /// <summary>사람 한 명 뿌리기(E3). 값: [칸, 타일 x, 타일 y]</summary>
        public const string SpawnPerson = "spawn_person";
        /// <summary>가족 뿌리기(짝 + 아이 0~2). 값: [칸, 타일 x, 타일 y]</summary>
        public const string SpawnFamily = "spawn_family";
    }

    public bool Equals(Intervention? o) => o is not null && Tick == o.Tick && Kind == o.Kind && Values.AsSpan().SequenceEqual(o.Values);
    public override int GetHashCode() => HashCode.Combine(Tick, Kind, Values.Length);
}

/// <summary>시작 기록 = 시드 + 설정 + 개입 기록. 이것만 있으면 세계를 처음부터 똑같이 다시 만든다.</summary>
public sealed record StartRecord(ulong Seed, WorldConfig Config, IReadOnlyList<Intervention> Interventions);
