namespace Wtw.Sim.People;

/// <summary>(E6) 건물의 단계. 값은 저장 파일에 남는다.</summary>
public enum BuildStage : byte { Site = 0, Done = 1 }

/// <summary>(E6) 건물 일 한 가지(Act.Build 의 TargetB). 값은 저장 파일에 남는다.</summary>
public enum BuildJob : byte { None = 0, Fetch, Deliver, Work, Repair, Bury }

/// <summary>
/// (E6) 지방 위의 건물 하나(짓는 중 · 다 지음). 건물의 왼쪽 위 타일(Tile)부터 W × H 타일을 차지한다.
/// 낡음은 예약 방식: Stamp 시각에 Cond0(0~1000)이었고, 그 뒤로 품질에 따른 빠르기로 줄어 Due 시각에 무너진다(그때만 본다).
/// 짓는 중인 자리는 Due 까지 아무도 손대지 않으면 버려진다.
/// </summary>
public sealed class Building
{
    public int Id;
    /// <summary>건물 종류(BuildingData 번호).</summary>
    public int Kind;
    public int Tile;
    public BuildStage Stage;
    /// <summary>재료마다 날라 온 수(BuildingDef.Parts 순서).</summary>
    public int[] Have = Array.Empty<int>();
    /// <summary>쌓은 일(분).</summary>
    public int Work;
    /// <summary>품질 0 나쁨 · 1 보통 · 2 좋음 · 3 훌륭함(다 지을 때 정해진다).</summary>
    public int Quality = 1;
    /// <summary>자리를 정한 사람 · 짓기 시작한 때 · 다 지은 때(−1 = 아직).</summary>
    public int Founder = -1;
    public long Started, Finished = -1;
    /// <summary>함께 지은 사람(먼저 손댄 차례, 여덟까지).</summary>
    public readonly List<int> Builders = new();
    /// <summary>움집의 주인 집안(자리를 정한 사람 — 그 짝 · 아이도 함께 잔다). −1 = 무리 모두.</summary>
    public int Owner = -1;
    /// <summary>무덤에 묻힌 사람(−1 = 아님).</summary>
    public int Person = -1;
    /// <summary>낡음: Stamp 시각의 상태(0~1000) · 그 뒤 무너지는(또는 버려지는) 때(0 = 없음).</summary>
    public long Stamp;
    public int Cond0 = 1000;
    public long Due;
    /// <summary>작업대를 쓰는 사람과 그 일이 끝나는 때(한 사람이 한 작업대).</summary>
    public int User = -1;
    public long UserUntil;
    /// <summary>지은 무리의 말(낱말 · 이름).</summary>
    public int Lang = -1;
    /// <summary>마지막으로 고친 때(−1 = 고친 적 없음).</summary>
    public long Repaired = -1;

    public bool Done => Stage == BuildStage.Done;
}
