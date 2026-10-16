using Wtw.Sim.Planet;

namespace Wtw.Sim.World;

/// <summary>새 세계 설정. 시작 기록의 일부.</summary>
public sealed record WorldConfig
{
    /// <summary>땅 칸 수 (기획서 5-2: 16,000 기본 · 20,000 · 24,000).</summary>
    public int LandCells { get; init; } = 16_000;
    /// <summary>기후 경향.</summary>
    public ClimateBias Climate { get; init; } = ClimateBias.Normal;
    public PlanetSettings PlanetSettings => new(LandCells, Climate);

    /// <summary>움직임 시험용 점 개수.</summary>
    public int DotCount { get; init; } = 500;
    /// <summary>시험 지도 한 변 타일 수 (기획서 5-3: 지방 150×150).</summary>
    public int MapTiles { get; init; } = 150;

    /// <summary>사람 상한(기획서 18절: 기본 300, 설정 100~500). 상한에 닿으면 아이가 생기지 않는다 — 마지막 안전장치(실제 인구는 식량으로 멈춰야 한다).</summary>
    public int PeopleCap { get; init; } = 300;
    public const int MinCap = 100, MaxCap = 500;
    /// <summary>열정 시스템을 켤까(E3 측정: 켠 세계와 끈 세계를 비교해 남길지 정한다).</summary>
    public bool Passion { get; init; } = true;
}

public sealed record Province(int Id, string Name);

/// <summary>엔티티 아이디 발급. 1 부터 차례로 — 결정론적.</summary>
public sealed class EntityStore
{
    public int NextId { get; internal set; } = 1;
    public int Allocate() => NextId++;
}

/// <summary>예약 종류 번호. 저장 파일에 숫자로 남으므로 바꾸지 말 것.</summary>
public static class EventKinds
{
    public const int DotDepart = 1;
    public const int DotArrive = 2;
    /// <summary>사람의 다음 판단(E3).</summary>
    public const int Person = 3;
}
